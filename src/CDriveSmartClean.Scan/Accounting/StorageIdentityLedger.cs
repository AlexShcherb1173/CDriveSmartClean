using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Scan.Accounting;

internal sealed class StorageIdentityLedger
{
    internal sealed class ResourceLimitException : Exception;
    private sealed record Evidence(StorageObjectIdentity? Identity, StorageMeasurement Measurement,
        StorageObjectKind Kind, ReparseKind Reparse, StorageEntryAttributes Attributes);
    private sealed class PathState(string path, int parent, int node, Evidence evidence)
    {
        internal readonly string Path = path;
        internal readonly int Parent = parent;
        internal readonly int Node = node;
        internal readonly Evidence Evidence = evidence;
        internal bool Conflict;
        internal readonly HashSet<StorageObjectIdentity> Identities = [];
    }
    private sealed class IdentityState(Evidence evidence)
    {
        internal readonly Evidence Evidence = evidence;
        internal readonly HashSet<string> Paths = new(StringComparer.Ordinal);
        internal AccountingReason Reasons;
    }

    private readonly StorageAccountingOptions options;
    private readonly Dictionary<string, PathState> paths = new(StringComparer.Ordinal);
    private readonly Dictionary<StorageObjectIdentity, IdentityState> identities = [];
    private readonly StorageHierarchyAccumulator hierarchy;
    private long charged;
    internal AccountingReason Reasons { get; private set; }

    internal StorageIdentityLedger(StorageAccountingOptions options)
    {
        this.options = options;
        hierarchy = new StorageHierarchyAccumulator(Charge, options.MaximumDirectories);
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
        if (!paths.TryGetValue(relative, out PathState? path))
        {
            if (paths.Count >= options.MaximumDistinctPaths) throw new ResourceLimitException();
            Charge(checked(768L + relative.Length * 8L));
            int separator = relative.LastIndexOf('\\');
            int parent = hierarchy.Directory(separator < 0 ? "" : relative[..separator]);
            // Every location can also become an implicit parent, independent of observation order.
            int node = entry.ObjectKind == StorageObjectKind.Directory ? hierarchy.Directory(relative) : parent;
            path = new PathState(relative, parent, node, evidence);
            paths.Add(relative, path);
        }
        else if (path.Evidence != evidence)
        {
            path.Conflict = true;
            Reasons |= AccountingReason.ConflictingPathEvidence;
            foreach (StorageObjectIdentity previous in path.Identities)
                identities[previous].Reasons |= AccountingReason.ConflictingPathEvidence;
            // Ensure directory shape is independent of which conflicting kind arrived first.
            if (entry.ObjectKind == StorageObjectKind.Directory) hierarchy.Directory(relative);
        }

        if (entry.ObjectIdentity is not { } identity) return;
        if (!identities.TryGetValue(identity, out IdentityState? group))
        {
            if (identities.Count >= options.MaximumIdentities) throw new ResourceLimitException();
            Charge(768);
            group = new IdentityState(evidence);
            identities.Add(identity, group);
        }
        else if (group.Evidence != evidence) group.Reasons |= AccountingReason.ConflictingIdentityEvidence;
        if (!group.Paths.Contains(relative))
        {
            Charge(128);
            group.Paths.Add(relative);
            path.Identities.Add(identity);
        }
        if (path.Conflict) group.Reasons |= AccountingReason.ConflictingPathEvidence;
        group.Reasons |= Eligibility(evidence);
    }

    private static AccountingReason Eligibility(Evidence evidence)
    {
        AccountingReason reasons = AccountingReason.None;
        if (evidence.Identity is null) reasons |= AccountingReason.IdentityUnavailable;
        if (evidence.Measurement.Availability != StorageMeasurementAvailability.Available) reasons |= AccountingReason.MeasurementUnavailable;
        if (evidence.Kind != StorageObjectKind.File || evidence.Reparse != ReparseKind.None ||
            evidence.Measurement.Scope != StorageMeasurementScope.FileContent ||
            (evidence.Attributes & ~(StorageEntryAttributes.Sparse | StorageEntryAttributes.Compressed)) != 0)
            reasons |= AccountingReason.UnsupportedAllocationEvidence;
        return reasons;
    }

    internal (StorageHierarchyNode Root, AllocationGroup[] Groups) Finish(CancellationToken token)
    {
        int[][] ancestors = hierarchy.Ancestors(token);
        var groups = new List<AllocationGroup>();
        foreach (var pair in identities)
        {
            token.ThrowIfCancellationRequested();
            IdentityState group = pair.Value;
            int lca = -1;
            foreach (string name in group.Paths)
                lca = lca < 0 ? paths[name].Parent : hierarchy.CommonAncestor(lca, paths[name].Parent, ancestors);
            long[] values = hierarchy.Nodes[lca].Values;
            bool conflict = (group.Reasons & (AccountingReason.ConflictingIdentityEvidence | AccountingReason.ConflictingPathEvidence)) != 0;
            values[conflict ? 13 : 12] = checked(values[conflict ? 13 : 12] + 1);
            values[14] = checked(values[14] + group.Paths.Count - 1L);
            long? allocation = group.Reasons == AccountingReason.None ? group.Evidence.Measurement.ReportedAllocatedBytes : null;
            if (allocation is { } bytes) values[3] = checked(values[3] + bytes);
            Reasons |= group.Reasons;
            Charge(checked(256L + 32L * group.Paths.Count));
            groups.Add(new AllocationGroup(pair.Key, group.Paths, allocation, group.Reasons, hierarchy.Nodes[lca].Path));
        }
        foreach (PathState path in paths.Values)
        {
            token.ThrowIfCancellationRequested();
            if (path.Conflict) continue;
            Evidence evidence = path.Evidence;
            AccountingReason reasons = Eligibility(evidence);
            if (evidence.Identity is { } identity) reasons |= identities[identity].Reasons;
            Reasons |= reasons;
            long[] v = hierarchy.Nodes[path.Node].Values;
            if (evidence.Kind == StorageObjectKind.File) v[5] = checked(v[5] + 1);
            if (evidence.Kind == StorageObjectKind.Directory) v[6] = checked(v[6] + 1);
            if (evidence.Reparse != ReparseKind.None) v[7] = checked(v[7] + 1);
            if (evidence.Identity is null) v[11] = checked(v[11] + 1);
            int availability = evidence.Measurement.Availability == StorageMeasurementAvailability.Available ? 8 :
                evidence.Measurement.Availability == StorageMeasurementAvailability.Unavailable ? 9 : 10;
            v[availability] = checked(v[availability] + 1);
            if (availability == 8)
            {
                v[0] = checked(v[0] + evidence.Measurement.LogicalBytes!.Value);
                v[1] = checked(v[1] + evidence.Measurement.ReportedAllocatedBytes!.Value);
                if (reasons != AccountingReason.None) v[2] = checked(v[2] + evidence.Measurement.ReportedAllocatedBytes.Value);
            }
        }
        return (hierarchy.Finish(token), groups.ToArray());
    }
}
