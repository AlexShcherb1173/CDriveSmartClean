using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Analysis;

internal static class CompactStorageAnalysisBuilder
{
    internal const long CompactPathStateCharge = 24;
    internal const long CandidateDescriptorCharge = 64;

    private sealed class Bucket
    {
        internal long Deduplicated;
        internal long Raw;
        internal long Uncertain;
        internal long Paths;
        internal long Groups;
    }

    private readonly record struct CandidateDescriptor(AnalysisCandidateScope Scope, FindingCategory Category,
        long Bytes, int IdentityId, StorageHierarchyNode? HierarchyNode);

    internal static long CalculateCharge(int pathCount, int candidateLimit) => checked(
        pathCount * CompactPathStateCharge + candidateLimit * 4L * CandidateDescriptorCharge);

    internal static StorageAnalysisResult Build(StorageAnalysisRequest request, StorageAccountingResult accounting,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        ValidateVolume(request, accounting, token);
        AnalysisReason reasons = MapAccounting(accounting);
        CompactAccountingSnapshot? snapshot = accounting.CompactSnapshot;
        if (snapshot is null || accounting.Summary.Quality == AccountingQuality.Unavailable || accounting.Root is null)
            return Unavailable(accounting, reasons |
                (snapshot is null && accounting.Summary.Quality != AccountingQuality.Unavailable
                    ? AnalysisReason.AccountingMismatch
                    : AnalysisReason.None));
        if (snapshot.PathCount > request.Options.MaximumPathStates)
            return Unavailable(accounting, reasons | AnalysisReason.ResourceLimit,
                CountDiagnostic(ResourceLimitDimension.MaximumPathStates,
                    request.Options.MaximumPathStates));
        if (snapshot.IdentityCount > request.Options.MaximumIdentityStates)
            return Unavailable(accounting, reasons | AnalysisReason.ResourceLimit,
                CountDiagnostic(ResourceLimitDimension.MaximumIdentityStates,
                    request.Options.MaximumIdentityStates));

        var resourceGuard = new AnalysisResourceGuard(request.Options.AnalysisStateBudget);
        try
        {
            long charge;
            try
            {
                charge = CalculateCharge(snapshot.PathCount, request.Options.CandidateLimit);
            }
            catch (OverflowException)
            {
                return Unavailable(accounting, reasons | AnalysisReason.ArithmeticOverflow);
            }
            if (!resourceGuard.TryCharge(charge, out bool overflow, out long attempted))
                return Unavailable(accounting, reasons |
                    (overflow ? AnalysisReason.ArithmeticOverflow : AnalysisReason.ResourceLimit),
                    overflow
                        ? null
                        : new ResourceLimitDiagnostic(ResourceLimitStage.Analysis,
                            ResourceLimitDimension.AnalysisStateBudget,
                            request.Options.AnalysisStateBudget, attempted));
            return BuildAvailable(request, accounting, snapshot, reasons, token);
        }
        catch (OverflowException)
        {
            return Unavailable(accounting, reasons | AnalysisReason.ArithmeticOverflow);
        }
        finally
        {
            resourceGuard.Clear();
        }
    }

