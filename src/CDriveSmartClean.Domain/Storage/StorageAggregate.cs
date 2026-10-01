namespace CDriveSmartClean.Domain.Storage;

/// <summary>Measured path subtotals and LCA-attributed identity totals; parent and child inclusive totals overlap.</summary>
public sealed class StorageAggregate
{
    public StorageAggregate(long visibleLogicalMeasuredBytes = 0, long rawReportedAllocatedBytes = 0,
        long uncertainMeasuredAllocatedBytes = 0, long directAttributedObservedAllocatedBytes = 0,
        long inclusiveAttributedObservedAllocatedBytes = 0, long fileCount = 0, long directoryCount = 0,
        long reparseCount = 0, long availableMeasurementCount = 0, long unavailableMeasurementCount = 0,
        long notApplicableMeasurementCount = 0, long identityUnavailableCount = 0,
        long uniqueIdentityCount = 0, long conflictedIdentityCount = 0, long aliasPathCount = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(visibleLogicalMeasuredBytes);
        VisibleLogicalMeasuredBytes = visibleLogicalMeasuredBytes;
        ArgumentOutOfRangeException.ThrowIfNegative(rawReportedAllocatedBytes);
        RawReportedAllocatedBytes = rawReportedAllocatedBytes;
        ArgumentOutOfRangeException.ThrowIfNegative(uncertainMeasuredAllocatedBytes);
        UncertainMeasuredAllocatedBytes = uncertainMeasuredAllocatedBytes;
        ArgumentOutOfRangeException.ThrowIfNegative(directAttributedObservedAllocatedBytes);
        DirectAttributedObservedAllocatedBytes = directAttributedObservedAllocatedBytes;
        ArgumentOutOfRangeException.ThrowIfNegative(inclusiveAttributedObservedAllocatedBytes);
        InclusiveAttributedObservedAllocatedBytes = inclusiveAttributedObservedAllocatedBytes;
        ArgumentOutOfRangeException.ThrowIfNegative(fileCount);
        FileCount = fileCount;
        ArgumentOutOfRangeException.ThrowIfNegative(directoryCount);
        DirectoryCount = directoryCount;
        ArgumentOutOfRangeException.ThrowIfNegative(reparseCount);
        ReparseCount = reparseCount;
        ArgumentOutOfRangeException.ThrowIfNegative(availableMeasurementCount);
        AvailableMeasurementCount = availableMeasurementCount;
        ArgumentOutOfRangeException.ThrowIfNegative(unavailableMeasurementCount);
        UnavailableMeasurementCount = unavailableMeasurementCount;
        ArgumentOutOfRangeException.ThrowIfNegative(notApplicableMeasurementCount);
        NotApplicableMeasurementCount = notApplicableMeasurementCount;
        ArgumentOutOfRangeException.ThrowIfNegative(identityUnavailableCount);
        IdentityUnavailableCount = identityUnavailableCount;
        ArgumentOutOfRangeException.ThrowIfNegative(uniqueIdentityCount);
        UniqueIdentityCount = uniqueIdentityCount;
        ArgumentOutOfRangeException.ThrowIfNegative(conflictedIdentityCount);
        ConflictedIdentityCount = conflictedIdentityCount;
        ArgumentOutOfRangeException.ThrowIfNegative(aliasPathCount);
        AliasPathCount = aliasPathCount;
        if (directAttributedObservedAllocatedBytes > inclusiveAttributedObservedAllocatedBytes)
            throw new ArgumentException("Direct allocation exceeds inclusive allocation.");
    }
    public long VisibleLogicalMeasuredBytes { get; }
    public long RawReportedAllocatedBytes { get; }
    public long UncertainMeasuredAllocatedBytes { get; }
    public long DirectAttributedObservedAllocatedBytes { get; }
    public long InclusiveAttributedObservedAllocatedBytes { get; }
    public long FileCount { get; }
    public long DirectoryCount { get; }
    public long ReparseCount { get; }
    public long AvailableMeasurementCount { get; }
    public long UnavailableMeasurementCount { get; }
    public long NotApplicableMeasurementCount { get; }
    public long IdentityUnavailableCount { get; }
    public long UniqueIdentityCount { get; }
    public long ConflictedIdentityCount { get; }
    public long AliasPathCount { get; }
}
