namespace CDriveSmartClean.Application.Analysis;

public interface IStorageAnalyzer
{
    IStorageAnalysisSession CreateSession(StorageAnalysisRequest request);
}
