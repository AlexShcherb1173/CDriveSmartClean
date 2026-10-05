using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;

namespace CDriveSmartClean.Application.Analysis;

public interface IStorageAnalysisSession : IStorageEntrySink
{
    StorageAnalysisResult Complete(StorageAccountingResult accountingResult, CancellationToken cancellationToken);
}
