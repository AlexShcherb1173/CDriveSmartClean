using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Enumeration;

public sealed class StorageEntry
{
    public StorageEntry(
        VolumeIdentity volumeIdentity,
        StorageObjectIdentity? objectIdentity,
        string canonicalPath,
        StorageObjectKind objectKind,
        ReparseKind reparseKind)
        : this(volumeIdentity, objectIdentity, canonicalPath, objectKind, reparseKind,
            StorageMeasurement.Unavailable(reparseKind != ReparseKind.None
                ? StorageMeasurementScope.ReparseEntryMetadata
                : objectKind == StorageObjectKind.Directory
                    ? StorageMeasurementScope.DirectoryEntryMetadata
                    : StorageMeasurementScope.FileContent), StorageEntryAttributes.None)
    {
    }

    public StorageEntry(
        VolumeIdentity volumeIdentity,
        StorageObjectIdentity? objectIdentity,
        string canonicalPath,
        StorageObjectKind objectKind,
        ReparseKind reparseKind,
        StorageMeasurement measurement,
        StorageEntryAttributes attributes)
    {
        ArgumentNullException.ThrowIfNull(volumeIdentity);
        if (objectIdentity is not null && !objectIdentity.VolumeIdentity.Equals(volumeIdentity))
        {
            throw new ArgumentException("Object identity must belong to the entry volume.", nameof(objectIdentity));
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (objectKind == 0 || !Enum.IsDefined(objectKind))
        {
            throw new ArgumentOutOfRangeException(nameof(objectKind));
        }

        if (reparseKind == 0 || !Enum.IsDefined(reparseKind))
        {
            throw new ArgumentOutOfRangeException(nameof(reparseKind));
        }

        ArgumentNullException.ThrowIfNull(measurement);

        VolumeIdentity = volumeIdentity;
        ObjectIdentity = objectIdentity;
        CanonicalPath = canonicalPath;
        ObjectKind = objectKind;
        ReparseKind = reparseKind;
        Measurement = measurement;
        Attributes = attributes;
    }

    public VolumeIdentity VolumeIdentity { get; }

    /// <summary>Native object identity, or null when unavailable; never a path-based substitute.</summary>
    public StorageObjectIdentity? ObjectIdentity { get; }

    public string CanonicalPath { get; }

    public StorageObjectKind ObjectKind { get; }

    public ReparseKind ReparseKind { get; }

    public StorageMeasurement Measurement { get; }

    public StorageEntryAttributes Attributes { get; }

    public bool IsReparsePoint => ReparseKind != ReparseKind.None;
}
