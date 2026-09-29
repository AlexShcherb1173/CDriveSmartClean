using System.Buffers.Binary;
using System.Reflection;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

[Collection("WindowsNativeTraversal")]
public sealed class WindowsDirectoryEntryReaderTests
{
    [Fact]
    public void NativeTestsRunInWindowsX64Process()
    {
        TestContext.Current.TestOutputHelper!.WriteLine($"OS architecture: {RuntimeInformation.OSArchitecture}; process architecture: {RuntimeInformation.ProcessArchitecture}");
        Assert.True(OperatingSystem.IsWindows());
        Assert.Equal(Architecture.X64, RuntimeInformation.ProcessArchitecture);
    }

    [Fact]
    public void ProductionAssemblyPeMachineIsAmd64() =>
        AssertAmd64(WindowsStorageFixture.ProductionType("WindowsDirectoryEntryReader").Assembly);

    [Fact]
    public void WindowsTestAssemblyPeMachineIsAmd64() =>
        AssertAmd64(typeof(WindowsDirectoryEntryReaderTests).Assembly);

    private static void AssertAmd64(Assembly assembly)
    {
        using var stream = File.OpenRead(assembly.Location);
        using var pe = new PEReader(stream);
        Machine actual = pe.PEHeaders.CoffHeader.Machine;
        TestContext.Current.TestOutputHelper!.WriteLine($"Assembly: {assembly.Location}; PE Machine: {actual}");
        Assert.Equal(Machine.Amd64, actual);
    }

