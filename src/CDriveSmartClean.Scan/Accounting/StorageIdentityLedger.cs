using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Scan.Accounting;

internal sealed class StorageIdentityLedger
{
    internal readonly record struct PathId(int Value);
    internal readonly record struct IdentityId(int Value);

    internal sealed class ResourceLimitException : Exception;

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

    private struct IdentityRecord(Evidence evidence)
    {
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
    private readonly Dictionary<string, PathId> paths = new(StringComparer.Ordinal);
    private readonly List<PathRecord> pathRecords = [];
    private readonly Dictionary<StorageObjectIdentity, IdentityId> identities = [];
    private readonly List<IdentityRecord> identityRecords = [];
    private readonly List<PathEdge> additionalIdentityPaths = [];
    private readonly List<IdentityEdge> additionalPathIdentities = [];
    private readonly StorageHierarchyAccumulator hierarchy;
    private long charged;

    internal AccountingReason Reasons { get; private set; }
    internal long ChargedState => charged;
    internal int PathCount => pathRecords.Count;
    internal int IdentityCount => identityRecords.Count;
    internal int ExceptionalIdentityPathEdgeCount => additionalIdentityPaths.Count;
    internal int ExceptionalPathIdentityEdgeCount => additionalPathIdentities.Count;

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
        if (next > options.AccountingStateBudget) throw new ResourceLimitException();
        charged = next;
    }

    internal void Add(StorageEntry entry, string relative)
    {
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
        if (pathRecords.Count >= options.MaximumDistinctPaths) throw new ResourceLimitException();
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
        if (identityRecords.Count >= options.MaximumIdentities) throw new ResourceLimitException();
        Charge(IdentityRecordCharge);
        identityId = new IdentityId(identityRecords.Count);
        identityRecords.Add(new IdentityRecord(evidence));
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

    internal (StorageHierarchyNode Root, AllocationGroup[] Groups) Finish(CancellationToken token)
    {
        var groups = new List<AllocationGroup>(identityRecords.Count);
        foreach ((StorageObjectIdentity identity, IdentityId identityId) in identities)
        {
            token.ThrowIfCancellationRequested();
            IdentityRecord group = identityRecords[identityId.Value];
            int lca = pathRecords[group.SinglePath.Value].Parent;
            for (int edge = group.AdditionalPathHead; edge >= 0; edge = additionalIdentityPaths[edge].Next)
            {
                token.ThrowIfCancellationRequested();
                PathId pathId = additionalIdentityPaths[edge].Path;
                lca = hierarchy.CommonAncestor(lca, pathRecords[pathId.Value].Parent, token);
            }

            bool conflict = (group.Reasons &
                (AccountingReason.ConflictingIdentityEvidence | AccountingReason.ConflictingPathEvidence)) != 0;
            hierarchy.AddValue(lca, conflict ? 13 : 12, 1);
            hierarchy.AddValue(lca, 14, group.PathCount - 1L);
            long? allocation = group.Reasons == AccountingReason.None ? group.Evidence.Measurement.ReportedAllocatedBytes : null;
            if (allocation is { } bytes) hierarchy.AddValue(lca, 3, bytes);
            Reasons |= group.Reasons;
            Charge(checked(256L + 32L * group.PathCount));
            groups.Add(new AllocationGroup(identity, Paths(group), allocation, group.Reasons, hierarchy.Path(lca)));
        }

        foreach (PathRecord path in pathRecords)
        {
            token.ThrowIfCancellationRequested();
            if (path.Conflict) continue;
            Evidence evidence = path.Evidence;
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
        return (hierarchy.Finish(token), groups.ToArray());
    }

    private IEnumerable<string> Paths(IdentityRecord identity)
    {
        yield return pathRecords[identity.SinglePath.Value].Path;
        for (int edge = identity.AdditionalPathHead; edge >= 0; edge = additionalIdentityPaths[edge].Next)
            yield return pathRecords[additionalIdentityPaths[edge].Path.Value].Path;
    }
}
