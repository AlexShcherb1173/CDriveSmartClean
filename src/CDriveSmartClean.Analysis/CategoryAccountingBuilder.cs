using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Analysis;

internal static class CategoryAccountingBuilder
{
    private sealed class Bucket
    {
        internal long Deduplicated;
        internal long Raw;
        internal long Uncertain;
        internal long Paths;
        internal long Groups;
    }

    internal static StorageAnalysisResult Build(StorageAnalysisRequest request,
        IReadOnlyDictionary<string, AnalysisPathState> paths, StorageAccountingResult accounting,
        AnalysisReason sessionReasons, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Dictionary<FindingCategory, Bucket> buckets = Enum.GetValues<FindingCategory>()
            .ToDictionary(category => category, _ => new Bucket());
        AnalysisReason reasons = sessionReasons | MapAccounting(accounting);
        bool accountingAvailable = accounting.Summary.Quality != AccountingQuality.Unavailable && accounting.Root is not null;
        Dictionary<StorageObjectIdentity, AllocationGroup> groups = accounting.AllocationGroups
            .ToDictionary(group => group.Identity);

        foreach ((string path, AnalysisPathState state) in paths.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            token.ThrowIfCancellationRequested();
            if (state.Poisoned)
            {
                buckets[FindingCategory.Unknown].Paths = checked(buckets[FindingCategory.Unknown].Paths + 1);
                continue;
            }
            FindingCategory category = state.Classification.Category;
            Bucket bucket = buckets[category];
            bucket.Paths = checked(bucket.Paths + 1);
            AnalysisEntryFact fact = state.Fact;
            if (fact.Measurement.Availability != StorageMeasurementAvailability.Available) continue;
            long bytes = fact.Measurement.ReportedAllocatedBytes!.Value;
            bucket.Raw = checked(bucket.Raw + bytes);
            AccountingReason eligibility = Eligibility(fact);
            AllocationGroup? group = null;
            if (fact.Identity is { } objectIdentity)
            {
                groups.TryGetValue(objectIdentity, out group);
                if (accountingAvailable && group is null) return Mismatch(accounting, reasons);
                if (group is not null) eligibility |= group.Reasons;
            }
            if (eligibility != AccountingReason.None)
            {
                FindingCategory uncertainCategory = group?.IsConflicted == true
                    ? FindingCategory.Unknown
                    : category;
                buckets[uncertainCategory].Uncertain =
                    checked(buckets[uncertainCategory].Uncertain + bytes);
            }
        }

        var hierarchy = new BoundedCandidateSet(request.Options.CandidateLimit);
        var identityCandidates = new BoundedCandidateSet(request.Options.CandidateLimit);
        var files = new BoundedCandidateSet(request.Options.CandidateLimit);
        var unknown = new BoundedCandidateSet(request.Options.CandidateLimit);

        foreach (AllocationGroup group in accounting.AllocationGroups)
        {
            token.ThrowIfCancellationRequested();
            var states = new List<AnalysisPathState>(group.Paths.Count);
            foreach (string path in group.Paths)
            {
                if (!paths.TryGetValue(path, out AnalysisPathState? state) ||
                    state.Poisoned && !group.IsConflicted)
                    return Mismatch(accounting, reasons);
                states.Add(state);
            }
            FindingCategory groupCategory = GroupCategory(states, group.IsConflicted);
            if (states.Select(state => state.Classification.Category).Distinct().Count() > 1)
                reasons |= AnalysisReason.CrossCategoryIdentityConflict;
            buckets[groupCategory].Groups = checked(buckets[groupCategory].Groups + 1);
            if (group.EligibleReportedAllocatedBytes is { } bytes)
                buckets[groupCategory].Deduplicated = checked(buckets[groupCategory].Deduplicated + bytes);
            if (group.IsConflicted) reasons |= AnalysisReason.ConflictingIdentityEvidence;
            if (group.EligibleReportedAllocatedBytes is null) continue;

            StorageAnalysisCandidate identityCandidate = GroupCandidate(
                AnalysisCandidateScope.IdentityGroup, group, states, groupCategory);
            identityCandidates.Add(identityCandidate);
            if (identityCandidate.PrimaryCategory == FindingCategory.Unknown) unknown.Add(identityCandidate);
            if (group.Paths.Count == 1 && states[0].Fact.Kind == StorageObjectKind.File)
            {
                StorageAnalysisCandidate fileCandidate = GroupCandidate(
                    AnalysisCandidateScope.File, group, states, groupCategory);
                files.Add(fileCandidate);
                if (fileCandidate.PrimaryCategory == FindingCategory.Unknown) unknown.Add(fileCandidate);
            }
        }

        if (accounting.Root is not null)
        {
            var classifier = new DeterministicClassificationEngine(request.ClassificationContext);
            AddHierarchy(accounting.Root, request, paths, classifier, hierarchy, unknown, token);
        }

        if (accountingAvailable)
        {
            long raw = Sum(buckets, bucket => bucket.Raw);
            long uncertain = Sum(buckets, bucket => bucket.Uncertain);
            long deduplicated = Sum(buckets, bucket => bucket.Deduplicated);
            if (raw != accounting.Root!.Aggregate.RawReportedAllocatedBytes ||
                uncertain != accounting.Root.Aggregate.UncertainMeasuredAllocatedBytes ||
                deduplicated != accounting.Summary.DeduplicatedObservedAllocatedBytes)
                return Mismatch(accounting, reasons);
        }

        AnalysisQuality quality = reasons == AnalysisReason.None ? AnalysisQuality.Complete : AnalysisQuality.Incomplete;
        CategorySummary[] summaries = Enum.GetValues<FindingCategory>().Select(category =>
        {
            Bucket bucket = buckets[category];
            return new CategorySummary(category, accountingAvailable ? bucket.Deduplicated : null,
                bucket.Raw, bucket.Uncertain, bucket.Paths, bucket.Groups, quality, reasons);
        }).ToArray();
        return new StorageAnalysisResult(quality, reasons, accounting.Summary.Quality, accounting.Summary.Reasons,
            summaries, hierarchy.ToArray(), identityCandidates.ToArray(), files.ToArray(), unknown.ToArray());
    }

