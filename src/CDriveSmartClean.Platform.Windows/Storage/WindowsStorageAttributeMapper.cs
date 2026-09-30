using CDriveSmartClean.Application.Scanning.Enumeration;

namespace CDriveSmartClean.Platform.Windows.Storage;

internal static class WindowsStorageAttributeMapper
{
    internal const uint EnumerationAttributeMask = 0x005C1E00;
    internal const uint ValidPostOpenAttributeMask = 0x00581E00;
    internal const uint EnumerationRecallBlockMask = 0x00541000;
    internal const uint PostOpenRecallBlockMask = 0x00501000;
    internal const uint DirectoryTypeBit = 0x00000010;
    internal const uint ReparseBit = 0x00000400;

    internal static StorageEntryAttributes FromDirectoryEnumeration(uint attributes) =>
        MapCommon(attributes) |
        ((attributes & 0x00040000) != 0 ? StorageEntryAttributes.RecallOnOpen : StorageEntryAttributes.None);

    internal static StorageEntryAttributes FromHandleAttributeTag(uint attributes) => MapCommon(attributes);

    private static StorageEntryAttributes MapCommon(uint attributes)
    {
        StorageEntryAttributes mapped = StorageEntryAttributes.None;
        if ((attributes & 0x00000200) != 0) mapped |= StorageEntryAttributes.Sparse;
        if ((attributes & 0x00000800) != 0) mapped |= StorageEntryAttributes.Compressed;
        if ((attributes & 0x00001000) != 0) mapped |= StorageEntryAttributes.Offline;
        if ((attributes & 0x00080000) != 0) mapped |= StorageEntryAttributes.Pinned;
        if ((attributes & 0x00100000) != 0) mapped |= StorageEntryAttributes.Unpinned;
        if ((attributes & 0x00400000) != 0) mapped |= StorageEntryAttributes.RecallOnDataAccess;
        return mapped;
    }
}
