namespace CDriveSmartClean.Domain.Storage;

public enum VolumeSpaceFailure
{
    None = 0, UnsupportedPlatform, AccessDenied, VolumeUnavailable, NativeFailure, InvalidNativeData, Overflow,
}

public sealed class VolumeSpaceSnapshot
{
    private VolumeSpaceSnapshot(VolumeIdentity volumeIdentity, DateTimeOffset acquiredAt, VolumeSpaceFailure failure,
        long? capacityBytes, long? freeBytes, long? callerCapacityBytes, long? callerFreeBytes,
        long? nativeUsedBytes, long? nativeReservedBytes, long? nativeVolumeReserveBytes, int? nativeError, int? extendedNativeError = null)
    {
        ArgumentNullException.ThrowIfNull(volumeIdentity);
        if (!Enum.IsDefined(failure)) throw new ArgumentOutOfRangeException(nameof(failure));
        VolumeIdentity = volumeIdentity;
        AcquiredAt = acquiredAt;
        Failure = failure;
        CapacityBytes = capacityBytes;
        FreeBytes = freeBytes;
        CallerCapacityBytes = callerCapacityBytes;
        CallerFreeBytes = callerFreeBytes;
        NativeUsedBytes = nativeUsedBytes;
        NativeReservedBytes = nativeReservedBytes;
        NativeVolumeReserveBytes = nativeVolumeReserveBytes;
        NativeError = nativeError;
        ExtendedNativeError = extendedNativeError;
    }
    public static VolumeSpaceSnapshot Available(VolumeIdentity identity, DateTimeOffset acquiredAt,
        long capacityBytes, long freeBytes, long callerCapacityBytes, long callerFreeBytes,
        long nativeUsedBytes, long nativeReservedBytes, long nativeVolumeReserveBytes)
    {
        foreach (long value in new[] { capacityBytes, freeBytes, callerCapacityBytes, callerFreeBytes, nativeUsedBytes, nativeReservedBytes, nativeVolumeReserveBytes })
            ArgumentOutOfRangeException.ThrowIfNegative(value);
        if (freeBytes > capacityBytes || callerFreeBytes > callerCapacityBytes || callerCapacityBytes > capacityBytes ||
            nativeUsedBytes > capacityBytes || nativeVolumeReserveBytes > nativeReservedBytes)
            throw new ArgumentException("Contradictory volume values.");
        return new(identity, acquiredAt, VolumeSpaceFailure.None, capacityBytes, freeBytes, callerCapacityBytes,
            callerFreeBytes, nativeUsedBytes, nativeReservedBytes, nativeVolumeReserveBytes, null);
    }
    public static VolumeSpaceSnapshot Unavailable(VolumeIdentity identity, DateTimeOffset acquiredAt, VolumeSpaceFailure failure, int? nativeError = null, int? extendedNativeError = null)
    {
        if (failure == VolumeSpaceFailure.None) throw new ArgumentException("Failure is required.", nameof(failure));
        return new(identity, acquiredAt, failure, null, null, null, null, null, null, null, nativeError, extendedNativeError);
    }
    public VolumeIdentity VolumeIdentity { get; }
    public DateTimeOffset AcquiredAt { get; }
    public VolumeSpaceFailure Failure { get; }
    public bool IsAvailable => Failure == VolumeSpaceFailure.None;
    public long? CapacityBytes { get; }
    public long? FreeBytes { get; }
    public long? UsedBytes => checked(CapacityBytes - FreeBytes);
    public long? CallerCapacityBytes { get; }
    public long? CallerFreeBytes { get; }
    public long? NativeUsedBytes { get; }
    public long? NativeReservedBytes { get; }
    public long? NativeVolumeReserveBytes { get; }
    public int? NativeError { get; }
    public int? ExtendedNativeError { get; }
}
