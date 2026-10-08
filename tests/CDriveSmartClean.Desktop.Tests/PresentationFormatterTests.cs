using CDriveSmartClean.Desktop.Presentation;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Desktop.Tests;

public sealed class PresentationFormatterTests
{
    [Fact]
    public void NullAndZeroBytesRemainDistinct()
    {
        Assert.Equal("Unavailable", PresentationFormatter.FormatBytes(null));
        Assert.Equal("0 B", PresentationFormatter.FormatBytes(0));
        Assert.Equal("1 KiB", PresentationFormatter.FormatBytes(1024));
    }

    [Fact]
    public void PercentagePreservesUnavailableAndDoesNotClamp()
    {
        Assert.Equal("Unavailable", PresentationFormatter.FormatPercentage(null));
        Assert.Equal("125.5%", PresentationFormatter.FormatPercentage(125.5m));
    }

    [Fact]
    public void UnknownReclaimIsNonnumericAndNeverUsesAllocation()
    {
        string value = PresentationFormatter.FormatReclaim(ReclaimEstimate.Unknown("unknown"));
        Assert.Equal("Not estimated", value);
        Assert.DoesNotContain("4096", value, StringComparison.Ordinal);
    }

    [Fact]
    public void FindingProjectionPreservesDistinctRiskConfidenceAndProtection()
    {
        Guid session = Guid.NewGuid();
        var finding = new Finding(Guid.NewGuid(), session, "Example", FindingScope.File,
            FindingCategory.ApplicationData, ["cache.bin"],
            new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid()), 1, 0,
            [FindingFacet.Large, FindingFacet.CacheLike], new SizeMetrics(200, 100, null),
            ReclaimEstimate.Unknown("unknown"),
            new RiskAssessment(RiskLevel.High, Confidence.Medium, ["review"]), Confidence.Low,
            ProtectionState.ReviewRequired, [new Evidence("test", "test", Confidence.High)]);

        FindingRow row = FindingRow.FromFinding(finding);
        Assert.Equal(RiskLevel.High, row.Risk);
        Assert.Equal(Confidence.Medium, row.RiskConfidence);
        Assert.Equal(Confidence.Low, row.Confidence);
        Assert.Equal(ProtectionState.ReviewRequired, row.ProtectionState);
        Assert.Equal("100 B", row.Allocated);
        Assert.Equal("200 B", row.Logical);
        Assert.Equal("Not estimated", row.Reclaim);
        Assert.Contains("Large", row.Facets, StringComparison.Ordinal);
        Assert.Contains("CacheLike", row.Facets, StringComparison.Ordinal);
    }
}