    private static StorageAnalysisResult BuildAvailable(StorageAnalysisRequest request,
        StorageAccountingResult accounting, CompactAccountingSnapshot snapshot, AnalysisReason reasons,
        CancellationToken token)
    {
        var classifier = new DeterministicClassificationEngine(request.ClassificationContext);
        var classifications = new DeterministicClassificationEngine.CompactResult[snapshot.PathCount];
        var buckets = Enum.GetValues<FindingCategory>().Select(_ => new Bucket()).ToArray();
        if (!request.ClassificationContext.IsComplete)
            reasons |= AnalysisReason.ClassificationContextIncomplete;

        string root = request.SystemVolume.RootPath.TrimEnd('\\') + "\\";
        for (int pathId = 0; pathId < snapshot.PathCount; pathId++)
        {
            token.ThrowIfCancellationRequested();
            CompactAccountingSnapshot.PathFact path = snapshot.GetPath(pathId);
            DeterministicClassificationEngine.CompactResult classification =
                classifier.ClassifyCompact(root + path.RelativePath, path.Attributes);
            classifications[pathId] = classification;
            reasons |= classification.Reasons;
            if (path.Conflict)
            {
                buckets[(int)FindingCategory.Unknown].Paths = checked(
                    buckets[(int)FindingCategory.Unknown].Paths + 1);
                continue;
            }

            Bucket bucket = buckets[(int)classification.Category];
            bucket.Paths = checked(bucket.Paths + 1);
            if (path.Measurement.Availability != StorageMeasurementAvailability.Available) continue;
            long bytes = path.Measurement.ReportedAllocatedBytes!.Value;
            bucket.Raw = checked(bucket.Raw + bytes);
            AccountingReason eligibility = Eligibility(path);
            bool identityConflicted = false;
            ReadOnlySpan<int> identityIds = snapshot.GetPathIdentities(pathId);
            if (identityIds.Length > 1) return Mismatch(accounting, reasons);
            if (identityIds.Length == 1)
            {
                CompactAccountingSnapshot.IdentityFact identity = snapshot.GetIdentity(identityIds[0]);
                eligibility |= identity.Reasons;
                identityConflicted = IsConflicted(identity.Reasons);
            }
            if (eligibility != AccountingReason.None)
            {
                FindingCategory uncertainCategory = identityConflicted
                    ? FindingCategory.Unknown
                    : classification.Category;
                buckets[(int)uncertainCategory].Uncertain = checked(
                    buckets[(int)uncertainCategory].Uncertain + bytes);
            }
        }

        var identityCandidates = new DescriptorSet(request.Options.CandidateLimit, snapshot);
        var fileCandidates = new DescriptorSet(request.Options.CandidateLimit, snapshot);
        var hierarchyCandidates = new DescriptorSet(request.Options.CandidateLimit, snapshot);
        var unknownCandidates = new DescriptorSet(request.Options.CandidateLimit, snapshot);

        for (int identityId = 0; identityId < snapshot.IdentityCount; identityId++)
        {
            token.ThrowIfCancellationRequested();
            CompactAccountingSnapshot.IdentityFact identity = snapshot.GetIdentity(identityId);
            ReadOnlySpan<int> pathIds = snapshot.GetIdentityPaths(identityId);
            if (pathIds.Length != identity.PathCount || pathIds.Length == 0) return Mismatch(accounting, reasons);
            bool conflicted = IsConflicted(identity.Reasons);
            FindingCategory category = conflicted
                ? FindingCategory.Unknown
                : classifications[pathIds[0]].Category;
            bool crossCategory = false;
            for (int index = 0; index < pathIds.Length; index++)
            {
                int pathId = pathIds[index];
                CompactAccountingSnapshot.PathFact path = snapshot.GetPath(pathId);
                if (path.Conflict && !conflicted) return Mismatch(accounting, reasons);
                if (classifications[pathId].Category != classifications[pathIds[0]].Category)
                    crossCategory = true;
            }
            if (crossCategory)
            {
                category = FindingCategory.Unknown;
                reasons |= AnalysisReason.CrossCategoryIdentityConflict;
            }
            Bucket bucket = buckets[(int)category];
            bucket.Groups = checked(bucket.Groups + 1);
            if (identity.Reasons != AccountingReason.None ||
                identity.Measurement.Availability != StorageMeasurementAvailability.Available)
                continue;
            long bytes = identity.Measurement.ReportedAllocatedBytes!.Value;
            bucket.Deduplicated = checked(bucket.Deduplicated + bytes);
            var group = new CandidateDescriptor(AnalysisCandidateScope.IdentityGroup, category,
                bytes, identityId, null);
            identityCandidates.Add(group);
            if (category == FindingCategory.Unknown) unknownCandidates.Add(group);
            if (pathIds.Length == 1 && snapshot.GetPath(pathIds[0]).ObjectKind == StorageObjectKind.File)
            {
                var file = group with { Scope = AnalysisCandidateScope.File };
                fileCandidates.Add(file);
                if (category == FindingCategory.Unknown) unknownCandidates.Add(file);
            }
        }

        StorageHierarchyNode rootNode = accounting.Root!;
        AddHierarchy(rootNode, request, classifier, hierarchyCandidates, unknownCandidates, token);
        if (Sum(buckets, bucket => bucket.Raw) != rootNode.Aggregate.RawReportedAllocatedBytes ||
            Sum(buckets, bucket => bucket.Uncertain) != rootNode.Aggregate.UncertainMeasuredAllocatedBytes ||
            Sum(buckets, bucket => bucket.Deduplicated) != accounting.Summary.DeduplicatedObservedAllocatedBytes)
            return Mismatch(accounting, reasons);

        CandidateDescriptor[] hierarchyDescriptors = hierarchyCandidates.ToArray();
        CandidateDescriptor[] identityDescriptors = identityCandidates.ToArray();
        CandidateDescriptor[] fileDescriptors = fileCandidates.ToArray();
        CandidateDescriptor[] unknownDescriptors = unknownCandidates.ToArray();
        SelectedHierarchyPathIndex selectedHierarchyPaths = IndexSelectedHierarchyPaths(
            snapshot, hierarchyDescriptors, unknownDescriptors, token);

        StorageAnalysisCandidate[] hierarchy = Materialize(hierarchyDescriptors, request, snapshot,
            classifications, classifier, selectedHierarchyPaths, token);
        StorageAnalysisCandidate[] identities = Materialize(identityDescriptors, request, snapshot,
            classifications, classifier, selectedHierarchyPaths, token);
        StorageAnalysisCandidate[] files = Materialize(fileDescriptors, request, snapshot,
            classifications, classifier, selectedHierarchyPaths, token);
        StorageAnalysisCandidate[] unknown = Materialize(unknownDescriptors, request, snapshot,
            classifications, classifier, selectedHierarchyPaths, token);

        AnalysisQuality quality = reasons == AnalysisReason.None ? AnalysisQuality.Complete : AnalysisQuality.Incomplete;
        CategorySummary[] summaries = Enum.GetValues<FindingCategory>().Select(category =>
        {
            token.ThrowIfCancellationRequested();
            Bucket bucket = buckets[(int)category];
            return new CategorySummary(category, bucket.Deduplicated, bucket.Raw, bucket.Uncertain,
                bucket.Paths, bucket.Groups, quality, reasons);
        }).ToArray();
        token.ThrowIfCancellationRequested();
        return new StorageAnalysisResult(quality, reasons, accounting.Summary.Quality, accounting.Summary.Reasons,
            summaries, hierarchy, identities, files, unknown);
    }

