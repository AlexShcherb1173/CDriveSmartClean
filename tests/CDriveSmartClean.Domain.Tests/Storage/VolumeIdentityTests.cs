using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Storage;

public sealed class VolumeIdentityTests
{
    [Fact]
    public void ValidIdentityAccepted()
    {
        Assert.NotNull(new VolumeIdentity(Guid.NewGuid()));
    }

    [Fact]
    public void EmptyGuidRejected()
    {
        Assert.Throws<ArgumentException>("id", () => new VolumeIdentity(Guid.Empty));
    }

    [Fact]
    public void IdRetained()
    {
        Guid id = Guid.NewGuid();
        Assert.Equal(id, new VolumeIdentity(id).Id);
    }

    [Fact]
    public void SameGuidDifferentInstancesAreEqual()
    {
        Guid id = Guid.NewGuid();
        var first = new VolumeIdentity(id);
        var second = new VolumeIdentity(id);
        Assert.NotSame(first, second);
        Assert.True(first.Equals(second));
        Assert.True(first.Equals((object)second));
        Assert.True(second.Equals(first));
    }

    [Fact]
    public void SameGuidHasSameHashCode()
    {
        Guid id = Guid.NewGuid();
        Assert.Equal(new VolumeIdentity(id).GetHashCode(), new VolumeIdentity(id).GetHashCode());
    }

    [Fact]
    public void DifferentGuidNotEqual()
    {
        Assert.NotEqual(new VolumeIdentity(Guid.NewGuid()), new VolumeIdentity(Guid.NewGuid()));
    }

    [Fact]
    public void NullObjectComparisonIsFalse()
    {
        var identity = new VolumeIdentity(Guid.NewGuid());
        Assert.False(identity.Equals((VolumeIdentity?)null));
        Assert.False(identity.Equals((object?)null));
        Assert.False(identity.Equals(new object()));
    }

    [Fact]
    public void PublicPropertiesCannotBeMutated()
    {
        Assert.All(typeof(VolumeIdentity).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void IdentityHasNoPathDriveOrSessionProperty()
    {
        var property = Assert.Single(typeof(VolumeIdentity).GetProperties());
        Assert.Equal("Id", property.Name);
        Assert.Equal(typeof(Guid), property.PropertyType);
    }
}
