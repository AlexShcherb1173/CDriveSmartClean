using System.Reflection;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Enumeration;

public sealed class StorageEnumerationContractsTests
{
    [Fact]
    public void StorageEntryValidValuesAccepted()
    {
        var identity = Identity();
        foreach (var kind in Enum.GetValues<StorageObjectKind>())
        {
            foreach (var reparse in Enum.GetValues<ReparseKind>())
            {
                var entry = new StorageEntry(identity, "path", kind, reparse);
                Assert.Same(identity, entry.VolumeIdentity);
                Assert.Equal(kind, entry.ObjectKind);
                Assert.Equal(reparse, entry.ReparseKind);
            }
        }
    }

    [Fact]
    public void NullVolumeIdentityRejected() =>
        Assert.Throws<ArgumentNullException>("volumeIdentity", () => new StorageEntry(null!, "path", StorageObjectKind.File, ReparseKind.None));

    [Fact]
    public void NullCanonicalPathRejected() =>
        Assert.Throws<ArgumentNullException>("canonicalPath", () => Entry(null!));

    [Fact]
    public void EmptyCanonicalPathRejected() =>
        Assert.Throws<ArgumentException>("canonicalPath", () => Entry(""));

    [Fact]
    public void WhitespaceCanonicalPathRejected() =>
        Assert.Throws<ArgumentException>("canonicalPath", () => Entry(" \t\r\n"));

    [Fact]
    public void CanonicalPathRetainedExactly()
    {
        const string path = @" z:\MixedCase/..\Child ";
        Assert.Equal(path, Entry(path).CanonicalPath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void InvalidStorageObjectKindRejected(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>("objectKind", () => new StorageEntry(Identity(), "path", (StorageObjectKind)value, ReparseKind.None));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(999)]
    public void InvalidReparseKindRejected(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>("reparseKind", () => new StorageEntry(Identity(), "path", StorageObjectKind.File, (ReparseKind)value));

    [Fact]
    public void NonReparseEntryReportsFalse() => Assert.False(Entry("path").IsReparsePoint);

    [Theory]
    [InlineData(ReparseKind.SymbolicLink)]
    [InlineData(ReparseKind.Junction)]
    [InlineData(ReparseKind.MountPoint)]
    [InlineData(ReparseKind.Other)]
    public void ReparseEntryReportsTrue(ReparseKind kind) =>
        Assert.True(new StorageEntry(Identity(), "path", StorageObjectKind.Directory, kind).IsReparsePoint);

    [Fact]
    public void StorageEntryPropertiesAreImmutable()
    {
        Assert.True(typeof(StorageEntry).IsSealed);
        var properties = typeof(StorageEntry).GetProperties();
        Assert.Equal(
            ["CanonicalPath", "IsReparsePoint", "ObjectKind", "ReparseKind", "VolumeIdentity"],
            properties.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, p => Assert.False(p.CanWrite));
        Assert.Empty(typeof(StorageEntry).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static));
    }

    [Fact]
    public void StorageEnumeratorIsInterface() => Assert.True(typeof(IStorageEnumerator).IsInterface);

    [Fact]
    public void StorageEnumeratorHasExactlyTwoMethods() =>
        Assert.Equal(["EnumerateChildrenAsync", "EnumerateRootAsync"], typeof(IStorageEnumerator).GetMethods().Select(m => m.Name).Order(StringComparer.Ordinal));

    [Fact]
    public void EnumerateChildrenAsyncHasExactContract()
    {
        var method = typeof(IStorageEnumerator).GetMethod("EnumerateChildrenAsync")!;
        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.True(method.IsAbstract);
        Assert.Equal([typeof(SystemVolumeDescriptor), typeof(StorageEntry), typeof(IStorageEntrySink), typeof(CancellationToken)],
            method.GetParameters().Select(p => p.ParameterType));
        Assert.All(method.GetParameters(), p => Assert.False(p.IsOptional));
    }

    [Fact]
    public void EnumerateRootAsyncReturnsTask() => Assert.Equal(typeof(Task), EnumeratorMethod().ReturnType);

    [Fact]
    public void EnumerateRootAsyncHasExactParameters()
    {
        var parameters = EnumeratorMethod().GetParameters();
        Assert.Equal([typeof(SystemVolumeDescriptor), typeof(IStorageEntrySink), typeof(CancellationToken)], parameters.Select(p => p.ParameterType));
        Assert.All(parameters, p => Assert.False(p.IsOptional));
        Assert.True(EnumeratorMethod().IsAbstract);
    }

    [Fact]
    public void StorageEntrySinkIsInterface() => Assert.True(typeof(IStorageEntrySink).IsInterface);

    [Fact]
    public void StorageEntrySinkHasExactlyOneMethod() => Assert.Equal("WriteAsync", SinkMethod().Name);

    [Fact]
    public void WriteAsyncReturnsValueTask() => Assert.Equal(typeof(ValueTask), SinkMethod().ReturnType);

    [Fact]
    public void WriteAsyncHasExactParameters()
    {
        var parameters = SinkMethod().GetParameters();
        Assert.Equal([typeof(StorageEntry), typeof(CancellationToken)], parameters.Select(p => p.ParameterType));
        Assert.All(parameters, p => Assert.False(p.IsOptional));
        Assert.True(SinkMethod().IsAbstract);
    }

    [Fact]
    public void ContractsExposeOnlyExactPortableTypes()
    {
        Assert.Equal(
            [typeof(VolumeIdentity), typeof(string), typeof(StorageObjectKind), typeof(ReparseKind)],
            Assert.Single(typeof(StorageEntry).GetConstructors()).GetParameters().Select(p => p.ParameterType));
        Assert.Equal(
            [typeof(string), typeof(bool), typeof(StorageObjectKind), typeof(ReparseKind), typeof(VolumeIdentity)],
            typeof(StorageEntry).GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => p.PropertyType));
        Assert.Equal(19, typeof(StorageEntry).Assembly.GetExportedTypes().Length);
        Assert.Equal(14, typeof(VolumeIdentity).Assembly.GetExportedTypes().Length);
    }

    private static VolumeIdentity Identity() => new(new Guid("336d3520-fd79-4b49-85df-2f41bf352376"));

    private static StorageEntry Entry(string path) => new(Identity(), path, StorageObjectKind.File, ReparseKind.None);

    private static MethodInfo EnumeratorMethod() => typeof(IStorageEnumerator).GetMethod("EnumerateRootAsync")!;

    private static MethodInfo SinkMethod() => Assert.Single(typeof(IStorageEntrySink).GetMethods());
}
