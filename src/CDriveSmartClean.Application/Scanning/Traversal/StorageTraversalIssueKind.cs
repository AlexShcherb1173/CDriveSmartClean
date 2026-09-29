namespace CDriveSmartClean.Application.Scanning.Traversal;

public enum StorageTraversalIssueKind
{
    Inaccessible = 1,
    Disappeared = 2,
    TargetChanged = 3,
    IoFailure = 4,
    IdentityUnavailable = 5,
}
