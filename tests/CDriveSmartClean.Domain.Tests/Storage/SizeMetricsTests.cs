using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Storage;

public sealed class SizeMetricsTests
{
    [Fact]
    public void ZeroValuesAreAccepted()
    {
        var metrics = new SizeMetrics(0, 0, 0);

        Assert.Equal(0, metrics.LogicalBytes);
        Assert.Equal(0, metrics.AllocatedBytes);
        Assert.Equal(0, metrics.ExclusiveAllocatedBytes);
    }

    [Fact]
    public void PositiveValuesAreAccepted()
    {
        var metrics = new SizeMetrics(100, 80, 60);

        Assert.Equal(100, metrics.LogicalBytes);
        Assert.Equal(80, metrics.AllocatedBytes);
        Assert.Equal(60, metrics.ExclusiveAllocatedBytes);
    }

    [Fact]
    public void AllocatedMayExceedLogical()
    {
        var metrics = new SizeMetrics(10, 20, 15);

        Assert.Equal(20, metrics.AllocatedBytes);
    }

    [Fact]
    public void LogicalMayExceedAllocated()
    {
        var metrics = new SizeMetrics(20, 10, 5);

        Assert.Equal(20, metrics.LogicalBytes);
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void NegativeValuesAreRejected(
        long logicalBytes,
        long allocatedBytes,
        long exclusiveAllocatedBytes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new SizeMetrics(logicalBytes, allocatedBytes, exclusiveAllocatedBytes));
    }

    [Fact]
    public void ExclusiveAllocatedCannotExceedAllocated()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SizeMetrics(10, 5, 6));
    }

    [Fact]
    public void PublicPropertiesCannotBeMutated()
    {
        Assert.All(
            typeof(SizeMetrics).GetProperties(),
            property => Assert.False(property.CanWrite));
    }
}
