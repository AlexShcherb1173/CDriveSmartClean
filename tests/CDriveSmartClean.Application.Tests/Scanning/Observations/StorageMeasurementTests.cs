using CDriveSmartClean.Application.Scanning.Observations;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Observations;

public sealed class StorageMeasurementTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 1)]
    [InlineData(1, 100)]
    [InlineData(long.MaxValue, long.MaxValue)]
    public void AvailableValuesArePreserved(long logical, long allocated)
    {
        StorageMeasurement measurement = Available(logical, allocated);
        Assert.Equal(StorageMeasurementAvailability.Available, measurement.Availability);
        Assert.Equal(logical, measurement.LogicalBytes);
        Assert.Equal(allocated, measurement.ReportedAllocatedBytes);
        Assert.Equal(StorageMeasurementQuality.FileSystemReported, measurement.Quality);
        Assert.Equal(StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo, measurement.Source);
        Assert.Equal(StorageMeasurementFreshness.LivePointInTime, measurement.Freshness);
    }

    [Theory]
    [InlineData(StorageMeasurementAvailability.Unavailable)]
    [InlineData(StorageMeasurementAvailability.NotApplicable)]
    public void NonAvailableValuesAreNullNotZero(StorageMeasurementAvailability availability)
    {
        var measurement = new StorageMeasurement(null, null, availability, StorageMeasurementQuality.Unknown,
            StorageMeasurementSource.None, StorageMeasurementScope.DirectoryEntryMetadata,
            StorageMeasurementFreshness.Unknown);
        Assert.Null(measurement.LogicalBytes);
        Assert.Null(measurement.ReportedAllocatedBytes);
    }

    [Fact]
    public void AvailableRequiresBothValues()
    {
        Assert.Throws<ArgumentNullException>("logicalBytes", () => Create(null, 0));
        Assert.Throws<ArgumentNullException>("reportedAllocatedBytes", () => Create(0, null));
    }

    [Fact]
    public void NonAvailableRejectsValues()
    {
        Assert.Throws<ArgumentException>(() => new StorageMeasurement(0, null,
            StorageMeasurementAvailability.Unavailable, StorageMeasurementQuality.Unknown,
            StorageMeasurementSource.None, StorageMeasurementScope.FileContent,
            StorageMeasurementFreshness.Unknown));
    }

    [Theory]
    [InlineData(-1, 0, "logicalBytes")]
    [InlineData(0, -1, "reportedAllocatedBytes")]
    public void NegativeValuesAreRejected(long logical, long allocated, string parameter) =>
        Assert.Throws<ArgumentOutOfRangeException>(parameter, () => Available(logical, allocated));

    [Fact]
    public void AvailableCannotClaimUnknownOrMissingNativeEvidence()
    {
        Assert.Throws<ArgumentException>(() => new StorageMeasurement(0, 0,
            StorageMeasurementAvailability.Available, StorageMeasurementQuality.Unknown,
            StorageMeasurementSource.None, StorageMeasurementScope.FileContent,
            StorageMeasurementFreshness.Unknown));
    }

    [Fact]
    public void AllScopesAreRetained()
    {
        foreach (StorageMeasurementScope scope in Enum.GetValues<StorageMeasurementScope>())
        {
            Assert.Equal(scope, Available(0, 0, scope).Scope);
        }
    }

    [Fact]
    public void ContractContainsNoExclusiveOrReclaimSemantics()
    {
        string[] names = typeof(StorageMeasurement).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Exclusive", StringComparison.Ordinal));
        Assert.DoesNotContain(names, name => name.Contains("Reclaim", StringComparison.Ordinal));
        Assert.All(typeof(StorageMeasurement).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void EnumsHaveOnlyApprovedValues()
    {
        Assert.Equal(["Available", "Unavailable", "NotApplicable"], Enum.GetNames<StorageMeasurementAvailability>());
        Assert.Equal(["FileSystemReported", "Unknown"], Enum.GetNames<StorageMeasurementQuality>());
        Assert.Equal(["WindowsFileIdExtendedDirectoryInfo", "None"], Enum.GetNames<StorageMeasurementSource>());
        Assert.Equal(["FileContent", "DirectoryEntryMetadata", "ReparseEntryMetadata"], Enum.GetNames<StorageMeasurementScope>());
        Assert.Equal(["LivePointInTime", "Unknown"], Enum.GetNames<StorageMeasurementFreshness>());
    }

    private static StorageMeasurement Available(long logical, long allocated,
        StorageMeasurementScope scope = StorageMeasurementScope.FileContent) => Create(logical, allocated, scope);

    private static StorageMeasurement Create(long? logical, long? allocated,
        StorageMeasurementScope scope = StorageMeasurementScope.FileContent) => new(
        logical, allocated, StorageMeasurementAvailability.Available,
        StorageMeasurementQuality.FileSystemReported,
        StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo, scope,
        StorageMeasurementFreshness.LivePointInTime);
}
