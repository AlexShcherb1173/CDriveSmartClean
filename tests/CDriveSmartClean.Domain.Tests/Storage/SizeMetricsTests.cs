using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Storage;

public sealed class SizeMetricsTests
{
    [Fact]
    public void AllUnknownValuesAreAccepted()
    {
        var metrics = new SizeMetrics(null, null, null);

        Assert.Null(metrics.LogicalBytes);
        Assert.Null(metrics.AllocatedBytes);
        Assert.Null(metrics.ExclusiveAllocatedBytes);
    }

    [Fact]
    public void KnownZeroIsDistinctFromUnknown()
    {
        var metrics = new SizeMetrics(0, 0, 0);

        Assert.Equal(0, metrics.LogicalBytes);
        Assert.Equal(0, metrics.AllocatedBytes);
        Assert.Equal(0, metrics.ExclusiveAllocatedBytes);
    }

    [Theory]
    [InlineData(10L, null)]
    [InlineData(null, 10L)]
    [InlineData(10L, 20L)]
    [InlineData(20L, 10L)]
    public void LogicalAndAllocatedAvailabilityIsIndependent(long? logical, long? allocated)
    {
        var metrics = new SizeMetrics(logical, allocated, null);

        Assert.Equal(logical, metrics.LogicalBytes);
        Assert.Equal(allocated, metrics.AllocatedBytes);
    }

    [Fact]
    public void ExclusiveRequiresKnownAllocation() =>
        Assert.Throws<ArgumentException>(() => new SizeMetrics(null, null, 0));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(10, 10)]
    public void ExclusiveAtOrBelowAllocationIsAccepted(long allocated, long exclusive)
    {
        var metrics = new SizeMetrics(null, allocated, exclusive);

        Assert.Equal(exclusive, metrics.ExclusiveAllocatedBytes);
    }

    [Fact]
    public void ExclusiveAboveAllocationIsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeMetrics(10, 5, 6));

    [Theory]
    [InlineData(-1L, null, null)]
    [InlineData(null, -1L, null)]
    [InlineData(null, 0L, -1L)]
    public void NegativeKnownValuesAreRejected(long? logical, long? allocated, long? exclusive) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeMetrics(logical, allocated, exclusive));

    [Fact]
    public void PublicPropertiesAreImmutable() =>
        Assert.All(typeof(SizeMetrics).GetProperties(), property => Assert.False(property.CanWrite));
}