    private static void AddHierarchy(StorageHierarchyNode root, StorageAnalysisRequest request,
        DeterministicClassificationEngine classifier, DescriptorSet hierarchy, DescriptorSet unknown,
        CancellationToken token)
    {
        string volumeRoot = request.SystemVolume.RootPath.TrimEnd('\\') + "\\";
        var pending = new Stack<StorageHierarchyNode>();
        for (int index = root.Children.Count - 1; index >= 0; index--) pending.Push(root.Children[index]);
        while (pending.TryPop(out StorageHierarchyNode? node))
        {
            token.ThrowIfCancellationRequested();
            FindingCategory category = classifier.ClassifyCompact(
                volumeRoot + node.RelativePath, StorageEntryAttributes.None).Category;
            var descriptor = new CandidateDescriptor(AnalysisCandidateScope.HierarchyNode, category,
                node.Aggregate.InclusiveAttributedObservedAllocatedBytes, -1, node);
            hierarchy.Add(descriptor);
            if (category == FindingCategory.Unknown) unknown.Add(descriptor);
            for (int index = node.Children.Count - 1; index >= 0; index--) pending.Push(node.Children[index]);
        }
    }

    private static SelectedHierarchyPathIndex IndexSelectedHierarchyPaths(CompactAccountingSnapshot snapshot,
        CandidateDescriptor[] hierarchy, CandidateDescriptor[] unknown, CancellationToken token)
    {
        var selected = new List<string>();
        foreach (CandidateDescriptor descriptor in hierarchy.Concat(unknown))
            if (descriptor.HierarchyNode is { } node && !selected.Contains(node.RelativePath, StringComparer.Ordinal))
                selected.Add(node.RelativePath);
        selected.Sort(StringComparer.Ordinal);
        var result = new SelectedHierarchyPathIndex(selected.ToArray());
        for (int pathId = 0; pathId < snapshot.PathCount; pathId++)
        {
            token.ThrowIfCancellationRequested();
            result.Observe(snapshot.GetPath(pathId).RelativePath, pathId);
        }
        return result;
    }

