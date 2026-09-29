using System.Reflection;
using System.Security;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using Microsoft.Win32.SafeHandles;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

[Collection("WindowsNativeTraversal")]
public sealed class WindowsStorageTraversalSecurityTests
{
    private static readonly string[] MissingComponent = ["child", "absent"];
    private static readonly string[] ExistingComponent = ["child"];
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AncestorJunctionBeforeDescentIsRejectedEvenOnSameVolume(bool retarget)
    {
        using var f = new WindowsStorageFixture();
        string ancestor = f.AddDirectory("ancestor");
        string leaf = f.AddDirectory("ancestor\\leaf");
        f.AddDirectory("alternate");
        f.AddDirectory("alternate\\leaf");
        var candidate = Candidate(f, leaf);
        string saved = f.Owned("saved");
        Directory.Move(ancestor, saved);
        try
        {
            f.AddJunction("ancestor", retarget ? "saved" : "alternate");
            if (retarget)
            {
                Directory.Delete(ancestor);
                f.AddJunction("ancestor", "alternate");
            }

            var sink = new Sink();
            await Assert.ThrowsAsync<StorageTraversalTargetChangedException>(() =>
                f.Enumerator.EnumerateChildrenAsync(f.Volume, candidate, sink, TestContext.Current.CancellationToken));
            Assert.Empty(sink.Entries);
        }
        finally
        {
            if (Directory.Exists(ancestor))
            {
                Directory.Delete(ancestor); // fixture junction, nonrecursive
            }

            Directory.Move(saved, ancestor);
        }
    }