    private static AnalysisReason MapAccounting(StorageAccountingResult accounting)
    {
        AnalysisReason result = AnalysisReason.None;
        if (accounting.Summary.Quality == AccountingQuality.Unavailable)
            result |= AnalysisReason.UpstreamAccountingUnavailable;
        else if (accounting.Summary.Quality != AccountingQuality.Complete)
            result |= AnalysisReason.UpstreamAccountingIncomplete;
        if (accounting.Reconciliation.Quality == AccountingQuality.Inconsistent)
            result |= AnalysisReason.UpstreamReconciliationInconsistent;
        AccountingReason source = accounting.Summary.Reasons;
        if ((source & AccountingReason.IdentityUnavailable) != 0) result |= AnalysisReason.IdentityUnavailable;
        if ((source & AccountingReason.MeasurementUnavailable) != 0) result |= AnalysisReason.MeasurementUnavailable;
        if ((source & AccountingReason.ConflictingIdentityEvidence) != 0) result |= AnalysisReason.ConflictingIdentityEvidence;
        if ((source & AccountingReason.ConflictingPathEvidence) != 0) result |= AnalysisReason.ConflictingPathEvidence;
        if ((source & AccountingReason.UnsupportedAllocationEvidence) != 0) result |= AnalysisReason.UnsupportedAllocationEvidence;
        return result;
    }

    private static AccountingReason Eligibility(AnalysisEntryFact fact)
    {
        AccountingReason result = AccountingReason.None;
        if (fact.Identity is null) result |= AccountingReason.IdentityUnavailable;
        if (fact.Measurement.Availability != StorageMeasurementAvailability.Available)
            result |= AccountingReason.MeasurementUnavailable;
        if (fact.Kind != StorageObjectKind.File || fact.Reparse != ReparseKind.None ||
            fact.Measurement.Scope != StorageMeasurementScope.FileContent ||
            (fact.Attributes & ~(StorageEntryAttributes.Sparse | StorageEntryAttributes.Compressed)) != 0)
            result |= AccountingReason.UnsupportedAllocationEvidence;
        return result;
    }

