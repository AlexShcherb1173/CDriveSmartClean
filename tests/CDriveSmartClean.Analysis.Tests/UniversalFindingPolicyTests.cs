using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class UniversalFindingPolicyTests
{
    private readonly UniversalFindingBuilder builder = new();

    [Fact]
    public void RiskAndProtectionMatricesAreExact()
    {
        var categories = new Dictionary<FindingCategory, long?>
        {
            [FindingCategory.System] = 1,
            [FindingCategory.Application] = 1,
            [FindingCategory.ApplicationData] = 1,
            [FindingCategory.UserData] = 1,
            [FindingCategory.Unknown] = 1
        };
        var result = builder.Build(UniversalFindingTestData.Request(categories), TestContext.Current.CancellationToken);
        Assert.Equal(RiskLevel.Critical,
            result.Findings.Single(item => item.PrimaryCategory == FindingCategory.System).RiskAssessment.Level);
        Assert.Equal(ProtectionState.Protected,
            result.Findings.Single(item => item.PrimaryCategory == FindingCategory.System).ProtectionState);
        Assert.All(result.Findings.Where(item => item.PrimaryCategory != FindingCategory.System), item =>
        {
            Assert.Equal(RiskLevel.High, item.RiskAssessment.Level);
            Assert.Equal(ProtectionState.ReviewRequired, item.ProtectionState);
        });
        Assert.All(result.Findings, item => Assert.Equal(Confidence.Verified, item.RiskAssessment.Confidence));
    }

    [Fact]
    public void FindingConfidenceIsIndependentFromRiskAndReclaimConfidence()
    {
        var result = builder.Build(UniversalFindingTestData.Request(
            new Dictionary<FindingCategory, long?> { [FindingCategory.Unknown] = 1 }), TestContext.Current.CancellationToken);
        Finding finding = Assert.Single(result.Findings);
        Assert.Equal(Confidence.Unknown, finding.Confidence);
        Assert.Equal(Confidence.Verified, finding.RiskAssessment.Confidence);
        Assert.Equal(Confidence.Unknown, finding.ReclaimEstimate.Confidence);
    }

    [Theory]
    [InlineData(1_073_741_823, false)]
    [InlineData(1_073_741_824, true)]
    [InlineData(1_073_741_825, true)]
    public void LargeThresholdUsesInclusiveOneGiBFloor(long allocation, bool expected)
    {
        Finding finding = BuildFile(allocation, 50L * 1_073_741_824);
        Assert.Equal(expected, finding.Facets.Contains(FindingFacet.Large));
    }

    [Theory]
    [InlineData(17_179_869_183, false)]
    [InlineData(17_179_869_184, true)]
    public void LargeThresholdUsesSixteenGiBCeiling(long allocation, bool expected)
    {
        Finding finding = BuildFile(allocation, 3L * 1024 * 1_073_741_824);
        Assert.Equal(expected, finding.Facets.Contains(FindingFacet.Large));
    }

    [Theory]
    [InlineData("ProgramData\\Thing\\cache", true)]
    [InlineData("ProgramData\\Thing\\CACHES", true)]
    [InlineData("ProgramData\\Thing\\cacheable", false)]
    [InlineData("Users\\Current\\Documents\\Cache", false)]
    [InlineData("Unknown\\Cache", false)]
    [InlineData("Program Files\\Thing\\Cache", false)]
    [InlineData("Windows\\Cache", false)]
    public void CacheLikeRequiresExactComponentUnderTrustedApplicationData(string path, bool expected)
    {
        var node = UniversalFindingTestData.HierarchyNode(path, 10);
        var result = builder.Build(UniversalFindingTestData.Request(hierarchyChildren: [node]), TestContext.Current.CancellationToken);
        Assert.Equal(expected, result.Findings.Any(item => item.Facets.Contains(FindingFacet.CacheLike)));
    }

    [Fact]
    public void CacheLikeRemainsApplicationDataHighRiskAndUnknownReclaim()
    {
        var node = UniversalFindingTestData.HierarchyNode(
            "Users\\Current\\AppData\\Local\\Thing\\Cache", 10);
        Finding finding = Assert.Single(builder.Build(
            UniversalFindingTestData.Request(hierarchyChildren: [node]), TestContext.Current.CancellationToken).Findings);
        Assert.Equal(FindingCategory.ApplicationData, finding.PrimaryCategory);
        Assert.Equal(Confidence.Medium, finding.Confidence);
        Assert.Equal(RiskLevel.High, finding.RiskAssessment.Level);
        Assert.Equal(ProtectionState.ReviewRequired, finding.ProtectionState);
        Assert.Equal(ReclaimKind.Unknown, finding.ReclaimEstimate.Kind);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CacheMatchingIsIndependentOfDriveOrGuidRootRepresentation(
        bool systemUsesGuid, bool contextUsesGuid)
    {
        string driveRoot = @"C:\";
        string guidRoot = @"\\?\Volume{" + UniversalFindingTestData.Volume.Id.ToString("D") + @"}\";
        string systemRoot = systemUsesGuid ? guidRoot : driveRoot;
        string contextRoot = contextUsesGuid ? guidRoot : driveRoot;
        var context = new StorageClassificationContext(UniversalFindingTestData.Volume,
            Join(contextRoot, "Windows"), [Join(contextRoot, "Program Files")],
            Join(contextRoot, "ProgramData"), Join(contextRoot, "Users\\Current"),
            [Join(contextRoot, "Users\\Current\\AppData\\Local")],
            Join(contextRoot, "Users\\Public"), Join(contextRoot, "Users"), []);
        StorageHierarchyNode node = UniversalFindingTestData.HierarchyNode(
            "Users\\Current\\AppData\\Local\\Vendor\\Cache", 10);
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.HierarchyNode,
            FindingCategory.ApplicationData,
            node.RelativePath,
            10,
            10,
            fileCount: 1,
            directoryCount: 0);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            hierarchy: [candidate], hierarchyChildren: [node], classificationContext: context,
            systemRoot: systemRoot, deriveHierarchyNodes: false),
            TestContext.Current.CancellationToken);
        Assert.NotEqual(AnalysisQuality.Unavailable, result.Quality);
        Finding finding = Assert.Single(result.Findings);
        Assert.Equal(FindingCategory.ApplicationData, finding.PrimaryCategory);
        Assert.Contains(FindingFacet.CacheLike, finding.Facets);
    }

    [Fact]
    public void SystemCandidateClassificationSupportsMixedRootRepresentation()
    {
        string guidRoot = @"\\?\Volume{" + UniversalFindingTestData.Volume.Id.ToString("D") + @"}\";
        var context = new StorageClassificationContext(UniversalFindingTestData.Volume,
            @"C:\Windows", [@"C:\Program Files"], @"C:\ProgramData", @"C:\Users\Current",
            [@"C:\Users\Current\AppData\Local"], @"C:\Users\Public", @"C:\Users", []);
        StorageHierarchyNode node = UniversalFindingTestData.HierarchyNode("Windows\\System32", 10);
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.System, node.RelativePath, 10, 10,
            fileCount: 1, directoryCount: 0);

        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            hierarchy: [candidate], hierarchyChildren: [node], classificationContext: context,
            systemRoot: guidRoot, deriveHierarchyNodes: false), TestContext.Current.CancellationToken);

        Assert.NotEqual(AnalysisQuality.Unavailable, result.Quality);
        Assert.Equal(FindingCategory.System, Assert.Single(result.Findings).PrimaryCategory);
    }

    [Fact]
    public void EveryGeneratedFindingUsesUnknownReclaim()
    {
        var categories = Enum.GetValues<FindingCategory>().Take(5)
            .ToDictionary(category => category, _ => (long?)10);
        var result = builder.Build(UniversalFindingTestData.Request(categories), TestContext.Current.CancellationToken);
        Assert.NotEmpty(result.Findings);
        Assert.All(result.Findings, finding =>
        {
            Assert.Equal(ReclaimKind.Unknown, finding.ReclaimEstimate.Kind);
            Assert.Null(finding.ReclaimEstimate.MinimumBytes);
            Assert.Null(finding.ReclaimEstimate.ExpectedBytes);
            Assert.Null(finding.ReclaimEstimate.MaximumBytes);
        });
    }

    private Finding BuildFile(long allocation, long capacity)
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, "Users\\Current\\large.bin",
            allocation, allocation);
        return Assert.Single(builder.Build(
            UniversalFindingTestData.Request(files: [candidate], capacityBytes: capacity), TestContext.Current.CancellationToken).Findings);
    }

    private static string Join(string root, string relative) => root + relative;
}
