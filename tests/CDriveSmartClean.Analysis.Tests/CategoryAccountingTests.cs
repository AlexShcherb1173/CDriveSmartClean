using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class CategoryAccountingTests
{
    [Fact]
    public async Task SameCategoryAliasesCountPhysicalAllocationOnce()
    {
        StorageObjectIdentity id = TestData.Identity();
        StorageEntry[] entries =
        [
            TestData.Entry(@"ProgramData\one", id, 10),
            TestData.Entry(@"ProgramData\two", id, 10),
        ];
        StorageAnalysisResult result = await TestData.Analyze(entries);
        CategorySummary summary = Summary(result, FindingCategory.ApplicationData);
        Assert.Equal(10, summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(20, summary.RawVisibleAllocatedBytes);
        StorageAnalysisCandidate group = Assert.Single(result.LargestIdentityCandidates);
        Assert.Contains(FindingFacet.HardLinked, group.Facets);
    }

    [Fact]
    public async Task CrossCategoryAliasesAreUnknownOnceRegardlessOfOrder()
    {
        StorageObjectIdentity id = TestData.Identity();
        StorageEntry user = TestData.Entry(@"Users\Current\one", id, 10);
        StorageEntry app = TestData.Entry(@"ProgramData\two", id, 10);
        StorageAnalysisResult left = await TestData.Analyze([user, app]);
        StorageAnalysisResult right = await TestData.Analyze([app, user]);
        Assert.Equal(10, Summary(left, FindingCategory.Unknown).DeduplicatedObservedAllocatedBytes);
        Assert.Equal(10, Summary(right, FindingCategory.Unknown).DeduplicatedObservedAllocatedBytes);
        Assert.Equal(0, Summary(left, FindingCategory.UserData).DeduplicatedObservedAllocatedBytes);
        Assert.Equal(0, Summary(left, FindingCategory.ApplicationData).DeduplicatedObservedAllocatedBytes);
        Assert.True(left.Reasons.HasFlag(AnalysisReason.CrossCategoryIdentityConflict));
    }

    [Fact]
    public async Task IdentityUnavailableIsRawAndUncertainOnly()
    {
        StorageEntry entry = TestData.Entry(@"ProgramData\one", null, 10);
        StorageAnalysisResult result = await TestData.Analyze([entry]);
        CategorySummary summary = Summary(result, FindingCategory.ApplicationData);
        Assert.Equal(0, summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(10, summary.RawVisibleAllocatedBytes);
        Assert.Equal(10, summary.UncertainMeasuredAllocatedBytes);
    }

    [Fact]
    public async Task UnavailableMeasurementDoesNotFabricateBytes()
    {
        StorageEntry entry = TestData.UnavailableEntry("one", TestData.Identity());
        StorageAnalysisResult result = await TestData.Analyze([entry]);
        CategorySummary summary = Summary(result, FindingCategory.Unknown);
        Assert.Equal(0, summary.RawVisibleAllocatedBytes);
        Assert.Equal(0, summary.UncertainMeasuredAllocatedBytes);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.MeasurementUnavailable));
    }

    [Fact]
    public async Task ConflictedIdentityHasNoAuthoritativeAllocationAndUncertainGoesUnknown()
    {
        StorageObjectIdentity id = TestData.Identity();
        StorageEntry[] entries =
        [
            TestData.Entry(@"ProgramData\one", id, 10),
            TestData.Entry(@"ProgramData\two", id, 20),
        ];
        StorageAnalysisResult result = await TestData.Analyze(entries);
        Assert.Equal(0, Summary(result, FindingCategory.Unknown).DeduplicatedObservedAllocatedBytes);
        Assert.Equal(30, Summary(result, FindingCategory.Unknown).UncertainMeasuredAllocatedBytes);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.ConflictingIdentityEvidence));
    }

    [Fact]
    public async Task ConflictedPathExcludesAmbiguousNumericEvidence()
    {
        StorageObjectIdentity first = TestData.Identity();
        StorageObjectIdentity second = TestData.Identity();
        StorageEntry a = TestData.Entry("same", first, 10);
        StorageEntry b = TestData.Entry("same", second, 20);
        StorageAccountingResult accounting = ConflictedPathAccounting(first, second);
        StorageAnalysisResult result = await TestData.Analyze([a, b], accounting: accounting);
        Assert.NotEqual(AnalysisQuality.Unavailable, result.Quality);
        Assert.Equal(0, result.CategorySummaries.Sum(summary => summary.RawVisibleAllocatedBytes));
        Assert.True(result.Reasons.HasFlag(AnalysisReason.ConflictingPathEvidence));
    }

    [Fact]
    public async Task ProviderSensitiveAllocationRemainsRawAndUncertain()
    {
        StorageEntry entry = TestData.Entry(@"Users\Current\cloud", TestData.Identity(), 10,
            StorageEntryAttributes.Offline);
        StorageAnalysisResult result = await TestData.Analyze([entry]);
        CategorySummary summary = Summary(result, FindingCategory.UserData);
        Assert.Equal(0, summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(10, summary.RawVisibleAllocatedBytes);
        Assert.Equal(10, summary.UncertainMeasuredAllocatedBytes);
    }

    [Fact]
    public async Task EligibleUnknownRetainsPhysicalBytes()
    {
        StorageEntry entry = TestData.Entry("ordinary", TestData.Identity(), 10);
        StorageAnalysisResult result = await TestData.Analyze([entry]);
        Assert.Equal(10, Summary(result, FindingCategory.Unknown).DeduplicatedObservedAllocatedBytes);
    }

    [Fact]
    public async Task CategorySumsReconcileExactly()
    {
        StorageEntry[] entries =
        [
            TestData.Entry(@"Windows\a", TestData.Identity(), 10),
            TestData.Entry(@"ProgramData\b", null, 20),
            TestData.Entry("c", TestData.Identity(), 30),
        ];
        StorageAccountingResult accounting = TestData.Accounting(entries);
        StorageAnalysisResult result = await TestData.Analyze(entries, accounting: accounting);
        Assert.Equal(accounting.Summary.DeduplicatedObservedAllocatedBytes,
            result.CategorySummaries.Sum(summary => summary.DeduplicatedObservedAllocatedBytes));
        Assert.Equal(accounting.Root!.Aggregate.RawReportedAllocatedBytes,
            result.CategorySummaries.Sum(summary => summary.RawVisibleAllocatedBytes));
        Assert.Equal(accounting.Root.Aggregate.UncertainMeasuredAllocatedBytes,
            result.CategorySummaries.Sum(summary => summary.UncertainMeasuredAllocatedBytes));
    }

    [Fact]
    public async Task VolumeResidualDoesNotBecomeUnknownAllocation()
    {
        StorageEntry entry = TestData.Entry(@"Windows\a", TestData.Identity(), 10);
        StorageAccountingResult baseline = TestData.Accounting([entry]);
        var snapshot = VolumeSpaceSnapshot.Available(TestData.Volume, DateTimeOffset.UnixEpoch,
            1000, 900, 1000, 900, 100, 0, 0);
        var accounting = new StorageAccountingResult(baseline.Summary, baseline.Root,
            baseline.AllocationGroups, new VolumeReconciliation(snapshot, snapshot, baseline.Summary, true),
            true, baseline.IssueCounts);
        StorageAnalysisResult result = await TestData.Analyze([entry], accounting: accounting);
        Assert.Equal(0, Summary(result, FindingCategory.Unknown).DeduplicatedObservedAllocatedBytes);
        Assert.Equal(90, accounting.Reconciliation.SignedResidualBytes);
    }

    private static CategorySummary Summary(StorageAnalysisResult result, FindingCategory category) =>
        result.CategorySummaries.Single(summary => summary.Category == category);

    private static StorageAccountingResult ConflictedPathAccounting(
        StorageObjectIdentity first, StorageObjectIdentity second)
    {
        AccountingReason reason = AccountingReason.ConflictingPathEvidence;
        var aggregate = new StorageAggregate(conflictedIdentityCount: 2);
        var root = new StorageHierarchyNode("", aggregate, []);
        var summary = new StorageAccountingSummary(aggregate, reason);
        var snapshot = VolumeSpaceSnapshot.Available(TestData.Volume, DateTimeOffset.UnixEpoch,
            100, 100, 100, 100, 0, 0, 0);
        AllocationGroup[] groups =
        [
            new(first, ["same"], null, reason, ""),
            new(second, ["same"], null, reason, ""),
        ];
        return new StorageAccountingResult(summary, root, groups,
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<CDriveSmartClean.Application.Scanning.Traversal.StorageTraversalIssueKind, long>());
    }
}
