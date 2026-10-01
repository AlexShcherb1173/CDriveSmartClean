using CDriveSmartClean.Domain.Analysis;

namespace CDriveSmartClean.Analysis;

internal sealed class BoundedCandidateSet
{
    private readonly int limit;
    private readonly SortedSet<StorageAnalysisCandidate> candidates = new(new CandidateComparer());

    internal BoundedCandidateSet(int limit) => this.limit = limit;

    internal void Add(StorageAnalysisCandidate candidate)
    {
        candidates.Add(candidate);
        if (candidates.Count > limit) candidates.Remove(candidates.Max!);
    }

    internal StorageAnalysisCandidate[] ToArray() => candidates.ToArray();

    private sealed class CandidateComparer : IComparer<StorageAnalysisCandidate>
    {
        public int Compare(StorageAnalysisCandidate? left, StorageAnalysisCandidate? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return 1;
            if (right is null) return -1;
            int result = Nullable.Compare(right.SizeEvidence.ObservedAttributedAllocatedBytes,
                left.SizeEvidence.ObservedAttributedAllocatedBytes);
            if (result != 0) return result;
            result = left.Scope.CompareTo(right.Scope);
            if (result != 0) return result;
            int count = Math.Min(left.RelativePaths.Count, right.RelativePaths.Count);
            for (int index = 0; index < count; index++)
            {
                result = StringComparer.Ordinal.Compare(left.RelativePaths[index], right.RelativePaths[index]);
                if (result != 0) return result;
            }
            result = left.RelativePaths.Count.CompareTo(right.RelativePaths.Count);
            if (result != 0) return result;
            result = Nullable.Compare(left.ObjectIdentity?.VolumeIdentity.Id, right.ObjectIdentity?.VolumeIdentity.Id);
            if (result != 0) return result;
            return Nullable.Compare(left.ObjectIdentity?.ObjectId, right.ObjectIdentity?.ObjectId);
        }
    }
}
