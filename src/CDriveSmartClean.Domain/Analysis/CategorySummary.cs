using CDriveSmartClean.Domain.Findings;

namespace CDriveSmartClean.Domain.Analysis;

public sealed class CategorySummary
{
    public CategorySummary(FindingCategory category, long? deduplicatedObservedAllocatedBytes,
        long rawVisibleAllocatedBytes, long uncertainMeasuredAllocatedBytes, long pathCount,
        long identityGroupCount, AnalysisQuality quality, AnalysisReason reasons)
    {
        if (!Enum.IsDefined(category)) throw new ArgumentOutOfRangeException(nameof(category));
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        AnalysisReason knownReasons = Enum.GetValues<AnalysisReason>()
            .Aggregate(AnalysisReason.None, (current, value) => current | value);
        if ((reasons & ~knownReasons) != 0) throw new ArgumentOutOfRangeException(nameof(reasons));
        if (deduplicatedObservedAllocatedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(deduplicatedObservedAllocatedBytes));
        ArgumentOutOfRangeException.ThrowIfNegative(rawVisibleAllocatedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(uncertainMeasuredAllocatedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(pathCount);
        ArgumentOutOfRangeException.ThrowIfNegative(identityGroupCount);
        Category = category;
        DeduplicatedObservedAllocatedBytes = deduplicatedObservedAllocatedBytes;
        RawVisibleAllocatedBytes = rawVisibleAllocatedBytes;
        UncertainMeasuredAllocatedBytes = uncertainMeasuredAllocatedBytes;
        PathCount = pathCount;
        IdentityGroupCount = identityGroupCount;
        Quality = quality;
        Reasons = reasons;
    }

    public FindingCategory Category { get; }
    public long? DeduplicatedObservedAllocatedBytes { get; }
    public long RawVisibleAllocatedBytes { get; }
    public long UncertainMeasuredAllocatedBytes { get; }
    public long PathCount { get; }
    public long IdentityGroupCount { get; }
    public AnalysisQuality Quality { get; }
    public AnalysisReason Reasons { get; }
}
