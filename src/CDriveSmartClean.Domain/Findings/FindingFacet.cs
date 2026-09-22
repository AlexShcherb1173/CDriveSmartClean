namespace CDriveSmartClean.Domain.Findings;

public enum FindingFacet
{
    Large = 1,
    Old = 2,
    Recent = 3,
    Growing = 4,
    DuplicateCandidate = 5,
    OrphanCandidate = 6,
    CacheLike = 7,
    TempLike = 8,
    UserContent = 9,
    Compressed = 10,
    Sparse = 11,
    HardLinked = 12,
    CloudPlaceholder = 13,
    Virtualized = 14,
    Active = 15,
    SystemProtected = 16,
    UnknownOwner = 17
}
