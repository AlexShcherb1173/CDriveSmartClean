using System.Runtime.Versioning;
using CDriveSmartClean.Analysis;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Platform.Windows.Storage;
using CDriveSmartClean.Scan.Accounting;
using CDriveSmartClean.Scan.Traversal;

namespace CDriveSmartClean.Runtime;

public sealed class SystemVolumeScanWorkflow
{
    private readonly ISystemVolumeProvider systemVolumeProvider;
    private readonly IStorageClassificationContextProvider classificationContextProvider;
    private readonly IStorageEnumerator storageEnumerator;
    private readonly IVolumeSpaceProvider volumeSpaceProvider;
    private readonly IStorageAnalyzer analyzer;
    private readonly IUniversalFindingBuilder findingBuilder;
    private readonly StorageTraversalPolicy traversalPolicy;

    [SupportedOSPlatform("windows")]
    public SystemVolumeScanWorkflow()
        : this(new WindowsSystemVolumeProvider(), new WindowsStorageClassificationContextProvider(),
            new WindowsStorageEnumerator(), new WindowsVolumeSpaceProvider(), new UniversalStorageAnalyzer(),
            new UniversalFindingBuilder(), new StorageTraversalPolicy())
    {
    }

    internal SystemVolumeScanWorkflow(ISystemVolumeProvider systemVolumeProvider,
        IStorageClassificationContextProvider classificationContextProvider, IStorageEnumerator storageEnumerator,
        IVolumeSpaceProvider volumeSpaceProvider, IStorageAnalyzer analyzer,
        IUniversalFindingBuilder findingBuilder, StorageTraversalPolicy traversalPolicy)
    {
        ArgumentNullException.ThrowIfNull(systemVolumeProvider);
        ArgumentNullException.ThrowIfNull(classificationContextProvider);
        ArgumentNullException.ThrowIfNull(storageEnumerator);
        ArgumentNullException.ThrowIfNull(volumeSpaceProvider);
        ArgumentNullException.ThrowIfNull(analyzer);
        ArgumentNullException.ThrowIfNull(findingBuilder);
        ArgumentNullException.ThrowIfNull(traversalPolicy);
        this.systemVolumeProvider = systemVolumeProvider;
        this.classificationContextProvider = classificationContextProvider;
        this.storageEnumerator = storageEnumerator;
        this.volumeSpaceProvider = volumeSpaceProvider;
        this.analyzer = analyzer;
        this.findingBuilder = findingBuilder;
        this.traversalPolicy = traversalPolicy;
    }

    public async Task<ProductScanResult> ScanAsync(ProductScanRequest request,
        IProgress<ProductScanProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        var progressState = new ProgressState(request.ScanSessionId, progress, cancellationToken);
        progressState.ReportPhase(ProductScanPhase.DiscoveringSystemVolume);

        SystemVolumeDescriptor systemVolume = systemVolumeProvider.GetSystemVolume() ??
            throw new InvalidOperationException("System-volume provider returned null.");

        cancellationToken.ThrowIfCancellationRequested();
        progressState.ReportPhase(ProductScanPhase.ResolvingClassificationContext);
        StorageClassificationContext classificationContext =
            classificationContextProvider.GetClassificationContext(systemVolume) ??
            throw new InvalidOperationException("Classification-context provider returned null.");
        if (!classificationContext.VolumeIdentity.Equals(systemVolume.VolumeIdentity))
            throw new InvalidOperationException("Classification context volume identity contradiction.");

        var analysisRequest = new StorageAnalysisRequest(systemVolume, classificationContext, request.AnalysisOptions);
        IStorageAnalysisSession analysisSession = analyzer.CreateSession(analysisRequest) ??
            throw new InvalidOperationException("Analyzer returned null session.");

        cancellationToken.ThrowIfCancellationRequested();
        progressState.ReportPhase(ProductScanPhase.TraversingAndAccounting);
        var walker = new StorageTreeWalker(storageEnumerator, traversalPolicy);
        var accountingEngine = new StorageAccountingEngine(walker, volumeSpaceProvider, request.AccountingOptions);
        StorageAccountingResult accountingResult = await accountingEngine.AccountAsync(systemVolume,
            new ProgressForwardingEntrySink(analysisSession, progressState),
            new ProgressIssueSink(progressState), cancellationToken).ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();
        progressState.ReportPhase(ProductScanPhase.CompletingAnalysis);
        StorageAnalysisResult analysisResult = analysisSession.Complete(accountingResult, cancellationToken) ??
            throw new InvalidOperationException("Analysis session returned null result.");

        cancellationToken.ThrowIfCancellationRequested();
        progressState.ReportPhase(ProductScanPhase.BuildingFindings);
        UniversalFindingResult findingResult = findingBuilder.Build(new UniversalFindingRequest(
                request.ScanSessionId, analysisRequest, analysisResult, accountingResult, request.FindingOptions),
            cancellationToken) ?? throw new InvalidOperationException("Finding builder returned null result.");
        cancellationToken.ThrowIfCancellationRequested();

        var result = new ProductScanResult(request.ScanSessionId, systemVolume, accountingResult,
            analysisResult, findingResult);
        progressState.ReportPhase(ProductScanPhase.Completed);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private sealed class ProgressForwardingEntrySink(IStorageAnalysisSession analysisSession,
        ProgressState progressState) : IStorageEntrySink
    {
        public async ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entry);
            cancellationToken.ThrowIfCancellationRequested();
            await analysisSession.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            progressState.ObserveEntry(entry);
        }
    }

    private sealed class ProgressIssueSink(ProgressState progressState) : IStorageTraversalIssueSink
    {
        public ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(issue);
            cancellationToken.ThrowIfCancellationRequested();
            progressState.ObserveIssue();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ProgressState(Guid scanSessionId, IProgress<ProductScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        private ProductScanPhase phase;
        private bool phaseReported;
        private long objectsObserved;
        private long rawReportedAllocatedBytesObserved;
        private long traversalIssuesObserved;

        internal void ReportPhase(ProductScanPhase next)
        {
            if (!Enum.IsDefined(next)) throw new ArgumentOutOfRangeException(nameof(next));
            if (phaseReported && next < phase) throw new InvalidOperationException("Product scan phase cannot regress.");
            phase = next;
            phaseReported = true;
            Report();
        }

        internal void ObserveEntry(StorageEntry entry)
        {
            objectsObserved = checked(objectsObserved + 1);
            if (entry.Measurement.ReportedAllocatedBytes is { } bytes)
                rawReportedAllocatedBytesObserved = checked(rawReportedAllocatedBytesObserved + bytes);
            Report();
        }

        internal void ObserveIssue()
        {
            traversalIssuesObserved = checked(traversalIssuesObserved + 1);
            Report();
        }

        private void Report()
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ProductScanProgress(scanSessionId, phase, objectsObserved,
                rawReportedAllocatedBytesObserved, traversalIssuesObserved));
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
