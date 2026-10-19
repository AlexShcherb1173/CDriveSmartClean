using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Scan.Accounting;

internal sealed class StorageIdentityLedger
{
    internal readonly record struct PathId(int Value);
    internal readonly record struct IdentityId(int Value);

    internal sealed class ResourceLimitException(
        ResourceLimitDimension dimension, long configuredLimit, long observedOrAttemptedValue) : Exception
    {
        internal ResourceLimitDiagnostic Diagnostic { get; } = new(
            ResourceLimitStage.Accounting, dimension, configuredLimit, observedOrAttemptedValue);
    }

    private readonly record struct Evidence(StorageObjectIdentity? Identity, StorageMeasurement Measurement,
        StorageObjectKind Kind, ReparseKind Reparse, StorageEntryAttributes Attributes);

    private struct PathRecord(string path, int parent, int node, Evidence evidence)
    {
        internal readonly string Path = path;
        internal readonly int Parent = parent;
        internal readonly int Node = node;
        internal readonly Evidence Evidence = evidence;
        internal IdentityId SingleIdentity;
        internal int AdditionalIdentityHead = -1;
        internal int IdentityCount;
        internal bool Conflict;
    }

    private struct IdentityRecord(StorageObjectIdentity identity, Evidence evidence)
    {
        internal readonly StorageObjectIdentity Identity = identity;
        internal readonly Evidence Evidence = evidence;
        internal PathId SinglePath;
        internal int AdditionalPathHead = -1;
        internal int PathCount;
        internal AccountingReason Reasons;
    }

    private readonly record struct PathEdge(PathId Path, int Next);
    private readonly record struct IdentityEdge(IdentityId Identity, int Next);

    private const long PathRecordBaseCharge = 128;
    private const long IdentityRecordCharge = 128;
    private const long PathCharacterCharge = 2;
    private const long ExceptionalEdgeCharge = 16;
    private const long LegacyPathRecordBaseCharge = 768;
    private const long LegacyIdentityRecordCharge = 768;
    private const long LegacyPathCharacterCharge = 8;
    private const long LegacyAssociationCharge = 128;

    private readonly StorageAccountingOptions options;
    private Dictionary<string, PathId> paths = new(StringComparer.Ordinal);
    private List<PathRecord> pathRecords = [];
    private Dictionary<StorageObjectIdentity, IdentityId> identities = [];
    private List<IdentityRecord> identityRecords = [];
    private List<PathEdge> additionalIdentityPaths = [];
    private List<IdentityEdge> additionalPathIdentities = [];
    private StorageHierarchyAccumulator hierarchy;
    private long charged;
    private bool sealedState;

    internal AccountingReason Reasons { get; private set; }
    internal long ChargedState => charged;
    internal int PathCount => pathRecords.Count;
    internal int IdentityCount => identityRecords.Count;
    internal int ExceptionalIdentityPathEdgeCount => additionalIdentityPaths.Count;
    internal int ExceptionalPathIdentityEdgeCount => additionalPathIdentities.Count;
    internal bool TraversalStateReleased => sealedState && paths.Count == 0 && pathRecords.Count == 0 &&
        identities.Count == 0 && identityRecords.Count == 0 && additionalIdentityPaths.Count == 0 &&
        additionalPathIdentities.Count == 0 && hierarchy is null;

    internal StorageIdentityLedger(StorageAccountingOptions options)
    {
        this.options = options;
        hierarchy = new StorageHierarchyAccumulator(Charge, options.MaximumDirectories);
    }

