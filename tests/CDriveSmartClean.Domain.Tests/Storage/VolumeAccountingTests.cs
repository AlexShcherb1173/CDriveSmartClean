using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Storage;

public sealed class VolumeAccountingTests
{
    [Fact]
    public void ValidAccountingIsAccepted()
    {
        var accounting = new VolumeAccounting(100, 80, 20, 40, 10, 5);

        Assert.Equal(100, accounting.CapacityBytes);
        Assert.Equal(80, accounting.UsedBytes);
        Assert.Equal(20, accounting.FreeBytes);
        Assert.Equal(40, accounting.AccountedAllocatedBytes);
        Assert.Equal(10, accounting.UnknownAllocatedBytes);
        Assert.Equal(5, accounting.FilesystemReservedBytes);
        Assert.Equal(25, accounting.UnattributedBytes);
    }

    [Fact]
    public void NegativeCapacityRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(-1, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void NegativeUsedRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(0, -1, 0, 0, 0, 0));
    }

    [Fact]
    public void NegativeFreeRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(0, 0, -1, 0, 0, 0));
    }

    [Fact]
    public void NegativeAccountedRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(1, 1, 0, -1, 0, 0));
    }

    [Fact]
    public void NegativeUnknownRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(1, 1, 0, 0, -1, 0));
    }

    [Fact]
    public void NegativeFilesystemReservedRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(1, 1, 0, 0, 0, -1));
    }

    [Fact]
    public void UsedGreaterThanCapacityRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(100, 101, 0, 0, 0, 0));
    }

    [Fact]
    public void FreeDoesNotReconcileWithCapacityRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new VolumeAccounting(100, 80, 21, 0, 0, 0));
    }

    [Fact]
    public void UsedBucketsExactlyEqualUsedAccepted()
    {
        var accounting = new VolumeAccounting(120, 100, 20, 60, 20, 20);

        Assert.Equal(0, accounting.UnattributedBytes);
    }

    [Fact]
    public void UsedBucketsBelowUsedDeriveUnattributed()
    {
        var accounting = new VolumeAccounting(120, 100, 20, 60, 20, 10);

        Assert.Equal(10, accounting.UnattributedBytes);
    }

    [Fact]
    public void UsedBucketsAboveUsedRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(120, 100, 20, 60, 30, 11));
    }

    [Fact]
    public void UnknownCountsTowardCoverage()
    {
        var accounting = new VolumeAccounting(120, 100, 20, 60, 20, 10);

        Assert.Equal(10, accounting.UnattributedBytes);
        Assert.Equal(90m, accounting.CoveragePercent);
    }

    [Fact]
    public void FilesystemReservedCountsTowardCoverage()
    {
        var accounting = new VolumeAccounting(100, 100, 0, 0, 0, 25);

        Assert.Equal(25m, accounting.CoveragePercent);
    }

    [Fact]
    public void UnattributedDoesNotCountTowardCoverage()
    {
        var accounting = new VolumeAccounting(100, 100, 0, 20, 0, 0);

        Assert.Equal(80, accounting.UnattributedBytes);
        Assert.Equal(20m, accounting.CoveragePercent);
    }

    [Fact]
    public void FullCoverageIsOneHundredPercent()
    {
        var accounting = new VolumeAccounting(100, 80, 20, 40, 20, 20);

        Assert.Equal(100m, accounting.CoveragePercent);
    }

    [Fact]
    public void ZeroCoverageIsZeroPercent()
    {
        var accounting = new VolumeAccounting(100, 80, 20, 0, 0, 0);

        Assert.Equal(80, accounting.UnattributedBytes);
        Assert.Equal(0m, accounting.CoveragePercent);
    }

    [Fact]
    public void EmptyVolumeHasOneHundredPercentCoverage()
    {
        var accounting = new VolumeAccounting(0, 0, 0, 0, 0, 0);

        Assert.Equal(0, accounting.UnattributedBytes);
        Assert.Equal(100m, accounting.CoveragePercent);
    }

    [Fact]
    public void FullyFreeNonzeroVolumeHasOneHundredPercentCoverage()
    {
        var accounting = new VolumeAccounting(100, 0, 100, 0, 0, 0);

        Assert.Equal(100m, accounting.CoveragePercent);
    }

    [Fact]
    public void UsedZeroRejectsNonzeroAccountedBucket()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(100, 0, 100, 1, 0, 0));
    }

    [Fact]
    public void UsedZeroRejectsNonzeroUnknownBucket()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(100, 0, 100, 0, 1, 0));
    }

    [Fact]
    public void UsedZeroRejectsNonzeroReservedBucket()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new VolumeAccounting(100, 0, 100, 0, 0, 1));
    }

    [Fact]
    public void LongMaxValueValidAccountingDoesNotOverflow()
    {
        var accounting = new VolumeAccounting(
            long.MaxValue,
            long.MaxValue,
            0,
            long.MaxValue - 2,
            1,
            1);

        Assert.Equal(0, accounting.UnattributedBytes);
        Assert.Equal(100m, accounting.CoveragePercent);
    }

    [Fact]
    public void NearLongMaxOverAccountingIsRejectedWithoutOverflow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new VolumeAccounting(
            long.MaxValue,
            long.MaxValue,
            0,
            long.MaxValue,
            1,
            0));
    }

    [Fact]
    public void CoverageIsNotRoundedByDomain()
    {
        var accounting = new VolumeAccounting(100, 80, 20, 65, 0, 0);

        Assert.Equal(81.25m, accounting.CoveragePercent);
    }

    [Fact]
    public void PublicPropertiesCannotBeMutated()
    {
        Assert.All(
            typeof(VolumeAccounting).GetProperties(),
            property => Assert.False(property.CanWrite));
    }
}
