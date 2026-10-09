using CDriveSmartClean.Application.Scanning.Accounting;

namespace CDriveSmartClean.Application.Analysis;

internal interface ICompactStorageAnalyzer
{
    StorageAnalysisResult Analyze(StorageAnalysisRequest request, StorageAccountingResult accountingResult,
        CancellationToken cancellationToken);
}
