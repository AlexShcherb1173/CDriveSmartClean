using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Application.Analysis;

/// <summary>Establishes authoritative classification roots for a supplied system volume.</summary>
/// <remarks>
/// Production implementations are responsible for trusted root provenance and same-volume binding. Analysis treats
/// the returned context as authoritative input and performs no filesystem or native provenance revalidation.
/// </remarks>
public interface IStorageClassificationContextProvider
{
    /// <summary>Returns trusted, volume-bound classification context for <paramref name="systemVolume"/>.</summary>
    StorageClassificationContext GetClassificationContext(SystemVolumeDescriptor systemVolume);
}
