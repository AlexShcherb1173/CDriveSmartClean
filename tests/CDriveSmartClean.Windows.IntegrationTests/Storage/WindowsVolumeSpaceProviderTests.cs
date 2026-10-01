using System.Reflection;
using System.Runtime.InteropServices;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Storage;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

public sealed class WindowsVolumeSpaceProviderTests
{
    private static SystemVolumeDescriptor Volume => new(new VolumeIdentity(new Guid("11111111-1111-1111-1111-111111111111")), @"C:\");
    private static ulong[] Values => [100, 40, 0, 50, 20, 0, 60, 2, 1, 0, 0, 8, 512];
    private static WindowsVolumeSpaceProvider Provider(Func<string, (int, int, ulong[])> query) =>
        (WindowsVolumeSpaceProvider)Activator.CreateInstance(typeof(WindowsVolumeSpaceProvider),
            BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { (Func<SystemVolumeDescriptor>)(() => Volume), query }, null)!;

    [Fact]
    public void NativeLayoutAndSignature()
    {
        Type native = typeof(WindowsVolumeSpaceProvider).Assembly.GetType("CDriveSmartClean.Platform.Windows.Interop.Kernel32VolumeNative", true)!;
        Type layout = native.GetNestedType("DiskSpaceInformation", BindingFlags.NonPublic)!;
        Assert.Equal(96, Marshal.SizeOf(layout));
        string[] fields = ["ActualTotalAllocationUnits", "ActualAvailableAllocationUnits", "ActualPoolUnavailableAllocationUnits",
            "CallerTotalAllocationUnits", "CallerAvailableAllocationUnits", "CallerPoolUnavailableAllocationUnits", "UsedAllocationUnits",
            "TotalReservedAllocationUnits", "VolumeStorageReserveAllocationUnits", "AvailableCommittedAllocationUnits", "PoolAvailableAllocationUnits",
            "SectorsPerAllocationUnit", "BytesPerSector"];
        for (int i = 0; i < fields.Length; i++) Assert.Equal(i < 12 ? i * 8 : 92, Marshal.OffsetOf(layout, fields[i]).ToInt32());
        MethodInfo method = native.GetMethod("GetDiskSpaceInformationW", BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Equal(typeof(int), method.ReturnType);
        var import = method.GetCustomAttribute<DllImportAttribute>()!;
        Assert.Equal("kernel32.dll", import.Value);
        Assert.True(import.ExactSpelling);
        Assert.Equal(CharSet.Unicode, import.CharSet);
        Assert.Equal(DllImportSearchPath.System32, method.GetCustomAttribute<DefaultDllImportSearchPathsAttribute>()!.Paths);
    }

    [Fact]
    public void GuidRootAndQuotaValuesRemainSeparate()
    {
        int calls = 0;
        var provider = Provider(root =>
        {
            calls++;
            Assert.Equal(@"\\?\Volume{11111111-1111-1111-1111-111111111111}\", root);
            return (0, 0, Values);
        });
        var result = provider.GetVolumeSpace(Volume);
        Assert.Equal(1, calls);
        Assert.Equal(409600, result.CapacityBytes);
        Assert.Equal(163840, result.FreeBytes);
        Assert.Equal(245760, result.UsedBytes);
        Assert.Equal(204800, result.CallerCapacityBytes);
        Assert.Equal(81920, result.CallerFreeBytes);
    }

    [Theory]
    [InlineData(11, 0, VolumeSpaceFailure.InvalidNativeData)]
    [InlineData(12, 0, VolumeSpaceFailure.InvalidNativeData)]
    [InlineData(1, 101, VolumeSpaceFailure.InvalidNativeData)]
    [InlineData(4, 51, VolumeSpaceFailure.InvalidNativeData)]
    [InlineData(0, ulong.MaxValue, VolumeSpaceFailure.Overflow)]
    public void InvalidNativeDataFailsExplicitly(int field, ulong value, VolumeSpaceFailure expected)
    {
        ulong[] values = Values;
        values[field] = value;
        var result = Provider(_ => (0, 0, values)).GetVolumeSpace(Volume);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.CapacityBytes);
    }

    [Theory]
    [InlineData(5, VolumeSpaceFailure.AccessDenied)]
    [InlineData(3, VolumeSpaceFailure.VolumeUnavailable)]
    [InlineData(50, VolumeSpaceFailure.UnsupportedPlatform)]
    [InlineData(1234, VolumeSpaceFailure.NativeFailure)]
    public void HresultFailureDoesNotFallback(int error, VolumeSpaceFailure expected)
    {
        int calls = 0;
        var result = Provider(_ => { calls++; return (unchecked((int)0x80070000) | error, error, Values); }).GetVolumeSpace(Volume);
        Assert.Equal(1, calls);
        Assert.Equal(expected, result.Failure);
        Assert.Null(result.FreeBytes);
    }

    [Fact]
    public void UnsupportedApiIsUnavailable()
        => Assert.Equal(VolumeSpaceFailure.UnsupportedPlatform,
            Provider(_ => throw new EntryPointNotFoundException()).GetVolumeSpace(Volume).Failure);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DescriptorContradictionsAreFatal(bool root)
    {
        var supplied = new SystemVolumeDescriptor(root ? Volume.VolumeIdentity : new(Guid.NewGuid()), root ? @"C:\child" : @"C:\");
        Assert.Throws<InvalidOperationException>(() => Provider(_ => throw new InvalidOperationException("Must not query")).GetVolumeSpace(supplied));
    }

    [Fact]
    public void RealSystemGuidRootQuery()
    {
        var volume = new WindowsSystemVolumeProvider().GetSystemVolume();
        var snapshot = new WindowsVolumeSpaceProvider().GetVolumeSpace(volume);
        Assert.Equal(volume.VolumeIdentity, snapshot.VolumeIdentity);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            Assert.Equal(VolumeSpaceFailure.UnsupportedPlatform, snapshot.Failure);
        else
        {
            Assert.True(snapshot.IsAvailable, $"Native query failed: {snapshot.Failure}, {snapshot.NativeError}");
            Assert.Equal(snapshot.CapacityBytes - snapshot.FreeBytes, snapshot.UsedBytes);
            Assert.NotNull(snapshot.CallerCapacityBytes);
        }
    }
}
