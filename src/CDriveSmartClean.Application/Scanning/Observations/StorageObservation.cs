using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Observations;

/// <summary>A raw measurement with caller-supplied location metadata; it makes no traversal or reclaim decision.</summary>
public sealed class StorageObservation
{
    public StorageObservation(
        Guid scanSessionId,
        VolumeIdentity volumeIdentity,
        StorageObjectIdentity? objectIdentity,
        string canonicalPath,
        StorageObjectKind objectKind,
        long logicalBytes,
        long allocatedBytes,
        ReparseKind reparseKind,
        VolumeIdentity? reparseTargetVolumeIdentity)
    {
        if (scanSessionId == Guid.Empty)
        {
            throw new ArgumentException("Scan session ID cannot be empty.", nameof(scanSessionId));
        }

        ArgumentNullException.ThrowIfNull(volumeIdentity);
        if (objectIdentity is not null && !volumeIdentity.Equals(objectIdentity.VolumeIdentity))
        {
            throw new ArgumentException("Object identity must belong to the source volume.", nameof(objectIdentity));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (!Enum.IsDefined(objectKind))
        {
            throw new ArgumentOutOfRangeException(nameof(objectKind));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(logicalBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(allocatedBytes);
        if (!Enum.IsDefined(reparseKind))
        {
            throw new ArgumentOutOfRangeException(nameof(reparseKind));
        }

        if (reparseKind == ReparseKind.None && reparseTargetVolumeIdentity is not null)
        {
            throw new ArgumentException("A non-reparse observation cannot have a target volume.", nameof(reparseTargetVolumeIdentity));
        }

        ScanSessionId = scanSessionId;
        VolumeIdentity = volumeIdentity;
        ObjectIdentity = objectIdentity;
        CanonicalPath = canonicalPath;
        ObjectKind = objectKind;
        LogicalBytes = logicalBytes;
        AllocatedBytes = allocatedBytes;
        ReparseKind = reparseKind;
        ReparseTargetVolumeIdentity = reparseTargetVolumeIdentity;
    }

    public Guid ScanSessionId { get; }

    public VolumeIdentity VolumeIdentity { get; }

    /// <summary>Null when a stable native identity was unavailable; the path does not substitute for identity.</summary>
    public StorageObjectIdentity? ObjectIdentity { get; }

    public string CanonicalPath { get; }

    public StorageObjectKind ObjectKind { get; }

    public long LogicalBytes { get; }

    public long AllocatedBytes { get; }

    public ReparseKind ReparseKind { get; }

    public VolumeIdentity? ReparseTargetVolumeIdentity { get; }

    public bool IsReparsePoint => ReparseKind != ReparseKind.None;
}
