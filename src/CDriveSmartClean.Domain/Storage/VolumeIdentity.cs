namespace CDriveSmartClean.Domain.Storage;

/// <summary>An opaque volume token supplied by a platform adapter, independent of paths and scan sessions.</summary>
public sealed class VolumeIdentity : IEquatable<VolumeIdentity>
{
    public VolumeIdentity(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Volume identity cannot be empty.", nameof(id));
        }

        Id = id;
    }

    public Guid Id { get; }

    public bool Equals(VolumeIdentity? other) => other is not null && Id == other.Id;

    public override bool Equals(object? obj) => obj is VolumeIdentity other && Equals(other);

    public override int GetHashCode() => Id.GetHashCode();
}
