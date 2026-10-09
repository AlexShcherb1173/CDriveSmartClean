using System.Diagnostics;
using System.Globalization;
using CDriveSmartClean.Analysis;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Runtime;
using CDriveSmartClean.Scan.Traversal;
using Xunit;

namespace CDriveSmartClean.Runtime.Tests;

public sealed class LargeVolumeCapacityTests(ITestOutputHelper output)
{
    private const long AllocatedBytesPerObject = 4096;

    [Fact]
    [Trait("Category", "Capacity")]
    public Task NoteWsScaleCompletesCompactProductionWorkflow() =>
        RunCase(new CapacityCase("NOTEWS", 1_100_000, 150_000));

    [Fact]
    [Trait("Category", "Capacity")]
    public Task PublicTargetCompletesCompactProductionWorkflow() =>
        RunCase(new CapacityCase("PUBLIC_TARGET", 1_500_000, 250_000));

    private async Task RunCase(CapacityCase capacityCase)
    {
        var harness = new CapacityHarness(capacityCase);
        var stopwatch = Stopwatch.StartNew();

        ProductScanResult result = await harness.Run();

        stopwatch.Stop();
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        long peakWorkingSet = process.PeakWorkingSet64;
        long gcHeapSize = GC.GetGCMemoryInfo().HeapSizeBytes;

        Assert.True(result.TraversalCompleted);
        Assert.True(harness.Progress.IsMonotonic);
        Assert.Equal(capacityCase.Paths, harness.Progress.Last.ObjectsObserved);
        Assert.Equal(checked(capacityCase.Paths * AllocatedBytesPerObject),
            harness.Progress.Last.RawReportedAllocatedBytesObserved);
        Assert.Equal(0, harness.Progress.Last.TraversalIssuesObserved);
        Assert.Equal(AccountingQuality.Complete, result.AccountingQuality);
        Assert.Equal(AccountingReason.None, result.AccountingReason);
        Assert.Equal(AccountingQuality.Complete, result.ReconciliationQuality);
        Assert.Equal(AccountingReason.None, result.ReconciliationReasons);
        Assert.Equal(0, result.EndSnapshot.UsedBytes - result.StartSnapshot.UsedBytes);
        Assert.Equal(AnalysisQuality.Complete, result.AnalysisQuality);
        Assert.Equal(AnalysisReason.None, result.AnalysisReason);
        Assert.Equal(AnalysisQuality.Complete, result.FindingQuality);
        Assert.Equal(UniversalFindingReason.None, result.UniversalFindingReason);
        Assert.Null(result.ResourceLimitDiagnostic);
        Assert.NotEmpty(result.Findings);
        Assert.All(result.IssueCounts.Values, count => Assert.Equal(0, count));
        Assert.Equal(capacityCase.DirectoryNodes, harness.Analyzer.HierarchyNodeCount);
        Assert.Equal(0, harness.Analyzer.ProjectedGroupsAfterAnalysis);
        Assert.Equal(0, harness.FindingBuilder.ProjectedGroupsAfterFindings);
        Assert.Equal(capacityCase.Paths, harness.Enumerator.Emitted);

        WriteEvidence(capacityCase, result, harness.Progress.Last,
            stopwatch.ElapsedMilliseconds, peakWorkingSet, gcHeapSize);
    }

