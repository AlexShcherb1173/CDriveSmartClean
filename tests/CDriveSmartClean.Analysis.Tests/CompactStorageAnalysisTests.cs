using System.Text.Json;
using CDriveSmartClean.Analysis;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Scan.Accounting;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class CompactStorageAnalysisTests
{
    private static readonly VolumeIdentity Volume = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void SnapshotNativeResultEqualsLegacyStreamingResultForRepresentativeEvidence()
    {
        StorageEntry[] entries = RepresentativeEntries();
        StorageAnalysisRequest request = Request();
        StorageAccountingResult compactAccounting = Accounting(entries);
        StorageAnalysisResult compact = AnalyzeCompact(request, compactAccounting);
        Assert.Equal(0, compactAccounting.ProjectedAllocationGroupCount);

        StorageAccountingResult legacyAccounting = Accounting(entries);
        StorageAnalysisResult legacy = AnalyzeLegacy(request, legacyAccounting, entries);
        Assert.Equal(Serialize(legacy), Serialize(compact));
    }

    [Fact]
    public void SnapshotNativeResultIsIndependentOfObservationAndSnapshotPhysicalOrder()
    {
        StorageEntry[] entries = RepresentativeEntries();
        StorageAnalysisResult first = AnalyzeCompact(Request(), Accounting(entries));
        StorageAnalysisResult second = AnalyzeCompact(Request(), Accounting(entries.Reverse().ToArray()));
        Assert.Equal(Serialize(first), Serialize(second));
    }

    [Fact]
    public void PreservesIncompleteContextAndInconsistentReconciliationEquivalence()
    {
        StorageEntry[] entries = RepresentativeEntries();
        StorageAnalysisRequest request = Request(incompleteContext: true);
        StorageAnalysisResult compact = AnalyzeCompact(request, Accounting(entries, inconsistent: true));
        StorageAnalysisResult legacy = AnalyzeLegacy(request, Accounting(entries, inconsistent: true), entries);
        Assert.Equal(Serialize(legacy), Serialize(compact));
        Assert.True(compact.Reasons.HasFlag(AnalysisReason.ClassificationContextIncomplete));
        Assert.True(compact.Reasons.HasFlag(AnalysisReason.UpstreamReconciliationInconsistent));
    }

    [Fact]
    public void DeepHierarchyIsStackSafeAndEquivalent()
    {
        string relative = string.Join('\\', Enumerable.Range(0, 600).Select(index => $"d{index}")) + "\\file.bin";
        StorageEntry entry = Entry(relative, 42, Identity(9000));
        StorageAnalysisRequest request = Request();
        StorageAnalysisResult compact = AnalyzeCompact(request, Accounting([entry], maximumDirectories: 1000));
        StorageAnalysisResult legacy = AnalyzeLegacy(request, Accounting([entry], maximumDirectories: 1000), [entry]);
        Assert.Equal(Serialize(legacy), Serialize(compact));
    }

    [Fact]
    public void EnforcesSnapshotLimitsWithoutPartialResult()
    {
        StorageEntry[] entries = [Entry("a", 1, Identity(1)), Entry("b", 1, Identity(2))];
        StorageAnalysisRequest request = Request(new StorageAnalysisOptions(maximumPathStates: 1));
        StorageAnalysisResult result = AnalyzeCompact(request, Accounting(entries));
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.ResourceLimit));
        Assert.Empty(result.CategorySummaries);
    }

    [Fact]
    public void AccountingWithoutAuthoritativeSnapshotIsUnavailable()
    {
        var summary = new StorageAccountingSummary(null, AccountingReason.ResourceLimit);
        VolumeSpaceSnapshot unavailable = VolumeSpaceSnapshot.Unavailable(Volume, DateTimeOffset.UnixEpoch,
            VolumeSpaceFailure.VolumeUnavailable);
        var accounting = new StorageAccountingResult(summary, null, [],
            new VolumeReconciliation(unavailable, unavailable, summary, true), true, EmptyIssues());
        StorageAnalysisResult result = AnalyzeCompact(Request(), accounting);
        Assert.Equal(AnalysisQuality.Unavailable, result.Quality);
        Assert.True(result.Reasons.HasFlag(AnalysisReason.UpstreamAccountingUnavailable));
    }

    [Fact]
    public void CapacityChargesAreDeterministicAndMateriallyBelowLegacyGraph()
    {
        Assert.Equal(2_425_600, CompactStorageAnalysisBuilder.CalculateCharge(100_000, 100));
        Assert.Equal(6_025_600, CompactStorageAnalysisBuilder.CalculateCharge(250_000, 100));
        Assert.Equal(12_025_600, CompactStorageAnalysisBuilder.CalculateCharge(500_000, 100));
        Assert.Equal(24_025_600, CompactStorageAnalysisBuilder.CalculateCharge(1_000_000, 100));
        Assert.True(CompactStorageAnalysisBuilder.CompactPathStateCharge <
            StorageAnalysisSession.LegacyCommonCaseCharge(16));
    }

    [Fact]
    public void CancellationIsAuthoritativeBeforeSnapshotAnalysis()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(() =>
            ((ICompactStorageAnalyzer)new UniversalStorageAnalyzer()).Analyze(
                Request(), Accounting([Entry("a", 1, Identity(1))]), cancellation.Token));
    }

    private static StorageEntry[] RepresentativeEntries()
    {
        StorageObjectIdentity alias = Identity(100);
        StorageObjectIdentity conflicting = Identity(200);
        return
        [
            Entry(@"Windows\System32\compressed.sys", 100, Identity(1), StorageEntryAttributes.Compressed),
            Entry(@"Program Files\App\sparse.bin", 90, Identity(2), StorageEntryAttributes.Sparse),
            Entry(@"mystery\unknown.dat", 80, Identity(3)),
            Entry(@"Users\Current\AppData\Local\cloud.dat", 70, Identity(4),
                StorageEntryAttributes.RecallOnOpen),
            Entry(@"no-identity.dat", 60, null),
            Entry(@"Program Files\Shared\alias.dat", 50, alias),
            Entry(@"Users\Current\alias.dat", 50, alias),
            Entry(@"case\Name.dat", 40, Identity(5)),
            Entry(@"case\name.dat", 30, Identity(6)),
            Entry(@"unicode\данные-文件.dat", 20, Identity(7)),
            Entry(@"conflict\first.dat", 10, conflicting),
            Entry(@"conflict\second.dat", 11, conflicting),
            Entry(@"same-path.dat", 9, Identity(8)),
            Entry(@"same-path.dat", 8, Identity(9)),
        ];
    }

    private static StorageEntry Entry(string relativePath, long bytes, StorageObjectIdentity? identity,
        StorageEntryAttributes attributes = StorageEntryAttributes.None) => new(Volume, identity,
        @"C:\" + relativePath, StorageObjectKind.File, ReparseKind.None,
        new StorageMeasurement(bytes, bytes, StorageMeasurementAvailability.Available,
            StorageMeasurementQuality.FileSystemReported,
            StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo,
            StorageMeasurementScope.FileContent, StorageMeasurementFreshness.LivePointInTime), attributes);

    private static StorageObjectIdentity Identity(int value) => new(Volume,
        new Guid(value, 0, 0, new byte[8]));

    private static StorageAnalysisRequest Request(StorageAnalysisOptions? options = null,
        bool incompleteContext = false) => new(new SystemVolumeDescriptor(Volume, @"C:\"),
        new StorageClassificationContext(Volume, @"C:\Windows", [@"C:\Program Files"],
            @"C:\ProgramData", @"C:\Users\Current", [@"C:\Users\Current\AppData\Local"],
            @"C:\Users\Public", @"C:\Users", incompleteContext ? ["profile"] : []), options);

    private static StorageAccountingResult Accounting(IEnumerable<StorageEntry> source,
        bool inconsistent = false, int maximumDirectories = 100_000)
    {
        StorageEntry[] entries = source.ToArray();
        var ledger = new StorageIdentityLedger(new StorageAccountingOptions(maximumDirectories: maximumDirectories));
        foreach (StorageEntry entry in entries) ledger.Add(entry, entry.CanonicalPath[3..]);
        var (root, snapshot) = ledger.Finish(CancellationToken.None);
        var summary = new StorageAccountingSummary(root.Aggregate, ledger.Reasons);
        long used = summary.DeduplicatedObservedAllocatedBytes ?? 0;
        long capacity = Math.Max(1_000_000, used + 10);
        VolumeSpaceSnapshot start = Space(capacity, used);
        VolumeSpaceSnapshot end = Space(capacity, inconsistent ? used + 1 : used);
        return new StorageAccountingResult(summary, root, snapshot,
            new VolumeReconciliation(start, end, summary, true), true, EmptyIssues());
    }

    private static VolumeSpaceSnapshot Space(long capacity, long used) => VolumeSpaceSnapshot.Available(
        Volume, DateTimeOffset.UnixEpoch, capacity, capacity - used, capacity, capacity - used, used, 0, 0);

    private static Dictionary<StorageTraversalIssueKind, long> EmptyIssues() =>
        Enum.GetValues<StorageTraversalIssueKind>().ToDictionary(kind => kind, _ => 0L);

    private static StorageAnalysisResult AnalyzeCompact(StorageAnalysisRequest request,
        StorageAccountingResult accounting) => ((ICompactStorageAnalyzer)new UniversalStorageAnalyzer()).Analyze(
            request, accounting, CancellationToken.None);

    private static StorageAnalysisResult AnalyzeLegacy(StorageAnalysisRequest request,
        StorageAccountingResult accounting, IEnumerable<StorageEntry> entries)
    {
        IStorageAnalysisSession session = new UniversalStorageAnalyzer().CreateSession(request);
        foreach (StorageEntry entry in entries)
            session.WriteAsync(entry, CancellationToken.None).AsTask().GetAwaiter().GetResult();
        return session.Complete(accounting, CancellationToken.None);
    }

    private static string Serialize(StorageAnalysisResult result) => JsonSerializer.Serialize(result);
}