    internal static long LegacyCommonCaseCharge(int pathLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pathLength);
        return checked(LegacyPathRecordBaseCharge + pathLength * LegacyPathCharacterCharge +
            LegacyIdentityRecordCharge + LegacyAssociationCharge);
    }

    internal static long CompactCommonCaseCharge(int pathLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pathLength);
        return checked(PathRecordBaseCharge + pathLength * PathCharacterCharge + IdentityRecordCharge);
    }

    private void Charge(long amount)
    {
        long next = checked(charged + amount);
        if (next > options.AccountingStateBudget)
            throw new ResourceLimitException(ResourceLimitDimension.AccountingStateBudget,
                options.AccountingStateBudget, next);
        charged = next;
    }

    internal void Add(StorageEntry entry, string relative)
    {
        if (sealedState) throw new InvalidOperationException("The accounting ledger is sealed.");
        var evidence = new Evidence(entry.ObjectIdentity, entry.Measurement, entry.ObjectKind, entry.ReparseKind, entry.Attributes);
        PathId pathId = GetOrAddPath(relative, evidence, entry.ObjectKind);
        PathRecord path = pathRecords[pathId.Value];

        if (path.Evidence != evidence)
        {
            path.Conflict = true;
            pathRecords[pathId.Value] = path;
            Reasons |= AccountingReason.ConflictingPathEvidence;
            MarkPathIdentitiesConflicted(path);
            // Ensure directory shape is independent of which conflicting kind arrived first.
            if (entry.ObjectKind == StorageObjectKind.Directory) hierarchy.Directory(relative);
        }

        if (entry.ObjectIdentity is not { } identity) return;
        IdentityId identityId = GetOrAddIdentity(identity, evidence);
        IdentityRecord group = identityRecords[identityId.Value];
        if (group.Evidence != evidence)
        {
            group.Reasons |= AccountingReason.ConflictingIdentityEvidence;
            identityRecords[identityId.Value] = group;
        }

        Associate(pathId, identityId);
        group = identityRecords[identityId.Value];
        if (pathRecords[pathId.Value].Conflict) group.Reasons |= AccountingReason.ConflictingPathEvidence;
        group.Reasons |= Eligibility(evidence);
        identityRecords[identityId.Value] = group;
    }

    private PathId GetOrAddPath(string relative, Evidence evidence, StorageObjectKind kind)
    {
        if (paths.TryGetValue(relative, out PathId pathId)) return pathId;
        if (pathRecords.Count >= options.MaximumDistinctPaths)
            throw new ResourceLimitException(ResourceLimitDimension.MaximumDistinctPaths,
                options.MaximumDistinctPaths, pathRecords.Count + 1L);
        Charge(checked(PathRecordBaseCharge + relative.Length * PathCharacterCharge));
        int separator = relative.LastIndexOf('\\');
        int parent = hierarchy.Directory(separator < 0 ? "" : relative[..separator]);
        int node = kind == StorageObjectKind.Directory ? hierarchy.Directory(relative) : parent;
        pathId = new PathId(pathRecords.Count);
        pathRecords.Add(new PathRecord(relative, parent, node, evidence));
        paths.Add(relative, pathId);
        return pathId;
    }

    private IdentityId GetOrAddIdentity(StorageObjectIdentity identity, Evidence evidence)
    {
        if (identities.TryGetValue(identity, out IdentityId identityId)) return identityId;
        if (identityRecords.Count >= options.MaximumIdentities)
            throw new ResourceLimitException(ResourceLimitDimension.MaximumIdentities,
                options.MaximumIdentities, identityRecords.Count + 1L);
        Charge(IdentityRecordCharge);
        identityId = new IdentityId(identityRecords.Count);
        identityRecords.Add(new IdentityRecord(identity, evidence));
        identities.Add(identity, identityId);
        return identityId;
    }

    private void Associate(PathId pathId, IdentityId identityId)
    {
        PathRecord path = pathRecords[pathId.Value];
        if (ContainsIdentity(path, identityId)) return;

        if (path.IdentityCount == 0) path.SingleIdentity = identityId;
        else
        {
            Charge(ExceptionalEdgeCharge);
            int previous = path.AdditionalIdentityHead;
            path.AdditionalIdentityHead = additionalPathIdentities.Count;
            additionalPathIdentities.Add(new IdentityEdge(identityId, previous));
        }
        path.IdentityCount++;
        pathRecords[pathId.Value] = path;

        IdentityRecord identity = identityRecords[identityId.Value];
        if (identity.PathCount == 0) identity.SinglePath = pathId;
        else
        {
            Charge(ExceptionalEdgeCharge);
            int previous = identity.AdditionalPathHead;
            identity.AdditionalPathHead = additionalIdentityPaths.Count;
            additionalIdentityPaths.Add(new PathEdge(pathId, previous));
        }
        identity.PathCount++;
        identityRecords[identityId.Value] = identity;
    }

    private bool ContainsIdentity(PathRecord path, IdentityId identityId)
    {
        if (path.IdentityCount == 0) return false;
        if (path.SingleIdentity == identityId) return true;
        for (int edge = path.AdditionalIdentityHead; edge >= 0; edge = additionalPathIdentities[edge].Next)
            if (additionalPathIdentities[edge].Identity == identityId) return true;
        return false;
    }

    private void MarkPathIdentitiesConflicted(PathRecord path)
    {
        if (path.IdentityCount == 0) return;
        MarkIdentityConflicted(path.SingleIdentity);
        for (int edge = path.AdditionalIdentityHead; edge >= 0; edge = additionalPathIdentities[edge].Next)
            MarkIdentityConflicted(additionalPathIdentities[edge].Identity);
    }

    private void MarkIdentityConflicted(IdentityId identityId)
    {
        IdentityRecord group = identityRecords[identityId.Value];
        group.Reasons |= AccountingReason.ConflictingPathEvidence;
        identityRecords[identityId.Value] = group;
    }

    private static AccountingReason Eligibility(Evidence evidence)
    {
        AccountingReason reasons = AccountingReason.None;
        if (evidence.Identity is null) reasons |= AccountingReason.IdentityUnavailable;
        if (evidence.Measurement.Availability != StorageMeasurementAvailability.Available)
            reasons |= AccountingReason.MeasurementUnavailable;
        if (evidence.Kind != StorageObjectKind.File || evidence.Reparse != ReparseKind.None ||
            evidence.Measurement.Scope != StorageMeasurementScope.FileContent ||
            (evidence.Attributes & ~(StorageEntryAttributes.Sparse | StorageEntryAttributes.Compressed)) != 0)
            reasons |= AccountingReason.UnsupportedAllocationEvidence;
        return reasons;
    }

    internal (StorageHierarchyNode Root, CompactAccountingSnapshot Snapshot) Finish(CancellationToken token)
    {
        if (sealedState) throw new InvalidOperationException("The accounting ledger is sealed.");
        token.ThrowIfCancellationRequested();
        int associationCount = 0;
        foreach (IdentityRecord identity in identityRecords)
            associationCount = checked(associationCount + identity.PathCount);
        Charge(CompactAccountingSnapshot.SealCharge(pathRecords.Count, identityRecords.Count, associationCount));
        var pathFacts = new CompactAccountingSnapshot.PathFact[pathRecords.Count];
        var identityFacts = new CompactAccountingSnapshot.IdentityFact[identityRecords.Count];
        var identityPaths = new int[associationCount];
        var pathIdentities = new int[associationCount];

        int identityPathOffset = 0;
        for (int identityIndex = 0; identityIndex < identityRecords.Count; identityIndex++)
        {
            token.ThrowIfCancellationRequested();
            IdentityRecord group = identityRecords[identityIndex];
            if (group.PathCount <= 0) throw new InvalidOperationException("Identity has no associated path.");
            int lca = pathRecords[group.SinglePath.Value].Parent;
            for (int edge = group.AdditionalPathHead; edge >= 0; edge = additionalIdentityPaths[edge].Next)
            {
                token.ThrowIfCancellationRequested();
                PathId pathId = additionalIdentityPaths[edge].Path;
                lca = hierarchy.CommonAncestor(lca, pathRecords[pathId.Value].Parent, token);
            }
            if (!hierarchy.IsValidNode(lca)) throw new InvalidOperationException("Invalid attribution node.");

            bool conflict = (group.Reasons &
                (AccountingReason.ConflictingIdentityEvidence | AccountingReason.ConflictingPathEvidence)) != 0;
            hierarchy.AddValue(lca, conflict ? 13 : 12, 1);
            hierarchy.AddValue(lca, 14, group.PathCount - 1L);
            long? allocation = group.Reasons == AccountingReason.None ? group.Evidence.Measurement.ReportedAllocatedBytes : null;
            if (allocation is { } bytes) hierarchy.AddValue(lca, 3, bytes);
            Reasons |= group.Reasons;
            int start = identityPathOffset;
            CopyIdentityPaths(group, identityPaths, ref identityPathOffset);
            identityFacts[identityIndex] = new CompactAccountingSnapshot.IdentityFact(group.Identity,
                group.Evidence.Measurement, group.Evidence.Kind, group.Evidence.Reparse, group.Evidence.Attributes,
                group.Reasons, hierarchy.Path(lca), start, group.PathCount);
        }

        int pathIdentityOffset = 0;
        for (int pathIndex = 0; pathIndex < pathRecords.Count; pathIndex++)
        {
            token.ThrowIfCancellationRequested();
            PathRecord path = pathRecords[pathIndex];
            Evidence evidence = path.Evidence;
            if (!hierarchy.IsValidNode(path.Parent) || !hierarchy.IsValidNode(path.Node))
                throw new InvalidOperationException("Invalid path hierarchy node.");
            if (!path.Conflict)
            {
                AccountingReason reasons = Eligibility(evidence);
                if (path.IdentityCount != 0) reasons |= identityRecords[path.SingleIdentity.Value].Reasons;
                Reasons |= reasons;
                if (evidence.Kind == StorageObjectKind.File) hierarchy.AddValue(path.Node, 5, 1);
                if (evidence.Kind == StorageObjectKind.Directory) hierarchy.AddValue(path.Node, 6, 1);
                if (evidence.Reparse != ReparseKind.None) hierarchy.AddValue(path.Node, 7, 1);
                if (evidence.Identity is null) hierarchy.AddValue(path.Node, 11, 1);
                int availability = evidence.Measurement.Availability == StorageMeasurementAvailability.Available ? 8 :
                    evidence.Measurement.Availability == StorageMeasurementAvailability.Unavailable ? 9 : 10;
                hierarchy.AddValue(path.Node, availability, 1);
                if (availability == 8)
                {
                    hierarchy.AddValue(path.Node, 0, evidence.Measurement.LogicalBytes!.Value);
                    hierarchy.AddValue(path.Node, 1, evidence.Measurement.ReportedAllocatedBytes!.Value);
                    if (reasons != AccountingReason.None)
                        hierarchy.AddValue(path.Node, 2, evidence.Measurement.ReportedAllocatedBytes.Value);
                }
            }
            int start = pathIdentityOffset;
            CopyPathIdentities(path, pathIdentities, ref pathIdentityOffset);
            pathFacts[pathIndex] = new CompactAccountingSnapshot.PathFact(path.Path, evidence.Measurement,
                evidence.Kind, evidence.Reparse, evidence.Attributes, path.Conflict, start, path.IdentityCount);
        }
        if (identityPathOffset != associationCount || pathIdentityOffset != associationCount)
            throw new InvalidOperationException("Association count contradiction.");

        var snapshot = new CompactAccountingSnapshot(pathFacts, identityFacts, identityPaths, pathIdentities, token);
        StorageHierarchyNode root = hierarchy.Finish(token);
        ReleaseTraversalState();
        return (root, snapshot);
    }

    private void CopyIdentityPaths(IdentityRecord identity, int[] destination, ref int offset)
    {
        if ((uint)identity.SinglePath.Value >= (uint)pathRecords.Count)
            throw new InvalidOperationException("Invalid primary path association.");
        destination[offset++] = identity.SinglePath.Value;
        int remaining = identity.PathCount - 1;
        int edge = identity.AdditionalPathHead;
        while (remaining-- > 0)
        {
            if ((uint)edge >= (uint)additionalIdentityPaths.Count)
                throw new InvalidOperationException("Invalid identity/path edge.");
            PathEdge value = additionalIdentityPaths[edge];
            if ((uint)value.Path.Value >= (uint)pathRecords.Count)
                throw new InvalidOperationException("Invalid identity/path target.");
            destination[offset++] = value.Path.Value;
            edge = value.Next;
        }
        if (edge != -1) throw new InvalidOperationException("Identity path count contradiction.");
    }

    private void CopyPathIdentities(PathRecord path, int[] destination, ref int offset)
    {
        if (path.IdentityCount == 0)
        {
            if (path.AdditionalIdentityHead != -1)
                throw new InvalidOperationException("Identity edge exists without an association.");
            return;
        }
        if ((uint)path.SingleIdentity.Value >= (uint)identityRecords.Count)
            throw new InvalidOperationException("Invalid primary identity association.");
        destination[offset++] = path.SingleIdentity.Value;
        int remaining = path.IdentityCount - 1;
        int edge = path.AdditionalIdentityHead;
        while (remaining-- > 0)
        {
            if ((uint)edge >= (uint)additionalPathIdentities.Count)
                throw new InvalidOperationException("Invalid path/identity edge.");
            IdentityEdge value = additionalPathIdentities[edge];
            if ((uint)value.Identity.Value >= (uint)identityRecords.Count)
                throw new InvalidOperationException("Invalid path/identity target.");
            destination[offset++] = value.Identity.Value;
            edge = value.Next;
        }
        if (edge != -1) throw new InvalidOperationException("Path identity count contradiction.");
    }

    private void ReleaseTraversalState()
    {
        paths = new Dictionary<string, PathId>(StringComparer.Ordinal);
        pathRecords = [];
        identities = [];
        identityRecords = [];
        additionalIdentityPaths = [];
        additionalPathIdentities = [];
        hierarchy = null!;
        sealedState = true;
    }
}