    private void WriteEvidence(CapacityCase capacityCase, ProductScanResult result,
        ProductScanProgress progress, long elapsedMilliseconds, long peakWorkingSet, long gcHeapSize)
    {
        string block = string.Join(Environment.NewLine,
        [
            "CAPACITY_RESULT_BEGIN",
            $"CAPACITY_CASE={capacityCase.Name}",
            $"PATHS={capacityCase.Paths}",
            $"IDENTITIES={capacityCase.Paths}",
            $"DIRECTORY_NODES={capacityCase.DirectoryNodes}",
            "PATH_LENGTH=96",
            "DIRECTORY_PATH_LENGTH=64",
            $"OBJECTS_OBSERVED={progress.ObjectsObserved}",
            $"ACCOUNTING_QUALITY={result.AccountingQuality}",
            $"RECONCILIATION_QUALITY={result.ReconciliationQuality}",
            $"ANALYSIS_QUALITY={result.AnalysisQuality}",
            $"FINDING_QUALITY={result.FindingQuality}",
            $"RESOURCE_LIMIT_DIAGNOSTIC={(result.ResourceLimitDiagnostic is null ? "null" : "present")}",
            $"FINDINGS_COUNT={result.Findings.Count}",
            $"ELAPSED_MS={elapsedMilliseconds}",
            $"PEAK_WORKING_SET_BYTES={peakWorkingSet}",
            $"GC_HEAP_SIZE_BYTES={gcHeapSize}",
            "CAPACITY_RESULT_END",
        ]);
        output.WriteLine(block);
        Console.WriteLine(block);
    }

    private sealed record CapacityCase(string Name, int Paths, int DirectoryNodes)
    {
        internal int NonRootDirectoryNodes => DirectoryNodes - 1;
    }

    private sealed class CapacityHarness
    {
        private static readonly Guid SessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        private static readonly VolumeIdentity Volume =
            new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

        private readonly CapacityCase capacityCase;

        internal CapacityHarness(CapacityCase capacityCase)
        {
            this.capacityCase = capacityCase;
            Enumerator = new StreamingEnumerator(capacityCase, Volume);
            Analyzer = new InspectingAnalyzer();
            FindingBuilder = new InspectingFindingBuilder();
        }

        internal StreamingEnumerator Enumerator { get; }
        internal InspectingAnalyzer Analyzer { get; }
        internal InspectingFindingBuilder FindingBuilder { get; }
        internal ConstantProgress Progress { get; } = new();

        internal Task<ProductScanResult> Run()
        {
            var workflow = new SystemVolumeScanWorkflow(
                new FixedSystemVolumeProvider(Volume),
                new FixedContextProvider(Volume),
                Enumerator,
                new FixedVolumeSpaceProvider(Volume, capacityCase.Paths),
                Analyzer,
                FindingBuilder,
                new StorageTraversalPolicy());
            return workflow.ScanAsync(new ProductScanRequest(SessionId), Progress, CancellationToken.None);
        }
    }

