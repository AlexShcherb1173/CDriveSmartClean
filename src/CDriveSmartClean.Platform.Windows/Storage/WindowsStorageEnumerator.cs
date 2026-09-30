using System.Runtime.Versioning;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Platform.Windows.Storage;

[SupportedOSPlatform("windows")]
public sealed class WindowsStorageEnumerator : IStorageEnumerator
{
    private readonly WindowsStorageRootAnchor? fixtureAnchor;

    public WindowsStorageEnumerator()
    {
    }

    internal WindowsStorageEnumerator(WindowsStorageRootAnchor fixtureAnchor)
    {
        this.fixtureAnchor = fixtureAnchor;
    }

    public async Task EnumerateRootAsync(
        SystemVolumeDescriptor systemVolume, IStorageEntrySink entrySink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(entrySink);
        cancellationToken.ThrowIfCancellationRequested();
        var anchor = fixtureAnchor ?? WindowsStorageRootAnchor.Production(systemVolume);
        using var chain = anchor.Open(systemVolume, cancellationToken);
        await WindowsDirectoryEntryReader.ReadAsync(chain, systemVolume.RootPath, entrySink, cancellationToken).ConfigureAwait(false);
    }

    public async Task EnumerateChildrenAsync(
        SystemVolumeDescriptor systemVolume, StorageEntry directory,
        IStorageEntrySink entrySink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(entrySink);
        cancellationToken.ThrowIfCancellationRequested();
        if (!directory.VolumeIdentity.Equals(systemVolume.VolumeIdentity))
        {
            throw new ArgumentException("Directory must belong to the system volume.", nameof(directory));
        }

        if (directory.ObjectKind != StorageObjectKind.Directory || directory.ReparseKind != ReparseKind.None)
        {
            throw new ArgumentException("Only an ordinary non-reparse directory may be enumerated.", nameof(directory));
        }

        if (StorageEntryAttributePolicy.IsRecallSensitive(directory.Attributes))
        {
            throw new StorageRecallSensitiveException(directory.CanonicalPath);
        }

        string prefix = systemVolume.RootPath.TrimEnd('\\') + "\\";
        if (!directory.CanonicalPath.StartsWith(prefix, StringComparison.Ordinal) || directory.CanonicalPath.Length <= prefix.Length)
        {
            throw new ArgumentException("Directory must be strictly within the authorized root.", nameof(directory));
        }

        string[] components = directory.CanonicalPath[prefix.Length..].Split('\\');
        foreach (string component in components)
        {
            try
            {
                WindowsDirectoryHandleChain.ValidateComponent(component);
            }
            catch (ArgumentException error)
            {
                throw new ArgumentException("Invalid directory component.", nameof(directory), error);
            }
        }

        if (directory.ObjectIdentity is null)
        {
            throw new StorageObjectIdentityUnavailableException(directory.CanonicalPath);
        }

        var anchor = fixtureAnchor ?? WindowsStorageRootAnchor.Production(systemVolume);
        using var chain = anchor.Open(systemVolume, cancellationToken);
        chain.Append(components, directory.CanonicalPath, cancellationToken);
        chain.RequireExpected(directory.ObjectIdentity, directory.CanonicalPath);
        // Same validated handle throughout. Contents stay live; this is not a filesystem snapshot.
        await WindowsDirectoryEntryReader.ReadAsync(chain, directory.CanonicalPath, entrySink, cancellationToken).ConfigureAwait(false);
    }
}
