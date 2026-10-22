using System.Globalization;
using System.Reflection;
using System.Xml.Linq;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Desktop.Infrastructure;
using CDriveSmartClean.Desktop.Presentation;
using CDriveSmartClean.Desktop.Resources;
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
        Assert.Equal("System", viewModel.CategoryRows[0].Category);
        Assert.Single(viewModel.IssueRows);
        Assert.Equal("Inaccessible", viewModel.IssueRows[0].Kind);
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
        Assert.Equal("The scan failed.", viewModel.ErrorMessage);
        Assert.DoesNotContain("concise failure", viewModel.ErrorMessage, StringComparison.Ordinal);
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
    public void LanguageInitializationIsSideEffectFreeAndExposesExactlyTwoChoices()
    {
        int saves = 0;
        using var viewModel = CreateLocalizedViewModel("ru", _ => { saves++; return true; });
        Assert.Equal("ru", viewModel.SelectedLanguageCode);
        Assert.Equal(["en", "ru"], viewModel.Languages.Select(item => item.Code));
        Assert.Equal(["English", "Русский"], viewModel.Languages.Select(item => item.DisplayName));
        Assert.Equal(0, saves);
        Assert.Empty(viewModel.LanguageMessage);
    }

    [Theory]
    [InlineData("en", "ru", "Language saved. Restart CDriveSmartClean to apply it.")]
    [InlineData("ru", "en", "Язык сохранён. Перезапустите CDriveSmartClean, чтобы применить его.")]
    public void LanguageChangePersistsAndUsesCurrentWindowLanguage(string active, string requested, string message)
    {
        string? saved = null;
        using var viewModel = CreateLocalizedViewModel(active, value => { saved = value; return true; });
        viewModel.SelectedLanguageCode = requested;
        Assert.Equal(requested, saved);
        Assert.Equal(requested, viewModel.SelectedLanguageCode);
        Assert.Equal(message, viewModel.LanguageMessage);
    }

    [Fact]
    public void LanguageSaveFailureIsNonfatalAndRevertsWithoutClaimingSuccess()
    {
        using var viewModel = CreateLocalizedViewModel("en", _ => false);
        viewModel.SelectedLanguageCode = "ru";
        Assert.Equal("en", viewModel.SelectedLanguageCode);
        Assert.Equal("The language preference could not be saved.", viewModel.LanguageMessage);
        Assert.DoesNotContain("saved. Restart", viewModel.LanguageMessage, StringComparison.Ordinal);
        Assert.True(viewModel.StartCommand.CanExecute(null));
    }

    [Fact]
    public async Task RussianStatusPhaseAndResultRowsAreLocalized()
    {
        ProductScanRequest? request = null;
        IProgress<ProductScanProgress>? progress = null;
        var completion = NewCompletion();
        using var viewModel = CreateLocalizedViewModel("ru", _ => true, (value, observer, _) =>
        {
            request = value;
            progress = observer;
            return completion.Task;
        });

        Task running = viewModel.StartScanAsync();
        await WaitUntilAsync(() => progress is not null);
        Assert.Equal("Сканирование", viewModel.StatusText);
        progress!.Report(new ProductScanProgress(request!.ScanSessionId, ProductScanPhase.TraversingAndAccounting, 1, 2, 3));
        Assert.Equal("Обход и учёт", viewModel.ProgressPhaseText);
        completion.SetResult(CreateResult(request.ScanSessionId, includeMixedScopes: true));
        await running;

        Assert.Equal("Завершено", viewModel.StatusText);
        Assert.Equal(["Учёт пространства", "Сверка данных", "Анализ", "Результаты", "Полнота анализа"],
            viewModel.QualityRows.Select(row => row.Name));
        Assert.All(viewModel.QualityRows.Take(4), row => Assert.Equal("Полное", row.Value));
        Assert.All(viewModel.QualityRows.Take(4), row => Assert.Equal("Нет", row.Reason));
        Assert.Equal("Система", Assert.Single(viewModel.CategoryRows).Category);
        Assert.Equal("Нет доступа", Assert.Single(viewModel.IssueRows).Kind);
        Assert.Equal("Система", viewModel.FindingRows[0].DisplayName);
        Assert.Equal("Система", viewModel.FindingRows[0].Category);
        Assert.Equal("Сводка по категории", viewModel.FindingRows[0].Scope);
        Assert.Equal("Крупный", viewModel.FindingRows[0].Facets);
        Assert.Contains(viewModel.FindingRows, row => row.DisplayName == "file.bin");
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
        Assert.Equal([typeof(Strings)], typeof(MainWindowViewModel).Assembly.GetExportedTypes());
        Assert.Equal(5, typeof(SystemVolumeScanWorkflow).Assembly.GetExportedTypes()
            .Count(type => type.Namespace == "CDriveSmartClean.Runtime"));

        string[] sourceFiles = Directory.EnumerateFiles(Path.Combine(root, "src/CDriveSmartClean.Desktop"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();
        string preferenceStorePath = sourceFiles.Single(path => Path.GetFileName(path) == "LanguagePreferenceStore.cs");
        string source = string.Join('\n', sourceFiles.Where(path => path != preferenceStorePath).Select(File.ReadAllText));
        string[] forbidden = ["File.Delete", "File.Move", "File.Write", "Directory.Delete", "Registry", "Process.",
            "PowerShell", "cmd.exe", "FileStream", "Directory.Enumerate", "DllImport", "LibraryImport", "HOMESERVER",
            "WEB01", "Docker", "Caddy", "Tailscale", "Yandex Cloud", "REG.RU", "cdrivesmartc.tech"];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
        string preferenceSource = File.ReadAllText(preferenceStorePath);
        string[] preferenceForbidden = ["File.Delete", "File.Move", "Directory.Delete", "Directory.Enumerate",
            "Registry", "Process.", "PowerShell", "cmd.exe", "DllImport", "LibraryImport"];
        Assert.All(preferenceForbidden,
            value => Assert.DoesNotContain(value, preferenceSource, StringComparison.OrdinalIgnoreCase));
        Assert.Contains("File.ReadAllText", preferenceSource, StringComparison.Ordinal);
        Assert.Contains("File.WriteAllText", preferenceSource, StringComparison.Ordinal);
        Assert.Contains("Directory.CreateDirectory", preferenceSource, StringComparison.Ordinal);
        string xaml = File.ReadAllText(Path.Combine(root, "src/CDriveSmartClean.Desktop/MainWindow.xaml"));
        string[] localizedLiterals = ["Analysis only", "Start Scan", "Scan progress", "Objects observed",
            "Raw reported allocated bytes", "Traversal issues observed", "Quality and coverage",
            "Category allocation", "Risk confidence", "Finding confidence"];
        Assert.All(localizedLiterals, value => Assert.DoesNotContain($"\"{value}\"", xaml, StringComparison.Ordinal));
        Assert.Contains("{x:Static resources:Strings.AnalysisOnlyBanner}", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding SelectedLanguageCode", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Delete\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Clean\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Remove\"", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Content=\"Fix\"", xaml, StringComparison.OrdinalIgnoreCase);

        string appSource = File.ReadAllText(Path.Combine(root, "src/CDriveSmartClean.Desktop/App.xaml.cs"));
        Assert.True(appSource.IndexOf("DesktopCulture.Apply", StringComparison.Ordinal) <
                    appSource.IndexOf("new MainWindow", StringComparison.Ordinal));
        string presentationSource = string.Join('\n',
            File.ReadAllText(Path.Combine(root, "src/CDriveSmartClean.Desktop/ViewModels/MainWindowViewModel.cs")),
            File.ReadAllText(Path.Combine(root, "src/CDriveSmartClean.Desktop/Presentation/FindingRow.cs")),
            File.ReadAllText(Path.Combine(root, "src/CDriveSmartClean.Desktop/Presentation/SummaryRows.cs")));
        Assert.DoesNotContain(".ToString()", presentationSource, StringComparison.Ordinal);
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
        long allocated) => new(Guid.NewGuid(), sessionId,
        scope == FindingScope.CategoryAggregate ? category.ToString() : paths.FirstOrDefault() ?? category.ToString(),
        scope, category, paths, identity,
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

    private static MainWindowViewModel CreateLocalizedViewModel(string cultureName, Func<string, bool> save,
        Func<ProductScanRequest, IProgress<ProductScanProgress>?, CancellationToken, Task<ProductScanResult>>? scan = null)
    {
        var localizer = new PresentationLocalizer(CultureInfo.GetCultureInfo(cultureName));
        return new MainWindowViewModel(scan ?? ((_, _, _) => Task.FromResult<ProductScanResult>(null!)),
            new ImmediateSynchronizationContext(), localizer, save, cultureName);
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
