using CDriveSmartClean.Application.Scanning;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning;

public sealed class ScanProgressTests
{
    [Fact]
    public void ValidProgressAccepted()
    {
        Guid scanSessionId = Guid.NewGuid();
        var progress = new ScanProgress(scanSessionId, 12, 4096);

        Assert.Equal(scanSessionId, progress.ScanSessionId);
        Assert.Equal(12, progress.ObjectsObserved);
        Assert.Equal(4096, progress.AllocatedBytesObserved);
    }

    [Fact]
    public void EmptyScanSessionIdRejected()
    {
        Assert.Throws<ArgumentException>(() => new ScanProgress(Guid.Empty, 0, 0));
    }

    [Fact]
    public void NegativeObjectsObservedRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanProgress(Guid.NewGuid(), -1, 0));
    }

    [Fact]
    public void NegativeAllocatedBytesObservedRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanProgress(Guid.NewGuid(), 0, -1));
    }

    [Fact]
    public void ZeroValuesAccepted()
    {
        var progress = new ScanProgress(Guid.NewGuid(), 0, 0);

        Assert.Equal(0, progress.ObjectsObserved);
        Assert.Equal(0, progress.AllocatedBytesObserved);
    }

    [Fact]
    public void LargeLongValuesAccepted()
    {
        var progress = new ScanProgress(Guid.NewGuid(), long.MaxValue, long.MaxValue);

        Assert.Equal(long.MaxValue, progress.ObjectsObserved);
        Assert.Equal(long.MaxValue, progress.AllocatedBytesObserved);
    }

    [Fact]
    public void PublicPropertiesAreImmutable()
    {
        Assert.All(typeof(ScanProgress).GetProperties(), property => Assert.False(property.CanWrite));
    }
}