    private sealed class StreamingEnumerator(CapacityCase capacityCase, VolumeIdentity volume)
        : IStorageEnumerator
    {
        private static readonly StorageMeasurement Measurement = new(
            AllocatedBytesPerObject, AllocatedBytesPerObject,
            StorageMeasurementAvailability.Available,
            StorageMeasurementQuality.FileSystemReported,
            StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo,
            StorageMeasurementScope.FileContent,
            StorageMeasurementFreshness.LivePointInTime);

        internal long Emitted { get; private set; }

        public async Task EnumerateRootAsync(SystemVolumeDescriptor systemVolume,
            IStorageEntrySink entrySink, CancellationToken cancellationToken)
        {
            for (int index = 0; index < capacityCase.Paths; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string directory = Directory(index % capacityCase.NonRootDirectoryNodes);
                if (directory.Length != 64)
                    throw new InvalidOperationException("Synthetic directory path length contradiction.");
                string file = File(index);
                string relative = directory + (char)92 + file;
                if (relative.Length != 96)
                    throw new InvalidOperationException("Synthetic path length contradiction.");
                var identity = new StorageObjectIdentity(volume,
                    new Guid(index + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
                var entry = new StorageEntry(volume, identity, @"C:\" + relative,
                    StorageObjectKind.File, ReparseKind.None, Measurement, StorageEntryAttributes.None);
                await entrySink.WriteAsync(entry, cancellationToken);
                Emitted++;
            }
        }

        public Task EnumerateChildrenAsync(SystemVolumeDescriptor systemVolume, StorageEntry directory,
            IStorageEntrySink entrySink, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The synthetic capacity workload contains no directory entries.");

        private static string Directory(int index) =>
            "d" + index.ToString("D6", CultureInfo.InvariantCulture) + new string('p', 57);

        private static string File(int index) =>
            "f" + index.ToString("D7", CultureInfo.InvariantCulture) + new string('n', 23);
    }

    private sealed class FixedSystemVolumeProvider(VolumeIdentity volume) : ISystemVolumeProvider
    {
        public SystemVolumeDescriptor GetSystemVolume() => new(volume, @"C:\");
    }

    private sealed class FixedContextProvider(VolumeIdentity volume)
        : IStorageClassificationContextProvider
    {
        public StorageClassificationContext GetClassificationContext(SystemVolumeDescriptor systemVolume) =>
            new(volume, @"C:\Windows", [@"C:\Program Files"], @"C:\ProgramData",
                @"C:\Users\Current", [@"C:\Users\Current\AppData\Local"],
                @"C:\Users\Public", @"C:\Users", []);
    }

    private sealed class FixedVolumeSpaceProvider(VolumeIdentity volume, int objectCount)
        : IVolumeSpaceProvider
    {
        private readonly long used = checked(objectCount * AllocatedBytesPerObject);

        public VolumeSpaceSnapshot GetVolumeSpace(SystemVolumeDescriptor systemVolume)
        {
            long capacity = checked(used + 1L * 1024 * 1024 * 1024 * 1024);
            long free = capacity - used;
            return VolumeSpaceSnapshot.Available(volume, DateTimeOffset.UnixEpoch,
                capacity, free, capacity, free, used, 0, 0);
        }
    }

    private sealed class InspectingAnalyzer : IStorageAnalyzer, ICompactStorageAnalyzer
    {
        private readonly UniversalStorageAnalyzer inner = new();

        internal int HierarchyNodeCount { get; private set; }
        internal int ProjectedGroupsAfterAnalysis { get; private set; }

        public IStorageAnalysisSession CreateSession(StorageAnalysisRequest request) =>
            inner.CreateSession(request);

        public StorageAnalysisResult Analyze(StorageAnalysisRequest request,
            StorageAccountingResult accountingResult, CancellationToken cancellationToken)
        {
            StorageHierarchyNode root = accountingResult.Root ??
                throw new InvalidOperationException("Capacity accounting did not publish a hierarchy.");
            HierarchyNodeCount = checked(root.Children.Count + 1);
            StorageAnalysisResult result = ((ICompactStorageAnalyzer)inner).Analyze(
                request, accountingResult, cancellationToken);
            ProjectedGroupsAfterAnalysis = accountingResult.ProjectedAllocationGroupCount;
            return result;
        }
    }

    private sealed class InspectingFindingBuilder : IUniversalFindingBuilder
    {
        private readonly UniversalFindingBuilder inner = new();

        internal int ProjectedGroupsAfterFindings { get; private set; }

        public UniversalFindingResult Build(
            UniversalFindingRequest request, CancellationToken cancellationToken)
        {
            UniversalFindingResult result = inner.Build(request, cancellationToken);
            ProjectedGroupsAfterFindings = request.AccountingResult.ProjectedAllocationGroupCount;
            return result;
        }
    }

    private sealed class ConstantProgress : IProgress<ProductScanProgress>
    {
        internal ProductScanProgress Last { get; private set; } =
            new(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                ProductScanPhase.DiscoveringSystemVolume, 0, 0, 0);
        internal long ReportCount { get; private set; }
        internal bool IsMonotonic { get; private set; } = true;

        public void Report(ProductScanProgress value)
        {
            IsMonotonic &= value.Phase >= Last.Phase &&
                value.ObjectsObserved >= Last.ObjectsObserved &&
                value.RawReportedAllocatedBytesObserved >= Last.RawReportedAllocatedBytesObserved &&
                value.TraversalIssuesObserved >= Last.TraversalIssuesObserved;
            Last = value;
            ReportCount++;
        }
    }
}
