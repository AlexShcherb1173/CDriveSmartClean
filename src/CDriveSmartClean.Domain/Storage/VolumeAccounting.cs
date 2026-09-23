namespace CDriveSmartClean.Domain.Storage;

public sealed class VolumeAccounting
{
    public VolumeAccounting(
        long capacityBytes,
        long usedBytes,
        long freeBytes,
        long accountedAllocatedBytes,
        long unknownAllocatedBytes,
        long filesystemReservedBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(capacityBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(usedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(freeBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(accountedAllocatedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(unknownAllocatedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(filesystemReservedBytes);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(usedBytes, capacityBytes);

        if (freeBytes != capacityBytes - usedBytes)
        {
            throw new ArgumentException(
                "Free bytes must equal capacity bytes minus used bytes.",
                nameof(freeBytes));
        }

        long remainingBytes = usedBytes;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            accountedAllocatedBytes,
            remainingBytes);
        remainingBytes -= accountedAllocatedBytes;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            unknownAllocatedBytes,
            remainingBytes);
        remainingBytes -= unknownAllocatedBytes;

        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            filesystemReservedBytes,
            remainingBytes);
        remainingBytes -= filesystemReservedBytes;

        CapacityBytes = capacityBytes;
        UsedBytes = usedBytes;
        FreeBytes = freeBytes;
        AccountedAllocatedBytes = accountedAllocatedBytes;
        UnknownAllocatedBytes = unknownAllocatedBytes;
        FilesystemReservedBytes = filesystemReservedBytes;
        UnattributedBytes = remainingBytes;

        CoveragePercent = usedBytes == 0
            ? 100m
            : ((decimal)(usedBytes - remainingBytes) / usedBytes) * 100m;
    }

    public long CapacityBytes { get; }

    public long UsedBytes { get; }

    public long FreeBytes { get; }

    public long AccountedAllocatedBytes { get; }

    public long UnknownAllocatedBytes { get; }

    public long FilesystemReservedBytes { get; }

    public long UnattributedBytes { get; }

    public decimal CoveragePercent { get; }
}
