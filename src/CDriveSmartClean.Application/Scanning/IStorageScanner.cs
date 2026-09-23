using CDriveSmartClean.Application.Scanning.Observations;

namespace CDriveSmartClean.Application.Scanning;

public interface IStorageScanner
{
    Task<ScanResult> ScanAsync(
        ScanRequest request,
        IStorageObservationSink observationSink,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken);
}