    private static StorageAnalysisCandidate[] Materialize(CandidateDescriptor[] descriptors,
        StorageAnalysisRequest request, CompactAccountingSnapshot snapshot,
        DeterministicClassificationEngine.CompactResult[] classifications,
        DeterministicClassificationEngine classifier, SelectedHierarchyPathIndex hierarchyPaths,
        CancellationToken token)
    {
        var result = new StorageAnalysisCandidate[descriptors.Length];
        for (int index = 0; index < descriptors.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            CandidateDescriptor descriptor = descriptors[index];
            result[index] = descriptor.HierarchyNode is null
                ? MaterializeIdentity(descriptor, snapshot, classifications)
                : MaterializeHierarchy(descriptor, request, snapshot, classifications, classifier, hierarchyPaths);
        }
        return result;
    }

    private static StorageAnalysisCandidate MaterializeIdentity(CandidateDescriptor descriptor,
        CompactAccountingSnapshot snapshot, DeterministicClassificationEngine.CompactResult[] classifications)
    {
        CompactAccountingSnapshot.IdentityFact identity = snapshot.GetIdentity(descriptor.IdentityId);
        ReadOnlySpan<int> pathIds = snapshot.GetIdentityPaths(descriptor.IdentityId);
        string[] paths = new string[pathIds.Length];
        uint facets = 0;
        DeterministicClassificationEngine.EvidenceCode evidenceCodes = 0;
        Confidence confidence = Confidence.Verified;
        for (int index = 0; index < pathIds.Length; index++)
        {
            int pathId = pathIds[index];
            paths[index] = snapshot.GetPath(pathId).RelativePath;
            DeterministicClassificationEngine.CompactResult classification = classifications[pathId];
            facets |= classification.FacetBits;
            evidenceCodes |= classification.EvidenceCodes;
            if (classification.Confidence < confidence) confidence = classification.Confidence;
        }
        var compact = new DeterministicClassificationEngine.CompactResult(descriptor.Category, facets,
            descriptor.Category == FindingCategory.Unknown ? Confidence.Unknown : confidence,
            evidenceCodes, AnalysisReason.None);
        DeterministicClassificationEngine.Result rich = DeterministicClassificationEngine.Materialize(compact);
        var richFacets = rich.Facets.ToList();
        var evidence = rich.Evidence.ToList();
        if (pathIds.Length > 1)
        {
            richFacets.Add(FindingFacet.HardLinked);
            evidence.Add(new Evidence("facet.hardlink_observed_alias",
                "Multiple visible paths share the same observed native identity.", Confidence.Verified));
        }
        if (descriptor.Category == FindingCategory.Unknown &&
            pathIds.ToArray().Select(pathId => classifications[pathId].Category).Distinct().Count() > 1)
            evidence.Add(new Evidence("cross_category_identity_conflict",
                "Observed aliases have different deterministic primary categories; allocation is counted once as Unknown.",
                Confidence.Verified));
        long bytes = identity.Measurement.ReportedAllocatedBytes!.Value;
        long? logical = identity.Measurement.Availability == StorageMeasurementAvailability.Available
            ? identity.Measurement.LogicalBytes
            : null;
        return new StorageAnalysisCandidate(descriptor.Scope, descriptor.Category, richFacets,
            compact.Confidence, evidence, new AnalysisSizeEvidence(logical, bytes, bytes, 0), paths,
            identity.Identity, 1, 0);
    }

