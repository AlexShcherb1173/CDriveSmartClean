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

        VolumeIdentity = volumeIdentity;
        ObjectIdentity = objectIdentity;
        CanonicalPath = canonicalPath;
        ObjectKind = objectKind;
        ReparseKind = reparseKind;
    }

    public VolumeIdentity VolumeIdentity { get; }

    /// <summary>Native object identity, or null when unavailable; never a path-based substitute.</summary>
    public StorageObjectIdentity? ObjectIdentity { get; }

    public string CanonicalPath { get; }

    public StorageObjectKind ObjectKind { get; }

    public ReparseKind ReparseKind { get; }

    public bool IsReparsePoint => ReparseKind != ReparseKind.None;
}