    [Fact]
    public async Task ActiveAncestorAndTargetStayPinnedAcrossAwaitedSink()
    {
        using var f = new WindowsStorageFixture();
        string ancestor = f.AddDirectory("ancestor");
        string leaf = f.AddDirectory("ancestor\\leaf");
        string marker = f.AddFile("ancestor\\leaf\\marker");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new Sink((_, _) => { entered.TrySetResult(); return new ValueTask(release.Task); });
        Task operation = f.Enumerator.EnumerateChildrenAsync(f.Volume, Candidate(f, leaf), sink, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(operation.IsCompleted);
            Assert.Throws<IOException>(() => Directory.Move(ancestor, f.Owned("moved-ancestor")));
            Assert.Throws<IOException>(() => Directory.Move(leaf, f.Owned("moved-leaf")));
            Assert.Equal(marker, Assert.Single(sink.Entries).CanonicalPath);
        }
        finally
        {
            release.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            // Restore only exact test-owned targets if an assertion exposed a pinning defect.
            if (Directory.Exists(f.Owned("moved-ancestor")))
            {
                Directory.Move(f.Owned("moved-ancestor"), ancestor);
            }

            if (Directory.Exists(f.Owned("moved-leaf")))
            {
                Directory.Move(f.Owned("moved-leaf"), leaf);
            }
        }

        Directory.Move(ancestor, f.Owned("moved-ancestor"));
        Directory.Move(f.Owned("moved-ancestor"), ancestor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SinkFailureAndCancellationReleaseEntireChain(bool cancel)
    {
        using var f = new WindowsStorageFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception sentinel = cancel ? new OperationCanceledException(cancellation.Token) : new IOException("sentinel");
        var sink = new Sink((_, _) => ValueTask.FromException(sentinel));
        Exception? actual = await Record.ExceptionAsync(() => f.Enumerator.EnumerateChildrenAsync(
            f.Volume, Candidate(f, f.Child), sink, TestContext.Current.CancellationToken));
        Assert.Same(sentinel, actual);
        if (cancel)
        {
            Assert.Equal(cancellation.Token, Assert.IsType<OperationCanceledException>(actual).CancellationToken);
        }

        string moved = f.Owned("released");
        Directory.Move(f.Child, moved);
        Directory.Move(moved, f.Child);
        string movedRoot = f.Root + "-released";
        Directory.Move(f.Root, movedRoot);
        Directory.Move(movedRoot, f.Root);
    }

    [Fact]
    public async Task MovedOutsideFixtureScopeIsNotSearchedById()
    {
        using var f = new WindowsStorageFixture();
        string scope = f.AddDirectory("scope");
        string candidatePath = f.AddDirectory("scope\\candidate");
        string outside = f.AddDirectory("outside");
        var binding = WindowsStorageFixture.Bind(scope);
        var candidate = Candidate(f, candidatePath);
        string moved = Path.Combine(outside, "candidate");
        Directory.Move(candidatePath, moved);
        try
        {
            var sink = new Sink();
            Exception? error = await Record.ExceptionAsync(() => binding.Enumerator.EnumerateChildrenAsync(
                binding.Volume, candidate, sink, TestContext.Current.CancellationToken));
            Assert.True(error is FileNotFoundException or DirectoryNotFoundException);
            Assert.NotNull(error!.Data["NTSTATUS"]);
            Assert.NotNull(error.Data["Win32Error"]);
            Assert.Empty(sink.Entries);
        }
        finally
        {
            Directory.Move(moved, candidatePath);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSerialWithoutSameVolumeProvenanceIsRejected(bool sameSerial)
    {
        const string expected = @"\\?\Volume{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}\";
        const string foreign = @"\\?\Volume{bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb}\child";
        Assert.ThrowsAny<SecurityException>(() => WindowsStorageFixture.Invoke("WindowsStorageObjectIdentityReader",
            "ValidateProvenance", null, expected, foreign, 42ul, sameSerial ? 42ul : 43ul));
    }

    [Fact]
    public void ContradictorySerialIsAlsoFatal()
    {
        const string root = @"\\?\Volume{aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa}\";
        Assert.ThrowsAny<SecurityException>(() => WindowsStorageFixture.Invoke("WindowsStorageObjectIdentityReader",
            "ValidateProvenance", null, root, root + "child", 42ul, 43ul));
    }

    [Fact]
    public void EqualFileIdsInDifferentDomainVolumesAreUnequal()
    {
        var id = Guid.NewGuid();
        Assert.NotEqual(new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), id),
            new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RootAuthorizationMismatchIsFatalBeforeEmission(bool mismatchVolume)
    {
        using var f = new WindowsStorageFixture();
        var supplied = mismatchVolume ? new SystemVolumeDescriptor(new VolumeIdentity(Guid.NewGuid()), f.Root) :
            new SystemVolumeDescriptor(f.Volume.VolumeIdentity, f.Child);
        var sink = new Sink();
        await Assert.ThrowsAnyAsync<SecurityException>(() => f.Enumerator.EnumerateRootAsync(supplied, sink, TestContext.Current.CancellationToken));
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public async Task ChangedFixtureRootIdentityIsFatalEvenForChildOperation()
    {
        using var f = new WindowsStorageFixture();
        var candidate = Candidate(f, f.Child);
        string saved = f.Root + "-saved";
        Directory.Move(f.Root, saved);
        Directory.CreateDirectory(f.Root);
        try
        {
            var sink = new Sink();
            await Assert.ThrowsAnyAsync<SecurityException>(() => f.Enumerator.EnumerateChildrenAsync(
                f.Volume, candidate, sink, TestContext.Current.CancellationToken));
            Assert.Empty(sink.Entries);
        }
        finally
        {
            Directory.Delete(f.Root);
            Directory.Move(saved, f.Root);
        }
    }

    [Fact]
    public async Task ProductionConstructorDoesNotAuthorizeSyntheticNativeFixtures()
    {
        using var f = new WindowsStorageFixture();
        var sink = new Sink();
        await Assert.ThrowsAnyAsync<SecurityException>(() => new CDriveSmartClean.Platform.Windows.Storage.WindowsStorageEnumerator()
            .EnumerateRootAsync(f.Volume, sink, TestContext.Current.CancellationToken));
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public async Task HardLinkPathsAndReparseObjectsStayVisibleWithoutTargetFollow()
    {
        using var f = new WindowsStorageFixture();
        f.AddHardLink("alias", "ordinary.bin");
        f.AddJunction("link", "child");
        var sink = new Sink();
        await f.Enumerator.EnumerateRootAsync(f.Volume, sink, TestContext.Current.CancellationToken);
        Assert.Equal(Assert.Single(sink.Entries, e => e.CanonicalPath == f.Ordinary).ObjectIdentity,
            Assert.Single(sink.Entries, e => e.CanonicalPath == f.Owned("alias")).ObjectIdentity);
        var link = Assert.Single(sink.Entries, e => e.CanonicalPath == f.Owned("link"));
        Assert.True(link.IsReparsePoint);
        Assert.Equal(WindowsStorageFixture.ReadIdentity(f.Volume.VolumeIdentity, link.CanonicalPath), link.ObjectIdentity);
        Assert.DoesNotContain(sink.Entries, e => e.CanonicalPath.EndsWith("grandchild.bin", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("a\\b")]
    [InlineData("a/b")]
    [InlineData("C:")]
    [InlineData("a\0b")]
    public void InvalidRelativeComponentRejected(string component) => Assert.Throws<ArgumentException>(() =>
        WindowsStorageFixture.Invoke("WindowsDirectoryHandleChain", "ValidateComponent", null, component));

    [Fact]
    public void PartialChainFailureAndDepthLimitReleaseHandles()
    {
        using var f = new WindowsStorageFixture();
        object chain = WindowsStorageFixture.Invoke("WindowsStorageRootAnchor", "Open", f.Anchor, f.Volume, TestContext.Current.CancellationToken)!;
        var handles = (List<SafeFileHandle>)chain.GetType().GetField("handles", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(chain)!;
        SafeFileHandle[] acquired;
        using ((IDisposable)chain)
        {
            Assert.Throws<FileNotFoundException>(() => WindowsStorageFixture.Invoke("WindowsDirectoryHandleChain", "Append", chain,
                MissingComponent, f.Root, TestContext.Current.CancellationToken));
            acquired = [.. handles]; // Includes the successfully opened child before the later component failed.
            // Deterministic bound test: duplicate references, not additional native handles.
            while (handles.Count < 257)
            {
                handles.Add(handles[0]);
            }

            Assert.Throws<IOException>(() => WindowsStorageFixture.Invoke("WindowsDirectoryHandleChain", "Append", chain,
                ExistingComponent, f.Root, TestContext.Current.CancellationToken));
        }

        Assert.All(acquired, h => Assert.True(h.IsClosed));
    }

    [Fact]
    public async Task NullDirectoryIdentityIsExplicitFailure()
    {
        using var f = new WindowsStorageFixture();
        var candidate = new StorageEntry(f.Volume.VolumeIdentity, null, f.Child, StorageObjectKind.Directory, ReparseKind.None);
        await Assert.ThrowsAsync<StorageObjectIdentityUnavailableException>(() =>
            f.Enumerator.EnumerateChildrenAsync(f.Volume, candidate, new Sink(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void UnavailableOpenedHandleGuidEvidenceIsFatal()
    {
        using var invalid = new SafeFileHandle(-1, false);
        Assert.ThrowsAny<SecurityException>(() => WindowsStorageFixture.Invoke("WindowsStorageObjectIdentityReader",
            "ReadGuidPath", null, invalid, "test-invalid-handle"));
    }

    [Fact]
    public void UnsafePathIdentityReaderIsRemovedAndRelativeImportsAreHardened()
    {
        Assert.Null(WindowsStorageFixture.ProductionType("WindowsStorageObjectIdentityReader").GetMethod("TryRead",
            BindingFlags.Static | BindingFlags.NonPublic));
        Type native = typeof(CDriveSmartClean.Platform.Windows.Storage.WindowsStorageEnumerator).Assembly.GetType(
            "CDriveSmartClean.Platform.Windows.Interop.NtFileNative", true)!;
        Assert.Equal(0x00200020u, native.GetField("OpenOptions", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        foreach (var method in native.GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            var import = method.GetCustomAttribute<System.Runtime.InteropServices.DllImportAttribute>();
            if (import is not null)
            {
                Assert.Equal("ntdll.dll", import.Value);
                Assert.True(import.ExactSpelling);
                Assert.Equal(System.Runtime.InteropServices.DllImportSearchPath.System32,
                    method.GetCustomAttribute<System.Runtime.InteropServices.DefaultDllImportSearchPathsAttribute>()!.Paths);
            }
        }

        Type kernel = native.Assembly.GetType("CDriveSmartClean.Platform.Windows.Interop.Kernel32FileIdentityNative", true)!;
        Assert.Equal(0x00100081u, kernel.GetField("TraversalAccess", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue());
        Assert.Equal(1u, kernel.GetField("TraversalShare", BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue());
    }

    private static StorageEntry Candidate(WindowsStorageFixture f, string path) => new(f.Volume.VolumeIdentity,
        WindowsStorageFixture.ReadIdentity(f.Volume.VolumeIdentity, path), path, StorageObjectKind.Directory, ReparseKind.None);

    private sealed class Sink(Func<StorageEntry, CancellationToken, ValueTask>? callback = null) : IStorageEntrySink
    {
        internal List<StorageEntry> Entries { get; } = [];
        public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return callback?.Invoke(entry, cancellationToken) ?? ValueTask.CompletedTask;
        }
    }
}
