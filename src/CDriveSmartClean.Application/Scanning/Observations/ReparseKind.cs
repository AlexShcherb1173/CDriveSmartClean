namespace CDriveSmartClean.Application.Scanning.Observations;

public enum ReparseKind
{
    None = 1,
    SymbolicLink = 2,
    Junction = 3,
    MountPoint = 4,
    Other = 5,
}
