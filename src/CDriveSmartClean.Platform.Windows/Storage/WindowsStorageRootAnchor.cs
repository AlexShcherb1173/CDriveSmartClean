using System.ComponentModel;
using System.Security;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Platform.Windows.Storage;

internal sealed class WindowsStorageRootAnchor
{
    private readonly SystemVolumeDescriptor descriptor;
    private readonly string[] components;
    private readonly StorageObjectIdentity? expectedRoot;

    private WindowsStorageRootAnchor(SystemVolumeDescriptor descriptor, string[] components, StorageObjectIdentity? expectedRoot)
    {
        this.descriptor = descriptor;
        this.components = (string[])components.Clone();
        this.expectedRoot = expectedRoot;
    }

    internal static WindowsStorageRootAnchor Production(SystemVolumeDescriptor supplied)
    {
        try
        {
            var discovered = new WindowsSystemVolumeProvider().GetSystemVolume();
            RequireDescriptor(discovered, supplied);
            return new(supplied, [], null); // Reuse the supplied value only after authoritative equality validation.
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException)
        {
            throw new WindowsStorageProvenanceException("Authoritative system-volume discovery unavailable.", error);
        }
    }

    // Immutable nonpublic fixture seam; native checks and subtree ID validation remain mandatory.
    internal static WindowsStorageRootAnchor Fixture(SystemVolumeDescriptor descriptor, string[] components, StorageObjectIdentity expectedRoot)
    {
        ArgumentNullException.ThrowIfNull(expectedRoot);
        if (components.Length == 0 || !expectedRoot.VolumeIdentity.Equals(descriptor.VolumeIdentity))
        {
            throw new WindowsStorageProvenanceException("Fixture requires an explicitly bound subtree identity.");
        }

        foreach (string component in components)
        {
            WindowsDirectoryHandleChain.ValidateComponent(component);
        }

        return new(descriptor, components, expectedRoot);
    }

    internal WindowsDirectoryHandleChain Open(SystemVolumeDescriptor supplied, CancellationToken token)
    {
        RequireDescriptor(descriptor, supplied);
        token.ThrowIfCancellationRequested();
        WindowsDirectoryHandleChain? chain = null;
        try
        {
            chain = new(descriptor.VolumeIdentity, token);
            chain.Append(components, descriptor.RootPath, token);
            if (expectedRoot is not null)
            {
                chain.RequireExpected(expectedRoot, descriptor.RootPath);
            }

            return chain;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            chain?.Dispose();
            throw new WindowsStorageProvenanceException("Trusted root anchor unavailable or changed.", error);
        }
        catch
        {
            chain?.Dispose();
            throw;
        }
    }

    private static void RequireDescriptor(SystemVolumeDescriptor expected, SystemVolumeDescriptor supplied)
    {
        if (!expected.VolumeIdentity.Equals(supplied.VolumeIdentity) ||
            !string.Equals(expected.RootPath, supplied.RootPath, StringComparison.Ordinal))
        {
            throw new WindowsStorageProvenanceException("Descriptor is not the authorized root.");
        }
    }
}

internal sealed class WindowsStorageProvenanceException(string message, Exception? innerException = null)
    : SecurityException(message, innerException)
{
}
