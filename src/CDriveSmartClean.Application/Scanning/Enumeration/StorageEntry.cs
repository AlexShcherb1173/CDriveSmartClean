using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Enumeration;

public sealed class StorageEntry
{
    public StorageEntry(
        VolumeIdentity volumeIdentity,
        string canonicalPath,
        StorageObjectKind objectKind,
        ReparseKind reparseKind)
    {
        ArgumentNullException.ThrowIfNull(volumeIdentity);
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
        CanonicalPath = canonicalPath;
        ObjectKind = objectKind;
        ReparseKind = reparseKind;
    }

    public VolumeIdentity VolumeIdentity { get; }

    public string CanonicalPath { get; }

    public StorageObjectKind ObjectKind { get; }

    public ReparseKind ReparseKind { get; }

    public bool IsReparsePoint => ReparseKind != ReparseKind.None;
}