    internal static byte[] Record(string name, Guid id = default, uint attributes = 0, int? capacity = null)
    {
        var bytes = new byte[capacity ?? (88 + name.Length * 2)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), attributes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), checked((uint)name.Length * 2));
        id.ToByteArray().CopyTo(bytes, 72);
        for (int i = 0; i < name.Length; i++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(88 + i * 2), name[i]);
        }

        return bytes;
    }

    private static Array Parse(byte[] bytes) => (Array)WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "ParseBatch", null, bytes)!;
    private static object Property(object value, string name) => value.GetType().GetProperty(name)!.GetValue(value)!;

    [Fact]
    public void ExactNativeX64LayoutIsVerified()
    {
        WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "VerifyLayout", null);
        Type reader = WindowsStorageFixture.ProductionType("WindowsDirectoryEntryReader");
        Assert.Equal(88, reader.GetField("NameOffset", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.Equal(65536, reader.GetField("BufferSize", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Type fileId = reader.Assembly.GetType("CDriveSmartClean.Platform.Windows.Interop.Kernel32FileIdentityNative+FileId128", true)!;
        Assert.Equal(16, Marshal.SizeOf(fileId));
    }

    [Fact]
    public void Utf16CodeUnitsAndAll128IdentityBitsArePreserved()
    {
        string name = "Юникод-\ud800-x-\udfff-😀. ";
        var id = new Guid(Enumerable.Range(0, 16).Select(i => (byte)(i * 13)).ToArray());
        object entry = Assert.Single(Parse(Record(name, id)).Cast<object>());
        Assert.Equal(name, Property(entry, "Name"));
        Assert.Equal(id, Property(entry, "Id"));
    }

    [Fact]
    public void ZeroIdRemainsExplicitAndDotEntriesAreSkipped()
    {
        Assert.Equal(Guid.Empty, Property(Assert.Single(Parse(Record("directory", attributes: 16)).Cast<object>()), "Id"));
        Assert.Empty(Parse(Record(".")));
        Assert.Empty(Parse(Record("..")));
    }

    [Fact]
    public void MultipleRecordsUseOffsetsRatherThanStructureSize()
    {
        byte[] first = Record("one", capacity: 96);
        BinaryPrimitives.WriteUInt32LittleEndian(first, 96);
        byte[] combined = [.. first, .. Record("two")];
        Assert.Equal(["one", "two"], Parse(combined).Cast<object>().Select(e => (string)Property(e, "Name")));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void MalformedBuffersFailBoundedly(int kind)
    {
        byte[] bytes = Record("valid", capacity: 128);
        switch (kind)
        {
            case 0: bytes = new byte[87]; break;
            case 1: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 0); break;
            case 2: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), 3); break;
            case 3: BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(60), uint.MaxValue - 1); break;
            case 4: BinaryPrimitives.WriteUInt32LittleEndian(bytes, 8); break;
            case 5: BinaryPrimitives.WriteUInt32LittleEndian(bytes, 99); break;
            case 6: BinaryPrimitives.WriteUInt32LittleEndian(bytes, uint.MaxValue - 7); break;
            case 7: BinaryPrimitives.WriteUInt32LittleEndian(bytes, 128); break;
            case 8: bytes = Record("abcdef")[..^2]; break;
            case 9: bytes = new byte[65537]; break;
            default:
                bytes = Record("a", capacity: 192);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, 96); // Missing terminal record: remaining bytes are not a valid header/name.
                break;
        }

        Assert.Throws<IOException>(() => Parse(bytes));
    }

    [Theory]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("a:b")]
    [InlineData("a\0b")]
    public void InvalidNativeComponentsAreIoFailures(string name) => Assert.Throws<IOException>(() => Parse(Record(name)));

    [Theory]
    [InlineData(24)]
    [InlineData(122)]
    [InlineData(234)]
    [InlineData(31)]
    public void BufferAndUnknownFailuresAreNotEof(int code)
    {
        var error = Assert.Throws<IOException>(() => WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "AcceptResult", null, false, code, "path"));
        Assert.Equal(code, Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException).NativeErrorCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(87)]
    public void UnsupportedQueryIsExplicit(int code) => Assert.Throws<StorageObjectIdentityUnavailableException>(() =>
        WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "AcceptResult", null, false, code, "path"));

    [Fact]
    public void OnlyNoMoreFilesIsCompletion()
    {
        Assert.Equal(false, WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "AcceptResult", null, false, 18, "path"));
        Assert.Equal(true, WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "AcceptResult", null, true, 18, "path"));
    }

    [Fact]
    public async Task MultipleBatchesDoNotRestartAndZeroIdIsNull()
    {
        using var fixture = new WindowsStorageFixture();
        object chain = WindowsStorageFixture.Invoke("WindowsStorageRootAnchor", "Open", fixture.Anchor, fixture.Volume, TestContext.Current.CancellationToken)!;
        using var lease = (IDisposable)chain;
        var firstFlags = new List<bool>();
        var sink = new Sink();
        Func<bool, byte[]?> read = first =>
        {
            firstFlags.Add(first);
            return firstFlags.Count switch { 1 => Record("one", Guid.NewGuid()), 2 => Record("two", attributes: 16), _ => null };
        };
        await (Task)WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "ReadBatchesAsync", null,
            chain, fixture.Root, sink, read, TestContext.Current.CancellationToken)!;
        Assert.Equal([true, false, false], firstFlags);
        Assert.Equal(2, sink.Entries.Count);
        Assert.NotNull(sink.Entries[0].ObjectIdentity);
        Assert.Null(sink.Entries[1].ObjectIdentity);
    }

    [Fact]
    public async Task NativeFailureAfterDeliveryDoesNotRestartOrBecomeCompletion()
    {
        using var fixture = new WindowsStorageFixture();
        object chain = WindowsStorageFixture.Invoke("WindowsStorageRootAnchor", "Open", fixture.Anchor, fixture.Volume, TestContext.Current.CancellationToken)!;
        using var lease = (IDisposable)chain;
        var flags = new List<bool>();
        var sink = new Sink();
        Func<bool, byte[]?> read = first =>
        {
            flags.Add(first);
            if (first)
            {
                return Record("observed");
            }

            WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "AcceptResult", null, false, 24, fixture.Root);
            throw new InvalidOperationException("Native failure was swallowed.");
        };
        await Assert.ThrowsAsync<IOException>(() => (Task)WindowsStorageFixture.Invoke("WindowsDirectoryEntryReader", "ReadBatchesAsync", null,
            chain, fixture.Root, sink, read, TestContext.Current.CancellationToken)!);
        Assert.Equal([true, false], flags);
        Assert.Single(sink.Entries);
    }

    private sealed class Sink : IStorageEntrySink
    {
        internal List<StorageEntry> Entries { get; } = [];
        public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }
}
