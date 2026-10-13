using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Accounting;

public sealed class StorageAccountingContractsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void LimitsMustBePositive(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAccountingOptions(maximumIdentities: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAccountingOptions(maximumDistinctPaths: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAccountingOptions(maximumDirectories: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAccountingOptions(accountingStateBudget: value));
    }

    [Fact]
    public void DefaultsAndPortableProviderContract()
    {
        var options = new StorageAccountingOptions();
        Assert.Equal(1_500_000, options.MaximumIdentities);
        Assert.Equal(1_500_000, options.MaximumDistinctPaths);
        Assert.Equal(250_000, options.MaximumDirectories);
        Assert.Equal(1536L * 1024 * 1024, options.AccountingStateBudget);
        var method = Assert.Single(typeof(IVolumeSpaceProvider).GetMethods());
        Assert.Equal(typeof(VolumeSpaceSnapshot), method.ReturnType);
        Assert.Equal(typeof(SystemVolumeDescriptor), Assert.Single(method.GetParameters()).ParameterType);
    }

    [Fact]
    public void CompletedTraversalMayHaveUnavailableAccounting()
    {
        var summary = new StorageAccountingSummary(null, AccountingReason.ResourceLimit);
        var snapshot = VolumeSpaceSnapshot.Unavailable(new(Guid.NewGuid()), DateTimeOffset.UnixEpoch, VolumeSpaceFailure.NativeFailure);
        var reconciliation = new VolumeReconciliation(snapshot, snapshot, summary, true);
        var issues = new Dictionary<StorageTraversalIssueKind, long> { [StorageTraversalIssueKind.Inaccessible] = 1 };
        var result = new StorageAccountingResult(summary, null, [], reconciliation, true, issues);
        issues.Clear();
        Assert.True(result.TraversalCompleted);
        Assert.Null(result.Root);
        Assert.Equal(1, result.IssueCounts[StorageTraversalIssueKind.Inaccessible]);
        Assert.Throws<ArgumentNullException>(() => new StorageAccountingResult(null!, null, [], reconciliation, true, issues));
        Assert.Throws<ArgumentException>(() => new StorageAccountingResult(summary, new("", new(), []), [], reconciliation, true, issues));
        issues[StorageTraversalIssueKind.Inaccessible] = -1;
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAccountingResult(summary, null, [], reconciliation, true, issues));
    }
}
