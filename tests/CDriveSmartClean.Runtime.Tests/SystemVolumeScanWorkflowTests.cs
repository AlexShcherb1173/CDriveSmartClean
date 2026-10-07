using CDriveSmartClean.Analysis;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Runtime;
using CDriveSmartClean.Scan.Traversal;
using Xunit;

namespace CDriveSmartClean.Runtime.Tests;

public sealed class SystemVolumeScanWorkflowTests
{
    [Fact]
    public async Task ExecutesRequiredOrderWithOneTraversalAndSameEntryStream()
    {
        var harness = new Harness();
        ProductScanResult result = await harness.Run();

        Assert.Equal(["system", "context", "analysis-create", "enumerate-root", "analysis-complete", "finding-build"],
            harness.Order);
        Assert.Equal(1, harness.Enumerator.RootCalls);
        Assert.Equal(harness.Enumerator.Entries, harness.Analyzer.Session.Entries);
        Assert.Equal(1, harness.Analyzer.Session.CompleteCalls);
        Assert.Same(harness.Analyzer.Session.CompletedAccounting, harness.FindingBuilder.Request!.AccountingResult);
        Assert.Same(harness.Analyzer.Session.Result, harness.FindingBuilder.Request.AnalysisResult);
        Assert.Equal(harness.SessionId, result.ScanSessionId);
    }

    [Fact]
    public async Task PreservesSessionAndVolumeIdentityThroughProgressFindingsAndResult()
    {
        var harness = new Harness();
        ProductScanResult result = await harness.Run();

        Assert.All(harness.Progress.Values, item => Assert.Equal(harness.SessionId, item.ScanSessionId));
        Assert.Equal(harness.SessionId, harness.FindingBuilder.Request!.ScanSessionId);
        Assert.All(result.Findings, finding => Assert.Equal(harness.SessionId, finding.ScanSessionId));
        Assert.Equal(harness.Volume, result.SystemVolume.VolumeIdentity);
        Assert.Equal(harness.Volume, result.StartSnapshot.VolumeIdentity);
        Assert.Equal(harness.Volume, result.EndSnapshot.VolumeIdentity);
        Assert.Equal(harness.Volume, harness.Analyzer.Request!.SystemVolume.VolumeIdentity);
        Assert.Equal(harness.Volume, harness.Analyzer.Request.ClassificationContext.VolumeIdentity);
    }

    [Fact]
    public async Task ProgressIsOrderedMonotonicAndCountsOnlyAvailableRawAllocation()
    {
        var harness = new Harness();
        harness.Enumerator.Entries.Add(harness.UnavailableEntry("unknown"));
        await harness.Run();

        ProductScanPhase[] phases = harness.Progress.Values.Select(item => item.Phase).Distinct().ToArray();
        Assert.Equal(Enum.GetValues<ProductScanPhase>(), phases);
        AssertMonotonic(harness.Progress.Values.Select(item => item.ObjectsObserved));
        AssertMonotonic(harness.Progress.Values.Select(item => item.RawReportedAllocatedBytesObserved));
        AssertMonotonic(harness.Progress.Values.Select(item => item.TraversalIssuesObserved));
        ProductScanProgress completed = harness.Progress.Values[^1];
        Assert.Equal(2, completed.ObjectsObserved);
        Assert.Equal(10, completed.RawReportedAllocatedBytesObserved);
        Assert.Equal(0, completed.TraversalIssuesObserved);
    }

    [Fact]
    public async Task EveryTraversalIssueUsesAccountingAuthoritativeSummaryWithoutPaths()
    {
        var harness = new Harness();
        harness.AddEveryIssueKind();
        ProductScanResult result = await harness.Run();

        foreach (StorageTraversalIssueKind kind in Enum.GetValues<StorageTraversalIssueKind>())
            Assert.Equal(1, result.IssueCounts[kind]);
        Assert.Equal(Enum.GetValues<StorageTraversalIssueKind>().Length,
            harness.Progress.Values[^1].TraversalIssuesObserved);
        Assert.DoesNotContain(typeof(ProductScanResult).GetProperties(), property =>
            property.Name.Contains("Path", StringComparison.Ordinal) ||
            property.PropertyType == typeof(StorageTraversalIssue));
    }

    [Fact]
    public async Task ContradictoryClassificationVolumeFailsBeforeTraversal()
    {
        var harness = new Harness { ContradictContext = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Run());
        Assert.Equal(0, harness.Enumerator.RootCalls);
    }

