using CDriveSmartClean.Application.Analysis;
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
        StorageAnalysisCandidate candidate = File("unknown", 10, FindingCategory.Unknown);
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
        StorageHierarchyNode node = UniversalFindingTestData.HierarchyNode("ProgramData\\cache\\leaf", 1);
        for (int depth = 0; depth < 2_000; depth++)
            node = UniversalFindingTestData.HierarchyNode($"ProgramData\\cache\\n{depth:D4}", 1, [node]);
        UniversalFindingResult result = builder.Build(UniversalFindingTestData.Request(
            hierarchyChildren: [node], options: new UniversalFindingOptions(25, 100, 3_000)), TestContext.Current.CancellationToken);
        Assert.NotEqual(AnalysisQuality.Unavailable, result.Quality);
        Assert.Contains(result.Findings, item => item.Facets.Contains(FindingFacet.CacheLike));
    }

    [Fact]
    public void HierarchyResourceLimitPublishesNoPrefix()
    {
        StorageHierarchyNode node = UniversalFindingTestData.HierarchyNode("ProgramData\\cache\\leaf", 1);
        for (int depth = 0; depth < 200; depth++)
            node = UniversalFindingTestData.HierarchyNode($"ProgramData\\cache\\n{depth:D3}", 1, [node]);
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
}
