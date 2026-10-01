namespace CDriveSmartClean.Domain.Storage;

public sealed class VolumeReconciliation
{
    public VolumeReconciliation(VolumeSpaceSnapshot startSnapshot, VolumeSpaceSnapshot endSnapshot,
        StorageAccountingSummary summary, bool traversalCompleted)
    {
        ArgumentNullException.ThrowIfNull(startSnapshot);
        ArgumentNullException.ThrowIfNull(endSnapshot);
        ArgumentNullException.ThrowIfNull(summary);
        if (!startSnapshot.VolumeIdentity.Equals(endSnapshot.VolumeIdentity)) throw new ArgumentException("Volume mismatch.");
        StartSnapshot = startSnapshot;
        EndSnapshot = endSnapshot;
        DeduplicatedObservedAllocatedBytes = summary.DeduplicatedObservedAllocatedBytes;
        UncertainMeasuredAllocatedBytes = summary.UncertainMeasuredAllocatedBytes;
        Reasons = summary.Reasons;
        if (!traversalCompleted) Reasons |= AccountingReason.TraversalCoverageGap;
        if (!startSnapshot.IsAvailable || !endSnapshot.IsAvailable) Reasons |= AccountingReason.VolumeSnapshotUnavailable;
        ObservedChangeBytes = checked(endSnapshot.UsedBytes - startSnapshot.UsedBytes);
        SignedResidualBytes = checked(endSnapshot.UsedBytes - DeduplicatedObservedAllocatedBytes);
        if (ObservedChangeBytes is not null and not 0) Reasons |= AccountingReason.VolumeDrift;
        Quality = summary.Quality;
        if (!startSnapshot.IsAvailable || !endSnapshot.IsAvailable || summary.Quality == AccountingQuality.Unavailable)
            Quality = AccountingQuality.Unavailable;
        else if (ObservedChangeBytes != 0 || SignedResidualBytes < 0)
            Quality = AccountingQuality.Inconsistent;
        else if (Reasons != AccountingReason.None)
            Quality = AccountingQuality.Incomplete;
        if (traversalCompleted && Quality is not (AccountingQuality.Unavailable or AccountingQuality.Inconsistent) &&
            DeduplicatedObservedAllocatedBytes is { } allocated && endSnapshot.UsedBytes is { } used)
        {
            ObservedCoveragePercent = used == 0 ? Reasons == AccountingReason.None ? 100m : null : 100m * allocated / used;
        }
    }
    public VolumeSpaceSnapshot StartSnapshot { get; }
    public VolumeSpaceSnapshot EndSnapshot { get; }
    public long? DeduplicatedObservedAllocatedBytes { get; }
    public long? UncertainMeasuredAllocatedBytes { get; }
    public long? ObservedChangeBytes { get; }
    public long? SignedResidualBytes { get; }
    public decimal? ObservedCoveragePercent { get; }
    public AccountingQuality Quality { get; }
    public AccountingReason Reasons { get; }
}
