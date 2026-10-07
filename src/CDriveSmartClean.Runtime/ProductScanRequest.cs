using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;

namespace CDriveSmartClean.Runtime;

public sealed class ProductScanRequest
{
    public ProductScanRequest(Guid scanSessionId, StorageAccountingOptions? accountingOptions = null,
        StorageAnalysisOptions? analysisOptions = null, UniversalFindingOptions? findingOptions = null)
    {
        if (scanSessionId == Guid.Empty)
            throw new ArgumentException("Scan session identifier cannot be empty.", nameof(scanSessionId));
        ScanSessionId = scanSessionId;
        AccountingOptions = accountingOptions ?? new StorageAccountingOptions();
        AnalysisOptions = analysisOptions ?? new StorageAnalysisOptions();
        FindingOptions = findingOptions ?? new UniversalFindingOptions();
    }

    public Guid ScanSessionId { get; }
    public StorageAccountingOptions AccountingOptions { get; }
    public StorageAnalysisOptions AnalysisOptions { get; }
    public UniversalFindingOptions FindingOptions { get; }
}
