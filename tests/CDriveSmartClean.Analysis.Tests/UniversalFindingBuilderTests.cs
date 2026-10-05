using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class UniversalFindingBuilderTests
{
    private readonly UniversalFindingBuilder builder = new();

    [Fact]
    public void CategoryAggregatesUseDeduplicatedAllocationAndUnknownReclaim()
    {
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            new Dictionary<FindingCategory, long?> { [FindingCategory.System] = 42 }), TestContext.Current.CancellationToken);
        Finding finding = Assert.Single(result.Findings);
        Assert.Equal(FindingScope.CategoryAggregate, finding.Scope);
        Assert.Null(finding.SizeMetrics.LogicalBytes);
        Assert.Equal(42, finding.SizeMetrics.AllocatedBytes);
        Assert.Null(finding.SizeMetrics.ExclusiveAllocatedBytes);
        Assert.Equal(ReclaimKind.Unknown, finding.ReclaimEstimate.Kind);
    }

    [Fact]
    public void CandidateViewsMapToAllObjectScopes()
    {
        StorageAnalysisCandidate hierarchy = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.Application, "Program Files\\Area", 20, 10);
        StorageAnalysisCandidate file = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, "Users\\Current\\file.bin", 30, 20);
        StorageObjectIdentity identity = new(UniversalFindingTestData.Volume,
            Guid.Parse("22222222-2222-2222-2222-222222222222"));
        StorageAnalysisCandidate aliases = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.IdentityGroup, FindingCategory.ApplicationData, "alias-a", 40, 30,
            [FindingFacet.HardLinked], identity, ["alias-b", "alias-a"]);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            hierarchy: [hierarchy], identities: [aliases], files: [file]), TestContext.Current.CancellationToken);
        Assert.Contains(result.Findings, item => item.Scope == FindingScope.HierarchyArea);
        Assert.Contains(result.Findings, item => item.Scope == FindingScope.IdentityGroup);
        Assert.Contains(result.Findings, item => item.Scope == FindingScope.File);
    }

    [Fact]
    public void SinglePathIdentityGroupIsSuppressed()
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.IdentityGroup, FindingCategory.Unknown, "only", 1, 1);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(identities: [candidate]), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(result.Findings, item => item.Scope == FindingScope.IdentityGroup);
    }

    [Theory]
    [InlineData(FindingFacet.Sparse)]
    [InlineData(FindingFacet.CloudPlaceholder)]
    public void LogicalInflationDoesNotProduceLarge(FindingFacet facet)
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, $"Users\\Current\\{facet}",
            100L * 1_073_741_824, 300L * 1024 * 1024, [facet]);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(files: [candidate]), TestContext.Current.CancellationToken);
        Finding finding = Assert.Single(result.Findings);
        Assert.Contains(facet, finding.Facets);
        Assert.DoesNotContain(FindingFacet.Large, finding.Facets);
        Assert.Equal(ReclaimKind.Unknown, finding.ReclaimEstimate.Kind);
    }

    [Fact]
    public void CapacityUnavailableProducesIncompleteOutputWithoutLarge()
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.Unknown, "large.bin", 20_000_000_000, 20_000_000_000);
        UniversalFindingResult result = builder.Build(
            UniversalFindingTestData.Request(files: [candidate], capacityBytes: null), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Incomplete, result.Quality);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.VolumeCapacityUnavailable));
        Assert.All(result.Findings, item => Assert.DoesNotContain(FindingFacet.Large, item.Facets));
        Assert.All(result.Findings, item => Assert.True(item.Confidence <= Confidence.Medium));
    }

    [Fact]
    public void AnalysisUnavailablePublishesNoFindings()
    {
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            analysisQuality: AnalysisQuality.Unavailable,
            analysisReasons: AnalysisReason.ResourceLimit), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.Empty(result.Findings);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.UpstreamAnalysisUnavailable));
    }

    [Fact]
    public void VolumeMismatchReturnsUnavailableInputMismatch()
    {
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            accountingVolume: new VolumeIdentity(Guid.Parse("33333333-3333-3333-3333-333333333333"))), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.Empty(result.Findings);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.InputMismatch));
    }

    [Fact]
    public void UniversalFindingsRemainUsefulWithZeroProductRecognition()
    {
        const long large = 3L * 1_073_741_824;
        var categories = new Dictionary<FindingCategory, long?>
        {
            [FindingCategory.System] = 10,
            [FindingCategory.Application] = 20,
            [FindingCategory.ApplicationData] = 30,
            [FindingCategory.UserData] = 40,
            [FindingCategory.Unknown] = 50
        };
        StorageAnalysisCandidate largeUserFile = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, "Users\\Current\\large.iso", large, large);
        StorageAnalysisCandidate largeUnknown = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.Unknown, "Mystery", large, large,
            [FindingFacet.Sparse]);
        StorageHierarchyNode cache = UniversalFindingTestData.HierarchyNode(
            "Users\\Current\\AppData\\Local\\Vendorless\\Cache", 100);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(categories,
            hierarchy: [largeUnknown], files: [largeUserFile], hierarchyChildren: [cache]), TestContext.Current.CancellationToken);

        Assert.Contains(result.Findings, item => item.PrimaryCategory == FindingCategory.System);
        Assert.Contains(result.Findings, item => item.PrimaryCategory == FindingCategory.Application);
        Assert.Contains(result.Findings, item => item.PrimaryCategory == FindingCategory.ApplicationData);
        Assert.Contains(result.Findings, item => item.PrimaryCategory == FindingCategory.UserData);
        Assert.Contains(result.Findings, item => item.PrimaryCategory == FindingCategory.Unknown);
        Assert.Contains(result.Findings, item => item.Facets.Contains(FindingFacet.CacheLike));
        Assert.Contains(result.Findings, item => item.Facets.Contains(FindingFacet.Large));
        Assert.All(result.Findings, item =>
        {
            Assert.Equal(ReclaimKind.Unknown, item.ReclaimEstimate.Kind);
            Assert.Null(item.ReclaimEstimate.MinimumBytes);
            Assert.Null(item.ReclaimEstimate.ExpectedBytes);
            Assert.Null(item.ReclaimEstimate.MaximumBytes);
            Assert.Null(item.SizeMetrics.ExclusiveAllocatedBytes);
            Assert.True(item.RiskAssessment.Level is RiskLevel.High or RiskLevel.Critical);
        });
    }

    [Fact]
    public void AnalysisAssemblyExportsOnlyBuilderAndAnalyzer()
    {
        Type[] exported = typeof(UniversalFindingBuilder).Assembly.GetExportedTypes();
        Assert.Equal(2, exported.Length);
        Assert.Contains(typeof(UniversalFindingBuilder), exported);
    }
}
