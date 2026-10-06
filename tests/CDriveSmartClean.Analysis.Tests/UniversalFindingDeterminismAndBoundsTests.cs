using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class UniversalFindingDeterminismAndBoundsTests
{
    private readonly UniversalFindingBuilder builder = new();

    [Fact]
    public void PermutedCandidateInputsProduceSameIdsAndOrdering()
    {
        StorageAnalysisCandidate a = File("a", 10);
        StorageAnalysisCandidate b = File("b", 20);
        StorageAnalysisCandidate c = File("c", 20);
        UniversalFindingResult first = builder.Build(
            UniversalFindingTestData.Request(files: [a, b, c]), TestContext.Current.CancellationToken);
        UniversalFindingResult second = builder.Build(
            UniversalFindingTestData.Request(files: [c, a, b]), TestContext.Current.CancellationToken);
        Assert.Equal(first.Findings.Select(item => item.Id), second.Findings.Select(item => item.Id));
        Assert.Equal(first.Findings.Select(item => item.DisplayName),
            second.Findings.Select(item => item.DisplayName));
    }

    [Fact]
    public void UnknownViewDuplicateIsDeduplicatedByStableKey()
    {
        StorageAnalysisCandidate candidate = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.Unknown, "Mystery\\unknown", 10, 10);
        UniversalFindingResult result = builder.Build(
            UniversalFindingTestData.Request(files: [candidate], unknown: [candidate]), TestContext.Current.CancellationToken);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void ContradictoryDuplicateReturnsInputMismatch()
    {
        StorageAnalysisCandidate first = File("same", 10);
        StorageAnalysisCandidate second = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, first.RelativePaths[0], 10, 11,
            identity: first.ObjectIdentity);
        UniversalFindingResult result = builder.Build(
            UniversalFindingTestData.Request(files: [first], unknown: [second]), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.InputMismatch));
        Assert.Empty(result.Findings);
    }

    [Theory]
    [InlineData("logical", false)]
    [InlineData("logical", true)]
    [InlineData("counts", false)]
    [InlineData("counts", true)]
    [InlineData("evidence", false)]
    [InlineData("evidence", true)]
    [InlineData("confidence", false)]
    [InlineData("confidence", true)]
    public void SameKeyContradictionsFailBeforeTopKInEitherOrder(string mutation, bool reverse)
    {
        StorageAnalysisCandidate first = File("same-key", 10);
        StorageAnalysisCandidate second = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, first.RelativePaths[0],
            mutation == "logical" ? 11 : 10, 10, identity: first.ObjectIdentity,
            confidence: mutation == "confidence" ? Confidence.Low : Confidence.High,
            fileCount: mutation == "counts" ? 2 : 1,
            additionalEvidence: mutation == "evidence"
                ? [new Evidence("semantic.shared", "second", Confidence.High)]
                : [new Evidence("semantic.shared", "first", Confidence.High)]);
        first = UniversalFindingTestData.Candidate(AnalysisCandidateScope.File, FindingCategory.UserData,
            first.RelativePaths[0], 10, 10, identity: first.ObjectIdentity,
            additionalEvidence: [new Evidence("semantic.shared", "first", Confidence.High)]);
        StorageAnalysisCandidate[] values = reverse ? [second, first] : [first, second];
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(files: values,
            options: new UniversalFindingOptions(1, 5, 100)), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.InputMismatch));
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void SameKeyCompatibleFacetsAndEvidenceMergeBeforeTopK()
    {
        StorageAnalysisCandidate first = File("merge", 10);
        StorageAnalysisCandidate sparse = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, first.RelativePaths[0], 10, 10,
            [FindingFacet.Sparse], first.ObjectIdentity);
        StorageAnalysisCandidate compressed = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, first.RelativePaths[0], 10, 10,
            [FindingFacet.Compressed], first.ObjectIdentity);
        Finding finding = Assert.Single(builder.Build(UniversalFindingTestData.Request(
            files: [sparse, compressed]), TestContext.Current.CancellationToken).Findings);
        Assert.Contains(FindingFacet.Sparse, finding.Facets);
        Assert.Contains(FindingFacet.Compressed, finding.Facets);
    }

    [Fact]
    public void CrossViewContradictionCannotBeHiddenByPerViewTopK()
    {
        StorageAnalysisCandidate genuine = File("small", 1);
        StorageAnalysisCandidate larger = File("large", 2);
        StorageAnalysisCandidate contradiction = UniversalFindingTestData.Candidate(
            AnalysisCandidateScope.File, FindingCategory.UserData, genuine.RelativePaths[0], 99, 1,
            identity: genuine.ObjectIdentity);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            files: [genuine, larger], unknown: [contradiction],
            options: new UniversalFindingOptions(1, 5, 100)), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.InputMismatch));
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void PerViewAndTotalBoundsAreEnforced()
    {
        StorageAnalysisCandidate[] files = Enumerable.Range(0, 20)
            .Select(index => File($"file-{index:D2}", index + 1)).ToArray();
        var options = new UniversalFindingOptions(3, 5, 100);
        UniversalFindingResult result = builder.Build(
            UniversalFindingTestData.Request(files: files, options: options), TestContext.Current.CancellationToken);
        Assert.True(result.Findings.Count <= 3);
    }

    [Fact]
    public void TopKRetainsLargestThreeItems()
    {
        StorageAnalysisCandidate[] files = [File("one", 1), File("three", 3), File("two", 2), File("four", 4)];
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(files: files,
            options: new UniversalFindingOptions(3, 5, 100)), TestContext.Current.CancellationToken);
        Assert.Equal([4L, 3L, 2L], result.Findings.Select(item => item.SizeMetrics.AllocatedBytes));
    }

    [Fact]
    public void CategoryReservationHonorsMaximumTotalFindings()
    {
        var categories = new Dictionary<FindingCategory, long?>
        {
            [FindingCategory.System] = 1,
            [FindingCategory.Application] = 1,
            [FindingCategory.ApplicationData] = 1,
            [FindingCategory.UserData] = 1,
            [FindingCategory.Unknown] = 1
        };
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(categories,
            files: [File("extra", 100)], options: new UniversalFindingOptions(5, 5, 100)), TestContext.Current.CancellationToken);
        Assert.Equal(5, result.Findings.Count);
        Assert.All(result.Findings, item => Assert.Equal(FindingScope.CategoryAggregate, item.Scope));
    }

    [Fact]
    public void Deep2000LevelHierarchyIsTraversedIteratively()
    {
        StorageHierarchyNode node = DeepHierarchy(2_000);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            hierarchyChildren: [node], options: new UniversalFindingOptions(25, 100, 3_000)), TestContext.Current.CancellationToken);
        Assert.NotEqual(AnalysisQuality.Unavailable, result.Quality);
        Assert.Contains(result.Findings, item => item.Facets.Contains(FindingFacet.CacheLike));
    }

    [Fact]
    public void HierarchyResourceLimitPublishesNoPrefix()
    {
        StorageHierarchyNode node = DeepHierarchy(200);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            hierarchyChildren: [node], options: new UniversalFindingOptions(25, 100, 100)), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(UniversalFindingReason.ResourceLimit));
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void CancellationPropagates()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            builder.Build(UniversalFindingTestData.Request(), source.Token));
    }

    private static StorageAnalysisCandidate File(
        string name, long allocated, FindingCategory category = FindingCategory.UserData) =>
        UniversalFindingTestData.Candidate(AnalysisCandidateScope.File, category,
            $"Users\\Current\\{name}", allocated, allocated);

    private static StorageHierarchyNode DeepHierarchy(int depth)
    {
        var paths = new string[depth + 1];
        paths[0] = "ProgramData\\cache";
        for (int index = 1; index <= depth; index++) paths[index] = $"{paths[index - 1]}\\n{index:D4}";
        StorageHierarchyNode node = UniversalFindingTestData.HierarchyNode(paths[^1], 1);
        for (int index = depth - 1; index >= 0; index--)
            node = UniversalFindingTestData.HierarchyNode(paths[index], 1, [node]);
        return node;
    }
}
