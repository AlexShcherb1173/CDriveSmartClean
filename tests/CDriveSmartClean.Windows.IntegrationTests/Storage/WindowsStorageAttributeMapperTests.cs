using System.Reflection;
using CDriveSmartClean.Application.Scanning.Enumeration;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

public sealed class WindowsStorageAttributeMapperTests
{
    private static readonly Type Mapper = WindowsStorageFixture.ProductionType("WindowsStorageAttributeMapper");

    [Fact]
    public void MasksAreExact()
    {
        Assert.Equal(0x005C1E00u, Constant("EnumerationAttributeMask"));
        Assert.Equal(0x00581E00u, Constant("ValidPostOpenAttributeMask"));
        Assert.Equal(0x00541000u, Constant("EnumerationRecallBlockMask"));
        Assert.Equal(0x00501000u, Constant("PostOpenRecallBlockMask"));
        Assert.Equal(0x10u, Constant("DirectoryTypeBit"));
        Assert.Equal(0x400u, Constant("ReparseBit"));
    }

    [Theory]
    [InlineData(0x00000200u, StorageEntryAttributes.Sparse)]
    [InlineData(0x00000800u, StorageEntryAttributes.Compressed)]
    [InlineData(0x00001000u, StorageEntryAttributes.Offline)]
    [InlineData(0x00040000u, StorageEntryAttributes.RecallOnOpen)]
    [InlineData(0x00080000u, StorageEntryAttributes.Pinned)]
    [InlineData(0x00100000u, StorageEntryAttributes.Unpinned)]
    [InlineData(0x00400000u, StorageEntryAttributes.RecallOnDataAccess)]
    public void EnumerationMappingUsesEnumerationSemantics(uint native, StorageEntryAttributes expected) =>
        Assert.Equal(expected, Map("FromDirectoryEnumeration", native));

    [Fact]
    public void HandleEaBitIsNeverRecallOnOpen()
    {
        StorageEntryAttributes mapped = Map("FromHandleAttributeTag", 0x00040000);
        Assert.Equal(StorageEntryAttributes.None, mapped);
        Assert.False((mapped & StorageEntryAttributes.RecallOnOpen) != 0);
    }

    [Theory]
    [InlineData(0x00001000u, StorageEntryAttributes.Offline)]
    [InlineData(0x00080000u, StorageEntryAttributes.Pinned)]
    [InlineData(0x00100000u, StorageEntryAttributes.Unpinned)]
    [InlineData(0x00400000u, StorageEntryAttributes.RecallOnDataAccess)]
    public void HandleMappingRetainsOnlyHandleValidCloudSemantics(uint native, StorageEntryAttributes expected) =>
        Assert.Equal(expected, Map("FromHandleAttributeTag", native));

    [Fact]
    public void BlockingPredicateTreatsPinnedAloneAsEligibleAndUnpinnedAsBlocking()
    {
        Assert.False(StorageEntryAttributePolicy.IsRecallSensitive(StorageEntryAttributes.Pinned));
        Assert.True(StorageEntryAttributePolicy.IsRecallSensitive(
            StorageEntryAttributes.Pinned | StorageEntryAttributes.Unpinned));
    }

    [Fact]
    public void MapperHasNoGenericAttributeMethod() =>
        Assert.DoesNotContain(Mapper.GetMethods(BindingFlags.NonPublic | BindingFlags.Static),
            method => method.Name == "MapAttributes");

    private static uint Constant(string name) =>
        (uint)Mapper.GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;

    private static StorageEntryAttributes Map(string method, uint value) =>
        Assert.IsType<StorageEntryAttributes>(Mapper.GetMethod(method,
            BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [value]));
}
