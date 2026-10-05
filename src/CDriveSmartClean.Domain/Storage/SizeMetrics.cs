namespace CDriveSmartClean.Domain.Storage;

public sealed class SizeMetrics
{
    public SizeMetrics(long? logicalBytes, long? allocatedBytes, long? exclusiveAllocatedBytes)
    {
        Validate(logicalBytes, nameof(logicalBytes));
        Validate(allocatedBytes, nameof(allocatedBytes));
        Validate(exclusiveAllocatedBytes, nameof(exclusiveAllocatedBytes));

        if (exclusiveAllocatedBytes is not null && allocatedBytes is null)
        {
            throw new ArgumentException(
                "Exclusive allocated bytes require known allocated bytes.",
                nameof(exclusiveAllocatedBytes));
        }

        if (exclusiveAllocatedBytes > allocatedBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(exclusiveAllocatedBytes),
                "Exclusive allocated bytes cannot exceed allocated bytes.");
        }

        LogicalBytes = logicalBytes;
        AllocatedBytes = allocatedBytes;
        ExclusiveAllocatedBytes = exclusiveAllocatedBytes;
    }

    public long? LogicalBytes { get; }

    public long? AllocatedBytes { get; }

    public long? ExclusiveAllocatedBytes { get; }

    private static void Validate(long? value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName);
        }
    }
}
