using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class UniversalStorageAnalyzerTests
{
    [Fact]
    public async Task OrdinaryUnknownCanStillBeComplete()
    {
        StorageEntry entry = TestData.Entry("ordinary.bin", TestData.Identity(), 10);
        StorageAnalysisResult result = await TestData.Analyze([entry]);
        Assert.Equal(AnalysisQuality.Complete, result.Quality);
        Assert.Equal(10, result.CategorySummaries[0].DeduplicatedObservedAllocatedBytes);
    }

    [Fact]
    public async Task IncompleteContextIsReportedSeparatelyFromUnknown()
    {
        StorageEntry entry = TestData.Entry("ordinary.bin", TestData.Identity(), 10);
        StorageClassificationContext context = TestData.Context(["ProgramData"]);
        StorageAnalysisResult result = await TestData.Analyze([entry], context: context);
        Assert.Equal(AnalysisQuality.Incomplete, result.Quality);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.ClassificationContextIncomplete));
    }

    [Fact]
    public async Task UpstreamUnavailableKeepsPhysicalTotalsNull()
    {
        StorageEntry entry = TestData.Entry("ordinary.bin", TestData.Identity(), 10);
        StorageAccountingResult accounting = TestData.UnavailableAccounting();
        StorageAnalysisResult result = await TestData.Analyze([entry], accounting: accounting);
        Assert.Equal(AnalysisQuality.Incomplete, result.Quality);
        Assert.All(result.CategorySummaries, summary => Assert.Null(summary.DeduplicatedObservedAllocatedBytes));
    }

    [Fact]
    public async Task StreamAccountingMismatchPublishesNoPartialOutput()
    {
        StorageEntry entry = TestData.Entry("ordinary.bin", TestData.Identity(), 10);
        StorageAccountingResult wrong = TestData.Accounting([entry], rawAdjustment: 1);
        StorageAnalysisResult result = await TestData.Analyze([entry], accounting: wrong);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.AccountingMismatch));
        Assert.Empty(result.CategorySummaries);
        Assert.Empty(result.LargestFileCandidates);
    }

    [Fact]
    public async Task AnalyzerSessionsAreIsolated()
    {
        var analyzer = new UniversalStorageAnalyzer();
        var request = new StorageAnalysisRequest(TestData.SystemVolume, TestData.Context());
        IStorageAnalysisSession first = analyzer.CreateSession(request);
        IStorageAnalysisSession second = analyzer.CreateSession(request);
        StorageEntry a = TestData.Entry("a", TestData.Identity(), 1);
        StorageEntry b = TestData.Entry("b", TestData.Identity(), 2);
        CancellationToken token = TestContext.Current.CancellationToken;
        await first.WriteAsync(a, token);
        await second.WriteAsync(b, token);
        Assert.Single(first.Complete(TestData.Accounting([a]), token).LargestFileCandidates);
        Assert.Single(second.Complete(TestData.Accounting([b]), token).LargestFileCandidates);
    }

    [Fact]
    public async Task CrossVolumeEntryIsRejected()
    {
        IStorageAnalysisSession session = new UniversalStorageAnalyzer().CreateSession(
            new StorageAnalysisRequest(TestData.SystemVolume, TestData.Context()));
        var entry = new StorageEntry(new(Guid.NewGuid()), null, @"C:\bad", StorageObjectKind.File, ReparseKind.None);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await session.WriteAsync(entry, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void AnalysisAssemblyHasNoWindowsPlatformDependency() =>
        Assert.DoesNotContain(typeof(UniversalStorageAnalyzer).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "CDriveSmartClean.Platform.Windows");
}

internal static class TestData
{
    internal static readonly VolumeIdentity Volume = new(new Guid("11111111-1111-1111-1111-111111111111"));
    internal static readonly SystemVolumeDescriptor SystemVolume = new(Volume, @"C:\");

    internal static StorageObjectIdentity Identity() => new(Volume, Guid.NewGuid());

    internal static StorageClassificationContext Context(IEnumerable<string>? missing = null) =>
        new(Volume, @"C:\Windows", [@"C:\Program Files", @"C:\Program Files (x86)"], @"C:\ProgramData",
            @"C:\Users\Current", [@"C:\Users\Current\AppData\Local", @"C:\Users\Current\AppData\Roaming"],
            @"C:\Users\Public", @"C:\Users", missing ?? []);

    internal static StorageEntry Entry(string relativePath, StorageObjectIdentity? identity, long bytes,
        StorageEntryAttributes attributes = StorageEntryAttributes.None,
        StorageObjectKind kind = StorageObjectKind.File, ReparseKind reparse = ReparseKind.None)
    {
        StorageMeasurementScope scope = reparse != ReparseKind.None
            ? StorageMeasurementScope.ReparseEntryMetadata
            : kind == StorageObjectKind.File
                ? StorageMeasurementScope.FileContent
                : StorageMeasurementScope.DirectoryEntryMetadata;
        var measurement = new StorageMeasurement(bytes, bytes, StorageMeasurementAvailability.Available,
            StorageMeasurementQuality.FileSystemReported, StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo,
            scope, StorageMeasurementFreshness.LivePointInTime);
        return new StorageEntry(Volume, identity, @"C:\" + relativePath, kind, reparse, measurement, attributes);
    }

    internal static StorageEntry UnavailableEntry(string relativePath, StorageObjectIdentity? identity) =>
        new(Volume, identity, @"C:\" + relativePath, StorageObjectKind.File, ReparseKind.None);

    internal static async Task<StorageAnalysisResult> Analyze(IEnumerable<StorageEntry> source,
        StorageClassificationContext? context = null, StorageAnalysisOptions? options = null,
        StorageAccountingResult? accounting = null)
    {
        StorageEntry[] entries = source.ToArray();
        IStorageAnalysisSession session = new UniversalStorageAnalyzer().CreateSession(
            new StorageAnalysisRequest(SystemVolume, context ?? Context(), options));
        foreach (StorageEntry entry in entries) await session.WriteAsync(entry, default);
        return session.Complete(accounting ?? Accounting(entries), default);
    }

    internal static StorageAccountingResult Accounting(
        IReadOnlyList<StorageEntry> entries, long rawAdjustment = 0)
    {
        StorageEntry[] distinct = entries.GroupBy(entry => entry.CanonicalPath, StringComparer.Ordinal)
            .Select(group => group.First()).ToArray();
        var allocationGroups = new List<AllocationGroup>();
        AccountingReason allReasons = AccountingReason.None;
        long raw = 0;
        long uncertain = 0;
        long deduplicated = 0;
        foreach (StorageEntry entry in distinct)
            if (entry.Measurement.Availability == StorageMeasurementAvailability.Available)
                raw = checked(raw + entry.Measurement.ReportedAllocatedBytes!.Value);
        foreach (IGrouping<StorageObjectIdentity, StorageEntry> grouping in
                 distinct.Where(entry => entry.ObjectIdentity is not null).GroupBy(entry => entry.ObjectIdentity!))
        {
            StorageEntry first = grouping.First();
            AccountingReason reasons = Eligibility(first);
            if (grouping.Any(entry => !SameEvidence(first, entry)))
                reasons |= AccountingReason.ConflictingIdentityEvidence;
            long? eligible = reasons == AccountingReason.None ? first.Measurement.ReportedAllocatedBytes : null;
            if (eligible is { } value) deduplicated = checked(deduplicated + value);
            allocationGroups.Add(new AllocationGroup(grouping.Key, grouping.Select(Relative),
                eligible, reasons, ""));
            allReasons |= reasons;
        }
        foreach (StorageEntry entry in distinct)
        {
            AccountingReason reasons = Eligibility(entry);
            if (entry.ObjectIdentity is { } identity)
                reasons |= allocationGroups.Single(group => group.Identity.Equals(identity)).Reasons;
            if (entry.Measurement.Availability == StorageMeasurementAvailability.Available &&
                reasons != AccountingReason.None)
                uncertain = checked(uncertain + entry.Measurement.ReportedAllocatedBytes!.Value);
            allReasons |= reasons;
        }
        raw = checked(raw + rawAdjustment);
        var aggregate = new StorageAggregate(rawReportedAllocatedBytes: raw,
            uncertainMeasuredAllocatedBytes: uncertain,
            inclusiveAttributedObservedAllocatedBytes: deduplicated,
            fileCount: distinct.LongCount(entry => entry.ObjectKind == StorageObjectKind.File),
            directoryCount: distinct.LongCount(entry => entry.ObjectKind == StorageObjectKind.Directory));
        StorageHierarchyNode[] children = distinct.Select(entry =>
            new StorageHierarchyNode(Relative(entry),
                new StorageAggregate(
                    visibleLogicalMeasuredBytes: entry.Measurement.LogicalBytes ?? 0,
                    rawReportedAllocatedBytes: entry.Measurement.ReportedAllocatedBytes ?? 0,
                    uncertainMeasuredAllocatedBytes: Eligibility(entry) == AccountingReason.None
                        ? 0 : entry.Measurement.ReportedAllocatedBytes ?? 0,
                    fileCount: entry.ObjectKind == StorageObjectKind.File ? 1 : 0,
                    directoryCount: entry.ObjectKind == StorageObjectKind.Directory ? 1 : 0), []))
            .ToArray();
        var root = new StorageHierarchyNode("", aggregate, children);
        var summary = new StorageAccountingSummary(root.Aggregate, allReasons);
        VolumeSpaceSnapshot snapshot = Snapshot(deduplicated);
        return new StorageAccountingResult(summary, root, allocationGroups,
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<CDriveSmartClean.Application.Scanning.Traversal.StorageTraversalIssueKind, long>());
    }

    internal static StorageAccountingResult UnavailableAccounting()
    {
        var summary = new StorageAccountingSummary(null, AccountingReason.ResourceLimit);
        VolumeSpaceSnapshot snapshot = VolumeSpaceSnapshot.Unavailable(
            Volume, DateTimeOffset.UnixEpoch, VolumeSpaceFailure.VolumeUnavailable);
        return new StorageAccountingResult(summary, null, [], new VolumeReconciliation(
            snapshot, snapshot, summary, true), true,
            new Dictionary<CDriveSmartClean.Application.Scanning.Traversal.StorageTraversalIssueKind, long>());
    }

    private static VolumeSpaceSnapshot Snapshot(long used) =>
        VolumeSpaceSnapshot.Available(Volume, DateTimeOffset.UnixEpoch, 1_000_000, 1_000_000 - used,
            1_000_000, 1_000_000 - used, used, 0, 0);

    private static string Relative(StorageEntry entry) => entry.CanonicalPath[3..];

    private static bool SameEvidence(StorageEntry left, StorageEntry right) =>
        left.Measurement.Equals(right.Measurement) && left.ObjectKind == right.ObjectKind &&
        left.ReparseKind == right.ReparseKind && left.Attributes == right.Attributes;

    private static AccountingReason Eligibility(StorageEntry entry)
    {
        AccountingReason reasons = AccountingReason.None;
        if (entry.ObjectIdentity is null) reasons |= AccountingReason.IdentityUnavailable;
        if (entry.Measurement.Availability != StorageMeasurementAvailability.Available)
            reasons |= AccountingReason.MeasurementUnavailable;
        if (entry.ObjectKind != StorageObjectKind.File || entry.ReparseKind != ReparseKind.None ||
            entry.Measurement.Scope != StorageMeasurementScope.FileContent ||
            (entry.Attributes & ~(StorageEntryAttributes.Sparse | StorageEntryAttributes.Compressed)) != 0)
            reasons |= AccountingReason.UnsupportedAllocationEvidence;
        return reasons;
    }
}
