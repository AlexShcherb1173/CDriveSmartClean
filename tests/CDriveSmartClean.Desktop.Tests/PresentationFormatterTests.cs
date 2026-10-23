using System.Collections;
using System.Globalization;
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
    private static readonly PresentationLocalizer English = new(CultureInfo.GetCultureInfo("en"));
    private static readonly PresentationLocalizer Russian = new(CultureInfo.GetCultureInfo("ru"));

    [Fact]
    public void NullAndZeroBytesRemainDistinct()
    {
        Assert.Equal("Unavailable", PresentationFormatter.FormatBytes(null, "Unavailable"));
        Assert.Equal("Недоступно", PresentationFormatter.FormatBytes(null, "Недоступно"));
        Assert.Equal("0 B", PresentationFormatter.FormatBytes(0, "Unavailable"));
        Assert.Equal("1 KiB", PresentationFormatter.FormatBytes(1024, "Unavailable"));
    }

    [Fact]
    public void PercentagePreservesUnavailableAndDoesNotClamp()
    {
        Assert.Equal("Unavailable", PresentationFormatter.FormatPercentage(null, "Unavailable"));
        Assert.Equal("125.5%", PresentationFormatter.FormatPercentage(125.5m, "Unavailable"));
    }

    [Fact]
    public void UnknownReclaimIsNonnumericAndNeverUsesAllocation()
    {
        string value = English.Localize(ReclaimEstimate.Unknown("unknown"));
        Assert.Equal("Not estimated", value);
        Assert.DoesNotContain("4096", value, StringComparison.Ordinal);
    }

    [Fact]
    public void PathSummaryHandlesZeroOneThreeAndFourPathBoundaries()
    {
        Assert.Equal(string.Empty, PresentationFormatter.FormatPathSummary(Array.Empty<string>(), English.FormatAdditionalPaths));
        Assert.Equal("one", PresentationFormatter.FormatPathSummary(["one"], English.FormatAdditionalPaths));
        Assert.Equal("one; two; three", PresentationFormatter.FormatPathSummary(["one", "two", "three"], English.FormatAdditionalPaths));
        Assert.Equal("one; two; three … (+1 more)",
            PresentationFormatter.FormatPathSummary(["one", "two", "three", "four"], English.FormatAdditionalPaths));
    }

    [Fact]
    public void MillionPathSummaryReadsOnlyThreeIndexesAndNeverEnumerates()
    {
        var paths = new CountingPathList(1_000_000);

        string summary = PresentationFormatter.FormatPathSummary(paths, English.FormatAdditionalPaths);

        Assert.Equal("path-0; path-1; path-2 … (+999997 more)", summary);
        Assert.Equal(3, paths.IndexerReads);
    }

    [Fact]
    public void LongIndividualPathsAreBoundedDeterministically()
    {
        string path = new('x', 1_000);

        string first = PresentationFormatter.FormatPathSummary([path], English.FormatAdditionalPaths);
        string second = PresentationFormatter.FormatPathSummary([path], English.FormatAdditionalPaths);

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

        FindingRow row = FindingRow.FromFinding(finding, English);

        Assert.Equal(PresentationFormatter.FormatPathSummary(finding.RelativePaths, English.FormatAdditionalPaths), row.RelativePathSummary);
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

        FindingRow row = FindingRow.FromFinding(finding, English);
        Assert.Equal("High", row.Risk);
        Assert.Equal("Medium", row.RiskConfidence);
        Assert.Equal("Low", row.Confidence);
        Assert.Equal("Review required", row.ProtectionState);
        Assert.Equal("100 B", row.Allocated);
        Assert.Equal("200 B", row.Logical);
        Assert.Equal("Not estimated", row.Reclaim);
        Assert.Contains("Large", row.Facets, StringComparison.Ordinal);
        Assert.Contains("Cache-like", row.Facets, StringComparison.Ordinal);
    }

    [Fact]
    public void FindingProjectionLocalizesRussianValuesAndPreservesRealDisplayName()
    {
        var finding = new Finding(Guid.NewGuid(), Guid.NewGuid(), @"Users\Alex\file.bin", FindingScope.File,
            FindingCategory.UserData, [@"Users\Alex\file.bin"],
            new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid()), 1, 0,
            [FindingFacet.UserContent], new SizeMetrics(200, 100, null), ReclaimEstimate.Unknown("unknown"),
            new RiskAssessment(RiskLevel.Low, Confidence.High, ["review"]), Confidence.Verified,
            ProtectionState.Normal, [new Evidence("test", "test", Confidence.High)]);

        FindingRow row = FindingRow.FromFinding(finding, Russian);

        Assert.Equal(@"Users\Alex\file.bin", row.DisplayName);
        Assert.Equal(@"Users\Alex\file.bin", row.RelativePathSummary);
        Assert.Equal("Данные пользователя", row.Category);
        Assert.Equal("Файл", row.Scope);
        Assert.Equal("Не оценено", row.Reclaim);
        Assert.Equal("Низкий", row.Risk);
        Assert.Equal("Высокая", row.RiskConfidence);
        Assert.Equal("Подтверждена", row.Confidence);
        Assert.Equal("Обычный", row.ProtectionState);
        Assert.Equal("Пользовательские данные", row.Facets);
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
