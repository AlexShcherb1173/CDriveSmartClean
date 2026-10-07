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
            FindingScope.CategoryAggregate,
            FindingCategory.Unknown,
            [],
            null,
            null,
            null,
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
            FindingScope.CategoryAggregate,
            FindingCategory.Unknown,
            [],
            null,
            null,
            null,
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

    [Theory]
    [InlineData(FindingScope.CategoryAggregate)]
    [InlineData(FindingScope.HierarchyArea)]
    [InlineData(FindingScope.IdentityGroup)]
    [InlineData(FindingScope.File)]
    public void AllFindingScopesAreAccepted(FindingScope scope)
    {
        Finding finding = scope switch
        {
            FindingScope.CategoryAggregate => CreateFinding(scope: scope),
            FindingScope.HierarchyArea => CreateFinding(scope: scope, relativePaths: ["area"],
                fileCount: 2, directoryCount: 1),
            FindingScope.IdentityGroup => CreateFinding(scope: scope, relativePaths: ["b", "a"],
                objectIdentity: Identity(), fileCount: 1, directoryCount: 0),
            FindingScope.File => CreateFinding(scope: scope, relativePaths: ["file"],
                objectIdentity: Identity(), fileCount: 1, directoryCount: 0),
            _ => throw new InvalidOperationException()
        };

        Assert.Equal(scope, finding.Scope);
    }

    [Fact]
    public void RelativePathsAreSortedDeduplicatedAndDefensivelyCopied()
    {
        var paths = new List<string> { "b", "a", "a" };
        Finding finding = CreateFinding(scope: FindingScope.IdentityGroup, relativePaths: paths,
            objectIdentity: Identity(), fileCount: 1, directoryCount: 0);
        paths.Add("c");
        Assert.Equal(["a", "b"], finding.RelativePaths);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void InvalidScopeIsRejected(int scope) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateFinding(scope: (FindingScope)scope));

    [Fact]
    public void CategoryScopeRejectsObjectMetadata() =>
        Assert.Throws<ArgumentException>(() => CreateFinding(relativePaths: ["path"]));

    [Fact]
    public void HierarchyScopeRequiresOnePathAndCounts() =>
        Assert.Throws<ArgumentException>(() => CreateFinding(scope: FindingScope.HierarchyArea,
            relativePaths: ["area"]));

    [Fact]
    public void IdentityScopeRequiresIdentityAndTwoDistinctPaths() =>
        Assert.Throws<ArgumentException>(() => CreateFinding(scope: FindingScope.IdentityGroup,
            relativePaths: ["same", "same"], objectIdentity: Identity(), fileCount: 1, directoryCount: 0));

    [Fact]
    public void FileScopeRequiresExactCounts() =>
        Assert.Throws<ArgumentException>(() => CreateFinding(scope: FindingScope.File,
            relativePaths: ["file"], objectIdentity: Identity(), fileCount: 2, directoryCount: 0));

    [Fact]
    public void UnknownReclaimAcceptsUnknownAllocation()
    {
        Finding finding = CreateFinding(sizeMetrics: new SizeMetrics(null, null, null),
            reclaimEstimate: ReclaimEstimate.Unknown("unknown"));
        Assert.Null(finding.SizeMetrics.AllocatedBytes);
    }

    [Fact]
    public void NumericReclaimRejectsUnknownAllocation() =>
        Assert.Throws<ArgumentException>(() => CreateFinding(
            sizeMetrics: new SizeMetrics(10, null, null),
            reclaimEstimate: ReclaimEstimate.Exact(0, "numeric")));

    [Fact]
    public void EvidenceIsSortedAndIdenticalCodesAreDeduplicated()
    {
        Evidence duplicate = new("b", "same", Confidence.High);
        Finding finding = CreateFinding(evidence:
            [duplicate, new Evidence("a", "first", Confidence.Medium), duplicate]);
        Assert.Equal(["a", "b"], finding.Evidence.Select(item => item.Code));
    }

    [Fact]
    public void ConflictingEvidenceCodesAreRejected() =>
        Assert.Throws<ArgumentException>(() => CreateFinding(evidence:
        [
            new Evidence("same", "first", Confidence.High),
            new Evidence("same", "second", Confidence.High)
        ]));

    private static Finding CreateFinding(
        Guid? id = null,
        Guid? scanSessionId = null,
        string displayName = "example",
        FindingScope scope = FindingScope.CategoryAggregate,
        FindingCategory primaryCategory = FindingCategory.Unknown,
        IEnumerable<string>? relativePaths = null,
        StorageObjectIdentity? objectIdentity = null,
        long? fileCount = null,
        long? directoryCount = null,
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
            scope,
            primaryCategory,
            relativePaths ?? [],
            objectIdentity,
            fileCount,
            directoryCount,
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
            FindingScope.CategoryAggregate,
            FindingCategory.Unknown,
            [],
            null,
            null,
            null,
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

    private static StorageObjectIdentity Identity() =>
        new(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid());
}
