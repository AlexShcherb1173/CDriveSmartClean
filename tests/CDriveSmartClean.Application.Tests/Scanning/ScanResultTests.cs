using CDriveSmartClean.Application.Scanning;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning;

public sealed class ScanResultTests
{
    [Fact]
    public void CompletedResultAccepted()
    {
        ScanResult result = CreateResult(completion: ScanCompletion.Completed);
        Assert.Equal(ScanCompletion.Completed, result.Completion);
    }

    [Fact]
    public void CancelledResultAccepted()
    {
        ScanResult result = CreateResult(completion: ScanCompletion.Cancelled);
        Assert.Equal(ScanCompletion.Cancelled, result.Completion);
    }

    [Fact]
    public void ScanSessionIdRetained()
    {
        Guid scanSessionId = Guid.NewGuid();
        ScanResult result = CreateResult(scanSessionId: scanSessionId);

        Assert.Equal(scanSessionId, result.ScanSessionId);
    }

    [Fact]
    public void ModeRetained()
    {
        ScanResult result = CreateResult(mode: ScanMode.Deep);
        Assert.Equal(ScanMode.Deep, result.Mode);
    }

    [Fact]
    public void CompletionRetained()
    {
        ScanResult result = CreateResult(completion: ScanCompletion.Cancelled);
        Assert.Equal(ScanCompletion.Cancelled, result.Completion);
    }

    [Fact]
    public void VolumeAccountingRetained()
    {
        var accounting = new VolumeAccounting(100, 80, 20, 60, 10, 5);
        ScanResult result = CreateResult(volumeAccounting: accounting);

        Assert.Same(accounting, result.VolumeAccounting);
    }

    [Fact]
    public void EmptyScanSessionIdRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateResult(scanSessionId: Guid.Empty));
    }

    [Fact]
    public void DefaultScanModeRejected()
    {
        Assert.False(Enum.IsDefined(default(ScanMode)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateResult(mode: default));
    }

    [Fact]
    public void UndefinedScanModeRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateResult(mode: (ScanMode)42));
    }

    [Fact]
    public void DefaultScanCompletionRejected()
    {
        Assert.False(Enum.IsDefined(default(ScanCompletion)));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateResult(completion: default));
    }

    [Fact]
    public void UndefinedScanCompletionRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateResult(completion: (ScanCompletion)42));
    }

    [Fact]
    public void NullVolumeAccountingRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new ScanResult(
                Guid.NewGuid(),
                ScanMode.Smart,
                ScanCompletion.Completed,
                null!));
    }

    [Fact]
    public void PublicPropertiesAreImmutable()
    {
        Assert.All(typeof(ScanResult).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void ResultHasNoFindingCollectionOrProperty()
    {
        Assert.DoesNotContain(
            typeof(ScanResult).GetProperties(),
            property => property.Name.Contains("Finding", StringComparison.Ordinal) ||
                property.PropertyType.Name.Contains("Finding", StringComparison.Ordinal));
    }

    private static ScanResult CreateResult(
        Guid? scanSessionId = null,
        ScanMode mode = ScanMode.Smart,
        ScanCompletion completion = ScanCompletion.Completed,
        VolumeAccounting? volumeAccounting = null)
    {
        return new ScanResult(
            scanSessionId ?? Guid.NewGuid(),
            mode,
            completion,
            volumeAccounting ?? new VolumeAccounting(100, 80, 20, 60, 10, 5));
    }
}
