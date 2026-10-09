using System.Collections;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Accounting;

internal sealed class CompactAccountingSnapshot
{
    internal readonly record struct PathFact(string RelativePath, StorageMeasurement Measurement,
        StorageObjectKind ObjectKind, ReparseKind ReparseKind, StorageEntryAttributes Attributes,
        bool Conflict, int IdentityOffset, int IdentityCount);

    internal readonly record struct IdentityFact(StorageObjectIdentity Identity, StorageMeasurement Measurement,
        StorageObjectKind ObjectKind, ReparseKind ReparseKind, StorageEntryAttributes Attributes,
        AccountingReason Reasons, string AttributionPath, int PathOffset, int PathCount);

    internal const long ArrayOverheadCharge = 64;
    internal const long PathFactCharge = 64;
    internal const long IdentityFactCharge = 96;
    internal const long AssociationIdCharge = 4;
    internal const long OffsetCharge = 4;
    internal const long OrderingIdCharge = 4;
    internal const long OrderingBufferCharge = 4;

    private readonly PathFact[] paths;
    private readonly IdentityFact[] identities;
    private readonly int[] identityPaths;
    private readonly int[] pathIdentities;
    private readonly int[] identityOrder;

    internal CompactAccountingSnapshot(PathFact[] paths, IdentityFact[] identities, int[] identityPaths,
        int[] pathIdentities, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(identityPaths);
        ArgumentNullException.ThrowIfNull(pathIdentities);
        this.paths = paths;
        this.identities = identities;
        this.identityPaths = identityPaths;
        this.pathIdentities = pathIdentities;
        Validate(token);
        identityOrder = CreateOrderingIndex(token);
    }

    internal int PathCount => paths.Length;
    internal int IdentityCount => identities.Length;
    internal PathFact GetPath(int pathId) => paths[pathId];
    internal IdentityFact GetIdentity(int identityId) => identities[identityId];
    internal ReadOnlySpan<int> GetIdentityPaths(int identityId)
    {
        IdentityFact identity = identities[identityId];
        return identityPaths.AsSpan(identity.PathOffset, identity.PathCount);
    }
    internal ReadOnlySpan<int> GetPathIdentities(int pathId)
    {
        PathFact path = paths[pathId];
        return pathIdentities.AsSpan(path.IdentityOffset, path.IdentityCount);
    }
    internal bool TryFindIdentity(StorageObjectIdentity identity, out int identityId)
    {
        ArgumentNullException.ThrowIfNull(identity);
        int low = 0;
        int high = identityOrder.Length - 1;
        while (low <= high)
        {
            int middle = low + (high - low) / 2;
            int candidateId = identityOrder[middle];
            int comparison = CompareIdentity(identities[candidateId].Identity, identity);
            if (comparison == 0)
            {
                identityId = candidateId;
                return true;
            }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        identityId = -1;
        return false;
    }
    internal CompactAllocationGroupList CreateAllocationGroups() => new(this);

    internal static long SealCharge(int pathCount, int identityCount, int associationCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pathCount);
        ArgumentOutOfRangeException.ThrowIfNegative(identityCount);
        ArgumentOutOfRangeException.ThrowIfNegative(associationCount);
        return checked(6L * ArrayOverheadCharge + pathCount * (PathFactCharge + OffsetCharge) +
            identityCount * (IdentityFactCharge + OffsetCharge + OrderingIdCharge + OrderingBufferCharge) +
            associationCount * 2L * AssociationIdCharge);
    }

    private void Validate(CancellationToken token)
    {
        int expectedIdentityPathOffset = 0;
        for (int identityId = 0; identityId < identities.Length; identityId++)
        {
            token.ThrowIfCancellationRequested();
            IdentityFact identity = identities[identityId];
            if (identity.Identity is null || identity.Measurement is null || identity.AttributionPath is null ||
                identity.PathCount <= 0 || identity.PathOffset != expectedIdentityPathOffset ||
                identity.PathOffset > identityPaths.Length - identity.PathCount)
                throw new InvalidOperationException("Invalid compact identity state.");
            expectedIdentityPathOffset = checked(expectedIdentityPathOffset + identity.PathCount);
        }
        if (expectedIdentityPathOffset != identityPaths.Length)
            throw new InvalidOperationException("Compact identity/path edge count contradiction.");

        int expectedPathIdentityOffset = 0;
        for (int pathId = 0; pathId < paths.Length; pathId++)
        {
            token.ThrowIfCancellationRequested();
            PathFact path = paths[pathId];
            if (string.IsNullOrWhiteSpace(path.RelativePath) || path.Measurement is null || path.IdentityCount < 0 ||
                path.IdentityOffset != expectedPathIdentityOffset ||
                path.IdentityOffset > pathIdentities.Length - path.IdentityCount)
                throw new InvalidOperationException("Invalid compact path state.");
            expectedPathIdentityOffset = checked(expectedPathIdentityOffset + path.IdentityCount);
        }
        if (expectedPathIdentityOffset != pathIdentities.Length)
            throw new InvalidOperationException("Compact path/identity edge count contradiction.");

        for (int identityId = 0; identityId < identities.Length; identityId++)
        {
            token.ThrowIfCancellationRequested();
            IdentityFact identity = identities[identityId];
            for (int index = identity.PathOffset; index < identity.PathOffset + identity.PathCount; index++)
            {
                int pathId = identityPaths[index];
                if ((uint)pathId >= (uint)paths.Length || HasEarlierValue(identityPaths, identity.PathOffset, index, pathId) ||
                    !ContainsIdentity(paths[pathId], identityId))
                    throw new InvalidOperationException("Compact identity/path association contradiction.");
            }
        }
        for (int pathId = 0; pathId < paths.Length; pathId++)
        {
            token.ThrowIfCancellationRequested();
            PathFact path = paths[pathId];
            for (int index = path.IdentityOffset; index < path.IdentityOffset + path.IdentityCount; index++)
            {
                int identityId = pathIdentities[index];
                if ((uint)identityId >= (uint)identities.Length ||
                    HasEarlierValue(pathIdentities, path.IdentityOffset, index, identityId) ||
                    !ContainsPath(identities[identityId], pathId))
                    throw new InvalidOperationException("Compact path/identity association contradiction.");
            }
        }
    }

