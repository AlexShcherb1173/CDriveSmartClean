using System.Collections;
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
    public void PathSummaryHandlesZeroOneThreeAndFourPathBoundaries()
    {
        Assert.Equal(string.Empty, PresentationFormatter.FormatPathSummary(Array.Empty<string>()));
        Assert.Equal("one", PresentationFormatter.FormatPathSummary(["one"]));
        Assert.Equal("one; two; three", PresentationFormatter.FormatPathSummary(["one", "two", "three"]));
        Assert.Equal("one; two; three … (+1 more)",
            PresentationFormatter.FormatPathSummary(["one", "two", "three", "four"]));
    }

    [Fact]
    public void MillionPathSummaryReadsOnlyThreeIndexesAndNeverEnumerates()
    {
        var paths = new CountingPathList(1_000_000);

        string summary = PresentationFormatter.FormatPathSummary(paths);

        Assert.Equal("path-0; path-1; path-2 … (+999997 more)", summary);
        Assert.Equal(3, paths.IndexerReads);
    }

    [Fact]
    public void LongIndividualPathsAreBoundedDeterministically()
    {
        string path = new('x', 1_000);

        string first = PresentationFormatter.FormatPathSummary([path]);
        string second = PresentationFormatter.FormatPathSummary([path]);

        Assert.Equal(PresentationFormatter.MaxPathCharacters, first.Length);
        Assert.EndsWith("…", first, StringComparison.Ordinal);
        Assert.Equal(first, second);
    }

    [Fact]
    public void FindingRowUsesBoundedPathFormatter()
    {
        Guid session = Guid.NewGuid();
        var finding = new Finding(Guid.NewGuid(), session, "Aliases", FindingScope.IdentityGroup,
            FindingCategory.UserData, ["one", "two", "three", "four"],
            new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid()), 1, 0,
            [FindingFacet.HardLinked], new SizeMetrics(null, 100, null), ReclaimEstimate.Unknown("unknown"),
            new RiskAssessment(RiskLevel.Medium, Confidence.High, ["review"]), Confidence.High,
            ProtectionState.ReviewRequired, [new Evidence("test", "test", Confidence.High)]);

        FindingRow row = FindingRow.FromFinding(finding);

        Assert.Equal(PresentationFormatter.FormatPathSummary(finding.RelativePaths), row.RelativePathSummary);
        Assert.Equal("four; one; three … (+1 more)", row.RelativePathSummary);
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

    private sealed class CountingPathList(int count) : IReadOnlyList<string>
    {
        public int Count { get; } = count;
        internal int IndexerReads { get; private set; }

        public string this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                IndexerReads++;
                return $"path-{index}";
            }
        }

        public IEnumerator<string> GetEnumerator() =>
            throw new InvalidOperationException("Path summary must not enumerate the source collection.");

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
