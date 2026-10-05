using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Analysis;

public sealed class UniversalFindingBuilder : IUniversalFindingBuilder
{
    private static readonly FindingCategory[] UniversalCategories =
        [FindingCategory.System, FindingCategory.Application, FindingCategory.ApplicationData,
            FindingCategory.UserData, FindingCategory.Unknown];

    public UniversalFindingResult Build(UniversalFindingRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        UniversalFindingReason reasons = MapUpstreamReasons(request);
        try
        {
            if (!InputsAgree(request))
                return Unavailable(request, reasons | UniversalFindingReason.InputMismatch);

            if (request.AnalysisResult.Quality == AnalysisQuality.Unavailable)
                return Unavailable(request, reasons | UniversalFindingReason.UpstreamAnalysisUnavailable);

            bool incomplete = request.AnalysisResult.Quality == AnalysisQuality.Incomplete ||
                              request.AccountingResult.Summary.Quality != AccountingQuality.Complete ||
                              request.AccountingResult.Reconciliation.Quality != AccountingQuality.Complete;
            long? capacity = request.AccountingResult.Reconciliation.EndSnapshot.CapacityBytes;
            if (capacity is null)
            {
                reasons |= UniversalFindingReason.VolumeCapacityUnavailable;
                incomplete = true;
            }

            var categories = BuildCategoryFindings(request, capacity, incomplete);
            var drafts = new Dictionary<string, FindingDraft>(StringComparer.Ordinal);
            AddCandidateView(request.AnalysisResult.LargestHierarchyCandidates,
                AnalysisCandidateScope.HierarchyNode, request, capacity, drafts, cancellationToken);
            AddCandidateView(request.AnalysisResult.LargestIdentityCandidates,
                AnalysisCandidateScope.IdentityGroup, request, capacity, drafts, cancellationToken);
            AddCandidateView(request.AnalysisResult.LargestFileCandidates,
                AnalysisCandidateScope.File, request, capacity, drafts, cancellationToken);
            AddCandidateView(request.AnalysisResult.LargestUnknownCandidates,
                null, request, capacity, drafts, cancellationToken);
            AddCacheFindings(request, capacity, drafts, cancellationToken);

            int remaining = request.Options.MaximumTotalFindings - categories.Count;
            if (remaining == 0)
            {
                return new UniversalFindingResult(
                    incomplete ? AnalysisQuality.Incomplete : AnalysisQuality.Complete,
                    reasons,
                    request.AnalysisResult.Quality,
                    request.AnalysisResult.Reasons,
                    categories);
            }
            var bounded = new BoundedFindingSet<FindingDraft>(remaining, FindingDraftComparer.Instance);
            foreach (FindingDraft draft in drafts.Values) bounded.Add(draft);

            var findings = new List<Finding>(categories);
            findings.AddRange(bounded.ToArray().Select(draft => CreateFinding(request, draft, incomplete)));
            return new UniversalFindingResult(
                incomplete ? AnalysisQuality.Incomplete : AnalysisQuality.Complete,
                reasons,
                request.AnalysisResult.Quality,
                request.AnalysisResult.Reasons,
                findings);
        }
        catch (InputMismatchException)
        {
            return Unavailable(request, reasons | UniversalFindingReason.InputMismatch);
        }
        catch (ResourceLimitException)
        {
            return Unavailable(request, reasons | UniversalFindingReason.ResourceLimit);
        }
        catch (OverflowException)
        {
            return Unavailable(request, reasons | UniversalFindingReason.ArithmeticOverflow);
        }
    }

