using System.Collections.ObjectModel;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Analysis;

public sealed class StorageAnalysisResult
{
    public StorageAnalysisResult(AnalysisQuality quality, AnalysisReason reasons,
        AccountingQuality upstreamAccountingQuality, AccountingReason upstreamAccountingReasons,
        IEnumerable<CategorySummary> categorySummaries,
        IEnumerable<StorageAnalysisCandidate> largestHierarchyCandidates,
        IEnumerable<StorageAnalysisCandidate> largestIdentityCandidates,
        IEnumerable<StorageAnalysisCandidate> largestFileCandidates,
        IEnumerable<StorageAnalysisCandidate> largestUnknownCandidates)
    {
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        if (!Enum.IsDefined(upstreamAccountingQuality)) throw new ArgumentOutOfRangeException(nameof(upstreamAccountingQuality));
        AnalysisReason knownAnalysisReasons = Enum.GetValues<AnalysisReason>()
            .Aggregate(AnalysisReason.None, (current, value) => current | value);
        if ((reasons & ~knownAnalysisReasons) != 0) throw new ArgumentOutOfRangeException(nameof(reasons));
        AccountingReason knownAccountingReasons = Enum.GetValues<AccountingReason>()
            .Aggregate(AccountingReason.None, (current, value) => current | value);
        if ((upstreamAccountingReasons & ~knownAccountingReasons) != 0)
            throw new ArgumentOutOfRangeException(nameof(upstreamAccountingReasons));
        ArgumentNullException.ThrowIfNull(categorySummaries);
        ArgumentNullException.ThrowIfNull(largestHierarchyCandidates);
        ArgumentNullException.ThrowIfNull(largestIdentityCandidates);
        ArgumentNullException.ThrowIfNull(largestFileCandidates);
        ArgumentNullException.ThrowIfNull(largestUnknownCandidates);
        CategorySummary[] summaries = categorySummaries.OrderBy(item => item.Category).ToArray();
        if (quality == AnalysisQuality.Unavailable)
        {
            if (summaries.Length != 0 || largestHierarchyCandidates.Any() || largestIdentityCandidates.Any() ||
                largestFileCandidates.Any() || largestUnknownCandidates.Any())
                throw new ArgumentException("Unavailable analysis cannot publish partial authoritative output.");
        }
        else
        {
            FindingCategory[] categories = Enum.GetValues<FindingCategory>();
            if (!summaries.Select(item => item.Category).SequenceEqual(categories))
                throw new ArgumentException("Exactly one summary per category is required.", nameof(categorySummaries));
        }
        Quality = quality;
        Reasons = reasons;
        UpstreamAccountingQuality = upstreamAccountingQuality;
        UpstreamAccountingReasons = upstreamAccountingReasons;
        CategorySummaries = Array.AsReadOnly(summaries);
        LargestHierarchyCandidates = Copy(largestHierarchyCandidates, nameof(largestHierarchyCandidates));
        LargestIdentityCandidates = Copy(largestIdentityCandidates, nameof(largestIdentityCandidates));
        LargestFileCandidates = Copy(largestFileCandidates, nameof(largestFileCandidates));
        LargestUnknownCandidates = Copy(largestUnknownCandidates, nameof(largestUnknownCandidates));
    }

    public AnalysisQuality Quality { get; }
    public AnalysisReason Reasons { get; }
    public AccountingQuality UpstreamAccountingQuality { get; }
    public AccountingReason UpstreamAccountingReasons { get; }
    public IReadOnlyList<CategorySummary> CategorySummaries { get; }
    public IReadOnlyList<StorageAnalysisCandidate> LargestHierarchyCandidates { get; }
    public IReadOnlyList<StorageAnalysisCandidate> LargestIdentityCandidates { get; }
    public IReadOnlyList<StorageAnalysisCandidate> LargestFileCandidates { get; }
    public IReadOnlyList<StorageAnalysisCandidate> LargestUnknownCandidates { get; }

    private static ReadOnlyCollection<StorageAnalysisCandidate> Copy(
        IEnumerable<StorageAnalysisCandidate> candidates, string name)
    {
        ArgumentNullException.ThrowIfNull(candidates, name);
        StorageAnalysisCandidate[] values = candidates.ToArray();
        if (values.Any(value => value is null)) throw new ArgumentException("Candidates cannot contain null.", name);
        Array.Sort(values, Compare);
        return Array.AsReadOnly(values);
    }

    private static int Compare(StorageAnalysisCandidate left, StorageAnalysisCandidate right)
    {
        int result = Nullable.Compare(right.SizeEvidence.ObservedAttributedAllocatedBytes,
            left.SizeEvidence.ObservedAttributedAllocatedBytes);
        if (result != 0) return result;
        result = left.Scope.CompareTo(right.Scope);
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(left.RelativePaths[0], right.RelativePaths[0]);
        if (result != 0) return result;
        result = Nullable.Compare(left.ObjectIdentity?.VolumeIdentity.Id, right.ObjectIdentity?.VolumeIdentity.Id);
        if (result != 0) return result;
        return Nullable.Compare(left.ObjectIdentity?.ObjectId, right.ObjectIdentity?.ObjectId);
    }
}
