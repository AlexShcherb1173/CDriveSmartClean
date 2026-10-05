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
    public async Task SwappedAllocationGroupPathsAreRejectedEvenWhenTotalsBalance()
    {
        StorageObjectIdentity systemIdentity = TestData.Identity();
        StorageObjectIdentity dataIdentity = TestData.Identity();
        StorageEntry system = TestData.Entry(@"Windows\one", systemIdentity, 10);
        StorageEntry data = TestData.Entry(@"ProgramData\two", dataIdentity, 20);
        AllocationGroup[] groups =
        [
            new(systemIdentity, ["ProgramData\\two"], 10, AccountingReason.None, ""),
            new(dataIdentity, ["Windows\\one"], 20, AccountingReason.None, ""),
        ];
        StorageAccountingResult accounting = AccountingWithTotals(
            [system, data], groups, raw: 30, uncertain: 0, deduplicated: 30);

        AssertMismatch(await TestData.Analyze([system, data], accounting: accounting));
    }

    [Fact]
    public async Task EligibleAllocationTamperingIsRejectedEvenWhenAggregatesAgree()
    {
        StorageEntry entry = TestData.Entry("one", TestData.Identity(), 10);
        AllocationGroup[] groups =
        [
            new(entry.ObjectIdentity!, ["one"], 20, AccountingReason.None, ""),
        ];
        StorageAccountingResult accounting = AccountingWithTotals(
            [entry], groups, raw: 10, uncertain: 0, deduplicated: 20);

        AssertMismatch(await TestData.Analyze([entry], accounting: accounting));
    }

    [Fact]
    public async Task AllocationGroupReasonTamperingIsRejectedEvenWhenAggregatesAgree()
    {
        StorageEntry entry = TestData.Entry("one", TestData.Identity(), 10);
        const AccountingReason reason = AccountingReason.UnsupportedAllocationEvidence;
        AllocationGroup[] groups =
        [
            new(entry.ObjectIdentity!, ["one"], null, reason, ""),
        ];
        StorageAccountingResult accounting = AccountingWithTotals(
            [entry], groups, raw: 10, uncertain: 10, deduplicated: 0, reason);

        AssertMismatch(await TestData.Analyze([entry], accounting: accounting));
    }

    [Fact]
    public async Task MissingObservedIdentityGroupIsRejected()
    {
        StorageEntry entry = TestData.Entry("one", TestData.Identity(), 10);
        StorageAccountingResult accounting = AccountingWithTotals(
            [entry], [], raw: 10, uncertain: 10, deduplicated: 0,
            reasons: AccountingReason.IdentityUnavailable);

        AssertMismatch(await TestData.Analyze([entry], accounting: accounting));
    }

    [Fact]
    public async Task ExtraUnobservedIdentityGroupIsRejected()
    {
        StorageObjectIdentity identity = TestData.Identity();
        AllocationGroup[] groups = [new(identity, ["ghost"], 1, AccountingReason.None, "")];
        StorageAccountingResult accounting = AccountingWithTotals(
            [], groups, raw: 0, uncertain: 0, deduplicated: 1);

        AssertMismatch(await TestData.Analyze([], accounting: accounting));
    }

    [Fact]
    public async Task PoisonedPathIdentityPermutationsRemainValidAndDeterministic()
    {
        StorageObjectIdentity a = TestData.Identity();
        StorageObjectIdentity b = TestData.Identity();
        StorageObjectIdentity c = TestData.Identity();
        StorageEntry entryA = TestData.Entry("same", a, 10);
        StorageEntry entryB = TestData.Entry("same", b, 10);
        StorageEntry entryC = TestData.Entry("same", c, 10);
        StorageEntry entryNull = TestData.Entry("same", null, 10);

        StorageAnalysisResult ab = await AnalyzePoisoned([entryA, entryB]);
        StorageAnalysisResult ba = await AnalyzePoisoned([entryB, entryA]);
        StorageAnalysisResult aba = await AnalyzePoisoned([entryA, entryB, entryA]);
        StorageAnalysisResult nullA = await AnalyzePoisoned([entryNull, entryA]);
        StorageAnalysisResult aNull = await AnalyzePoisoned([entryA, entryNull]);
        StorageAnalysisResult abc = await AnalyzePoisoned([entryA, entryB, entryC]);
        StorageAnalysisResult cba = await AnalyzePoisoned([entryC, entryB, entryA]);

        AssertEquivalent(ab, ba);
        AssertEquivalent(ab, aba);
        AssertEquivalent(nullA, aNull);
        AssertEquivalent(abc, cba);
        foreach (StorageAnalysisResult result in new[] { ab, ba, aba, nullA, aNull, abc, cba })
        {
            Assert.NotEqual(AnalysisQuality.Unavailable, result.Quality);
            Assert.True(result.Reasons.HasFlag(AnalysisReason.ConflictingPathEvidence));
            Assert.False(result.Reasons.HasFlag(AnalysisReason.AccountingMismatch));
            Assert.Equal(0, result.CategorySummaries.Sum(summary => summary.RawVisibleAllocatedBytes));
            Assert.Equal(0, result.CategorySummaries.Sum(summary => summary.DeduplicatedObservedAllocatedBytes));
            Assert.Empty(result.LargestIdentityCandidates);
            Assert.Empty(result.LargestFileCandidates);
        }
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

    private static void AssertMismatch(StorageAnalysisResult result)
    {
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.AccountingMismatch));
        Assert.Empty(result.CategorySummaries);
        Assert.Empty(result.LargestHierarchyCandidates);
        Assert.Empty(result.LargestIdentityCandidates);
        Assert.Empty(result.LargestFileCandidates);
        Assert.Empty(result.LargestUnknownCandidates);
    }

    private static async Task<StorageAnalysisResult> AnalyzePoisoned(StorageEntry[] entries) =>
        await TestData.Analyze(entries, accounting: PoisonedPathAccounting(entries));

    private static void AssertEquivalent(StorageAnalysisResult left, StorageAnalysisResult right)
    {
        Assert.Equal(left.Quality, right.Quality);
        Assert.Equal(left.Reasons, right.Reasons);
        Assert.Equal(left.CategorySummaries.Select(Snapshot), right.CategorySummaries.Select(Snapshot));
    }

    private static object Snapshot(CategorySummary summary) => new
    {
        summary.Category,
        summary.DeduplicatedObservedAllocatedBytes,
        summary.RawVisibleAllocatedBytes,
        summary.UncertainMeasuredAllocatedBytes,
        summary.PathCount,
        summary.IdentityGroupCount,
        summary.Quality,
        summary.Reasons,
    };

    private static StorageAccountingResult PoisonedPathAccounting(StorageEntry[] entries)
    {
        const AccountingReason reason = AccountingReason.ConflictingPathEvidence;
        StorageObjectIdentity[] identities = entries.Where(entry => entry.ObjectIdentity is not null)
            .Select(entry => entry.ObjectIdentity!).Distinct().ToArray();
        AllocationGroup[] groups = identities.Select(identity =>
            new AllocationGroup(identity, ["same"], null, reason, "")).ToArray();
        return AccountingWithTotals(entries, groups, raw: 0, uncertain: 0,
            deduplicated: 0, reasons: reason, conflictedIdentityCount: identities.Length);
    }

    private static StorageAccountingResult AccountingWithTotals(StorageEntry[] entries,
        AllocationGroup[] groups, long raw, long uncertain, long deduplicated,
        AccountingReason reasons = AccountingReason.None, long conflictedIdentityCount = 0)
    {
        var aggregate = new StorageAggregate(rawReportedAllocatedBytes: raw,
            uncertainMeasuredAllocatedBytes: uncertain,
            inclusiveAttributedObservedAllocatedBytes: deduplicated,
            conflictedIdentityCount: conflictedIdentityCount);
        var root = new StorageHierarchyNode("", aggregate, []);
        var summary = new StorageAccountingSummary(aggregate, reasons);
        var snapshot = VolumeSpaceSnapshot.Available(TestData.Volume, DateTimeOffset.UnixEpoch,
            1_000_000, 1_000_000 - deduplicated, 1_000_000, 1_000_000 - deduplicated,
            deduplicated, 0, 0);
        return new StorageAccountingResult(summary, root, groups,
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<CDriveSmartClean.Application.Scanning.Traversal.StorageTraversalIssueKind, long>());
    }

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
