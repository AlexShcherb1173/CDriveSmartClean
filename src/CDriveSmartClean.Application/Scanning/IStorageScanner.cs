namespace CDriveSmartClean.Application.Scanning;

public interface IStorageScanner
{
    Task<ScanResult> ScanAsync(
        ScanRequest request,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}
