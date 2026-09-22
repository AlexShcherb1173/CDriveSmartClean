namespace CDriveSmartClean.Domain.Reclaim;

public enum ReclaimKind
{
    Unknown = 0,
    None = 1,
    Exact = 2,
    Estimated = 3,
    Conditional = 4,
    UserDecision = 5
}
