using CDriveSmartClean.Application.Analysis;

namespace CDriveSmartClean.Analysis;

public sealed class UniversalStorageAnalyzer : IStorageAnalyzer
{
    public IStorageAnalysisSession CreateSession(StorageAnalysisRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new StorageAnalysisSession(request);
    }
}
