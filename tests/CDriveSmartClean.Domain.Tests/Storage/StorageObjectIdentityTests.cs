using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Storage;

public sealed class StorageObjectIdentityTests
{
    [Fact]
    public void ValidIdentityAccepted()
    {
        Assert.NotNull(new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid()));
    }

    [Fact]
    public void NullVolumeRejected()
    {
        Assert.Throws<ArgumentNullException>("volumeIdentity", () => new StorageObjectIdentity(null!, Guid.NewGuid()));
    }

    [Fact]
    public void EmptyObjectIdRejected()
    {
        Assert.Throws<ArgumentException>("objectId", () => new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.Empty));
    }

    [Fact]
    public void PropertiesRetained()
    {
        var volume = new VolumeIdentity(Guid.NewGuid());
        Guid id = Guid.NewGuid();
        var identity = new StorageObjectIdentity(volume, id);
        Assert.Same(volume, identity.VolumeIdentity);
        Assert.Equal(id, identity.ObjectId);
    }

    [Fact]
    public void EquivalentVolumeAndObjectIdAreEqual()
    {
        Guid volumeId = Guid.NewGuid();
        Guid objectId = Guid.NewGuid();
        var first = new StorageObjectIdentity(new VolumeIdentity(volumeId), objectId);
        var second = new StorageObjectIdentity(new VolumeIdentity(volumeId), objectId);
        Assert.NotSame(first.VolumeIdentity, second.VolumeIdentity);
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(second.Equals(first));
    }

    [Fact]
    public void EquivalentIdentityHasSameHashCode()
    {
        Guid volumeId = Guid.NewGuid();
        Guid objectId = Guid.NewGuid();
        Assert.Equal(new StorageObjectIdentity(new VolumeIdentity(volumeId), objectId).GetHashCode(), new StorageObjectIdentity(new VolumeIdentity(volumeId), objectId).GetHashCode());
    }

    [Fact]
    public void SameObjectIdDifferentVolumeNotEqual()
    {
        Guid id = Guid.NewGuid();
        Assert.NotEqual(new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), id), new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), id));
    }

    [Fact]
    public void DifferentObjectIdSameVolumeNotEqual()
    {
        var volume = new VolumeIdentity(Guid.NewGuid());
        Assert.NotEqual(new StorageObjectIdentity(volume, Guid.NewGuid()), new StorageObjectIdentity(volume, Guid.NewGuid()));
    }

    [Fact]
    public void NullObjectComparisonIsFalse()
    {
        var identity = new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid());
        Assert.False(identity.Equals((StorageObjectIdentity?)null));
        Assert.False(identity.Equals((object?)null));
        Assert.False(identity.Equals(new object()));
    }

    [Fact]
    public void PublicPropertiesCannotBeMutated()
    {
        Assert.All(typeof(StorageObjectIdentity).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void IdentityHasNoPathOrSessionProperty()
    {
        Assert.Equal(["ObjectId", "VolumeIdentity"], typeof(StorageObjectIdentity).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }
}