    private static List<Finding> BuildCategoryFindings(
        UniversalFindingRequest request, long? capacity, bool incomplete)
    {
        var findings = new List<Finding>();
        foreach (FindingCategory category in UniversalCategories)
        {
            CategorySummary summary = request.AnalysisResult.CategorySummaries.Single(item => item.Category == category);
            if (summary.PathCount == 0 && summary.IdentityGroupCount == 0 &&
                summary.RawVisibleAllocatedBytes == 0 && summary.UncertainMeasuredAllocatedBytes == 0 &&
                summary.DeduplicatedObservedAllocatedBytes is not > 0)
                continue;

            var facets = new List<FindingFacet>();
            if (UniversalFindingPolicies.IsLarge(summary.DeduplicatedObservedAllocatedBytes, capacity))
                facets.Add(FindingFacet.Large);
            var evidence = new List<Evidence>
            {
                new("finding.scope.category", "This finding summarizes a universal category view.", Confidence.Verified),
                new(UniversalFindingPolicies.CategoryEvidenceCode(category),
                    "The upstream universal classifier assigned this category.",
                    UniversalFindingPolicies.CategoryConfidence(category)),
                ReclaimEvidence()
            };
            if (summary.DeduplicatedObservedAllocatedBytes is not null)
                evidence.Add(AllocatedEvidence());
            if (facets.Contains(FindingFacet.Large)) evidence.Add(LargeEvidence());

            var draft = new FindingDraft(
                $"category:{category}", FindingScope.CategoryAggregate, category, [], null, null, null,
                null, summary.DeduplicatedObservedAllocatedBytes, facets, evidence,
                UniversalFindingPolicies.CategoryConfidence(category));
            findings.Add(CreateFinding(request, draft, incomplete));
        }

        return findings;
    }

    private static void AddCandidateView(
        IEnumerable<StorageAnalysisCandidate> candidates,
        AnalysisCandidateScope? expectedScope,
        UniversalFindingRequest request,
        long? capacity,
        IDictionary<string, FindingDraft> combined,
        CancellationToken cancellationToken)
    {
        var bounded = new BoundedFindingSet<FindingDraft>(
            request.Options.MaximumFindingsPerView, FindingDraftComparer.Instance);
        foreach (StorageAnalysisCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedScope is not null && candidate.Scope != expectedScope) throw new InputMismatchException();
            FindingDraft? draft = FromCandidate(candidate, capacity);
            if (draft is not null) bounded.Add(draft);
        }

