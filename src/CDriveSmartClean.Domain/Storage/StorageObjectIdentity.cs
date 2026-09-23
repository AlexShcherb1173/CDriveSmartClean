namespace CDriveSmartClean.Domain.Storage;

/// <summary>A native object token scoped to its volume; it does not establish exclusive allocation ownership.</summary>
public sealed class StorageObjectIdentity : IEquatable<StorageObjectIdentity>
{
    public StorageObjectIdentity(VolumeIdentity volumeIdentity, Guid objectId)
    {
        ArgumentNullException.ThrowIfNull(volumeIdentity);
        if (objectId == Guid.Empty)
        {
            throw new ArgumentException("Object identity cannot be empty.", nameof(objectId));
        }

        VolumeIdentity = volumeIdentity;
        ObjectId = objectId;
    }

    public VolumeIdentity VolumeIdentity { get; }

    public Guid ObjectId { get; }

    public bool Equals(StorageObjectIdentity? other) =>
        other is not null && VolumeIdentity.Equals(other.VolumeIdentity) && ObjectId == other.ObjectId;

    public override bool Equals(object? obj) => obj is StorageObjectIdentity other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(VolumeIdentity, ObjectId);
}
