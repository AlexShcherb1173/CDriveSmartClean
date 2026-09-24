using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Traversal;

/// <summary>Evidence of incomplete traversal coverage, not a classification or cleanup decision.</summary>
public sealed class StorageTraversalIssue
{
    public StorageTraversalIssue(VolumeIdentity volumeIdentity, string canonicalPath, StorageTraversalIssueKind kind)
    {
        ArgumentNullException.ThrowIfNull(volumeIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        VolumeIdentity = volumeIdentity;
        CanonicalPath = canonicalPath;
        Kind = kind;
    }

    public VolumeIdentity VolumeIdentity { get; }

    public string CanonicalPath { get; }

    public StorageTraversalIssueKind Kind { get; }
}
