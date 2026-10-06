using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Enumeration;
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

            AccountingIndexes indexes = BuildAccountingIndexes(request, cancellationToken);
            var classifier = new DeterministicClassificationEngine(request.AnalysisRequest.ClassificationContext);

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
            var validatedCandidates = new Dictionary<string, FindingDraft>(StringComparer.Ordinal);
            AddCandidateView(request.AnalysisResult.LargestHierarchyCandidates,
                AnalysisCandidateScope.HierarchyNode, request, capacity, indexes, classifier,
                validatedCandidates, drafts, cancellationToken);
            AddCandidateView(request.AnalysisResult.LargestIdentityCandidates,
                AnalysisCandidateScope.IdentityGroup, request, capacity, indexes, classifier,
                validatedCandidates, drafts, cancellationToken);
            AddCandidateView(request.AnalysisResult.LargestFileCandidates,
                AnalysisCandidateScope.File, request, capacity, indexes, classifier,
                validatedCandidates, drafts, cancellationToken);
            AddCandidateView(request.AnalysisResult.LargestUnknownCandidates,
                null, request, capacity, indexes, classifier, validatedCandidates, drafts, cancellationToken);
            AddCacheFindings(request, capacity, indexes, drafts, cancellationToken);

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
            var bounded = new BoundedFindingSet<FindingDraft>(remaining, FindingDraftComparer.Instance, item => item.Key);
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
                null, summary.DeduplicatedObservedAllocatedBytes, null, facets, evidence,
                UniversalFindingPolicies.CategoryConfidence(category));
            findings.Add(CreateFinding(request, draft, incomplete));
        }

        return findings;
    }

    private static AccountingIndexes BuildAccountingIndexes(
        UniversalFindingRequest request, CancellationToken cancellationToken)
    {
        VolumeIdentity volume = request.AnalysisRequest.SystemVolume.VolumeIdentity;
        var groups = new Dictionary<StorageObjectIdentity, AllocationGroup>();
        int groupCount = 0;
        foreach (AllocationGroup group in request.AccountingResult.AllocationGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            groupCount = checked(groupCount + 1);
            if (groupCount > request.Options.MaximumHierarchyNodes) throw new ResourceLimitException();
            if (!group.Identity.VolumeIdentity.Equals(volume) || !groups.TryAdd(group.Identity, group) ||
                group.Paths.Any(path => !UniversalFindingPolicies.IsCanonicalRelativePath(path)))
                throw new InputMismatchException();
        }

        var hierarchy = new Dictionary<string, StorageHierarchyNode>(StringComparer.Ordinal);
        var caseInsensitivePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (request.AccountingResult.Root is not { } root) return new AccountingIndexes(groups, hierarchy);
        if (root.RelativePath.Length != 0) throw new InputMismatchException();
        var stack = new Stack<(StorageHierarchyNode Node, string Parent)>();
        stack.Push((root, string.Empty));
        int visited = 0;
        while (stack.TryPop(out (StorageHierarchyNode Node, string Parent) item))
        {
            cancellationToken.ThrowIfCancellationRequested();
            visited = checked(visited + 1);
            if (visited > request.Options.MaximumHierarchyNodes) throw new ResourceLimitException();
            string path = item.Node.RelativePath;
            if (path.Length != 0 && (!UniversalFindingPolicies.IsCanonicalRelativePath(path) ||
                !UniversalFindingPolicies.IsDescendantPath(path, item.Parent)))
                throw new InputMismatchException();
            if (!hierarchy.TryAdd(path, item.Node) || !caseInsensitivePaths.Add(path))
                throw new InputMismatchException();
            for (int index = item.Node.Children.Count - 1; index >= 0; index--)
                stack.Push((item.Node.Children[index], path));
        }
        return new AccountingIndexes(groups, hierarchy);
    }

    private static void ValidateGroupCandidate(StorageAnalysisCandidate candidate, AllocationGroup group)
    {
        if (!Equals(candidate.ObjectIdentity, group.Identity) || group.Reasons != AccountingReason.None ||
            group.EligibleReportedAllocatedBytes is not { } allocated ||
            !candidate.RelativePaths.SequenceEqual(group.Paths, StringComparer.Ordinal) ||
            candidate.FileCount != 1 || candidate.DirectoryCount != 0 ||
            candidate.SizeEvidence.RawReportedAllocatedBytes != allocated ||
            candidate.SizeEvidence.ObservedAttributedAllocatedBytes != allocated ||
            candidate.SizeEvidence.UncertainMeasuredAllocatedBytes != 0)
            throw new InputMismatchException();
    }

    private static void ValidateHierarchyCandidate(
        StorageAnalysisCandidate candidate, StorageHierarchyNode node)
    {
        StorageAggregate aggregate = node.Aggregate;
        if (candidate.FileCount != aggregate.FileCount || candidate.DirectoryCount != aggregate.DirectoryCount ||
            candidate.SizeEvidence.VisibleLogicalBytes != aggregate.VisibleLogicalMeasuredBytes ||
            candidate.SizeEvidence.RawReportedAllocatedBytes != aggregate.RawReportedAllocatedBytes ||
            candidate.SizeEvidence.ObservedAttributedAllocatedBytes !=
                aggregate.InclusiveAttributedObservedAllocatedBytes ||
            candidate.SizeEvidence.UncertainMeasuredAllocatedBytes != aggregate.UncertainMeasuredAllocatedBytes)
            throw new InputMismatchException();
    }

    private static void ValidateClassification(
        StorageAnalysisCandidate candidate,
        IReadOnlyList<string> paths,
        UniversalFindingRequest request,
        DeterministicClassificationEngine classifier)
    {
        DeterministicClassificationEngine.Result[] classifications = paths.Select(path =>
            classifier.Classify(CombineRoot(request.AnalysisRequest.SystemVolume.RootPath, path),
                StorageEntryAttributes.None)).ToArray();
        FindingCategory[] categories = classifications.Select(item => item.Category).Distinct().ToArray();
        FindingCategory expectedCategory = categories.Length == 1 ? categories[0] : FindingCategory.Unknown;
        Confidence expectedConfidence = expectedCategory == FindingCategory.Unknown
            ? Confidence.Unknown
            : classifications.Min(item => item.Confidence);
        if (candidate.PrimaryCategory != expectedCategory ||
            candidate.ClassificationConfidence != expectedConfidence)
            throw new InputMismatchException();
    }

    private static void AddCandidateView(
        IEnumerable<StorageAnalysisCandidate> candidates,
        AnalysisCandidateScope? expectedScope,
        UniversalFindingRequest request,
        long? capacity,
        AccountingIndexes indexes,
        DeterministicClassificationEngine classifier,
        IDictionary<string, FindingDraft> allValidated,
        IDictionary<string, FindingDraft> combined,
        CancellationToken cancellationToken)
    {
        var merged = new Dictionary<string, FindingDraft>(StringComparer.Ordinal);
        foreach (StorageAnalysisCandidate candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (expectedScope is not null && candidate.Scope != expectedScope) throw new InputMismatchException();
            FindingDraft? draft = FromCandidate(candidate, request, capacity, indexes, classifier);
            if (draft is not null)
            {
                Merge(allValidated, draft);
                Merge(merged, draft);
            }
        }

        var bounded = new BoundedFindingSet<FindingDraft>(
            request.Options.MaximumFindingsPerView, FindingDraftComparer.Instance, item => item.Key);
        foreach (FindingDraft draft in merged.Values) bounded.Add(draft);

        foreach (FindingDraft draft in bounded.ToArray()) Merge(combined, draft);
    }

    private static FindingDraft? FromCandidate(
        StorageAnalysisCandidate candidate,
        UniversalFindingRequest request,
        long? capacity,
        AccountingIndexes indexes,
        DeterministicClassificationEngine classifier)
    {
        if (!UniversalCategories.Contains(candidate.PrimaryCategory)) throw new InputMismatchException();
        FindingScope scope;
        string key;
        long? logicalBytes;
        long allocatedBytes;
        switch (candidate.Scope)
        {
            case AnalysisCandidateScope.HierarchyNode:
                if (candidate.RelativePaths.Count != 1 || candidate.ObjectIdentity is not null ||
                    !indexes.Hierarchy.TryGetValue(candidate.RelativePaths[0], out StorageHierarchyNode? node))
                    throw new InputMismatchException();
                ValidateHierarchyCandidate(candidate, node);
                ValidateClassification(candidate, [candidate.RelativePaths[0]], request, classifier);
                scope = FindingScope.HierarchyArea;
                key = $"hierarchy:{candidate.RelativePaths[0]}";
                logicalBytes = node.Aggregate.VisibleLogicalMeasuredBytes;
                allocatedBytes = node.Aggregate.InclusiveAttributedObservedAllocatedBytes;
                break;
            case AnalysisCandidateScope.IdentityGroup:
                if (candidate.ObjectIdentity is null ||
                    !indexes.Groups.TryGetValue(candidate.ObjectIdentity, out AllocationGroup? identityGroup))
                    throw new InputMismatchException();
                ValidateGroupCandidate(candidate, identityGroup);
                ValidateClassification(candidate, identityGroup.Paths, request, classifier);
                if (identityGroup.Paths.Count == 1) return null;
                scope = FindingScope.IdentityGroup;
                key = $"identity:{candidate.ObjectIdentity.VolumeIdentity.Id:D}:{candidate.ObjectIdentity.ObjectId:D}";
                logicalBytes = null;
                allocatedBytes = identityGroup.EligibleReportedAllocatedBytes!.Value;
                break;
            case AnalysisCandidateScope.File:
                if (candidate.ObjectIdentity is null || candidate.RelativePaths.Count != 1)
                    throw new InputMismatchException();
                if (!indexes.Groups.TryGetValue(candidate.ObjectIdentity, out AllocationGroup? fileGroup))
                    throw new InputMismatchException();
                ValidateGroupCandidate(candidate, fileGroup);
                if (fileGroup.Paths.Count != 1) throw new InputMismatchException();
                ValidateClassification(candidate, fileGroup.Paths, request, classifier);
                scope = FindingScope.File;
                key = $"file:{candidate.ObjectIdentity.VolumeIdentity.Id:D}:{candidate.ObjectIdentity.ObjectId:D}:{candidate.RelativePaths[0]}";
                logicalBytes = null;
                allocatedBytes = fileGroup.EligibleReportedAllocatedBytes!.Value;
                break;
            default:
                throw new InputMismatchException();
        }

        var facets = candidate.Facets.Where(IsSupportedFacet).ToList();
        if (UniversalFindingPolicies.IsLarge(candidate.SizeEvidence.ObservedAttributedAllocatedBytes, capacity) &&
            !facets.Contains(FindingFacet.Large))
            facets.Add(FindingFacet.Large);
        var evidence = candidate.Evidence.ToList();
        AddEvidence(evidence, new Evidence($"finding.scope.{ScopeCode(scope)}",
            "This finding is a bounded universal analysis view.", Confidence.Verified));
        AddEvidence(evidence, ReclaimEvidence());
        AddEvidence(evidence, AllocatedEvidence());
        if (facets.Contains(FindingFacet.Large)) AddEvidence(evidence, LargeEvidence());

        return new FindingDraft(key, scope, candidate.PrimaryCategory, candidate.RelativePaths,
            candidate.ObjectIdentity, candidate.FileCount, candidate.DirectoryCount,
            logicalBytes, allocatedBytes, candidate.SizeEvidence.VisibleLogicalBytes,
            facets, evidence, candidate.ClassificationConfidence);
    }

    private static void AddCacheFindings(
        UniversalFindingRequest request,
        long? capacity,
        AccountingIndexes indexes,
        IDictionary<string, FindingDraft> combined,
        CancellationToken cancellationToken)
    {
        VolumeIdentity volume = request.AnalysisRequest.SystemVolume.VolumeIdentity;
        string[] absoluteRoots = request.AnalysisRequest.ClassificationContext.CurrentUserAppDataRoots
            .Concat(request.AnalysisRequest.ClassificationContext.ProgramDataRoot is { } programData
                ? [programData] : Array.Empty<string>())
            .ToArray();
        var acceptedRoots = new List<string>(absoluteRoots.Length);
        foreach (string root in absoluteRoots)
        {
            if (!UniversalFindingPolicies.TryGetVolumeRelativePath(root, volume, out string relativeRoot))
                throw new InputMismatchException();
            acceptedRoots.Add(relativeRoot);
        }
        var merged = new Dictionary<string, FindingDraft>(StringComparer.Ordinal);
        foreach (StorageHierarchyNode node in indexes.Hierarchy.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (node.RelativePath.Length == 0 ||
                !UniversalFindingPolicies.IsCacheLikeRelativePath(node.RelativePath, acceptedRoots)) continue;

            var facets = new List<FindingFacet> { FindingFacet.CacheLike };
            if (UniversalFindingPolicies.IsLarge(node.Aggregate.InclusiveAttributedObservedAllocatedBytes, capacity))
                facets.Add(FindingFacet.Large);
            var evidence = new List<Evidence>();
            AddEvidence(evidence, new Evidence("finding.cache_like.application_data_component",
                "An exact cache path component occurs under trusted application-data context.", Confidence.Medium));
            AddEvidence(evidence, new Evidence("finding.scope.hierarchy",
                "This finding is a bounded universal analysis view.", Confidence.Verified));
            AddEvidence(evidence, ReclaimEvidence());
            AddEvidence(evidence, AllocatedEvidence());
            if (facets.Contains(FindingFacet.Large)) AddEvidence(evidence, LargeEvidence());
            Merge(merged, new FindingDraft(
                $"hierarchy:{node.RelativePath}", FindingScope.HierarchyArea, FindingCategory.ApplicationData,
                [node.RelativePath], null, node.Aggregate.FileCount, node.Aggregate.DirectoryCount,
                node.Aggregate.VisibleLogicalMeasuredBytes, node.Aggregate.InclusiveAttributedObservedAllocatedBytes,
                node.Aggregate.VisibleLogicalMeasuredBytes, facets, evidence, Confidence.Medium));
        }

        var bounded = new BoundedFindingSet<FindingDraft>(
            request.Options.MaximumFindingsPerView, FindingDraftComparer.Instance, item => item.Key);
        foreach (FindingDraft draft in merged.Values) bounded.Add(draft);

        foreach (FindingDraft draft in bounded.ToArray()) Merge(combined, draft);
    }

    private static Finding CreateFinding(
        UniversalFindingRequest request, FindingDraft draft, bool incomplete)
    {
        ValidateFinalScope(draft);
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
        long? authoritativeDeduplicated = request.AccountingResult.Summary.DeduplicatedObservedAllocatedBytes;
        foreach (CategorySummary summary in request.AnalysisResult.CategorySummaries)
        {
            if (!UniversalCategories.Contains(summary.Category) &&
                (summary.PathCount != 0 || summary.IdentityGroupCount != 0 ||
                 summary.RawVisibleAllocatedBytes != 0 || summary.UncertainMeasuredAllocatedBytes != 0 ||
                 summary.DeduplicatedObservedAllocatedBytes is > 0))
                return false;
            raw = checked(raw + summary.RawVisibleAllocatedBytes);
            uncertain = checked(uncertain + summary.UncertainMeasuredAllocatedBytes);
            if (authoritativeDeduplicated is not null)
            {
                if (summary.DeduplicatedObservedAllocatedBytes is not { } value) return false;
                deduplicated = checked(deduplicated + value);
            }
            else if (summary.DeduplicatedObservedAllocatedBytes is not null)
            {
                return false;
            }
        }
        if (raw != aggregate.RawReportedAllocatedBytes || uncertain != aggregate.UncertainMeasuredAllocatedBytes)
            return false;
        return authoritativeDeduplicated is null || deduplicated == authoritativeDeduplicated;
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
            existing.AllocatedBytes != candidate.AllocatedBytes ||
            existing.SourceLogicalBytes != candidate.SourceLogicalBytes || existing.FileCount != candidate.FileCount ||
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

    private static void AddEvidence(List<Evidence> evidence, Evidence candidate)
    {
        Evidence? existing = evidence.FirstOrDefault(item => item.Code.Equals(candidate.Code, StringComparison.Ordinal));
        if (existing is null)
        {
            evidence.Add(candidate);
            return;
        }
        if (!existing.Description.Equals(candidate.Description, StringComparison.Ordinal) ||
            existing.Confidence != candidate.Confidence)
            throw new InputMismatchException();
    }

    private static void ValidateFinalScope(FindingDraft draft)
    {
        bool valid = draft.Scope switch
        {
            FindingScope.CategoryAggregate => draft.Paths.Count == 0 && draft.Identity is null &&
                draft.FileCount is null && draft.DirectoryCount is null,
            FindingScope.HierarchyArea => draft.Paths.Count == 1 && draft.Identity is null &&
                draft.FileCount is not null && draft.DirectoryCount is not null,
            FindingScope.IdentityGroup => draft.Paths.Count >= 2 && draft.Identity is not null &&
                draft.FileCount == 1 && draft.DirectoryCount == 0,
            FindingScope.File => draft.Paths.Count == 1 && draft.Identity is not null &&
                draft.FileCount == 1 && draft.DirectoryCount == 0,
            _ => false
        };
        if (!valid) throw new InputMismatchException();
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
        long? SourceLogicalBytes,
        IReadOnlyList<FindingFacet> Facets,
        IReadOnlyList<Evidence> Evidence,
        Confidence BaseConfidence);

    private sealed record AccountingIndexes(
        IReadOnlyDictionary<StorageObjectIdentity, AllocationGroup> Groups,
        IReadOnlyDictionary<string, StorageHierarchyNode> Hierarchy);

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
