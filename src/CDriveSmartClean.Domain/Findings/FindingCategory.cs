namespace CDriveSmartClean.Domain.Findings;

public enum FindingCategory
{
    Unknown = 0,
    System = 1,
    Application = 2,
    ApplicationData = 3,
    UserData = 4,
    Temporary = 5,
    Cache = 6,
    LogDiagnostic = 7,
    InstallerArchive = 8,
    BackupSnapshot = 9,
    VirtualStorage = 10,
    CloudBacked = 11,
    RecycleBin = 12,
    FilesystemOverhead = 13
}