        foreach (FindingDraft draft in bounded.ToArray()) Merge(combined, draft);
    }

    private static FindingDraft? FromCandidate(StorageAnalysisCandidate candidate, long? capacity)
    {
        if (!UniversalCategories.Contains(candidate.PrimaryCategory)) throw new InputMismatchException();
        FindingScope scope;
        string key;
        switch (candidate.Scope)
        {
            case AnalysisCandidateScope.HierarchyNode:
                scope = FindingScope.HierarchyArea;
                key = $"hierarchy:{candidate.RelativePaths[0]}";
                if (candidate.ObjectIdentity is not null) throw new InputMismatchException();
                break;
            case AnalysisCandidateScope.IdentityGroup:
                if (candidate.RelativePaths.Count < 2) return null;
                if (candidate.ObjectIdentity is null) throw new InputMismatchException();
                scope = FindingScope.IdentityGroup;
                key = $"identity:{candidate.ObjectIdentity.VolumeIdentity.Id:D}:{candidate.ObjectIdentity.ObjectId:D}";
                break;
            case AnalysisCandidateScope.File:
                if (candidate.ObjectIdentity is null || candidate.RelativePaths.Count != 1)
                    throw new InputMismatchException();
                scope = FindingScope.File;
                key = $"file:{candidate.ObjectIdentity.VolumeIdentity.Id:D}:{candidate.ObjectIdentity.ObjectId:D}:{candidate.RelativePaths[0]}";
                break;
            default:
                throw new InputMismatchException();
        }

        var facets = candidate.Facets.Where(IsSupportedFacet).ToList();
        if (UniversalFindingPolicies.IsLarge(candidate.SizeEvidence.ObservedAttributedAllocatedBytes, capacity) &&
            !facets.Contains(FindingFacet.Large))
            facets.Add(FindingFacet.Large);
        var evidence = candidate.Evidence.ToList();
        evidence.Add(new Evidence($"finding.scope.{ScopeCode(scope)}",
            "This finding is a bounded universal analysis view.", Confidence.Verified));
        evidence.Add(ReclaimEvidence());
        if (candidate.SizeEvidence.ObservedAttributedAllocatedBytes is not null) evidence.Add(AllocatedEvidence());
        if (facets.Contains(FindingFacet.Large)) evidence.Add(LargeEvidence());

        return new FindingDraft(key, scope, candidate.PrimaryCategory, candidate.RelativePaths,
            candidate.ObjectIdentity, candidate.FileCount, candidate.DirectoryCount,
            candidate.SizeEvidence.VisibleLogicalBytes, candidate.SizeEvidence.ObservedAttributedAllocatedBytes,
            facets, evidence, candidate.ClassificationConfidence);
    }

    private static void AddCacheFindings(
        UniversalFindingRequest request,
        long? capacity,
        IDictionary<string, FindingDraft> combined,
        CancellationToken cancellationToken)
    {
        StorageHierarchyNode? root = request.AccountingResult.Root;
        if (root is null) return;
        string[] acceptedRoots = request.AnalysisRequest.ClassificationContext.CurrentUserAppDataRoots
            .Concat(request.AnalysisRequest.ClassificationContext.ProgramDataRoot is { } programData
                ? [programData] : Array.Empty<string>())
            .ToArray();
        var stack = new Stack<StorageHierarchyNode>();
        stack.Push(root);
        int visited = 0;
        var bounded = new BoundedFindingSet<FindingDraft>(
            request.Options.MaximumFindingsPerView, FindingDraftComparer.Instance);
        while (stack.Count != 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StorageHierarchyNode node = stack.Pop();
            visited = checked(visited + 1);
            if (visited > request.Options.MaximumHierarchyNodes) throw new ResourceLimitException();
            for (int index = node.Children.Count - 1; index >= 0; index--) stack.Push(node.Children[index]);
            if (node.RelativePath.Length == 0) continue;
            string absolute = CombineRoot(request.AnalysisRequest.SystemVolume.RootPath, node.RelativePath);
            if (!UniversalFindingPolicies.IsCacheLikePath(absolute, acceptedRoots)) continue;

            var facets = new List<FindingFacet> { FindingFacet.CacheLike };
            if (UniversalFindingPolicies.IsLarge(node.Aggregate.InclusiveAttributedObservedAllocatedBytes, capacity))
                facets.Add(FindingFacet.Large);
            var evidence = new List<Evidence>
            {
                new("finding.cache_like.application_data_component",
                    "An exact cache path component occurs under trusted application-data context.", Confidence.Medium),
                new("finding.scope.hierarchy", "This finding is an in-memory hierarchy area.", Confidence.Verified),
                ReclaimEvidence(),
                AllocatedEvidence()
            };
            if (facets.Contains(FindingFacet.Large)) evidence.Add(LargeEvidence());
            bounded.Add(new FindingDraft(
                $"hierarchy:{node.RelativePath}", FindingScope.HierarchyArea, FindingCategory.ApplicationData,
                [node.RelativePath], null, node.Aggregate.FileCount, node.Aggregate.DirectoryCount,
                node.Aggregate.VisibleLogicalMeasuredBytes, node.Aggregate.InclusiveAttributedObservedAllocatedBytes,
                facets, evidence, Confidence.Medium));
        }

        foreach (FindingDraft draft in bounded.ToArray()) Merge(combined, draft);
    }

    private static Finding CreateFinding(
        UniversalFindingRequest request, FindingDraft draft, bool incomplete)
    {
        Confidence confidence = draft.BaseConfidence;
        foreach (FindingFacet facet in draft.Facets)
        {
            Confidence facetConfidence = facet switch
            {
                FindingFacet.Large => Confidence.Verified,
                FindingFacet.CacheLike => Confidence.Medium,
                _ => FacetConfidence(facet, draft.Evidence, confidence)
            };
            confidence = UniversalFindingPolicies.Minimum(confidence, facetConfidence);
        }
        confidence = UniversalFindingPolicies.QualityCap(confidence, incomplete);

        var riskReasons = new List<string>
        {
            UniversalFindingPolicies.CategoryRiskReason(draft.Category),
            "risk.reclaim_unknown"
        };
        if (draft.Facets.Contains(FindingFacet.CacheLike)) riskReasons.Add("risk.cache_like_unverified");
        if (draft.Facets.Contains(FindingFacet.HardLinked)) riskReasons.Add("risk.hardlink_observed_alias");
        if (draft.Facets.Contains(FindingFacet.CloudPlaceholder)) riskReasons.Add("risk.cloud_placeholder");
        if (confidence < Confidence.High) riskReasons.Add("risk.low_confidence");

        return new Finding(
            UniversalFindingPolicies.FindingId(request.ScanSessionId, draft.Key),
            request.ScanSessionId,
            draft.Scope == FindingScope.CategoryAggregate ? draft.Category.ToString() : draft.Paths[0],
            draft.Scope,
            draft.Category,
            draft.Paths,
            draft.Identity,
            draft.FileCount,
            draft.DirectoryCount,
            draft.Facets.Order().ToArray(),
            new SizeMetrics(draft.LogicalBytes, draft.AllocatedBytes, null),
            ReclaimEstimate.Unknown("finding.reclaim.unknown"),
            new RiskAssessment(UniversalFindingPolicies.Risk(draft.Category), Confidence.Verified,
                riskReasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)),
            confidence,
            UniversalFindingPolicies.Protection(draft.Category),
            draft.Evidence);
    }

    private static bool InputsAgree(UniversalFindingRequest request)
    {
        VolumeIdentity volume = request.AnalysisRequest.SystemVolume.VolumeIdentity;
        if (!volume.Equals(request.AccountingResult.Reconciliation.StartSnapshot.VolumeIdentity) ||
            !volume.Equals(request.AccountingResult.Reconciliation.EndSnapshot.VolumeIdentity)) return false;
        if (request.AnalysisResult.UpstreamAccountingQuality != request.AccountingResult.Summary.Quality ||
            request.AnalysisResult.UpstreamAccountingReasons != request.AccountingResult.Summary.Reasons) return false;

        IEnumerable<StorageAnalysisCandidate> allCandidates = request.AnalysisResult.LargestHierarchyCandidates
            .Concat(request.AnalysisResult.LargestIdentityCandidates)
            .Concat(request.AnalysisResult.LargestFileCandidates)
            .Concat(request.AnalysisResult.LargestUnknownCandidates);
        if (allCandidates.Any(candidate => candidate.ObjectIdentity is { } identity &&
                                           !identity.VolumeIdentity.Equals(volume))) return false;
        if (request.AnalysisResult.Quality == AnalysisQuality.Unavailable) return true;
        StorageAggregate? aggregate = request.AccountingResult.Summary.Aggregate;
        if (aggregate is null) return true;

        long raw = 0;
        long uncertain = 0;
        long deduplicated = 0;
        bool allDeduplicatedKnown = true;
        foreach (CategorySummary summary in request.AnalysisResult.CategorySummaries)
        {
            if (!UniversalCategories.Contains(summary.Category) &&
                (summary.PathCount != 0 || summary.IdentityGroupCount != 0 ||
                 summary.RawVisibleAllocatedBytes != 0 || summary.UncertainMeasuredAllocatedBytes != 0 ||
                 summary.DeduplicatedObservedAllocatedBytes is > 0))
                return false;
            raw = checked(raw + summary.RawVisibleAllocatedBytes);
            uncertain = checked(uncertain + summary.UncertainMeasuredAllocatedBytes);
            if (summary.DeduplicatedObservedAllocatedBytes is { } value)
                deduplicated = checked(deduplicated + value);
            else
                allDeduplicatedKnown = false;
        }
        if (raw != aggregate.RawReportedAllocatedBytes || uncertain != aggregate.UncertainMeasuredAllocatedBytes)
            return false;
        return !allDeduplicatedKnown || request.AccountingResult.Summary.DeduplicatedObservedAllocatedBytes is null ||
               deduplicated == request.AccountingResult.Summary.DeduplicatedObservedAllocatedBytes;
    }

    private static UniversalFindingReason MapUpstreamReasons(UniversalFindingRequest request)
    {
        UniversalFindingReason reasons = UniversalFindingReason.None;
        if (request.AnalysisResult.Quality == AnalysisQuality.Incomplete)
            reasons |= UniversalFindingReason.UpstreamAnalysisIncomplete;
        if (request.AnalysisResult.Quality == AnalysisQuality.Unavailable)
            reasons |= UniversalFindingReason.UpstreamAnalysisUnavailable;
        if (request.AccountingResult.Summary.Quality == AccountingQuality.Unavailable)
            reasons |= UniversalFindingReason.UpstreamAccountingUnavailable;
        if (request.AccountingResult.Summary.Quality == AccountingQuality.Incomplete)
            reasons |= UniversalFindingReason.UpstreamAccountingIncomplete;
        if (request.AccountingResult.Reconciliation.Quality == AccountingQuality.Inconsistent)
            reasons |= UniversalFindingReason.UpstreamReconciliationInconsistent;
        return reasons;
    }

    private static UniversalFindingResult Unavailable(
        UniversalFindingRequest request, UniversalFindingReason reasons) =>
        new(AnalysisQuality.Unavailable, reasons, request.AnalysisResult.Quality,
            request.AnalysisResult.Reasons, []);

    private static void Merge(IDictionary<string, FindingDraft> values, FindingDraft candidate)
    {
        if (!values.TryGetValue(candidate.Key, out FindingDraft? existing))
        {
            values.Add(candidate.Key, candidate);
            return;
        }
        if (existing.Scope != candidate.Scope || existing.Category != candidate.Category ||
            !Equals(existing.Identity, candidate.Identity) || existing.LogicalBytes != candidate.LogicalBytes ||
            existing.AllocatedBytes != candidate.AllocatedBytes || existing.FileCount != candidate.FileCount ||
            existing.DirectoryCount != candidate.DirectoryCount ||
            !existing.Paths.SequenceEqual(candidate.Paths, StringComparer.Ordinal))
            throw new InputMismatchException();

        var evidence = existing.Evidence.ToDictionary(item => item.Code, StringComparer.Ordinal);
        foreach (Evidence item in candidate.Evidence)
        {
            if (evidence.TryGetValue(item.Code, out Evidence? prior) &&
                (!prior.Description.Equals(item.Description, StringComparison.Ordinal) ||
                 prior.Confidence != item.Confidence))
                throw new InputMismatchException();
            evidence[item.Code] = item;
        }
        values[candidate.Key] = existing with
        {
            Facets = existing.Facets.Concat(candidate.Facets).Distinct().Order().ToArray(),
            Evidence = evidence.Values.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray(),
            BaseConfidence = UniversalFindingPolicies.Minimum(existing.BaseConfidence, candidate.BaseConfidence)
        };
    }

    private static Confidence FacetConfidence(
        FindingFacet facet, IReadOnlyList<Evidence> evidence, Confidence fallback)
    {
        string? code = facet switch
        {
            FindingFacet.Compressed => "facet.compressed",
            FindingFacet.Sparse => "facet.sparse",
            FindingFacet.HardLinked => "facet.hardlink_observed_alias",
            FindingFacet.CloudPlaceholder => "facet.cloud_placeholder",
            _ => null
        };
        return code is null ? fallback : evidence.FirstOrDefault(item => item.Code == code)?.Confidence ?? fallback;
    }

    private static bool IsSupportedFacet(FindingFacet facet) => facet is FindingFacet.Compressed or
        FindingFacet.Sparse or FindingFacet.HardLinked or FindingFacet.CloudPlaceholder;

    private static string ScopeCode(FindingScope scope) => scope switch
    {
        FindingScope.HierarchyArea => "hierarchy",
        FindingScope.IdentityGroup => "identity",
        FindingScope.File => "file",
        _ => "category"
    };

    private static string CombineRoot(string root, string relative) =>
        root.EndsWith('\\') ? root + relative.TrimStart('\\') : root + "\\" + relative.TrimStart('\\');

    private static Evidence ReclaimEvidence() => new(
        "finding.reclaim.unknown", "No deterministic reclaim amount has been established.", Confidence.Unknown);

    private static Evidence AllocatedEvidence() => new(
        "finding.size.observed_allocated", "Allocation is observed and attributed, not proven exclusive.",
        Confidence.Verified);

    private static Evidence LargeEvidence() => new(
        "finding.facet.large", "Observed attributed allocation meets the capacity-based threshold.",
        Confidence.Verified);

    private sealed record FindingDraft(
        string Key,
        FindingScope Scope,
        FindingCategory Category,
        IReadOnlyList<string> Paths,
        StorageObjectIdentity? Identity,
        long? FileCount,
        long? DirectoryCount,
        long? LogicalBytes,
        long? AllocatedBytes,
        IReadOnlyList<FindingFacet> Facets,
        IReadOnlyList<Evidence> Evidence,
        Confidence BaseConfidence);

    private sealed class FindingDraftComparer : IComparer<FindingDraft>
    {
        internal static readonly FindingDraftComparer Instance = new();

        public int Compare(FindingDraft? left, FindingDraft? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;
            if (right is null) return -1;
            int result = CompareAllocated(left.AllocatedBytes, right.AllocatedBytes);
            if (result != 0) return result;
            result = left.Scope.CompareTo(right.Scope);
            if (result != 0) return result;
            result = left.Category.CompareTo(right.Category);
            if (result != 0) return result;
            return StringComparer.Ordinal.Compare(left.Key, right.Key);
        }

        private static int CompareAllocated(long? left, long? right)
        {
            if (left is null) return right is null ? 0 : 1;
            if (right is null) return -1;
            return right.Value.CompareTo(left.Value);
        }
    }

    private sealed class InputMismatchException : Exception;
    private sealed class ResourceLimitException : Exception;
}
