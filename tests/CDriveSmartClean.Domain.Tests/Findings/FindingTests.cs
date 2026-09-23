using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Findings;

public sealed class FindingTests
{
    [Fact]
    public void ValidFindingIsAcceptedAndDisplayNameIsTrimmed()
    {
        Finding finding = CreateFinding(displayName: "  example  ");

        Assert.Equal("example", finding.DisplayName);
        Assert.Equal(FindingCategory.Unknown, finding.PrimaryCategory);
    }

    [Fact]
    public void EmptyIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(id: Guid.Empty));
    }

    [Fact]
    public void EmptyScanSessionIdIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(scanSessionId: Guid.Empty));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidDisplayNameIsRejected(string? displayName)
    {
        Assert.ThrowsAny<ArgumentException>(() => CreateFinding(displayName: displayName!));
    }

    [Fact]
    public void NullRequiredValueObjectsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => CreateFindingCore(null!, ValidReclaim(), ValidRisk()));
        Assert.Throws<ArgumentNullException>(() => CreateFindingCore(ValidSize(), null!, ValidRisk()));
        Assert.Throws<ArgumentNullException>(() => CreateFindingCore(ValidSize(), ValidReclaim(), null!));
    }

    [Fact]
    public void NullFacetsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Finding(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example",
            FindingCategory.Unknown,
            null!,
            ValidSize(),
            ValidReclaim(),
            ValidRisk(),
            Confidence.High,
            ProtectionState.Normal,
            ValidEvidence()));
    }

    [Fact]
    public void EmptyFacetsAreAccepted()
    {
        Finding finding = CreateFinding(facets: Array.Empty<FindingFacet>());

        Assert.Empty(finding.Facets);
    }

    [Fact]
    public void FacetsAreDeduplicatedInStableOrderAndDefensivelyCopied()
    {
        var source = new List<FindingFacet>
        {
            FindingFacet.Old,
            FindingFacet.Large,
            FindingFacet.Old
        };
        Finding finding = CreateFinding(facets: source);

        source.Add(FindingFacet.Growing);

        Assert.Equal([FindingFacet.Old, FindingFacet.Large], finding.Facets);
    }

    [Fact]
    public void UndefinedFacetIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateFinding(facets: [(FindingFacet)99]));
    }

    [Fact]
    public void UndefinedPrimaryCategoryIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateFinding(primaryCategory: (FindingCategory)99));
    }

    [Fact]
    public void UnknownPrimaryCategoryIsAccepted()
    {
        Finding finding = CreateFinding(primaryCategory: FindingCategory.Unknown);

        Assert.Equal(FindingCategory.Unknown, finding.PrimaryCategory);
    }

    [Fact]
    public void NullEvidenceIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Finding(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example",
            FindingCategory.Unknown,
            [FindingFacet.Large],
            ValidSize(),
            ValidReclaim(),
            ValidRisk(),
            Confidence.High,
            ProtectionState.Normal,
            null!));
    }

    [Fact]
    public void EmptyEvidenceIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(evidence: Array.Empty<Evidence>()));
    }

    [Fact]
    public void NullEvidenceItemIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(evidence: [null!]));
    }

    [Fact]
    public void EvidenceIsDefensivelyCopied()
    {
        var first = new Evidence("first", "description", Confidence.High);
        var source = new List<Evidence> { first };
        Finding finding = CreateFinding(evidence: source);

        source.Add(new Evidence("second", "description", Confidence.High));

        Assert.Equal([first], finding.Evidence);
    }

    [Fact]
    public void UndefinedConfidenceIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFinding(confidence: (Confidence)99));
    }

    [Fact]
    public void UndefinedProtectionStateIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateFinding(protectionState: (ProtectionState)99));
    }

    [Fact]
    public void SafeDefaultEnumsHaveExpectedValues()
    {
        Assert.Equal(Confidence.Unknown, default);
        Assert.Equal(FindingCategory.Unknown, default);
        Assert.Equal(ProtectionState.Blocked, default);
    }

    [Fact]
    public void PublicPropertiesCannotBeMutated()
    {
        Assert.All(typeof(Finding).GetProperties(), property => Assert.False(property.CanWrite));
        Assert.All(typeof(Evidence).GetProperties(), property => Assert.False(property.CanWrite));
        Assert.All(typeof(ReclaimEstimate).GetProperties(), property => Assert.False(property.CanWrite));
        Assert.All(typeof(RiskAssessment).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void ReclaimMaximumBelowAllocatedBytesIsAccepted()
    {
        Finding finding = CreateFinding(
            sizeMetrics: new SizeMetrics(1_000, 300, 300),
            reclaimEstimate: ReclaimEstimate.Exact(200, "physical allocation"));

        Assert.Equal(200, finding.ReclaimEstimate.MaximumBytes);
    }

    [Fact]
    public void ReclaimMaximumEqualToAllocatedBytesIsAccepted()
    {
        Finding finding = CreateFinding(
            sizeMetrics: new SizeMetrics(1_000, 300, 300),
            reclaimEstimate: ReclaimEstimate.Exact(300, "physical allocation"));

        Assert.Equal(300, finding.ReclaimEstimate.MaximumBytes);
    }

    [Fact]
    public void ReclaimMaximumAboveAllocatedBytesIsRejected()
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(
            sizeMetrics: new SizeMetrics(1_000, 300, 300),
            reclaimEstimate: ReclaimEstimate.Exact(301, "overclaim")));
    }

    [Fact]
    public void LogicalSizeCannotBeUsedToOverclaimSparsePhysicalAllocation()
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(
            sizeMetrics: new SizeMetrics(100_000_000_000, 300_000_000, 300_000_000),
            reclaimEstimate: ReclaimEstimate.Exact(100_000_000_000, "logical size")));
    }

    [Fact]
    public void UnknownReclaimIsCompatibleWithAnyAllocation()
    {
        Finding finding = CreateFinding(
            sizeMetrics: new SizeMetrics(100, 0, 0),
            reclaimEstimate: ReclaimEstimate.Unknown("unknown"));

        Assert.Null(finding.ReclaimEstimate.MaximumBytes);
    }

    [Fact]
    public void NoneReclaimIsCompatibleWithAnyAllocation()
    {
        Finding finding = CreateFinding(
            sizeMetrics: new SizeMetrics(100, 0, 0),
            reclaimEstimate: ReclaimEstimate.None("none"));

        Assert.Equal(0, finding.ReclaimEstimate.MaximumBytes);
    }

    [Fact]
    public void ZeroAllocationRejectsNonzeroReclaimMaximum()
    {
        Assert.Throws<ArgumentException>(() => CreateFinding(
            sizeMetrics: new SizeMetrics(100, 0, 0),
            reclaimEstimate: ReclaimEstimate.Exact(1, "overclaim")));
    }

    private static Finding CreateFinding(
        Guid? id = null,
        Guid? scanSessionId = null,
        string displayName = "example",
        FindingCategory primaryCategory = FindingCategory.Unknown,
        IEnumerable<FindingFacet>? facets = null,
        SizeMetrics? sizeMetrics = null,
        ReclaimEstimate? reclaimEstimate = null,
        RiskAssessment? riskAssessment = null,
        Confidence confidence = Confidence.High,
        ProtectionState protectionState = ProtectionState.Normal,
        IEnumerable<Evidence>? evidence = null)
    {
        return new Finding(
            id ?? Guid.NewGuid(),
            scanSessionId ?? Guid.NewGuid(),
            displayName,
            primaryCategory,
            facets ?? [FindingFacet.Large],
            sizeMetrics ?? new SizeMetrics(10, 10, 10),
            reclaimEstimate ?? ReclaimEstimate.Exact(10, "measured"),
            riskAssessment ?? new RiskAssessment(RiskLevel.Low, Confidence.High, ["reason"]),
            confidence,
            protectionState,
            evidence ?? [new Evidence("code", "description", Confidence.High)]);
    }

    private static Finding CreateFindingCore(
        SizeMetrics sizeMetrics,
        ReclaimEstimate reclaimEstimate,
        RiskAssessment riskAssessment)
    {
        return new Finding(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "example",
            FindingCategory.Unknown,
            [FindingFacet.Large],
            sizeMetrics,
            reclaimEstimate,
            riskAssessment,
            Confidence.High,
            ProtectionState.Normal,
            ValidEvidence());
    }

    private static SizeMetrics ValidSize() => new(10, 10, 10);

    private static ReclaimEstimate ValidReclaim() => ReclaimEstimate.Exact(10, "measured");

    private static RiskAssessment ValidRisk() =>
        new(RiskLevel.Low, Confidence.High, ["reason"]);

    private static Evidence[] ValidEvidence() =>
        [new Evidence("code", "description", Confidence.High)];
}