    private static StorageAnalysisCandidate MaterializeHierarchy(CandidateDescriptor descriptor,
        StorageAnalysisRequest request, CompactAccountingSnapshot snapshot,
        DeterministicClassificationEngine.CompactResult[] classifications,
        DeterministicClassificationEngine classifier, SelectedHierarchyPathIndex hierarchyPaths)
    {
        StorageHierarchyNode node = descriptor.HierarchyNode!;
        DeterministicClassificationEngine.CompactResult compact;
        FindingFacet[] facets;
        if (hierarchyPaths.TryGet(node.RelativePath, out int pathId) &&
            !snapshot.GetPath(pathId).Conflict)
        {
            compact = classifications[pathId];
            facets = snapshot.GetPath(pathId).ObjectKind == StorageObjectKind.Directory
                ? DeterministicClassificationEngine.Materialize(compact).Facets
                : [];
        }
        else
        {
            compact = classifier.ClassifyCompact(request.SystemVolume.RootPath.TrimEnd('\\') + "\\" +
                node.RelativePath, StorageEntryAttributes.None);
            facets = [];
        }
        DeterministicClassificationEngine.Result rich = DeterministicClassificationEngine.Materialize(compact);
        StorageAggregate aggregate = node.Aggregate;
        return new StorageAnalysisCandidate(AnalysisCandidateScope.HierarchyNode, compact.Category,
            facets, compact.Confidence, rich.Evidence,
            new AnalysisSizeEvidence(aggregate.VisibleLogicalMeasuredBytes, aggregate.RawReportedAllocatedBytes,
                aggregate.InclusiveAttributedObservedAllocatedBytes, aggregate.UncertainMeasuredAllocatedBytes),
            [node.RelativePath], null, aggregate.FileCount, aggregate.DirectoryCount);
    }

    private static AccountingReason Eligibility(CompactAccountingSnapshot.PathFact fact)
    {
        AccountingReason result = fact.IdentityCount == 0 ? AccountingReason.IdentityUnavailable : AccountingReason.None;
        if (fact.Measurement.Availability != StorageMeasurementAvailability.Available)
            result |= AccountingReason.MeasurementUnavailable;
        if (fact.ObjectKind != StorageObjectKind.File || fact.ReparseKind != ReparseKind.None ||
            fact.Measurement.Scope != StorageMeasurementScope.FileContent ||
            (fact.Attributes & ~(StorageEntryAttributes.Sparse | StorageEntryAttributes.Compressed)) != 0)
            result |= AccountingReason.UnsupportedAllocationEvidence;
        return result;
    }

    private static bool IsConflicted(AccountingReason reasons) => (reasons &
        (AccountingReason.ConflictingIdentityEvidence | AccountingReason.ConflictingPathEvidence)) != 0;

    private static void ValidateVolume(StorageAnalysisRequest request, StorageAccountingResult accounting,
        CancellationToken token)
    {
        VolumeIdentity expected = request.SystemVolume.VolumeIdentity;
        if (!accounting.Reconciliation.StartSnapshot.VolumeIdentity.Equals(expected) ||
            !accounting.Reconciliation.EndSnapshot.VolumeIdentity.Equals(expected))
            throw new InvalidOperationException("Accounting result volume identity contradiction.");
        if (accounting.CompactSnapshot is not { } snapshot) return;
        for (int identityId = 0; identityId < snapshot.IdentityCount; identityId++)
        {
            token.ThrowIfCancellationRequested();
            if (!snapshot.GetIdentity(identityId).Identity.VolumeIdentity.Equals(expected))
                throw new InvalidOperationException("Accounting result volume identity contradiction.");
        }
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
        if ((source & AccountingReason.ConflictingIdentityEvidence) != 0)
            result |= AnalysisReason.ConflictingIdentityEvidence;
        if ((source & AccountingReason.ConflictingPathEvidence) != 0)
            result |= AnalysisReason.ConflictingPathEvidence;
        if ((source & AccountingReason.UnsupportedAllocationEvidence) != 0)
            result |= AnalysisReason.UnsupportedAllocationEvidence;
        return result;
    }

    private static long Sum(IEnumerable<Bucket> buckets, Func<Bucket, long> selector)
    {
        long result = 0;
        foreach (Bucket bucket in buckets) result = checked(result + selector(bucket));
        return result;
    }

    private static StorageAnalysisResult Mismatch(StorageAccountingResult accounting, AnalysisReason reasons) =>
        Unavailable(accounting, reasons | AnalysisReason.AccountingMismatch);

    private static ResourceLimitDiagnostic CountDiagnostic(ResourceLimitDimension dimension, int limit) =>
        new(ResourceLimitStage.Analysis, dimension, limit, limit + 1L);