    private static FindingCategory GroupCategory(IReadOnlyList<AnalysisPathState> states, bool conflicted)
    {
        if (conflicted) return FindingCategory.Unknown;
        FindingCategory[] categories = states.Select(state => state.Classification.Category).Distinct().ToArray();
        return categories.Length == 1 ? categories[0] : FindingCategory.Unknown;
    }

    private static StorageAnalysisCandidate GroupCandidate(AnalysisCandidateScope scope, AllocationGroup group,
        List<AnalysisPathState> states, FindingCategory category)
    {
        var facets = states.SelectMany(state => state.Classification.Facets).ToList();
        var evidence = states.SelectMany(state => state.Classification.Evidence).ToList();
        if (states.Count > 1)
        {
            facets.Add(FindingFacet.HardLinked);
            evidence.Add(new Evidence("facet.hardlink_observed_alias",
                "Multiple visible paths share the same observed native identity.", Confidence.Verified));
        }
        if (category == FindingCategory.Unknown &&
            states.Select(state => state.Classification.Category).Distinct().Count() > 1)
        {
            evidence.Add(new Evidence("cross_category_identity_conflict",
                "Observed aliases have different deterministic primary categories; allocation is counted once as Unknown.",
                Confidence.Verified));
        }
        Confidence confidence = category == FindingCategory.Unknown
            ? Confidence.Unknown
            : states.Min(state => state.Classification.Confidence);
        long? logical = states[0].Fact.Measurement.Availability == StorageMeasurementAvailability.Available
            ? states[0].Fact.Measurement.LogicalBytes
            : null;
        long bytes = group.EligibleReportedAllocatedBytes!.Value;
        return new StorageAnalysisCandidate(scope, category, facets, confidence, evidence,
            new AnalysisSizeEvidence(logical, bytes, bytes, 0), group.Paths, group.Identity, 1, 0);
    }

    private static void AddHierarchy(StorageHierarchyNode node, StorageAnalysisRequest request,
        IReadOnlyDictionary<string, AnalysisPathState> paths, DeterministicClassificationEngine classifier,
        BoundedCandidateSet hierarchy, BoundedCandidateSet unknown, CancellationToken token)
    {
        foreach (StorageHierarchyNode child in node.Children)
        {
            token.ThrowIfCancellationRequested();
            DeterministicClassificationEngine.Result classification;
            FindingFacet[] facets;
            if (paths.TryGetValue(child.RelativePath, out AnalysisPathState? state) && !state.Poisoned)
            {
                classification = state.Classification;
                facets = state.Fact.Kind == StorageObjectKind.Directory ? classification.Facets : [];
            }
            else
            {
                string fullPath = request.SystemVolume.RootPath.TrimEnd('\\') + "\\" + child.RelativePath;
                classification = classifier.Classify(fullPath, StorageEntryAttributes.None);
                facets = [];
            }
            StorageAggregate aggregate = child.Aggregate;
            var candidate = new StorageAnalysisCandidate(AnalysisCandidateScope.HierarchyNode,
                classification.Category, facets, classification.Confidence, classification.Evidence,
                new AnalysisSizeEvidence(aggregate.VisibleLogicalMeasuredBytes, aggregate.RawReportedAllocatedBytes,
                    aggregate.InclusiveAttributedObservedAllocatedBytes, aggregate.UncertainMeasuredAllocatedBytes),
                [child.RelativePath], null, aggregate.FileCount, aggregate.DirectoryCount);
            hierarchy.Add(candidate);
            if (candidate.PrimaryCategory == FindingCategory.Unknown) unknown.Add(candidate);
            AddHierarchy(child, request, paths, classifier, hierarchy, unknown, token);
        }
    }

    private static long Sum(Dictionary<FindingCategory, Bucket> buckets, Func<Bucket, long> selector)
    {
        long total = 0;
        foreach (Bucket bucket in buckets.Values) total = checked(total + selector(bucket));
        return total;
    }

    private static StorageAnalysisResult Mismatch(StorageAccountingResult accounting, AnalysisReason reasons) =>
        new(AnalysisQuality.Unavailable, reasons | AnalysisReason.AccountingMismatch,
            accounting.Summary.Quality, accounting.Summary.Reasons, [], [], [], [], []);
}
