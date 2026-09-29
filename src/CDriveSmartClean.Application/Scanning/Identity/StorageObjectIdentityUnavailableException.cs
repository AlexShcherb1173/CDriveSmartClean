using System.Diagnostics.CodeAnalysis;

namespace CDriveSmartClean.Application.Scanning.Identity;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "Every identity-unavailable exception requires the affected canonical path.")]
public sealed class StorageObjectIdentityUnavailableException : IOException
{
    public StorageObjectIdentityUnavailableException(string canonicalPath)
        : base("A stable supported native object identity is unavailable.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        CanonicalPath = canonicalPath;
    }

    public string CanonicalPath { get; }
}
