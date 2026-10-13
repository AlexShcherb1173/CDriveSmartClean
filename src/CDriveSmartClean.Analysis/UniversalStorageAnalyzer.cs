using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;

namespace CDriveSmartClean.Analysis;

public sealed class UniversalStorageAnalyzer : IStorageAnalyzer, ICompactStorageAnalyzer
{
    public IStorageAnalysisSession CreateSession(StorageAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new StorageAnalysisSession(request);
    }

    StorageAnalysisResult ICompactStorageAnalyzer.Analyze(StorageAnalysisRequest request,
        StorageAccountingResult accountingResult, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(accountingResult);
        return CompactStorageAnalysisBuilder.Build(request, accountingResult, cancellationToken);
    }
}