    private static StorageAnalysisResult Unavailable(StorageAccountingResult accounting, AnalysisReason reasons,
        ResourceLimitDiagnostic? resourceLimitDiagnostic = null) =>
        new(AnalysisQuality.Unavailable, reasons, accounting.Summary.Quality, accounting.Summary.Reasons,
            [], [], [], [], [], resourceLimitDiagnostic);

    private sealed class SelectedHierarchyPathIndex
    {
        private readonly string[] paths;
        private readonly int[] pathIds;

        internal SelectedHierarchyPathIndex(string[] paths)
        {
            this.paths = paths;
            pathIds = Enumerable.Repeat(-1, paths.Length).ToArray();
        }

        internal void Observe(string path, int pathId)
        {
            int index = Array.BinarySearch(paths, path, StringComparer.Ordinal);
            if (index >= 0) pathIds[index] = pathId;
        }

        internal bool TryGet(string path, out int pathId)
        {
            int index = Array.BinarySearch(paths, path, StringComparer.Ordinal);
            pathId = index >= 0 ? pathIds[index] : -1;
            return pathId >= 0;
        }
    }

    private sealed class DescriptorSet
    {
        private readonly int limit;
        private readonly SortedSet<CandidateDescriptor> values;

        internal DescriptorSet(int limit, CompactAccountingSnapshot snapshot)
        {
            this.limit = limit;
            values = new SortedSet<CandidateDescriptor>(new DescriptorComparer(snapshot));
        }

        internal void Add(CandidateDescriptor value)
        {
            values.Add(value);
            if (values.Count > limit) values.Remove(values.Max);
        }

        internal CandidateDescriptor[] ToArray() => values.ToArray();
    }

    private sealed class DescriptorComparer(CompactAccountingSnapshot snapshot) : IComparer<CandidateDescriptor>
    {
        public int Compare(CandidateDescriptor left, CandidateDescriptor right)
        {
            int result = right.Bytes.CompareTo(left.Bytes);
            if (result != 0) return result;
            result = left.Scope.CompareTo(right.Scope);
            if (result != 0) return result;
            result = ComparePaths(left, right);
            if (result != 0) return result;
            StorageObjectIdentity? leftIdentity = left.IdentityId >= 0 ? snapshot.GetIdentity(left.IdentityId).Identity : null;
            StorageObjectIdentity? rightIdentity = right.IdentityId >= 0 ? snapshot.GetIdentity(right.IdentityId).Identity : null;
            result = Nullable.Compare(leftIdentity?.VolumeIdentity.Id, rightIdentity?.VolumeIdentity.Id);
            if (result != 0) return result;
            return Nullable.Compare(leftIdentity?.ObjectId, rightIdentity?.ObjectId);
        }

        private int ComparePaths(CandidateDescriptor left, CandidateDescriptor right)
        {
            int leftCount = PathCount(left);
            int rightCount = PathCount(right);
            int count = Math.Min(leftCount, rightCount);
            for (int index = 0; index < count; index++)
            {
                int result = StringComparer.Ordinal.Compare(PathAt(left, index), PathAt(right, index));
                if (result != 0) return result;
            }
            return leftCount.CompareTo(rightCount);
        }

        private int PathCount(CandidateDescriptor value) => value.HierarchyNode is not null
            ? 1
            : snapshot.GetIdentity(value.IdentityId).PathCount;

        private string PathAt(CandidateDescriptor value, int ordinal)
        {
            if (value.HierarchyNode is { } node) return node.RelativePath;
            ReadOnlySpan<int> ids = snapshot.GetIdentityPaths(value.IdentityId);
            string? previous = null;
            string? selected = null;
            for (int position = 0; position <= ordinal; position++)
            {
                selected = null;
                foreach (int pathId in ids)
                {
                    string path = snapshot.GetPath(pathId).RelativePath;
                    if (previous is not null && StringComparer.Ordinal.Compare(path, previous) <= 0) continue;
                    if (selected is null || StringComparer.Ordinal.Compare(path, selected) < 0) selected = path;
                }
                previous = selected ?? throw new InvalidOperationException("Invalid compact path ordering.");
            }
            return selected!;
        }
    }
}
