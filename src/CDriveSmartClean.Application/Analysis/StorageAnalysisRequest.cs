using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Application.Analysis;

public sealed class StorageAnalysisRequest
{
    public StorageAnalysisRequest(SystemVolumeDescriptor systemVolume,
        StorageClassificationContext classificationContext, StorageAnalysisOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(classificationContext);
        if (!systemVolume.VolumeIdentity.Equals(classificationContext.VolumeIdentity))
            throw new ArgumentException("System volume and classification context must agree.", nameof(classificationContext));
        SystemVolume = systemVolume;
        ClassificationContext = classificationContext;
        Options = options ?? new StorageAnalysisOptions();
    }

    public SystemVolumeDescriptor SystemVolume { get; }
    public StorageClassificationContext ClassificationContext { get; }
    public StorageAnalysisOptions Options { get; }
}
