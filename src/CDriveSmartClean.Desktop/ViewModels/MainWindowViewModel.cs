using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CDriveSmartClean.Desktop.Infrastructure;
using CDriveSmartClean.Desktop.Presentation;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Runtime;

namespace CDriveSmartClean.Desktop.ViewModels;

internal enum DesktopScanState { Idle, Scanning, Cancelling, Completed, Cancelled, Failed }

internal sealed class MainWindowViewModel : INotifyPropertyChanged, IDisposable
{
    private static readonly FindingCategory[] SupportedCategories =
    [FindingCategory.System, FindingCategory.Application, FindingCategory.ApplicationData,
        FindingCategory.UserData, FindingCategory.Unknown];
    private readonly Func<ProductScanRequest, IProgress<ProductScanProgress>?, CancellationToken,
        Task<ProductScanResult>> scanAsync;
    private readonly SynchronizationContext uiContext;
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
    private VolumeSummaryRow volumeSummary = VolumeSummaryRow.Empty;
    private IReadOnlyList<QualitySummaryRow> qualityRows = Array.Empty<QualitySummaryRow>();
    private IReadOnlyList<CategorySummaryRow> categoryRows = Array.Empty<CategorySummaryRow>();
    private IReadOnlyList<IssueSummaryRow> issueRows = Array.Empty<IssueSummaryRow>();
    private IReadOnlyList<FindingRow> findingRows = Array.Empty<FindingRow>();
    private bool disposed;

    internal MainWindowViewModel(
        Func<ProductScanRequest, IProgress<ProductScanProgress>?, CancellationToken, Task<ProductScanResult>> scanAsync,
        SynchronizationContext uiContext)
    {
        ArgumentNullException.ThrowIfNull(scanAsync);
        ArgumentNullException.ThrowIfNull(uiContext);
        this.scanAsync = scanAsync;
        this.uiContext = uiContext;
        startCommand = new AsyncCommand(StartScanAsync, CanStart);
        cancelCommand = new DelegateCommand(CancelScan, CanCancel);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public ICommand StartCommand => startCommand;
    public ICommand CancelCommand => cancelCommand;
    public DesktopScanState State => state;
    public string StatusText => state.ToString();
    public ProductScanPhase? ProgressPhase => progressPhase;
    public string ProgressPhaseText => progressPhase?.ToString() ?? "Not started";
    public long ObjectsObserved => objectsObserved;
    public long RawReportedAllocatedBytesObserved => rawReportedAllocatedBytesObserved;
    public long TraversalIssuesObserved => traversalIssuesObserved;
    public string ErrorMessage => errorMessage;
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
                SetError("The scan returned a result for a different session.");
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
        catch (Exception exception)
        {
            if (!DeactivateSession(sessionId, progress)) return;
            ClearResult();
            SetError(string.IsNullOrWhiteSpace(exception.Message) ? "The scan failed." : exception.Message);
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
        volumeSummary = new(result.SystemVolume.RootPath,
            PresentationFormatter.FormatBytes(result.EndSnapshot.CapacityBytes),
            PresentationFormatter.FormatBytes(result.EndSnapshot.UsedBytes),
            PresentationFormatter.FormatBytes(result.EndSnapshot.FreeBytes));
        qualityRows = Array.AsReadOnly(new[]
        {
            new QualitySummaryRow("Accounting", result.AccountingQuality.ToString(), result.AccountingReason.ToString()),
            new QualitySummaryRow("Reconciliation", result.ReconciliationQuality.ToString(), result.ReconciliationReasons.ToString()),
            new QualitySummaryRow("Analysis", result.AnalysisQuality.ToString(), result.AnalysisReason.ToString()),
            new QualitySummaryRow("Findings", result.FindingQuality.ToString(), result.UniversalFindingReason.ToString()),
            new QualitySummaryRow("Observed coverage", PresentationFormatter.FormatPercentage(result.ObservedCoveragePercent), string.Empty),
        });
        categoryRows = Array.AsReadOnly(result.Findings
            .Where(finding => finding.Scope == FindingScope.CategoryAggregate &&
                              SupportedCategories.Contains(finding.PrimaryCategory))
            .OrderBy(finding => Array.IndexOf(SupportedCategories, finding.PrimaryCategory))
            .Select(finding => new CategorySummaryRow(finding.PrimaryCategory,
                PresentationFormatter.FormatBytes(finding.SizeMetrics.AllocatedBytes))).ToArray());
        issueRows = Array.AsReadOnly(result.IssueCounts.Where(item => item.Value > 0).OrderBy(item => item.Key)
            .Select(item => new IssueSummaryRow(item.Key, item.Value)).ToArray());
        findingRows = Array.AsReadOnly(result.Findings.Select(FindingRow.FromFinding).ToArray());
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
        volumeSummary = VolumeSummaryRow.Empty;
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
