using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Application.Analysis;

/// <summary>Inputs for one deterministic storage-analysis session.</summary>
/// <remarks>
/// <see cref="ClassificationContext"/> is authoritative input. Production callers must obtain it from a trusted
/// <see cref="IStorageClassificationContextProvider"/> whose implementation establishes root provenance and
/// same-volume binding. Analysis does not reopen context roots or independently validate them against the filesystem.
/// </remarks>
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