    private static bool HasEarlierValue(int[] values, int start, int end, int value)
    {
        for (int index = start; index < end; index++)
            if (values[index] == value) return true;
        return false;
    }

    private bool ContainsIdentity(PathFact path, int identityId)
    {
        for (int index = path.IdentityOffset; index < path.IdentityOffset + path.IdentityCount; index++)
            if (pathIdentities[index] == identityId) return true;
        return false;
    }

    private bool ContainsPath(IdentityFact identity, int pathId)
    {
        for (int index = identity.PathOffset; index < identity.PathOffset + identity.PathCount; index++)
            if (identityPaths[index] == pathId) return true;
        return false;
    }

    private int[] CreateOrderingIndex(CancellationToken token)
    {
        var order = new int[identities.Length];
        for (int index = 0; index < order.Length; index++) order[index] = index;
        if (order.Length < 2) return order;
        var buffer = new int[order.Length];
        for (int width = 1; width < order.Length; width = checked(width * 2))
        {
            token.ThrowIfCancellationRequested();
            for (int left = 0; left < order.Length; left += checked(width * 2))
            {
                token.ThrowIfCancellationRequested();
                int middle = Math.Min(left + width, order.Length);
                int right = Math.Min(left + width * 2, order.Length);
                Merge(order, buffer, left, middle, right);
            }
            Array.Copy(buffer, order, order.Length);
            if (width > order.Length / 2) break;
        }
        return order;
    }

    private void Merge(int[] order, int[] buffer, int left, int middle, int right)
    {
        int first = left;
        int second = middle;
        for (int output = left; output < right; output++)
        {
            if (first < middle && (second >= right || Compare(order[first], order[second]) <= 0))
                buffer[output] = order[first++];
            else
                buffer[output] = order[second++];
        }
    }

    private int Compare(int leftIdentityId, int rightIdentityId)
    {
        return CompareIdentity(identities[leftIdentityId].Identity, identities[rightIdentityId].Identity);
    }

    private static int CompareIdentity(StorageObjectIdentity left, StorageObjectIdentity right)
    {
        int volume = left.VolumeIdentity.Id.CompareTo(right.VolumeIdentity.Id);
        return volume != 0 ? volume : left.ObjectId.CompareTo(right.ObjectId);
    }

    internal sealed class CompactAllocationGroupList(CompactAccountingSnapshot snapshot) : IReadOnlyList<AllocationGroup>
    {
        private int materializedCount;
        public int Count => snapshot.identityOrder.Length;
        internal int MaterializedCount => Volatile.Read(ref materializedCount);

        public AllocationGroup this[int index]
        {
            get
            {
                ArgumentOutOfRangeException.ThrowIfNegative(index);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);
                IdentityFact identity = snapshot.identities[snapshot.identityOrder[index]];
                string[] projectedPaths = new string[identity.PathCount];
                for (int path = 0; path < projectedPaths.Length; path++)
                    projectedPaths[path] = snapshot.paths[snapshot.identityPaths[identity.PathOffset + path]].RelativePath;
                long? allocation = identity.Reasons == AccountingReason.None
                    ? identity.Measurement.ReportedAllocatedBytes
                    : null;
                var group = new AllocationGroup(identity.Identity, projectedPaths, allocation,
                    identity.Reasons, identity.AttributionPath);
                Interlocked.Increment(ref materializedCount);
                return group;
            }
        }

        public IEnumerator<AllocationGroup> GetEnumerator()
        {
            for (int index = 0; index < Count; index++) yield return this[index];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
