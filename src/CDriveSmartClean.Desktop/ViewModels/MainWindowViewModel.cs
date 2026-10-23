using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CDriveSmartClean.Desktop.Infrastructure;
using CDriveSmartClean.Desktop.Localization;
using CDriveSmartClean.Desktop.Presentation;
using CDriveSmartClean.Desktop.Resources;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Runtime;

namespace CDriveSmartClean.Desktop.ViewModels;

internal enum DesktopScanState { Idle, Scanning, Cancelling, Completed, Cancelled, Failed }
internal sealed record LanguageOption(string Code, string DisplayName);

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly FindingCategory[] SupportedCategories =
    [FindingCategory.System, FindingCategory.Application, FindingCategory.ApplicationData,
        FindingCategory.UserData, FindingCategory.Unknown];
    private readonly Func<ProductScanRequest, IProgress<ProductScanProgress>?, CancellationToken,
        Task<ProductScanResult>> scanAsync;
    private readonly SynchronizationContext uiContext;
    private readonly PresentationLocalizer localizer;
    private readonly Func<string, bool> saveLanguagePreference;
    private readonly AsyncCommand startCommand;
    private readonly DelegateCommand cancelCommand;
    private CancellationTokenSource? activeCancellation;
    private CoalescingProgress? activeProgress;
    private Guid? activeSessionId;
    private DesktopScanState state = DesktopScanState.Idle;
    private ProductScanPhase? progressPhase;
    private long objectsObserved;
    private long rawReportedAllocatedBytesObserved;
    private long traversalIssuesObserved;
    private string errorMessage = string.Empty;
    private string languageMessage = string.Empty;
    private string selectedLanguageCode;
    private VolumeSummaryRow volumeSummary;
    private IReadOnlyList<QualitySummaryRow> qualityRows = Array.Empty<QualitySummaryRow>();
    private IReadOnlyList<CategorySummaryRow> categoryRows = Array.Empty<CategorySummaryRow>();
    private IReadOnlyList<IssueSummaryRow> issueRows = Array.Empty<IssueSummaryRow>();
    private IReadOnlyList<FindingRow> findingRows = Array.Empty<FindingRow>();
    private bool disposed;

    internal MainWindowViewModel(
        Func<ProductScanRequest, IProgress<ProductScanProgress>?, CancellationToken, Task<ProductScanResult>> scanAsync,
        SynchronizationContext uiContext)
        : this(scanAsync, uiContext, new PresentationLocalizer(CultureInfo.GetCultureInfo(DesktopCulture.English)),
            _ => false, DesktopCulture.English)
    {
    }

    internal MainWindowViewModel(
        Func<ProductScanRequest, IProgress<ProductScanProgress>?, CancellationToken, Task<ProductScanResult>> scanAsync,
        SynchronizationContext uiContext, PresentationLocalizer localizer,
        Func<string, bool> saveLanguagePreference, string activeLanguageCode)
    {
        ArgumentNullException.ThrowIfNull(scanAsync);
        ArgumentNullException.ThrowIfNull(uiContext);
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(saveLanguagePreference);
        this.scanAsync = scanAsync;
        this.uiContext = uiContext;
        this.localizer = localizer;
        this.saveLanguagePreference = saveLanguagePreference;
        selectedLanguageCode = DesktopCulture.Normalize(activeLanguageCode) ?? DesktopCulture.English;
        volumeSummary = VolumeSummaryRow.Empty(localizer);
        startCommand = new AsyncCommand(StartScanAsync, CanStart);
        cancelCommand = new DelegateCommand(CancelScan, CanCancel);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand StartCommand => startCommand;
    public ICommand CancelCommand => cancelCommand;
    public IReadOnlyList<LanguageOption> Languages { get; } =
        Array.AsReadOnly([new LanguageOption(DesktopCulture.English, "English"),
            new LanguageOption(DesktopCulture.Russian, "Русский")]);
    public string SelectedLanguageCode
    {
        get => selectedLanguageCode;
        set => ChangeLanguage(value);
    }
    public DesktopScanState State => state;
    public string StatusText => localizer.Localize(state);
    public ProductScanPhase? ProgressPhase => progressPhase;
    public string ProgressPhaseText => progressPhase is null
        ? localizer.Get(nameof(Strings.NotStarted))
        : localizer.Localize(progressPhase.Value);
    public long ObjectsObserved => objectsObserved;
    public long RawReportedAllocatedBytesObserved => rawReportedAllocatedBytesObserved;
    public long TraversalIssuesObserved => traversalIssuesObserved;
    public string ErrorMessage => errorMessage;
    public string LanguageMessage => languageMessage;
    public VolumeSummaryRow VolumeSummary => volumeSummary;
    public IReadOnlyList<QualitySummaryRow> QualityRows => qualityRows;
    public IReadOnlyList<CategorySummaryRow> CategoryRows => categoryRows;
    public IReadOnlyList<IssueSummaryRow> IssueRows => issueRows;
    public IReadOnlyList<FindingRow> FindingRows => findingRows;
    internal Guid? ActiveSessionId => activeSessionId;
    internal bool IsDisposed => disposed;

    internal async Task StartScanAsync()
    {
        if (!CanStart()) return;
        Guid sessionId = Guid.NewGuid();
        var cancellation = new CancellationTokenSource();
        var request = new ProductScanRequest(sessionId);
        var progress = new CoalescingProgress(uiContext, value => ApplyProgress(sessionId, value));
        activeSessionId = sessionId;
        activeCancellation = cancellation;
        activeProgress = progress;
        ResetForScan();
        SetState(DesktopScanState.Scanning);

        try
        {
            ProductScanResult result = await Task.Run(
                () => scanAsync(request, progress, cancellation.Token), cancellation.Token);
            if (!DeactivateSession(sessionId, progress)) return;
            if (result.ScanSessionId != sessionId)
            {
                SetError(localizer.Get(nameof(Strings.ScanWrongSession)));
                SetState(DesktopScanState.Failed);
                return;
            }
            PublishResult(result);
            SetState(DesktopScanState.Completed);
        }
        catch (OperationCanceledException)
        {
            if (!DeactivateSession(sessionId, progress)) return;
            ClearResult();
            SetState(DesktopScanState.Cancelled);
        }
        catch (Exception)
        {
            if (!DeactivateSession(sessionId, progress)) return;
            ClearResult();
            SetError(localizer.Get(nameof(Strings.ScanFailed)));
            SetState(DesktopScanState.Failed);
        }
        finally
        {
            if (ReferenceEquals(activeCancellation, cancellation)) activeCancellation = null;
            cancellation.Dispose();
            NotifyCommands();
        }
    }

    internal void CancelScan()
    {
        if (!CanCancel()) return;
        SetState(DesktopScanState.Cancelling);
        activeCancellation!.Cancel();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        activeProgress?.Dispose();
        activeProgress = null;
        activeSessionId = null;
        activeCancellation?.Cancel();
        NotifyCommands();
    }

    private bool CanStart() => !disposed && activeSessionId is null &&
                               state is not (DesktopScanState.Scanning or DesktopScanState.Cancelling);
    private bool CanCancel() => !disposed && state == DesktopScanState.Scanning && activeCancellation is not null;

    private void ApplyProgress(Guid sessionId, ProductScanProgress progress)
    {
        if (disposed || activeSessionId != sessionId || progress.ScanSessionId != sessionId ||
            state is not (DesktopScanState.Scanning or DesktopScanState.Cancelling)) return;
        progressPhase = progress.Phase;
        objectsObserved = progress.ObjectsObserved;
        rawReportedAllocatedBytesObserved = progress.RawReportedAllocatedBytesObserved;
        traversalIssuesObserved = progress.TraversalIssuesObserved;
        OnPropertyChanged(nameof(ProgressPhase));
        OnPropertyChanged(nameof(ProgressPhaseText));
        OnPropertyChanged(nameof(ObjectsObserved));
        OnPropertyChanged(nameof(RawReportedAllocatedBytesObserved));
        OnPropertyChanged(nameof(TraversalIssuesObserved));
    }

    private bool DeactivateSession(Guid sessionId, CoalescingProgress progress)
    {
        progress.Dispose();
        if (disposed || activeSessionId != sessionId) return false;
        if (ReferenceEquals(activeProgress, progress)) activeProgress = null;
        activeSessionId = null;
        return true;
    }

    private void PublishResult(ProductScanResult result)
    {
        string unavailable = localizer.Get(nameof(Strings.Unavailable));
        volumeSummary = new(result.SystemVolume.RootPath,
            PresentationFormatter.FormatBytes(result.EndSnapshot.CapacityBytes, unavailable),
            PresentationFormatter.FormatBytes(result.EndSnapshot.UsedBytes, unavailable),
            PresentationFormatter.FormatBytes(result.EndSnapshot.FreeBytes, unavailable));
        qualityRows = Array.AsReadOnly(new[]
        {
            new QualitySummaryRow(localizer.Get(nameof(Strings.Accounting)), localizer.Localize(result.AccountingQuality),
                localizer.Localize(result.AccountingReason)),
            new QualitySummaryRow(localizer.Get(nameof(Strings.Reconciliation)), localizer.Localize(result.ReconciliationQuality),
                localizer.Localize(result.ReconciliationReasons)),
            new QualitySummaryRow(localizer.Get(nameof(Strings.Analysis)), localizer.Localize(result.AnalysisQuality),
                localizer.Localize(result.AnalysisReason)),
            new QualitySummaryRow(localizer.Get(nameof(Strings.Findings)), localizer.Localize(result.FindingQuality),
                localizer.Localize(result.UniversalFindingReason)),
            new QualitySummaryRow(localizer.Get(nameof(Strings.ObservedCoverage)),
                PresentationFormatter.FormatPercentage(result.ObservedCoveragePercent, unavailable), string.Empty),
        });
        categoryRows = Array.AsReadOnly(result.Findings
            .Where(finding => finding.Scope == FindingScope.CategoryAggregate &&
                              SupportedCategories.Contains(finding.PrimaryCategory))
            .OrderBy(finding => Array.IndexOf(SupportedCategories, finding.PrimaryCategory))
            .Select(finding => new CategorySummaryRow(localizer.Localize(finding.PrimaryCategory),
                PresentationFormatter.FormatBytes(finding.SizeMetrics.AllocatedBytes, unavailable))).ToArray());
        issueRows = Array.AsReadOnly(result.IssueCounts.Where(item => item.Value > 0).OrderBy(item => item.Key)
            .Select(item => new IssueSummaryRow(localizer.Localize(item.Key), item.Value)).ToArray());
        findingRows = Array.AsReadOnly(result.Findings.Select(finding => FindingRow.FromFinding(finding, localizer)).ToArray());
        OnPropertyChanged(nameof(VolumeSummary));
        OnPropertyChanged(nameof(QualityRows));
        OnPropertyChanged(nameof(CategoryRows));
        OnPropertyChanged(nameof(IssueRows));
        OnPropertyChanged(nameof(FindingRows));
    }

    private void ResetForScan()
    {
        progressPhase = null;
        objectsObserved = 0;
        rawReportedAllocatedBytesObserved = 0;
        traversalIssuesObserved = 0;
        errorMessage = string.Empty;
        ClearResult();
        OnPropertyChanged(nameof(ProgressPhase));
        OnPropertyChanged(nameof(ProgressPhaseText));
        OnPropertyChanged(nameof(ObjectsObserved));
        OnPropertyChanged(nameof(RawReportedAllocatedBytesObserved));
        OnPropertyChanged(nameof(TraversalIssuesObserved));
        OnPropertyChanged(nameof(ErrorMessage));
    }

    private void ClearResult()
    {
        volumeSummary = VolumeSummaryRow.Empty(localizer);
        qualityRows = Array.Empty<QualitySummaryRow>();
        categoryRows = Array.Empty<CategorySummaryRow>();
        issueRows = Array.Empty<IssueSummaryRow>();
        findingRows = Array.Empty<FindingRow>();
        OnPropertyChanged(nameof(VolumeSummary));
        OnPropertyChanged(nameof(QualityRows));
        OnPropertyChanged(nameof(CategoryRows));
        OnPropertyChanged(nameof(IssueRows));
        OnPropertyChanged(nameof(FindingRows));
    }

    private void SetError(string message) { errorMessage = message; OnPropertyChanged(nameof(ErrorMessage)); }
    private void ChangeLanguage(string? requestedCulture)
    {
        string? normalized = DesktopCulture.Normalize(requestedCulture);
        if (normalized is null)
        {
            uiContext.Post(_ => OnPropertyChanged(nameof(SelectedLanguageCode)), null);
            return;
        }
        if (normalized == selectedLanguageCode) return;

        if (saveLanguagePreference(normalized))
        {
            selectedLanguageCode = normalized;
            OnPropertyChanged(nameof(SelectedLanguageCode));
            SetLanguageMessage(localizer.Get(nameof(Strings.LanguageRestartRequired)));
            return;
        }

        uiContext.Post(_ => OnPropertyChanged(nameof(SelectedLanguageCode)), null);
        SetLanguageMessage(localizer.Get(nameof(Strings.LanguagePreferenceSaveFailed)));
    }

    private void SetLanguageMessage(string message)
    {
        languageMessage = message;
        OnPropertyChanged(nameof(LanguageMessage));
    }
    private void SetState(DesktopScanState value)
    {
        if (state == value) return;
        state = value;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusText));
        NotifyCommands();
    }
    private void NotifyCommands()
    {
        startCommand.NotifyCanExecuteChanged();
        cancelCommand.NotifyCanExecuteChanged();
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
