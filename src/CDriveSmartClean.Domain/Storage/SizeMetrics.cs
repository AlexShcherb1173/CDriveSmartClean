namespace CDriveSmartClean.Domain.Storage;

public sealed class SizeMetrics
{
    public SizeMetrics(long logicalBytes, long allocatedBytes, long exclusiveAllocatedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(logicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(allocatedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(exclusiveAllocatedBytes);

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

    public long LogicalBytes { get; }

    public long AllocatedBytes { get; }

    public long ExclusiveAllocatedBytes { get; }
}
