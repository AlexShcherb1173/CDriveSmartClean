using System.Diagnostics.CodeAnalysis;

namespace CDriveSmartClean.Application.Scanning.Traversal;

[SuppressMessage("Design", "CA1032:Implement standard exception constructors",
    Justification = "The boundary requires a canonical path for every target-change exception.")]
public sealed class StorageTraversalTargetChangedException : IOException
{
    public StorageTraversalTargetChangedException(string canonicalPath)
        : base("The traversal target is no longer an ordinary non-reparse directory.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        CanonicalPath = canonicalPath;
    }

    public string CanonicalPath { get; }
}
