using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class LargestCandidateTests
{
    [Fact]
    public async Task HierarchyUsesInclusiveAllocationAndExcludesSyntheticRoot()
    {
        var a = TestData.Entry("a", TestData.Identity(), 5);
        var b = TestData.Entry("b", TestData.Identity(), 10);
        StorageAccountingResult accounting = HierarchyAccounting(a, b);
        StorageAnalysisResult result = await TestData.Analyze([a, b], accounting: accounting);
        Assert.Equal(["b", "a"], result.LargestHierarchyCandidates.Select(item => item.RelativePaths[0]));
        Assert.DoesNotContain(result.LargestHierarchyCandidates, item => item.RelativePaths[0].Length == 0);
        Assert.Equal(10, result.LargestHierarchyCandidates[0].SizeEvidence.ObservedAttributedAllocatedBytes);
    }

    [Fact]
    public async Task IdentityAndSinglePathFileUseEligibleAllocation()
    {
        var entry = TestData.Entry("a", TestData.Identity(), 10);
        StorageAnalysisResult result = await TestData.Analyze([entry]);
        Assert.Equal(10, Assert.Single(result.LargestIdentityCandidates)
            .SizeEvidence.ObservedAttributedAllocatedBytes);
        Assert.Equal(10, Assert.Single(result.LargestFileCandidates)
            .SizeEvidence.ObservedAttributedAllocatedBytes);
    }

    [Fact]
    public async Task MultiAliasIdentityDoesNotCreatePhysicalFileCandidates()
    {
        StorageObjectIdentity id = TestData.Identity();
        StorageAnalysisResult result = await TestData.Analyze(
            [TestData.Entry("a", id, 10), TestData.Entry("b", id, 10)]);
        Assert.Single(result.LargestIdentityCandidates);
        Assert.Empty(result.LargestFileCandidates);
    }

    [Fact]
    public async Task UnknownViewContainsOnlyUnknownCandidates()
    {
        StorageAnalysisResult result = await TestData.Analyze(
            [TestData.Entry("a", TestData.Identity(), 10),
             TestData.Entry(@"Windows\b", TestData.Identity(), 20)]);
        Assert.NotEmpty(result.LargestUnknownCandidates);
        Assert.All(result.LargestUnknownCandidates,
            candidate => Assert.Equal(FindingCategory.Unknown, candidate.PrimaryCategory));
    }

    [Fact]
    public async Task CandidateLimitOneKeepsLargest()
    {
        var options = new StorageAnalysisOptions(candidateLimit: 1);
        StorageAnalysisResult result = await TestData.Analyze(
            [TestData.Entry("small", TestData.Identity(), 1),
             TestData.Entry("large", TestData.Identity(), 9)], options: options);
        Assert.Single(result.LargestFileCandidates);
        Assert.Equal("large", result.LargestFileCandidates[0].RelativePaths[0]);
    }

    [Fact]
    public async Task DefaultLimitIsOneHundredAndSelectionIsBounded()
    {
        StorageEntry[] entries = Enumerable.Range(1, 110)
            .Select(value => TestData.Entry($"f{value:000}", TestData.Identity(), value)).ToArray();
        StorageAnalysisResult result = await TestData.Analyze(entries);
        Assert.Equal(100, result.LargestFileCandidates.Count);
        Assert.Equal(110, result.LargestFileCandidates[0].SizeEvidence.ObservedAttributedAllocatedBytes);
    }

    [Fact]
    public async Task MaximumLimitOneThousandIsAllowed()
    {
        var options = new StorageAnalysisOptions(candidateLimit: 1000);
        StorageAnalysisResult result = await TestData.Analyze(
            [TestData.Entry("a", TestData.Identity(), 1)], options: options);
        Assert.Single(result.LargestFileCandidates);
    }

    [Fact]
    public async Task TiesAndInputPermutationsUseOrdinalPathOrder()
    {
        var a = TestData.Entry("a", TestData.Identity(), 10);
        var b = TestData.Entry("b", TestData.Identity(), 10);
        StorageAnalysisResult left = await TestData.Analyze([b, a]);
        StorageAnalysisResult right = await TestData.Analyze([a, b]);
        Assert.Equal(["a", "b"], left.LargestFileCandidates.Select(item => item.RelativePaths[0]));
        Assert.Equal(left.LargestFileCandidates.Select(item => item.RelativePaths[0]),
            right.LargestFileCandidates.Select(item => item.RelativePaths[0]));
    }

    [Fact]
    public async Task RankingDoesNotCreateLargeFacet()
    {
        StorageAnalysisResult result = await TestData.Analyze(
            [TestData.Entry("huge", TestData.Identity(), 900_000)]);
        Assert.All(result.LargestFileCandidates,
            candidate => Assert.DoesNotContain(FindingFacet.Large, candidate.Facets));
    }

    [Fact]
    public async Task TwoThousandLevelHierarchyIsTraversedIterativelyAndRemainsBounded()
    {
        const int depth = 2000;
        var aggregate = new StorageAggregate();
        string[] paths = new string[depth];
        paths[0] = "d";
        for (int index = 1; index < paths.Length; index++)
            paths[index] = paths[index - 1] + "\\d";
        StorageHierarchyNode current = new(paths[^1], aggregate, []);
        for (int index = paths.Length - 2; index >= 0; index--)
            current = new StorageHierarchyNode(paths[index], aggregate, [current]);
        var root = new StorageHierarchyNode("", aggregate, [current]);
        var summary = new StorageAccountingSummary(aggregate, AccountingReason.None);
        var snapshot = VolumeSpaceSnapshot.Available(TestData.Volume, DateTimeOffset.UnixEpoch,
            1_000_000, 1_000_000, 1_000_000, 1_000_000, 0, 0, 0);
        var accounting = new StorageAccountingResult(summary, root, [],
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<CDriveSmartClean.Application.Scanning.Traversal.StorageTraversalIssueKind, long>());

        StorageAnalysisResult result = await TestData.Analyze([], accounting: accounting);

        Assert.Equal(AnalysisQuality.Complete, result.Quality);
        Assert.Equal(100, result.LargestHierarchyCandidates.Count);
        Assert.All(result.LargestHierarchyCandidates,
            candidate => Assert.NotEmpty(candidate.RelativePaths[0]));
    }

    private static StorageAccountingResult HierarchyAccounting(params CDriveSmartClean.Application.Scanning.Enumeration.StorageEntry[] entries)
    {
        AllocationGroup[] groups = entries.Select(entry => new AllocationGroup(entry.ObjectIdentity!,
            [entry.CanonicalPath[3..]], entry.Measurement.ReportedAllocatedBytes, AccountingReason.None, "")).ToArray();
        StorageHierarchyNode[] children = entries.Select(entry =>
            new StorageHierarchyNode(entry.CanonicalPath[3..],
                new StorageAggregate(visibleLogicalMeasuredBytes: entry.Measurement.LogicalBytes!.Value,
                    rawReportedAllocatedBytes: entry.Measurement.ReportedAllocatedBytes!.Value,
                    directAttributedObservedAllocatedBytes: entry.Measurement.ReportedAllocatedBytes.Value,
                    inclusiveAttributedObservedAllocatedBytes: entry.Measurement.ReportedAllocatedBytes.Value,
                    fileCount: 1), [])).ToArray();
        long total = entries.Sum(entry => entry.Measurement.ReportedAllocatedBytes!.Value);
        var aggregate = new StorageAggregate(visibleLogicalMeasuredBytes: total,
            rawReportedAllocatedBytes: total, directAttributedObservedAllocatedBytes: total,
            inclusiveAttributedObservedAllocatedBytes: total, fileCount: entries.Length);
        var root = new StorageHierarchyNode("", aggregate, children);
        var summary = new StorageAccountingSummary(aggregate, AccountingReason.None);
        var snapshot = VolumeSpaceSnapshot.Available(TestData.Volume, DateTimeOffset.UnixEpoch,
            1_000_000, 1_000_000 - total, 1_000_000, 1_000_000 - total, total, 0, 0);
        return new StorageAccountingResult(summary, root, groups,
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<CDriveSmartClean.Application.Scanning.Traversal.StorageTraversalIssueKind, long>());
    }
}
