namespace CDriveSmartClean.Application.Scanning.Enumeration;

[Flags]
public enum StorageEntryAttributes
{
    None = 0,
    Sparse = 1,
    Compressed = 2,
    Offline = 4,
    RecallOnOpen = 8,
    RecallOnDataAccess = 16,
    Pinned = 32,
    Unpinned = 64,
}

public static class StorageEntryAttributePolicy
{
    private const StorageEntryAttributes RecallSensitive =
        StorageEntryAttributes.Offline | StorageEntryAttributes.RecallOnOpen |
        StorageEntryAttributes.RecallOnDataAccess | StorageEntryAttributes.Unpinned;

    public static bool IsRecallSensitive(StorageEntryAttributes attributes) => (attributes & RecallSensitive) != 0;
}
