namespace CDriveSmartClean.Domain.Storage;

public sealed class StorageAccountingSummary
{
    public StorageAccountingSummary(StorageAggregate? aggregate, AccountingReason reasons)
    {
        bool disabled = (reasons & (AccountingReason.ResourceLimit | AccountingReason.ArithmeticOverflow)) != 0;
        if (disabled != (aggregate is null)) throw new ArgumentException("Unavailable accounting requires null aggregates.");
        Aggregate = aggregate;
        Reasons = reasons;
        Quality = disabled ? AccountingQuality.Unavailable : reasons == AccountingReason.None ? AccountingQuality.Complete : AccountingQuality.Incomplete;
    }
    public StorageAggregate? Aggregate { get; }
    public long? DeduplicatedObservedAllocatedBytes => Aggregate?.InclusiveAttributedObservedAllocatedBytes;
    public long? UncertainMeasuredAllocatedBytes => Aggregate?.UncertainMeasuredAllocatedBytes;
    public long? UniqueIdentityCount => Aggregate?.UniqueIdentityCount;
    public long? ConflictedIdentityCount => Aggregate?.ConflictedIdentityCount;
    public long? AliasPathCount => Aggregate?.AliasPathCount;
    public AccountingQuality Quality { get; }
    public AccountingReason Reasons { get; }
}