    [Fact]
    public async Task CompleteResultPreservesCompleteQualities()
    {
        ProductScanResult result = await new Harness().Run();
        Assert.Equal(AccountingQuality.Complete, result.AccountingQuality);
        Assert.Equal(AccountingQuality.Complete, result.ReconciliationQuality);
        Assert.Equal(AnalysisQuality.Complete, result.AnalysisQuality);
        Assert.Equal(AnalysisQuality.Complete, result.FindingQuality);
        Assert.True(result.TraversalCompleted);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public async Task IncompleteUsableResultRetainsFindingsAndReasons()
    {
        var harness = new Harness();
        harness.AddEveryIssueKind();
        ProductScanResult result = await harness.Run();
        Assert.Equal(AccountingQuality.Incomplete, result.AccountingQuality);
        Assert.Equal(AnalysisQuality.Incomplete, result.AnalysisQuality);
        Assert.Equal(AnalysisQuality.Incomplete, result.FindingQuality);
        Assert.NotEqual(AccountingReason.None, result.AccountingReason);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public async Task AccountingUnavailablePreservesUsableIncompleteDownstreamResults()
    {
        var harness = new Harness();
        var request = new ProductScanRequest(harness.SessionId,
            new StorageAccountingOptions(accountingStateBudget: 1));
        ProductScanResult result = await harness.Run(request);
        Assert.Equal(AccountingQuality.Unavailable, result.AccountingQuality);
        Assert.Equal(AnalysisQuality.Incomplete, result.AnalysisQuality);
        Assert.Equal(AnalysisQuality.Incomplete, result.FindingQuality);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public async Task AnalysisUnavailableRemainsUnavailableWithoutFabricatedFindings()
    {
        var harness = new Harness();
        var request = new ProductScanRequest(harness.SessionId, analysisOptions:
            new StorageAnalysisOptions(analysisStateBudget: 1));
        ProductScanResult result = await harness.Run(request);
        Assert.Equal(AccountingQuality.Complete, result.AccountingQuality);
        Assert.Equal(AnalysisQuality.Unavailable, result.AnalysisQuality);
        Assert.Equal(AnalysisQuality.Unavailable, result.FindingQuality);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task FindingUnavailableReturnsExplicitEmptyResult()
    {
        var harness = new Harness { ForceFindingUnavailable = true };
        ProductScanResult result = await harness.Run();
        Assert.Equal(AnalysisQuality.Complete, result.AnalysisQuality);
        Assert.Equal(AnalysisQuality.Unavailable, result.FindingQuality);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task CancellationBeforeDiscoveryPropagates()
    {
        var harness = new Harness();
        harness.Cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Run());
        Assert.Empty(harness.Order);
    }

    [Theory]
    [InlineData(ProductScanPhase.TraversingAndAccounting)]
    [InlineData(ProductScanPhase.CompletingAnalysis)]
    [InlineData(ProductScanPhase.BuildingFindings)]
    public async Task CancellationBeforeMajorStagePropagates(ProductScanPhase phase)
    {
        var harness = new Harness { CancelOnPhase = phase };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Run());
        Assert.DoesNotContain(ProductScanPhase.Completed, harness.Progress.Values.Select(item => item.Phase));
    }

    [Fact]
    public async Task CancellationDuringTraversalPropagates()
    {
        var harness = new Harness { CancelDuringTraversal = true };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.Run());
        Assert.Single(harness.Analyzer.Session.Entries);
        Assert.Equal(0, harness.Analyzer.Session.CompleteCalls);
    }

    [Fact]
    public async Task CancellationRequestedByCompletedObserverDoesNotCancelCompletedResult()
    {
        var harness = new Harness { CancelOnPhase = ProductScanPhase.Completed };
        ProductScanResult result = await harness.Run();
        Assert.Equal(harness.SessionId, result.ScanSessionId);
        Assert.Single(harness.Progress.Values, item => item.Phase == ProductScanPhase.Completed);
        Assert.Equal(AnalysisQuality.Complete, result.FindingQuality);
        Assert.NotEmpty(result.Findings);
    }

    [Fact]
    public async Task ProgressObserverFailurePropagates()
    {
        var expected = new IOException("progress");
        var harness = new Harness { ProgressFailure = expected };
        IOException actual = await Assert.ThrowsAsync<IOException>(() => harness.Run());
        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task SynchronousCompletedObserverFailurePropagates()
    {
        var expected = new IOException("completed progress");
        var harness = new Harness
        {
            ProgressFailure = expected,
            ProgressFailurePhase = ProductScanPhase.Completed,
        };
        IOException actual = await Assert.ThrowsAsync<IOException>(() => harness.Run());
        Assert.Same(expected, actual);
        Assert.Single(harness.Progress.Values, item => item.Phase == ProductScanPhase.Completed);
    }

    private static void AssertMonotonic(IEnumerable<long> values)
    {
        long previous = -1;
        foreach (long value in values)
        {
            Assert.True(value >= previous);
            previous = value;
        }
    }

    private sealed class Harness : IDisposable
    {
        internal readonly Guid SessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        internal readonly VolumeIdentity Volume = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        internal readonly List<string> Order = [];
        internal readonly CancellationTokenSource Cancellation = new();
        internal readonly CaptureProgress Progress = new();
        internal readonly FakeEnumerator Enumerator;
        internal readonly SpyAnalyzer Analyzer;
        internal readonly SpyFindingBuilder FindingBuilder;
        internal bool ContradictContext { get; init; }
        internal bool CancelDuringTraversal { get; init; }
        internal bool ForceFindingUnavailable { get; init; }
        internal ProductScanPhase? CancelOnPhase { get; init; }
        internal Exception? ProgressFailure { get; init; }
        internal ProductScanPhase? ProgressFailurePhase { get; init; }

        internal Harness()
        {
            Enumerator = new FakeEnumerator(this);
            Enumerator.Entries.Add(AvailableEntry("file", 10));
            Analyzer = new SpyAnalyzer(Order);
            FindingBuilder = new SpyFindingBuilder(Order, () => ForceFindingUnavailable);
            Progress.Callback = value =>
            {
                if (ProgressFailure is not null &&
                    (ProgressFailurePhase is null || ProgressFailurePhase == value.Phase))
                    throw ProgressFailure;
                if (CancelOnPhase == value.Phase) Cancellation.Cancel();
            };
        }

        internal async Task<ProductScanResult> Run(ProductScanRequest? request = null)
        {
            var workflow = new SystemVolumeScanWorkflow(
                new FakeSystemVolumeProvider(this), new FakeContextProvider(this), Enumerator,
                new FakeVolumeSpaceProvider(this), Analyzer, FindingBuilder, new StorageTraversalPolicy());
            return await workflow.ScanAsync(request ?? new ProductScanRequest(SessionId), Progress, Cancellation.Token);
        }

        internal StorageEntry AvailableEntry(string name, long bytes) => new(Volume,
            new StorageObjectIdentity(Volume, StableId(name)), $@"C:\{name}", StorageObjectKind.File,
            ReparseKind.None, new StorageMeasurement(bytes, bytes, StorageMeasurementAvailability.Available,
                StorageMeasurementQuality.FileSystemReported,
                StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo,
                StorageMeasurementScope.FileContent, StorageMeasurementFreshness.LivePointInTime),
            StorageEntryAttributes.None);

        internal StorageEntry UnavailableEntry(string name) => new(Volume,
            new StorageObjectIdentity(Volume, StableId(name)), $@"C:\{name}", StorageObjectKind.File,
            ReparseKind.None, StorageMeasurement.Unavailable(StorageMeasurementScope.FileContent),
            StorageEntryAttributes.None);

        internal void AddEveryIssueKind()
        {
            Enumerator.Entries.Add(Directory("inaccessible"));
            Enumerator.Entries.Add(Directory("disappeared"));
            Enumerator.Entries.Add(Directory("changed"));
            Enumerator.Entries.Add(Directory("io"));
            Enumerator.Entries.Add(Directory("identity", identity: false));
            Enumerator.Entries.Add(Directory("recall", attributes: StorageEntryAttributes.RecallOnOpen));
        }

        private StorageEntry Directory(string name, bool identity = true,
            StorageEntryAttributes attributes = StorageEntryAttributes.None) => new(Volume,
            identity ? new StorageObjectIdentity(Volume, StableId(name)) : null, $@"C:\{name}",
            StorageObjectKind.Directory, ReparseKind.None,
            StorageMeasurement.Unavailable(StorageMeasurementScope.DirectoryEntryMetadata), attributes);

        private static Guid StableId(string value)
        {
            byte[] bytes = new byte[16];
            for (int index = 0; index < value.Length; index++) bytes[index % bytes.Length] ^= (byte)value[index];
            if (bytes.All(item => item == 0)) bytes[0] = 1;
            return new Guid(bytes);
        }

        public void Dispose() => Cancellation.Dispose();
    }

    private sealed class FakeSystemVolumeProvider(Harness harness) : ISystemVolumeProvider
    {
        public SystemVolumeDescriptor GetSystemVolume()
        {
            harness.Order.Add("system");
            return new SystemVolumeDescriptor(harness.Volume, @"C:\");
        }
    }

    private sealed class FakeContextProvider(Harness harness) : IStorageClassificationContextProvider
    {
        public StorageClassificationContext GetClassificationContext(SystemVolumeDescriptor systemVolume)
        {
            harness.Order.Add("context");
            VolumeIdentity volume = harness.ContradictContext ? new VolumeIdentity(Guid.NewGuid()) : harness.Volume;
            return new StorageClassificationContext(volume, @"C:\Windows", [@"C:\Program Files"],
                @"C:\ProgramData", @"C:\Users\Current", [@"C:\Users\Current\AppData\Local"],
                @"C:\Users\Public", @"C:\Users", []);
        }
    }

    private sealed class FakeVolumeSpaceProvider(Harness harness) : IVolumeSpaceProvider
    {
        public VolumeSpaceSnapshot GetVolumeSpace(SystemVolumeDescriptor systemVolume) =>
            VolumeSpaceSnapshot.Available(harness.Volume, DateTimeOffset.UnixEpoch,
                100, 90, 100, 90, 10, 0, 0);
    }

    private sealed class FakeEnumerator(Harness harness) : IStorageEnumerator
    {
        internal List<StorageEntry> Entries { get; } = [];
        internal int RootCalls { get; private set; }

        public async Task EnumerateRootAsync(SystemVolumeDescriptor systemVolume, IStorageEntrySink entrySink,
            CancellationToken cancellationToken)
        {
            harness.Order.Add("enumerate-root");
            RootCalls++;
            foreach (StorageEntry entry in Entries)
            {
                await entrySink.WriteAsync(entry, cancellationToken);
                if (harness.CancelDuringTraversal)
                {
                    harness.Cancellation.Cancel();
                    cancellationToken.ThrowIfCancellationRequested();
                }
            }
        }

        public Task EnumerateChildrenAsync(SystemVolumeDescriptor systemVolume, StorageEntry directory,
            IStorageEntrySink entrySink, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (directory.CanonicalPath.EndsWith("inaccessible", StringComparison.Ordinal))
                throw new UnauthorizedAccessException();
            if (directory.CanonicalPath.EndsWith("disappeared", StringComparison.Ordinal))
                throw new DirectoryNotFoundException();
            if (directory.CanonicalPath.EndsWith("changed", StringComparison.Ordinal))
                throw new StorageTraversalTargetChangedException(directory.CanonicalPath);
            if (directory.CanonicalPath.EndsWith("io", StringComparison.Ordinal)) throw new IOException();
            return Task.CompletedTask;
        }
    }

    private sealed class SpyAnalyzer(List<string> order) : IStorageAnalyzer
    {
        internal SpyAnalysisSession Session { get; private set; } = null!;
        internal StorageAnalysisRequest? Request { get; private set; }

        public IStorageAnalysisSession CreateSession(StorageAnalysisRequest request)
        {
            order.Add("analysis-create");
            Request = request;
            Session = new SpyAnalysisSession(order, new UniversalStorageAnalyzer().CreateSession(request));
            return Session;
        }
    }

    private sealed class SpyAnalysisSession(List<string> order, IStorageAnalysisSession inner) : IStorageAnalysisSession
    {
        internal List<StorageEntry> Entries { get; } = [];
        internal int CompleteCalls { get; private set; }
        internal StorageAccountingResult? CompletedAccounting { get; private set; }
        internal StorageAnalysisResult? Result { get; private set; }

        public async ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            await inner.WriteAsync(entry, cancellationToken);
        }

        public StorageAnalysisResult Complete(StorageAccountingResult accountingResult,
            CancellationToken cancellationToken)
        {
            order.Add("analysis-complete");
            CompleteCalls++;
            CompletedAccounting = accountingResult;
            Result = inner.Complete(accountingResult, cancellationToken);
            return Result;
        }
    }

    private sealed class SpyFindingBuilder(List<string> order, Func<bool> forceUnavailable) : IUniversalFindingBuilder
    {
        internal UniversalFindingRequest? Request { get; private set; }

        public UniversalFindingResult Build(UniversalFindingRequest request, CancellationToken cancellationToken)
        {
            order.Add("finding-build");
            Request = request;
            if (forceUnavailable())
                return new UniversalFindingResult(AnalysisQuality.Unavailable,
                    UniversalFindingReason.ResourceLimit, request.AnalysisResult.Quality,
                    request.AnalysisResult.Reasons, []);
            return new UniversalFindingBuilder().Build(request, cancellationToken);
        }
    }

    private sealed class CaptureProgress : IProgress<ProductScanProgress>
    {
        internal List<ProductScanProgress> Values { get; } = [];
        internal Action<ProductScanProgress>? Callback { get; set; }

        public void Report(ProductScanProgress value)
        {
            Values.Add(value);
            Callback?.Invoke(value);
        }
    }
}
