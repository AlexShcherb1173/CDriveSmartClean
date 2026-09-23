using CDriveSmartClean.Application.Scanning;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning;

public sealed class ScanRequestTests
{
    [Fact]
    public void ValidRequestAccepted()
    {
        var request = new ScanRequest(Guid.NewGuid(), ScanMode.Quick);

        Assert.NotNull(request);
    }

    [Fact]
    public void ScanSessionIdRetained()
    {
        Guid scanSessionId = Guid.NewGuid();
        var request = new ScanRequest(scanSessionId, ScanMode.Smart);

        Assert.Equal(scanSessionId, request.ScanSessionId);
    }

    [Fact]
    public void ScanModeRetained()
    {
        var request = new ScanRequest(Guid.NewGuid(), ScanMode.Deep);

        Assert.Equal(ScanMode.Deep, request.Mode);
    }

    [Fact]
    public void EmptyScanSessionIdRejected()
    {
        Assert.Throws<ArgumentException>(() => new ScanRequest(Guid.Empty, ScanMode.Quick));
    }

    [Fact]
    public void DefaultScanModeRejected()
    {
        Assert.False(Enum.IsDefined(default(ScanMode)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanRequest(Guid.NewGuid(), default));
    }

    [Fact]
    public void UndefinedScanModeRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ScanRequest(Guid.NewGuid(), (ScanMode)42));
    }

    [Fact]
    public void PublicPropertiesAreImmutable()
    {
        Assert.All(typeof(ScanRequest).GetProperties(), property => Assert.False(property.CanWrite));
    }
}
