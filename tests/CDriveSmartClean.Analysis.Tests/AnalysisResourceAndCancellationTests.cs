using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class AnalysisResourceAndCancellationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ResourceLimitsDegradeAnalysisButContinueAcceptingEntries(int guard)
    {
        StorageAnalysisOptions options = guard switch
        {
            0 => new(maximumPathStates: 1),
            1 => new(maximumIdentityStates: 1),
            _ => new(analysisStateBudget: 1),
        };
        StorageEntry[] entries =
        [
            TestData.Entry("a", TestData.Identity(), 1),
            TestData.Entry("b", TestData.Identity(), 2),
            TestData.Entry("c", TestData.Identity(), 3),
        ];
        IStorageAnalysisSession session = Session(options);
        CancellationToken token = TestContext.Current.CancellationToken;
        foreach (StorageEntry entry in entries) await session.WriteAsync(entry, token);
        StorageAnalysisResult result = session.Complete(TestData.Accounting(entries), token);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.ResourceLimit));
        Assert.Empty(result.CategorySummaries);
        Assert.Empty(result.LargestHierarchyCandidates);
        Assert.Empty(result.LargestIdentityCandidates);
        Assert.Empty(result.LargestFileCandidates);
    }

    [Fact]
    public async Task CrossVolumeValidationRemainsActiveAfterDegradation()
    {
        IStorageAnalysisSession session = Session(new StorageAnalysisOptions(analysisStateBudget: 1));
        await session.WriteAsync(TestData.Entry("a", TestData.Identity(), 1), TestContext.Current.CancellationToken);
        var other = new VolumeIdentity(Guid.NewGuid());
        var cross = new StorageEntry(other, null, @"C:\cross", StorageObjectKind.File, ReparseKind.None);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.WriteAsync(cross, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ArithmeticOverflowProducesUnavailableWithoutThrowingFromSink()
    {
        StorageEntry first = TestData.Entry("a", TestData.Identity(), long.MaxValue);
        StorageEntry second = TestData.Entry("b", TestData.Identity(), 1);
        IStorageAnalysisSession session = Session();
        CancellationToken token = TestContext.Current.CancellationToken;
        await session.WriteAsync(first, token);
        await session.WriteAsync(second, token);
        StorageAnalysisResult result = session.Complete(ZeroAccounting(first, second), token);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.ArithmeticOverflow));
        Assert.Empty(result.CategorySummaries);
    }

    [Fact]
    public async Task CancellationDuringWritePropagates()
    {
        IStorageAnalysisSession session = Session();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await session.WriteAsync(TestData.Entry("a", TestData.Identity(), 1), source.Token));
    }

    [Fact]
    public async Task CancellationDuringCompletePropagatesWithoutResult()
    {
        StorageEntry entry = TestData.Entry("a", TestData.Identity(), 1);
        IStorageAnalysisSession session = Session();
        await session.WriteAsync(entry, TestContext.Current.CancellationToken);
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            session.Complete(TestData.Accounting([entry]), source.Token));
    }

    [Fact]
    public async Task ExactDuplicateIsIdempotent()
    {
        StorageEntry entry = TestData.Entry("a", TestData.Identity(), 1);
        IStorageAnalysisSession session = Session();
        CancellationToken token = TestContext.Current.CancellationToken;
        await session.WriteAsync(entry, token);
        await session.WriteAsync(entry, token);
        StorageAnalysisResult result = session.Complete(TestData.Accounting([entry]), token);
        Assert.Equal(1, result.CategorySummaries.Sum(summary => summary.PathCount));
    }

    private static IStorageAnalysisSession Session(StorageAnalysisOptions? options = null) =>
        new UniversalStorageAnalyzer().CreateSession(new StorageAnalysisRequest(
            TestData.SystemVolume, TestData.Context(), options));

    private static StorageAccountingResult ZeroAccounting(params StorageEntry[] entries)
    {
        AllocationGroup[] groups = entries.Select(entry => new AllocationGroup(entry.ObjectIdentity!,
            [entry.CanonicalPath[3..]], entry.Measurement.ReportedAllocatedBytes, AccountingReason.None, "")).ToArray();
        var aggregate = new StorageAggregate();
        var root = new StorageHierarchyNode("", aggregate, []);
        var summary = new StorageAccountingSummary(aggregate, AccountingReason.None);
        var snapshot = VolumeSpaceSnapshot.Available(TestData.Volume, DateTimeOffset.UnixEpoch,
            100, 100, 100, 100, 0, 0, 0);
        return new StorageAccountingResult(summary, root, groups,
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<CDriveSmartClean.Application.Scanning.Traversal.StorageTraversalIssueKind, long>());
    }
}
