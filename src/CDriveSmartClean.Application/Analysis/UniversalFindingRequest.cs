using CDriveSmartClean.Application.Scanning.Accounting;

namespace CDriveSmartClean.Application.Analysis;

public sealed class UniversalFindingRequest
{
    public UniversalFindingRequest(
        Guid scanSessionId,
        StorageAnalysisRequest analysisRequest,
        StorageAnalysisResult analysisResult,
        StorageAccountingResult accountingResult,
        UniversalFindingOptions? options = null)
    {
        if (scanSessionId == Guid.Empty)
            throw new ArgumentException("Scan session identifier cannot be empty.", nameof(scanSessionId));
        ArgumentNullException.ThrowIfNull(analysisRequest);
        ArgumentNullException.ThrowIfNull(analysisResult);
        ArgumentNullException.ThrowIfNull(accountingResult);

        ScanSessionId = scanSessionId;
        AnalysisRequest = analysisRequest;
        AnalysisResult = analysisResult;
        AccountingResult = accountingResult;
        Options = options ?? new UniversalFindingOptions();
    }

    public Guid ScanSessionId { get; }
    public StorageAnalysisRequest AnalysisRequest { get; }
    public StorageAnalysisResult AnalysisResult { get; }
    public StorageAccountingResult AccountingResult { get; }
    public UniversalFindingOptions Options { get; }
}
