using System.Reflection;
using System.Xml.Linq;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Desktop.Infrastructure;
using CDriveSmartClean.Desktop.ViewModels;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Runtime;
using Xunit;

namespace CDriveSmartClean.Desktop.Tests;

public sealed class MainWindowViewModelTests
{
    [Fact]
    public void InitialStateIsIdleWithNoScanOrResults()
    {
        int calls = 0;
        using var viewModel = new MainWindowViewModel((_, _, _) => { calls++; return Task.FromResult<ProductScanResult>(null!); },
            new ImmediateSynchronizationContext());
        Assert.Equal(DesktopScanState.Idle, viewModel.State);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Null(viewModel.ActiveSessionId);
        Assert.Empty(viewModel.FindingRows);
        Assert.Equal(0, viewModel.ObjectsObserved);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task StartCreatesSessionAndGuardsAgainstSecondScan()
    {
        var completion = NewCompletion();
        ProductScanRequest? request = null;
        int calls = 0;
        using var viewModel = new MainWindowViewModel((value, _, _) =>
        {
            request = value;
            Interlocked.Increment(ref calls);
            return completion.Task;
        }, new ImmediateSynchronizationContext());

        Task running = viewModel.StartScanAsync();
        await WaitUntilAsync(() => request is not null);
        Task ignored = viewModel.StartScanAsync();
        await ignored;
        Assert.NotEqual(Guid.Empty, request!.ScanSessionId);
        Assert.Equal(request.ScanSessionId, viewModel.ActiveSessionId);
        Assert.Equal(DesktopScanState.Scanning, viewModel.State);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.True(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal(1, calls);
        completion.SetResult(CreateResult(request.ScanSessionId));
        await running;
    }

    [Fact]
    public async Task SuccessfulResultPublishesTruthfulImmutableSummaries()
    {
        var completion = NewCompletion();
        ProductScanRequest? request = null;
        using var viewModel = new MainWindowViewModel((value, _, _) => { request = value; return completion.Task; },
            new ImmediateSynchronizationContext());
        Task running = viewModel.StartScanAsync();
        await WaitUntilAsync(() => request is not null);
        completion.SetResult(CreateResult(request!.ScanSessionId, includeMixedScopes: true));
        await running;

        Assert.Equal(DesktopScanState.Completed, viewModel.State);
        Assert.Null(viewModel.ActiveSessionId);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.Equal(@"C:\", viewModel.VolumeSummary.RootPath);
        Assert.Equal("1000 B", viewModel.VolumeSummary.Capacity);
        Assert.Equal("600 B", viewModel.VolumeSummary.Used);
        Assert.Equal("400 B", viewModel.VolumeSummary.Free);
        Assert.Equal(3, viewModel.FindingRows.Count);
        Assert.Single(viewModel.CategoryRows);
        Assert.Equal(FindingCategory.System, viewModel.CategoryRows[0].Category);
        Assert.Single(viewModel.IssueRows);
        Assert.Equal(StorageTraversalIssueKind.Inaccessible, viewModel.IssueRows[0].Kind);
    }

    [Fact]
    public async Task CancelTransitionsImmediatelyThenCancellationCompletes()
    {
        ProductScanRequest? request = null;
        CancellationToken token = default;
        using var viewModel = new MainWindowViewModel(async (value, _, cancellationToken) =>
        {
            request = value;
            token = cancellationToken;
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return null!;
        }, new ImmediateSynchronizationContext());
        Task running = viewModel.StartScanAsync();
        await WaitUntilAsync(() => request is not null);
        viewModel.CancelScan();
        Assert.Equal(DesktopScanState.Cancelling, viewModel.State);
        Assert.False(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
        Assert.True(token.IsCancellationRequested);
        Assert.Empty(viewModel.FindingRows);
        await running;
        Assert.Equal(DesktopScanState.Cancelled, viewModel.State);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task UnexpectedFailureIsNotConvertedToSuccess()
    {
        using var viewModel = new MainWindowViewModel((_, _, _) =>
            Task.FromException<ProductScanResult>(new IOException("concise failure")),
            new ImmediateSynchronizationContext());
        await viewModel.StartScanAsync();
        Assert.Equal(DesktopScanState.Failed, viewModel.State);
        Assert.Contains("concise failure", viewModel.ErrorMessage, StringComparison.Ordinal);
        Assert.Empty(viewModel.FindingRows);
        Assert.True(viewModel.StartCommand.CanExecute(null));
        Assert.False(viewModel.CancelCommand.CanExecute(null));
    }

    [Fact]
    public async Task ContradictoryResultSessionFailsClosed()
    {
        using var viewModel = new MainWindowViewModel((_, _, _) =>
            Task.FromResult(CreateResult(Guid.NewGuid())), new ImmediateSynchronizationContext());
        await viewModel.StartScanAsync();
        Assert.Equal(DesktopScanState.Failed, viewModel.State);
        Assert.Empty(viewModel.FindingRows);
        Assert.Contains("different session", viewModel.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletedProgressIsInformationalAndMatchingCountersAreExact()
    {
        var completion = NewCompletion();
        ProductScanRequest? request = null;
        IProgress<ProductScanProgress>? progress = null;
        using var viewModel = new MainWindowViewModel((value, observer, _) =>
        {
            request = value;
            progress = observer;
            return completion.Task;
        }, new ImmediateSynchronizationContext());
        Task running = viewModel.StartScanAsync();
        await WaitUntilAsync(() => progress is not null);
        progress!.Report(new ProductScanProgress(request!.ScanSessionId, ProductScanPhase.Completed, 7, 80, 2));
        Assert.Equal(DesktopScanState.Scanning, viewModel.State);
        Assert.Equal(ProductScanPhase.Completed, viewModel.ProgressPhase);
        Assert.Equal(7, viewModel.ObjectsObserved);
        Assert.Equal(80, viewModel.RawReportedAllocatedBytesObserved);
        Assert.Equal(2, viewModel.TraversalIssuesObserved);
        completion.SetResult(CreateResult(request.ScanSessionId));
        await running;
        Assert.Equal(DesktopScanState.Completed, viewModel.State);
    }

    [Fact]
    public async Task DelayedProgressAndPostDisposeCallbacksAreIgnored()
    {
        var context = new QueuedSynchronizationContext();
        var completion = NewCompletion();
        ProductScanRequest? request = null;
        IProgress<ProductScanProgress>? progress = null;
        using var viewModel = new MainWindowViewModel((value, observer, _) =>
        {
            request = value;
            progress = observer;
            return completion.Task;
        }, context);
        Task running = viewModel.StartScanAsync();
        await WaitUntilAsync(() => progress is not null);
        progress!.Report(new ProductScanProgress(request!.ScanSessionId, ProductScanPhase.TraversingAndAccounting, 9, 10, 1));
        Assert.Equal(1, context.PendingCount);
        completion.SetResult(CreateResult(request.ScanSessionId));
        await running;
        context.DrainAll();
        Assert.Equal(0, viewModel.ObjectsObserved);

        viewModel.Dispose();
        progress.Report(new ProductScanProgress(request.ScanSessionId, ProductScanPhase.Completed, 99, 99, 99));
        context.DrainAll();
        Assert.True(viewModel.IsDisposed);
        Assert.Equal(0, viewModel.ObjectsObserved);
    }

    [Fact]
    public void CoalescingProgressQueuesOneDeliveryAndDeliversLatest()
    {
        var context = new QueuedSynchronizationContext();
        ProductScanProgress? delivered = null;
        using var progress = new CoalescingProgress(context, value => delivered = value);
        Guid session = Guid.NewGuid();
        for (int index = 0; index < 100; index++)
            progress.Report(new ProductScanProgress(session, ProductScanPhase.TraversingAndAccounting, index, index, 0));
        Assert.Equal(1, context.PendingCount);
        context.DrainAll();
        Assert.NotNull(delivered);
        Assert.Equal(99, delivered!.ObjectsObserved);
        Assert.Equal(0, context.PendingCount);
    }

    [Fact]
    public void ProjectAndSourceContractsRemainNarrowAndReadOnly()
    {
        string root = FindRepositoryRoot();
        XDocument desktop = XDocument.Load(Path.Combine(root, "src/CDriveSmartClean.Desktop/CDriveSmartClean.Desktop.csproj"));
        Assert.Equal("net10.0-windows", Value(desktop, "TargetFramework"));
        Assert.Equal("x64", Value(desktop, "PlatformTarget"));
        Assert.Equal("true", Value(desktop, "UseWPF"));
        Assert.Equal(["CDriveSmartClean.Runtime"], desktop.Descendants("ProjectReference")
            .Select(item => Path.GetFileNameWithoutExtension(item.Attribute("Include")!.Value)).ToArray());
        Assert.Empty(desktop.Descendants("PackageReference"));
        Assert.Empty(typeof(MainWindowViewModel).Assembly.GetExportedTypes());
        Assert.Equal(5, typeof(SystemVolumeScanWorkflow).Assembly.GetExportedTypes()
            .Count(type => type.Namespace == "CDriveSmartClean.Runtime"));

        string source = string.Join('\n', Directory.EnumerateFiles(Path.Combine(root, "src/CDriveSmartClean.Desktop"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));
        string[] forbidden = ["File.Delete", "File.Move", "File.Write", "Directory.Delete", "Registry", "Process.",
            "PowerShell", "cmd.exe", "FileStream", "Directory.Enumerate", "DllImport", "LibraryImport", "HOMESERVER",
            "WEB01", "Docker", "Caddy", "Tailscale", "Yandex Cloud", "REG.RU", "cdrivesmartc.tech"];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
        string xaml = File.ReadAllText(Path.Combine(root, "src/CDriveSmartClean.Desktop/MainWindow.xaml"));
        Assert.Contains("Analysis only", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Delete\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Clean\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Remove\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Fix\"", xaml, StringComparison.OrdinalIgnoreCase);
    }

    private static TaskCompletionSource<ProductScanResult> NewCompletion() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 100 && !condition(); attempt++) await Task.Delay(10);
        Assert.True(condition());
    }

    private static ProductScanResult CreateResult(Guid sessionId, bool includeMixedScopes = false)
    {
        var volume = new VolumeIdentity(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var descriptor = new SystemVolumeDescriptor(volume, @"C:\");
        var aggregate = new StorageAggregate(rawReportedAllocatedBytes: 600,
            directAttributedObservedAllocatedBytes: 600, inclusiveAttributedObservedAllocatedBytes: 600);
        var summary = new StorageAccountingSummary(aggregate, AccountingReason.None);
        var root = new StorageHierarchyNode(string.Empty, aggregate, []);
        var snapshot = VolumeSpaceSnapshot.Available(volume, DateTimeOffset.UnixEpoch, 1000, 400, 1000, 400, 600, 0, 0);
        var accounting = new StorageAccountingResult(summary, root, [],
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<StorageTraversalIssueKind, long> { [StorageTraversalIssueKind.Inaccessible] = 2 });
        CategorySummary[] categories = Enum.GetValues<FindingCategory>().Select(category =>
            new CategorySummary(category, 0, 0, 0, 0, 0, AnalysisQuality.Complete, AnalysisReason.None)).ToArray();
        var analysis = new StorageAnalysisResult(AnalysisQuality.Complete, AnalysisReason.None,
            AccountingQuality.Complete, AccountingReason.None, categories, [], [], [], []);
        var findings = new List<Finding> { CreateFinding(sessionId, volume, FindingScope.CategoryAggregate,
            FindingCategory.System, [], null, null, null, 400) };
        if (includeMixedScopes)
        {
            findings.Add(CreateFinding(sessionId, volume, FindingScope.File, FindingCategory.ApplicationData,
                ["file.bin"], new StorageObjectIdentity(volume, Guid.NewGuid()), 1, 0, 100));
            findings.Add(CreateFinding(sessionId, volume, FindingScope.HierarchyArea, FindingCategory.UserData,
                ["Users"], null, 2, 1, 200));
        }
        var findingResult = new UniversalFindingResult(AnalysisQuality.Complete, UniversalFindingReason.None,
            AnalysisQuality.Complete, AnalysisReason.None, findings);
        ConstructorInfo constructor = typeof(ProductScanResult).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        return (ProductScanResult)constructor.Invoke([sessionId, descriptor, accounting, analysis, findingResult]);
    }

    private static Finding CreateFinding(Guid sessionId, VolumeIdentity volume, FindingScope scope,
        FindingCategory category, string[] paths, StorageObjectIdentity? identity, long? files, long? directories,
        long allocated) => new(Guid.NewGuid(), sessionId, category.ToString(), scope, category, paths, identity,
        files, directories, [FindingFacet.Large], new SizeMetrics(allocated + 1, allocated, null),
        ReclaimEstimate.Unknown("finding.reclaim.unknown"),
        new RiskAssessment(RiskLevel.High, Confidence.Medium, ["review"]), Confidence.Low,
        category == FindingCategory.System ? ProtectionState.Protected : ProtectionState.ReviewRequired,
        [new Evidence("test", "test", Confidence.High)]);

    private static string Value(XDocument document, string name) => document.Descendants(name).Single().Value;
    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "CDriveSmartClean.sln"))) return directory.FullName;
        throw new InvalidOperationException("Repository root was not found.");
    }

    private sealed class ImmediateSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => callback(state);
    }

    private sealed class QueuedSynchronizationContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> callbacks = new();
        internal int PendingCount => callbacks.Count;
        public override void Post(SendOrPostCallback callback, object? state) => callbacks.Enqueue((callback, state));
        internal void DrainAll()
        {
            while (callbacks.TryDequeue(out var item)) item.Callback(item.State);
        }
    }
}
