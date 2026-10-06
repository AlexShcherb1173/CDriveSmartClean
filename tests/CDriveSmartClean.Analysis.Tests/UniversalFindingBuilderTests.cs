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
            AnalysisCandidateScope.IdentityGroup, FindingCategory.ApplicationData,
            "Users\\Current\\AppData\\Local\\alias-a", 40, 30,
            [FindingFacet.HardLinked], identity,
            ["Users\\Current\\AppData\\Local\\alias-b", "Users\\Current\\AppData\\Local\\alias-a"]);
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

    [Theory]
    [InlineData("identity")]
    [InlineData("path")]
    [InlineData("allocation")]
    [InlineData("counts")]
    [InlineData("foreign-group")]
    [InlineData("missing-group")]
    [InlineData("group-path")]
    public void FileCandidateMustJoinExactEligibleAllocationGroup(string mutation)
    {
        StorageAnalysisCandidate genuine = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, "Users\\Current\\file.bin", 20, 10);
        StorageObjectIdentity identity = genuine.ObjectIdentity!;
        StorageAnalysisCandidate candidate = mutation switch
        {
            "identity" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.File,
                FindingCategory.UserData, genuine.RelativePaths[0], 20, 10,
                identity: new StorageObjectIdentity(UniversalFindingTestData.Volume, Guid.NewGuid())),
            "path" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.File,
                FindingCategory.UserData, "Users\\Current\\other.bin", 20, 10, identity: identity),
            "allocation" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.File,
                FindingCategory.UserData, genuine.RelativePaths[0], 20, 11, identity: identity),
            "counts" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.File,
                FindingCategory.UserData, genuine.RelativePaths[0], 20, 10, identity: identity, fileCount: 2),
            _ => genuine
        };
        var genuineGroup = new AllocationGroup(identity, genuine.RelativePaths, 10,
            AccountingReason.None, genuine.RelativePaths[0]);
        AllocationGroup[] groups = mutation switch
        {
            "missing-group" => [],
            "group-path" => [new AllocationGroup(identity, ["Users\\Current\\other.bin"], 10,
                AccountingReason.None, "Users\\Current\\other.bin")],
            "foreign-group" => [genuineGroup, new AllocationGroup(
                new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid()), ["foreign"], 1,
                AccountingReason.None, "foreign")],
            _ => [genuineGroup]
        };
        AssertInputMismatch(UniversalFindingTestData.Request(files: [candidate], allocationGroups: groups));
    }

    [Fact]
    public void PhysicalFileCandidateIsRejectedWhenAccountingIsUnavailable()
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, "Users\\Current\\file.bin", 20, 10);
        AssertInputMismatch(UniversalFindingTestData.Request(files: [candidate], allocationGroups: [],
            accountingReasons: AccountingReason.ResourceLimit));
    }

    [Fact]
    public void KnownAccountingDedupRejectsAnyNullCategoryValue()
    {
        CategorySummary[] summaries = Summaries(category => category == FindingCategory.System ? null : 0,
            category => category == FindingCategory.System ? 10 : 0);
        AssertInputMismatch(UniversalFindingTestData.Request(categorySummaries: summaries,
            accountingAggregate: new StorageAggregate(rawReportedAllocatedBytes: 10,
                inclusiveAttributedObservedAllocatedBytes: 10)));
    }

    [Fact]
    public void KnownAccountingDedupRejectsCategorySumMismatch()
    {
        CategorySummary[] summaries = Summaries(category => category == FindingCategory.System ? 9 : 0,
            category => category == FindingCategory.System ? 10 : 0);
        AssertInputMismatch(UniversalFindingTestData.Request(categorySummaries: summaries,
            accountingAggregate: new StorageAggregate(rawReportedAllocatedBytes: 10,
                inclusiveAttributedObservedAllocatedBytes: 10)));
    }

    [Fact]
    public void NullAccountingDedupAcceptsNullCategoryValuesWithoutFabrication()
    {
        CategorySummary[] summaries = Summaries(_ => null, _ => 0);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            categorySummaries: summaries, allocationGroups: [],
            accountingReasons: AccountingReason.ResourceLimit), TestContext.Current.CancellationToken);
        Assert.False(result.Reasons.HasFlag(UniversalFindingReason.InputMismatch));
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void UnavailableAccountingRejectsCallerKnownCategoryDedup()
    {
        const long large = 3L * 1_073_741_824;
        CategorySummary[] summaries = Summaries(
            category => category == FindingCategory.System ? large : null,
            category => category == FindingCategory.System ? large : 0);

        AssertInputMismatch(UniversalFindingTestData.Request(
            categorySummaries: summaries,
            allocationGroups: [],
            accountingReasons: AccountingReason.ResourceLimit));
    }

    [Fact]
    public void UnavailableAccountingAllowsOnlyNonAuthoritativeCategoryEvidence()
    {
        CategorySummary[] summaries = Enum.GetValues<FindingCategory>().Select(category => new CategorySummary(
            category,
            null,
            category == FindingCategory.System ? 10 : 0,
            category == FindingCategory.System ? 2 : 0,
            category == FindingCategory.System ? 1 : 0,
            0,
            AnalysisQuality.Incomplete,
            AnalysisReason.UpstreamAccountingUnavailable)).ToArray();

        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            categorySummaries: summaries,
            allocationGroups: [],
            accountingReasons: AccountingReason.ResourceLimit,
            analysisQuality: AnalysisQuality.Incomplete,
            analysisReasons: AnalysisReason.UpstreamAccountingUnavailable), TestContext.Current.CancellationToken);

        Assert.False(result.Reasons.HasFlag(UniversalFindingReason.InputMismatch));
        Finding finding = Assert.Single(result.Findings);
        Assert.Equal(FindingCategory.System, finding.PrimaryCategory);
        Assert.Null(finding.SizeMetrics.AllocatedBytes);
        Assert.Null(finding.SizeMetrics.ExclusiveAllocatedBytes);
        Assert.DoesNotContain(FindingFacet.Large, finding.Facets);
        Assert.Equal(ReclaimKind.Unknown, finding.ReclaimEstimate.Kind);
    }

    [Fact]
    public void CategoryRawAndDeferredValuesMustRemainCoherent()
    {
        CategorySummary[] rawMismatch = Summaries(_ => 0,
            category => category == FindingCategory.System ? 9 : 0);
        AssertInputMismatch(UniversalFindingTestData.Request(categorySummaries: rawMismatch,
            accountingAggregate: new StorageAggregate(rawReportedAllocatedBytes: 10)));

        CategorySummary[] deferred = Summaries(category => category == FindingCategory.Temporary ? 1 : 0,
            category => category == FindingCategory.Temporary ? 1 : 0);
        AssertInputMismatch(UniversalFindingTestData.Request(categorySummaries: deferred,
            accountingAggregate: new StorageAggregate(rawReportedAllocatedBytes: 1,
                inclusiveAttributedObservedAllocatedBytes: 1)));
    }

    [Theory]
    [InlineData("identity")]
    [InlineData("swapped")]
    [InlineData("extra")]
    [InlineData("missing")]
    [InlineData("allocation")]
    [InlineData("counts")]
    public void IdentityCandidateMustJoinExactEligibleAllocationGroup(string mutation)
    {
        string[] paths = ["Users\\Current\\AppData\\Local\\a", "Users\\Current\\AppData\\Local\\b"];
        var identity = new StorageObjectIdentity(UniversalFindingTestData.Volume, Guid.NewGuid());
        StorageAnalysisCandidate genuine = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.IdentityGroup, FindingCategory.ApplicationData, paths[0], 20, 10,
            [FindingFacet.HardLinked], identity, paths);
        StorageAnalysisCandidate candidate = mutation switch
        {
            "identity" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.IdentityGroup,
                FindingCategory.ApplicationData, paths[0], 20, 10, [FindingFacet.HardLinked],
                new StorageObjectIdentity(UniversalFindingTestData.Volume, Guid.NewGuid()), paths),
            "swapped" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.IdentityGroup,
                FindingCategory.ApplicationData, paths[0], 20, 10, [FindingFacet.HardLinked], identity,
                ["Users\\Current\\AppData\\Local\\c", paths[1]]),
            "extra" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.IdentityGroup,
                FindingCategory.ApplicationData, paths[0], 20, 10, [FindingFacet.HardLinked], identity,
                [.. paths, "Users\\Current\\AppData\\Local\\c"]),
            "missing" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.IdentityGroup,
                FindingCategory.ApplicationData, paths[0], 20, 10, identity: identity, paths: [paths[0]]),
            "allocation" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.IdentityGroup,
                FindingCategory.ApplicationData, paths[0], 20, 11, [FindingFacet.HardLinked], identity, paths),
            "counts" => UniversalFindingTestData.Candidate(AnalysisCandidateScope.IdentityGroup,
                FindingCategory.ApplicationData, paths[0], 20, 10, [FindingFacet.HardLinked], identity, paths,
                fileCount: 2),
            _ => genuine
        };
        var group = new AllocationGroup(identity, paths, 10, AccountingReason.None, paths[0]);
        AssertInputMismatch(UniversalFindingTestData.Request(identities: [candidate], allocationGroups: [group]));
    }

    [Fact]
    public void ContradictorySinglePathIdentityIsNotHiddenBySuppression()
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.IdentityGroup, FindingCategory.Unknown, "only", 1, 2);
        var group = new AllocationGroup(candidate.ObjectIdentity!, candidate.RelativePaths, 1,
            AccountingReason.None, candidate.RelativePaths[0]);
        AssertInputMismatch(UniversalFindingTestData.Request(identities: [candidate], allocationGroups: [group]));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("logical")]
    [InlineData("raw")]
    [InlineData("observed")]
    [InlineData("uncertain")]
    [InlineData("files")]
    [InlineData("directories")]
    [InlineData("multipath")]
    public void HierarchyCandidateMustMatchExactAggregate(string mutation)
    {
        const string path = "Program Files\\Area";
        StorageHierarchyNode node = new(path, new StorageAggregate(20, 11, 2, 3, 10,
            fileCount: 4, directoryCount: 5), []);
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.Application,
            mutation == "path" ? "Program Files\\Other" : path,
            mutation == "logical" ? 21 : 20, mutation == "observed" ? 9 : 10,
            paths: mutation == "multipath" ? [path, "Program Files\\Other"] : null,
            rawAllocatedBytes: mutation == "raw" ? 12 : 11,
            observedAllocatedBytes: mutation == "observed" ? 9 : 10,
            uncertainAllocatedBytes: mutation == "uncertain" ? 3 : 2,
            fileCount: mutation == "files" ? 6 : 4,
            directoryCount: mutation == "directories" ? 6 : 5);
        AssertInputMismatch(UniversalFindingTestData.Request(hierarchy: [candidate], hierarchyChildren: [node],
            deriveHierarchyNodes: false));
    }

    [Theory]
    [InlineData(AnalysisCandidateScope.File, FindingCategory.System, "Users\\Current\\file.bin")]
    [InlineData(AnalysisCandidateScope.IdentityGroup, FindingCategory.Unknown,
        "Users\\Current\\AppData\\Local\\a")]
    [InlineData(AnalysisCandidateScope.HierarchyNode, FindingCategory.System, "Program Files\\Area")]
    public void CandidateClassificationMustMatchDeterministicPathRules(
        AnalysisCandidateScope scope, FindingCategory category, string path)
    {
        string[] paths = scope == AnalysisCandidateScope.IdentityGroup
            ? [path, "Users\\Current\\AppData\\Local\\b"] : [path];
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(scope, category, path, 10, 10,
            identity: scope == AnalysisCandidateScope.HierarchyNode ? null :
                new StorageObjectIdentity(UniversalFindingTestData.Volume, Guid.NewGuid()), paths: paths);
        AssertInputMismatch(UniversalFindingTestData.Request(
            hierarchy: scope == AnalysisCandidateScope.HierarchyNode ? [candidate] : null,
            identities: scope == AnalysisCandidateScope.IdentityGroup ? [candidate] : null,
            files: scope == AnalysisCandidateScope.File ? [candidate] : null));
    }

    [Fact]
    public void JoinedFileAndIdentityWithholdLogicalWhileHierarchyRetainsIt()
    {
        StorageAnalysisCandidate file = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, "Users\\Current\\file", 31, 11);
        StorageAnalysisCandidate identity = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.IdentityGroup, FindingCategory.ApplicationData,
            "Users\\Current\\AppData\\Local\\a", 32, 12, [FindingFacet.HardLinked], paths:
            ["Users\\Current\\AppData\\Local\\a", "Users\\Current\\AppData\\Local\\b"]);
        StorageAnalysisCandidate hierarchy = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.Application, "Program Files\\Area", 33, 13);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            hierarchy: [hierarchy], identities: [identity], files: [file]), TestContext.Current.CancellationToken);
        Assert.Null(result.Findings.Single(item => item.Scope == FindingScope.File).SizeMetrics.LogicalBytes);
        Assert.Null(result.Findings.Single(item => item.Scope == FindingScope.IdentityGroup).SizeMetrics.LogicalBytes);
        Assert.Equal(33, result.Findings.Single(item => item.Scope == FindingScope.HierarchyArea).SizeMetrics.LogicalBytes);
    }

    [Theory]
    [InlineData("finding.reclaim.unknown", "No deterministic reclaim amount has been established.", Confidence.Unknown)]
    [InlineData("finding.scope.file", "conflict", Confidence.Low)]
    [InlineData("finding.size.observed_allocated", "conflict", Confidence.Verified)]
    [InlineData("finding.facet.large", "conflict", Confidence.Verified)]
    [InlineData("finding.cache_like.application_data_component", "conflict", Confidence.Medium)]
    [InlineData("finding.future.reserved", "conflict", Confidence.High)]
    public void ReservedEvidenceCollisionFailsClosed(string code, string description, Confidence confidence)
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, "Users\\Current\\file", 1, 1,
            additionalEvidence: [new Evidence(code, description, confidence)]);
        AssertInputMismatch(UniversalFindingTestData.Request(files: [candidate]));
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

    private void AssertInputMismatch(UniversalFindingRequest request)
    {
        UniversalFindingResult result = builder.Build(request, TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.InputMismatch));
        Assert.Empty(result.Findings);
    }

    private static CategorySummary[] Summaries(
        Func<FindingCategory, long?> deduplicated, Func<FindingCategory, long> raw) =>
        Enum.GetValues<FindingCategory>().Select(category => new CategorySummary(category,
            deduplicated(category), raw(category), 0, 0, 0, AnalysisQuality.Complete, AnalysisReason.None)).ToArray();
}
