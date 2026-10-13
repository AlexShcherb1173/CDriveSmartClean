using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Scan.Tests.Accounting;

public sealed class StorageAccountingEngineTests
{
    [Fact]
    public async Task CancellationAfterForwardingStillPropagates()
    {
        var h = new AccountingHarness();
        using var source = new CancellationTokenSource();
        h.Entries = [h.Entry("a", h.Id()), h.Entry("b", h.Id())];
        h.AfterEntry = source.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Run(token: source.Token));
        Assert.Single(h.Forwarded);
        Assert.Equal(1, h.Snapshots);
    }

    [Fact]
    public async Task SnapshotIdentityMismatchIsFatalBeforeTraversal()
    {
        var h = new AccountingHarness { WrongSnapshotIdentity = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Run());
        Assert.Empty(h.Forwarded);
    }

    [Fact]
    public async Task FinalizationBudgetCannotPublishPrefix()
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry("a", h.Id())];
        var result = await h.Run(new(accountingStateBudget: 1000));
        Assert.Single(h.Forwarded);
        Assert.Null(result.Root);
        Assert.Null(result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.True(result.TraversalCompleted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ResourceLimitsContinueEntriesAndIssuesWithoutPrefix(int guard)
    {
        var h = new AccountingHarness { ChildError = new UnauthorizedAccessException() };
        h.Entries = [h.Entry("one", h.Id()), h.Entry("two", h.Id()), h.Entry("dir", h.Id(), kind: StorageObjectKind.Directory)];
        var options = guard switch
        {
            0 => new StorageAccountingOptions(maximumIdentities: 1),
            1 => new StorageAccountingOptions(maximumDistinctPaths: 1),
            2 => new StorageAccountingOptions(maximumDirectories: 1),
            _ => new StorageAccountingOptions(accountingStateBudget: 1),
        };
        var result = await h.Run(options);
        Assert.True(result.TraversalCompleted);
        Assert.Equal(3, h.Forwarded.Count);
        Assert.Single(h.ForwardedIssues);
        Assert.Equal(2, h.Snapshots);
        Assert.Null(result.Root);
        Assert.Empty(result.AllocationGroups);
        Assert.Null(result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Null(result.Reconciliation.ObservedCoveragePercent);
        Assert.True(result.Summary.Reasons.HasFlag(AccountingReason.ResourceLimit));
        ResourceLimitDiagnostic diagnostic = Assert.IsType<ResourceLimitDiagnostic>(
            result.ResourceLimitDiagnostic);
        Assert.Equal(ResourceLimitStage.Accounting, diagnostic.Stage);
        Assert.Equal(guard switch
        {
            0 => ResourceLimitDimension.MaximumIdentities,
            1 => ResourceLimitDimension.MaximumDistinctPaths,
            2 => ResourceLimitDimension.MaximumDirectories,
            _ => ResourceLimitDimension.AccountingStateBudget,
        }, diagnostic.Dimension);
        Assert.Equal(1, diagnostic.ConfiguredLimit);
        Assert.Equal(guard == 3 ? 256 : 2, diagnostic.ObservedOrAttemptedValue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PathAndIdentityCountLimitAcceptsLimitAndRejectsLimitPlusOne(bool identityLimit)
    {
        var accepted = new AccountingHarness();
        accepted.Entries = [accepted.Entry("a", accepted.Id()), accepted.Entry("b", accepted.Id())];
        StorageAccountingOptions options = identityLimit
            ? new StorageAccountingOptions(maximumIdentities: 2)
            : new StorageAccountingOptions(maximumDistinctPaths: 2);
        StorageAccountingResult acceptedResult = await accepted.Run(options);
        Assert.NotNull(acceptedResult.Root);
        Assert.Null(acceptedResult.ResourceLimitDiagnostic);

        var rejected = new AccountingHarness();
        rejected.Entries =
        [
            rejected.Entry("a", rejected.Id()),
            rejected.Entry("b", rejected.Id()),
            rejected.Entry("c", rejected.Id()),
        ];
        StorageAccountingResult rejectedResult = await rejected.Run(options);
        ResourceLimitDiagnostic diagnostic = Assert.IsType<ResourceLimitDiagnostic>(
            rejectedResult.ResourceLimitDiagnostic);
        Assert.Equal(identityLimit
            ? ResourceLimitDimension.MaximumIdentities
            : ResourceLimitDimension.MaximumDistinctPaths, diagnostic.Dimension);
        Assert.Equal(2, diagnostic.ConfiguredLimit);
        Assert.Equal(3, diagnostic.ObservedOrAttemptedValue);
    }

    [Fact]
    public async Task DirectoryCountIsRootInclusiveAtExactBoundary()
    {
        var accepted = new AccountingHarness();
        accepted.Entries = [accepted.Entry("one", accepted.Id(), kind: StorageObjectKind.Directory)];
        StorageAccountingResult acceptedResult = await accepted.Run(
            new StorageAccountingOptions(maximumDirectories: 2));
        Assert.NotNull(acceptedResult.Root);
        Assert.Null(acceptedResult.ResourceLimitDiagnostic);

        var rejected = new AccountingHarness();
        rejected.Entries =
        [
            rejected.Entry("one", rejected.Id(), kind: StorageObjectKind.Directory),
            rejected.Entry("two", rejected.Id(), kind: StorageObjectKind.Directory),
        ];
        StorageAccountingResult rejectedResult = await rejected.Run(
            new StorageAccountingOptions(maximumDirectories: 2));
        ResourceLimitDiagnostic diagnostic = Assert.IsType<ResourceLimitDiagnostic>(
            rejectedResult.ResourceLimitDiagnostic);
        Assert.Equal(ResourceLimitDimension.MaximumDirectories, diagnostic.Dimension);
        Assert.Equal(2, diagnostic.ConfiguredLimit);
        Assert.Equal(3, diagnostic.ObservedOrAttemptedValue);
    }

    [Fact]
    public async Task AccountingBudgetAcceptsExactChargeAndReportsOneByteShortAttempt()
    {
        const long required = 2_106;
        var accepted = new AccountingHarness();
        accepted.Entries = [accepted.Entry("a", accepted.Id())];
        StorageAccountingResult acceptedResult = await accepted.Run(
            new StorageAccountingOptions(accountingStateBudget: required));
        Assert.NotNull(acceptedResult.Root);
        Assert.Null(acceptedResult.ResourceLimitDiagnostic);

        var rejected = new AccountingHarness();
        rejected.Entries = [rejected.Entry("a", rejected.Id())];
        StorageAccountingResult rejectedResult = await rejected.Run(
            new StorageAccountingOptions(accountingStateBudget: required - 1));
        ResourceLimitDiagnostic diagnostic = Assert.IsType<ResourceLimitDiagnostic>(
            rejectedResult.ResourceLimitDiagnostic);
        Assert.Equal(ResourceLimitDimension.AccountingStateBudget, diagnostic.Dimension);
        Assert.Equal(required - 1, diagnostic.ConfiguredLimit);
        Assert.Equal(required, diagnostic.ObservedOrAttemptedValue);
    }

    [Fact]
    public async Task OverflowDoesNotPublishPrefix()
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry("a", h.Id(), bytes: long.MaxValue), h.Entry("b", h.Id(), bytes: 1)];
        var result = await h.Run();
        Assert.Equal(2, h.Forwarded.Count);
        Assert.True(result.TraversalCompleted);
        Assert.Equal(2, h.Snapshots);
        Assert.Null(result.Root);
        Assert.Empty(result.AllocationGroups);
        Assert.Null(result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Null(result.Reconciliation.SignedResidualBytes);
        Assert.Null(result.Reconciliation.ObservedCoveragePercent);
        Assert.True(result.Summary.Reasons.HasFlag(AccountingReason.ArithmeticOverflow));
        Assert.Null(result.ResourceLimitDiagnostic);
    }

    [Fact]
    public async Task IssueCountOverflowDegradesAccountingAndContinuesForwarding()
    {
        var h = new AccountingHarness();
        h.Entries =
        [
            h.Entry("first", null, kind: StorageObjectKind.Directory),
            h.Entry("after", h.Id()),
            h.Entry("second", null, kind: StorageObjectKind.Directory),
        ];

        var result = await h.Run(issueCountSeed:
            new KeyValuePair<StorageTraversalIssueKind, long>(StorageTraversalIssueKind.IdentityUnavailable, long.MaxValue));

        Assert.True(result.TraversalCompleted);
        Assert.Equal(3, h.Forwarded.Count);
        Assert.Equal(2, h.ForwardedIssues.Count);
        Assert.Equal(2, h.Snapshots);
        Assert.True(result.Summary.Reasons.HasFlag(AccountingReason.ArithmeticOverflow));
        Assert.True(result.Summary.Reasons.HasFlag(AccountingReason.TraversalCoverageGap));
        Assert.Equal(AccountingQuality.Unavailable, result.Summary.Quality);
        Assert.Null(result.Root);
        Assert.Empty(result.AllocationGroups);
        Assert.Null(result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Null(result.Summary.UncertainMeasuredAllocatedBytes);
        Assert.Null(result.Reconciliation.SignedResidualBytes);
        Assert.Null(result.Reconciliation.ObservedCoveragePercent);
        Assert.Equal(long.MaxValue, result.IssueCounts[StorageTraversalIssueKind.IdentityUnavailable]);
    }

    [Theory]
    [InlineData(StorageTraversalIssueKind.Inaccessible)]
    [InlineData(StorageTraversalIssueKind.Disappeared)]
    [InlineData(StorageTraversalIssueKind.TargetChanged)]
    [InlineData(StorageTraversalIssueKind.IoFailure)]
    [InlineData(StorageTraversalIssueKind.IdentityUnavailable)]
    [InlineData(StorageTraversalIssueKind.RecallSensitive)]
    public async Task EveryIssueIsCountedAndForwarded(StorageTraversalIssueKind kind)
    {
        var h = new AccountingHarness();
        h.ChildError = kind switch
        {
            StorageTraversalIssueKind.Inaccessible => new UnauthorizedAccessException(),
            StorageTraversalIssueKind.Disappeared => new DirectoryNotFoundException(),
            StorageTraversalIssueKind.IoFailure => new IOException(),
            _ => null,
        };
        if (kind == StorageTraversalIssueKind.TargetChanged)
            h.ChildError = new StorageTraversalTargetChangedException("changed");
        h.Entries = [h.Entry("dir", kind == StorageTraversalIssueKind.IdentityUnavailable ? null : h.Id(),
            kind: StorageObjectKind.Directory,
            attributes: kind == StorageTraversalIssueKind.RecallSensitive ? StorageEntryAttributes.RecallOnOpen : StorageEntryAttributes.None)];
        var result = await h.Run();
        Assert.Equal(1, result.IssueCounts[kind]);
        Assert.Equal(kind, Assert.Single(h.ForwardedIssues).Kind);
        Assert.Equal(AccountingQuality.Incomplete, result.Summary.Quality);
    }

    [Theory]
    [InlineData(100, 100, 0, 100)]
    [InlineData(100, 100, 90, 10)]
    [InlineData(10, 10, -90, -1)]
    [InlineData(100, 101, 1, -1)]
    public async Task ReconciliationKeepsSignedEvidence(long start, long end, long residual, int coverage)
    {
        var h = new AccountingHarness { StartUsed = start, EndUsed = end };
        long bytes = start == 100 && residual == 90 ? 10 : 100;
        h.Entries = [h.Entry("a", h.Id(), bytes: bytes)];
        var result = await h.Run();
        Assert.Equal(residual, result.Reconciliation.SignedResidualBytes);
        Assert.Equal(coverage < 0 ? null : (decimal?)coverage, result.Reconciliation.ObservedCoveragePercent);
    }

    [Fact]
    public async Task EmptyAndUnavailableSnapshots()
    {
        var h = new AccountingHarness { StartUsed = 0, EndUsed = 0 };
        Assert.Equal(100m, (await h.Run()).Reconciliation.ObservedCoveragePercent);
        h.SnapshotUnavailable = true;
        var result = await h.Run();
        Assert.True(result.TraversalCompleted);
        Assert.Null(result.Reconciliation.ObservedCoveragePercent);
        Assert.Null(result.Reconciliation.SignedResidualBytes);
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        var h = new AccountingHarness();
        using var source = new CancellationTokenSource();
        await source.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Run(token: source.Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DownstreamExceptionIdentityPreserved(bool issue)
    {
        var error = new IOException("sink");
        var h = new AccountingHarness();
        if (issue) h.IssueError = error; else h.EntryError = error;
        h.Entries = [h.Entry("dir", null, kind: StorageObjectKind.Directory)];
        Assert.Same(error, await Assert.ThrowsAsync<IOException>(() => h.Run()));
    }

    [Fact]
    public async Task CrossVolumeFails()
    {
        var h = new AccountingHarness();
        var other = new VolumeIdentity(Guid.NewGuid());
        h.Entries = [new StorageEntry(other, null, @"C:\bad", StorageObjectKind.File, ReparseKind.None)];
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Run());
    }
}
