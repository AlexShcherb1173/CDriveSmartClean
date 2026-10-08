using System.Collections.ObjectModel;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Runtime;

public sealed class ProductScanResult
{
    internal ProductScanResult(Guid scanSessionId, SystemVolumeDescriptor systemVolume,
        StorageAccountingResult accountingResult, StorageAnalysisResult analysisResult,
        UniversalFindingResult findingResult)
    {
        if (scanSessionId == Guid.Empty)
            throw new ArgumentException("Scan session identifier cannot be empty.", nameof(scanSessionId));
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(accountingResult);
        ArgumentNullException.ThrowIfNull(analysisResult);
        ArgumentNullException.ThrowIfNull(findingResult);

        VolumeReconciliation reconciliation = accountingResult.Reconciliation;
        if (!systemVolume.VolumeIdentity.Equals(reconciliation.StartSnapshot.VolumeIdentity) ||
            !systemVolume.VolumeIdentity.Equals(reconciliation.EndSnapshot.VolumeIdentity))
            throw new ArgumentException("Result volume identities must agree.", nameof(accountingResult));

        var issueCounts = new Dictionary<StorageTraversalIssueKind, long>();
        foreach ((StorageTraversalIssueKind kind, long count) in accountingResult.IssueCounts)
        {
            if (!Enum.IsDefined(kind)) throw new ArgumentException("Unknown issue kind.", nameof(accountingResult));
            ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(accountingResult));
            issueCounts.Add(kind, count);
        }

        Finding[] findings = findingResult.Findings.ToArray();
        if (findings.Any(finding => finding is null))
            throw new ArgumentException("Findings cannot contain null items.", nameof(findingResult));
        if (findings.Any(finding => finding.ScanSessionId != scanSessionId))
            throw new ArgumentException("Finding scan session identifiers must agree.", nameof(findingResult));

        ScanSessionId = scanSessionId;
        SystemVolume = systemVolume;
        StartSnapshot = reconciliation.StartSnapshot;
        EndSnapshot = reconciliation.EndSnapshot;
        AccountingQuality = accountingResult.Summary.Quality;
        AccountingReason = accountingResult.Summary.Reasons;
        ReconciliationQuality = reconciliation.Quality;
        ReconciliationReasons = reconciliation.Reasons;
        ObservedCoveragePercent = reconciliation.ObservedCoveragePercent;
        AnalysisQuality = analysisResult.Quality;
        AnalysisReason = analysisResult.Reasons;
        FindingQuality = findingResult.Quality;
        UniversalFindingReason = findingResult.Reasons;
        TraversalCompleted = accountingResult.TraversalCompleted;
        IssueCounts = new ReadOnlyDictionary<StorageTraversalIssueKind, long>(issueCounts);
        Findings = new ReadOnlyCollection<Finding>(findings);
    }

    public Guid ScanSessionId { get; }
    public SystemVolumeDescriptor SystemVolume { get; }
    public VolumeSpaceSnapshot StartSnapshot { get; }
    public VolumeSpaceSnapshot EndSnapshot { get; }
    public AccountingQuality AccountingQuality { get; }
    public AccountingReason AccountingReason { get; }
    public AccountingQuality ReconciliationQuality { get; }
    public AccountingReason ReconciliationReasons { get; }
    public decimal? ObservedCoveragePercent { get; }
    public AnalysisQuality AnalysisQuality { get; }
    public AnalysisReason AnalysisReason { get; }
    public AnalysisQuality FindingQuality { get; }
    public UniversalFindingReason UniversalFindingReason { get; }
    public bool TraversalCompleted { get; }
    public IReadOnlyDictionary<StorageTraversalIssueKind, long> IssueCounts { get; }
    public IReadOnlyList<Finding> Findings { get; }
}
