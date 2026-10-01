using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Storage;

public sealed class StorageAccountingContractsTests
{
    private static VolumeIdentity Identity => new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static VolumeSpaceSnapshot Snapshot(long used) => VolumeSpaceSnapshot.Available(Identity, DateTimeOffset.UnixEpoch, 100, 100 - used, 80, 0, used, 0, 0);

    [Theory]
    [InlineData(AccountingReason.ResourceLimit)]
    [InlineData(AccountingReason.ArithmeticOverflow)]
    public void DisabledAccountingIsNull(AccountingReason reason)
    {
        var summary = new StorageAccountingSummary(null, reason);
        Assert.Null(summary.DeduplicatedObservedAllocatedBytes);
        Assert.Null(summary.UniqueIdentityCount);
        Assert.Equal(AccountingQuality.Unavailable, summary.Quality);
        Assert.Throws<ArgumentException>(() => new StorageAccountingSummary(new(), reason));
    }

    [Fact]
    public void SignedResidualAndUnavailableCoverage()
    {
        var summary = new StorageAccountingSummary(new(inclusiveAttributedObservedAllocatedBytes: 90), AccountingReason.None);
        var reconciliation = new VolumeReconciliation(Snapshot(80), Snapshot(80), summary, true);
        Assert.Equal(-10, reconciliation.SignedResidualBytes);
        Assert.Null(reconciliation.ObservedCoveragePercent);
        Assert.Equal(AccountingQuality.Inconsistent, reconciliation.Quality);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(1, -1)]
    [InlineData(1, 2)]
    public void SnapshotRejectsInvalidSpace(long capacity, long free)
        => Assert.ThrowsAny<ArgumentException>(() => VolumeSpaceSnapshot.Available(Identity, DateTimeOffset.UnixEpoch, capacity, free, 0, 0, 0, 0, 0));

    [Fact]
    public void SnapshotUnavailableDoesNotContainZeroSentinels()
    {
        var snapshot = VolumeSpaceSnapshot.Unavailable(Identity, DateTimeOffset.UnixEpoch, VolumeSpaceFailure.AccessDenied);
        Assert.Null(snapshot.CapacityBytes);
        Assert.Null(snapshot.FreeBytes);
        Assert.Null(snapshot.UsedBytes);
        Assert.Null(snapshot.CallerCapacityBytes);
    }

    [Fact]
    public void CollectionsAreCopiedSortedAndReadOnly()
    {
        var paths = new List<string> { "b", "a" };
        var group = new AllocationGroup(new(Identity, Guid.NewGuid()), paths, 10, AccountingReason.None, "");
        paths.Clear();
        Assert.Equal(["a", "b"], group.Paths);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)group.Paths).Add("c"));
        var children = new List<StorageHierarchyNode> { new("b", new(), []), new("a", new(), []) };
        var root = new StorageHierarchyNode("", new(), children);
        children.Clear();
        Assert.Equal(["a", "b"], root.Children.Select(n => n.RelativePath));
    }

    [Fact]
    public void NonnegativeAndMaximumCounters()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAggregate(fileCount: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAggregate(rawReportedAllocatedBytes: -1));
        Assert.Equal(long.MaxValue, new StorageAggregate(rawReportedAllocatedBytes: long.MaxValue).RawReportedAllocatedBytes);
        Assert.DoesNotContain(typeof(StorageAggregate).GetProperties(), p => p.Name.Contains("Exclusive", StringComparison.Ordinal));
        Assert.All(typeof(StorageAggregate).GetProperties(), p => Assert.False(p.CanWrite));
    }

    [Fact]
    public void ReasonsCombineAndVolumeMismatchFails()
    {
        var summary = new StorageAccountingSummary(new(), AccountingReason.IdentityUnavailable | AccountingReason.MeasurementUnavailable);
        Assert.Equal(AccountingQuality.Incomplete, summary.Quality);
        var other = VolumeSpaceSnapshot.Unavailable(new(Guid.NewGuid()), DateTimeOffset.UnixEpoch, VolumeSpaceFailure.NativeFailure);
        Assert.Throws<ArgumentException>(() => new VolumeReconciliation(Snapshot(80), other, summary, true));
    }
}
