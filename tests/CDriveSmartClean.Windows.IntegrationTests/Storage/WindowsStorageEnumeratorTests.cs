using System.Reflection;
using System.Runtime.Versioning;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Storage;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

public sealed class WindowsStorageEnumeratorTests
{
    [Fact]
    public async Task RealSystemRootContainsOnlyDirectChildrenWithActualAttributeMapping()
    {
        var volume = new WindowsSystemVolumeProvider().GetSystemVolume();
        var sink = new CollectingSink();
        await new WindowsStorageEnumerator().EnumerateRootAsync(volume, sink, CancellationToken.None);

        Assert.NotEmpty(sink.Entries);
        Assert.Equal(sink.Entries.Count, sink.Entries.Select(e => e.CanonicalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        int checkedEntries = 0;
        foreach (var entry in sink.Entries)
        {
            Assert.Same(volume.VolumeIdentity, entry.VolumeIdentity);
            Assert.False(string.IsNullOrWhiteSpace(entry.CanonicalPath));
            Assert.True(Path.IsPathFullyQualified(entry.CanonicalPath));
            Assert.True(Enum.IsDefined(entry.ObjectKind));
            Assert.True(Enum.IsDefined(entry.ReparseKind));
            AssertDirectChild(volume.RootPath, entry.CanonicalPath);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry.CanonicalPath);
            }
            catch (FileNotFoundException)
            {
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                continue;
            }

            Assert.Equal((attributes & FileAttributes.Directory) != 0 ? StorageObjectKind.Directory : StorageObjectKind.File, entry.ObjectKind);
            Assert.Equal((attributes & FileAttributes.ReparsePoint) != 0 ? ReparseKind.Other : ReparseKind.None, entry.ReparseKind);
            Assert.Equal((attributes & FileAttributes.ReparsePoint) != 0, entry.IsReparsePoint);
            checkedEntries++;
        }

        Assert.True(checkedEntries > 0);
    }

    [Fact]
    public async Task FixtureIncludesHiddenSystemEntriesButNeverGrandchildren()
    {
        using var fixture = new RootFixture();
        var sink = new CollectingSink();
        await new WindowsStorageEnumerator().EnumerateRootAsync(fixture.Volume, sink, CancellationToken.None);

        Assert.Equal(["child", "hidden.bin", "ordinary.bin"], sink.Entries.Select(e => Path.GetFileName(e.CanonicalPath)).Order(StringComparer.Ordinal));
        Assert.All(sink.Entries, entry =>
        {
            AssertDirectChild(fixture.Root, entry.CanonicalPath);
            Assert.Same(fixture.Volume.VolumeIdentity, entry.VolumeIdentity);
            Assert.Equal(ReparseKind.None, entry.ReparseKind);
        });
        Assert.Equal(StorageObjectKind.Directory, Assert.Single(sink.Entries, e => e.CanonicalPath == fixture.Child).ObjectKind);
        Assert.Equal(StorageObjectKind.File, Assert.Single(sink.Entries, e => e.CanonicalPath == fixture.Hidden).ObjectKind);
        Assert.DoesNotContain(sink.Entries, e => e.CanonicalPath == fixture.Grandchild);
    }

    [Fact]
    public async Task NullSystemVolumeRejected()
    {
        await Assert.ThrowsAsync<ArgumentNullException>("systemVolume", () =>
            new WindowsStorageEnumerator().EnumerateRootAsync(null!, new CollectingSink(), CancellationToken.None));
    }

    [Fact]
    public async Task NullEntrySinkRejected()
    {
        using var fixture = new RootFixture();
        await Assert.ThrowsAsync<ArgumentNullException>("entrySink", () =>
            new WindowsStorageEnumerator().EnumerateRootAsync(fixture.Volume, null!, CancellationToken.None));
    }

    [Fact]
    public async Task PreCancelledEnumerationRejectsBeforeFilesystemOrSinkAccess()
    {
        using var fixture = new RootFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var sink = new CollectingSink();
        var missing = new SystemVolumeDescriptor(fixture.Volume.VolumeIdentity, Path.Combine(fixture.Root, "missing"));
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsStorageEnumerator().EnumerateRootAsync(missing, sink, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public async Task CancellationBetweenEntriesStopsDeliveryAndPreservesToken()
    {
        using var fixture = new RootFixture();
        using var cancellation = new CancellationTokenSource();
        int calls = 0;
        var sink = new CallbackSink((_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            calls++;
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsStorageEnumerator().EnumerateRootAsync(fixture.Volume, sink, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SinkFailurePropagatesWithoutLaterDelivery(bool synchronous)
    {
        using var fixture = new RootFixture();
        var expected = new InvalidOperationException("Test sink failure.");
        int calls = 0;
        var sink = new CallbackSink((_, _) =>
        {
            calls++;
            if (synchronous)
            {
                throw expected;
            }

            return ValueTask.FromException(expected);
        });
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new WindowsStorageEnumerator().EnumerateRootAsync(fixture.Volume, sink, CancellationToken.None));
        Assert.Same(expected, actual);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SinkCancellationPropagatesWithoutLaterDelivery()
    {
        using var fixture = new RootFixture();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        int calls = 0;
        var sink = new CallbackSink((_, _) =>
        {
            calls++;
            return ValueTask.FromCanceled(cancellation.Token);
        });
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsStorageEnumerator().EnumerateRootAsync(fixture.Volume, sink, CancellationToken.None));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task SequentialBackpressureWaitsForFirstWrite()
    {
        using var fixture = new RootFixture();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var sink = new CallbackSink((_, _) =>
        {
            int call = Interlocked.Increment(ref calls);
            if (call == 1)
            {
                entered.SetResult();
                return new ValueTask(release.Task);
            }

            Assert.True(release.Task.IsCompletedSuccessfully);
            return ValueTask.CompletedTask;
        });
        Task enumeration = new WindowsStorageEnumerator().EnumerateRootAsync(fixture.Volume, sink, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(enumeration.IsCompleted);
            Assert.Equal(1, Volatile.Read(ref calls));
        }
        finally
        {
            release.TrySetResult();
            await enumeration.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task MissingRootFailureIsNotSilentlyIgnored()
    {
        using var fixture = new RootFixture();
        var volume = new SystemVolumeDescriptor(fixture.Volume.VolumeIdentity, Path.Combine(fixture.Root, "missing"));
        var sink = new CollectingSink();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() =>
            new WindowsStorageEnumerator().EnumerateRootAsync(volume, sink, CancellationToken.None));
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public void WindowsEnumeratorHasOnlyTheRequiredPublicOperations()
    {
        Type type = typeof(WindowsStorageEnumerator);
        Assert.True(type.IsSealed);
        Assert.Contains(typeof(IStorageEnumerator), type.GetInterfaces());
        Assert.Equal("windows", type.GetCustomAttribute<SupportedOSPlatformAttribute>()!.PlatformName);
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.Equal(["EnumerateChildrenAsync", "EnumerateRootAsync"], methods.Select(m => m.Name).Order(StringComparer.Ordinal));
        var method = Assert.Single(methods, m => m.Name == "EnumerateRootAsync");
        Assert.Equal("EnumerateRootAsync", method.Name);
        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Equal([typeof(SystemVolumeDescriptor), typeof(IStorageEntrySink), typeof(CancellationToken)], method.GetParameters().Select(p => p.ParameterType));
        Assert.Equal(2, type.Assembly.GetExportedTypes().Length);
    }

    [Fact]
    public async Task ChildEnumerationIsSingleLevelAndPropagatesVolumeIdentity()
    {
        using var fixture = new RootFixture();
        var sink = new CollectingSink();
        await new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, Candidate(fixture), sink, CancellationToken.None);
        var entry = Assert.Single(sink.Entries);
        Assert.Equal(fixture.Grandchild, entry.CanonicalPath);
        Assert.Same(fixture.Volume.VolumeIdentity, entry.VolumeIdentity);
        Assert.Equal(StorageObjectKind.File, entry.ObjectKind);
        AssertDirectChild(fixture.Child, entry.CanonicalPath);
    }

    [Fact]
    public async Task TargetChangedFromDirectoryToFileIsRejected()
    {
        using var fixture = new RootFixture();
        string path = Path.Combine(fixture.Root, "candidate");
        Directory.CreateDirectory(path);
        var candidate = Candidate(fixture, path);
        Directory.Delete(path);
        File.WriteAllText(path, "replacement");
        try
        {
            var sink = new CollectingSink();
            var error = await Assert.ThrowsAsync<StorageTraversalTargetChangedException>(() =>
                new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, candidate, sink, CancellationToken.None));
            Assert.Equal(path, error.CanonicalPath);
            Assert.Empty(sink.Entries);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task DisappearedChildDirectoryFailsInsteadOfSucceeding()
    {
        using var fixture = new RootFixture();
        string path = Path.Combine(fixture.Root, "candidate");
        Directory.CreateDirectory(path);
        var candidate = Candidate(fixture, path);
        Directory.Delete(path);
        var sink = new CollectingSink();
        var error = await Record.ExceptionAsync(() =>
            new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, candidate, sink, CancellationToken.None));
        Assert.True(error is DirectoryNotFoundException or FileNotFoundException);
        Assert.Empty(sink.Entries);
    }

    [Fact]
    public async Task ChildFromAnotherVolumeIsRejectedBeforeFilesystemAccess()
    {
        using var fixture = new RootFixture();
        var entry = new StorageEntry(new VolumeIdentity(Guid.NewGuid()), Path.Combine(fixture.Root, "missing"), StorageObjectKind.Directory, ReparseKind.None);
        await Assert.ThrowsAsync<ArgumentException>("directory", () =>
            new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, entry, new CollectingSink(), CancellationToken.None));
    }

    [Theory]
    [InlineData(ReparseKind.SymbolicLink)]
    [InlineData(ReparseKind.Junction)]
    [InlineData(ReparseKind.MountPoint)]
    [InlineData(ReparseKind.Other)]
    public async Task KnownReparseInputRejected(ReparseKind reparse)
    {
        using var fixture = new RootFixture();
        var entry = new StorageEntry(fixture.Volume.VolumeIdentity, fixture.Child, StorageObjectKind.Directory, reparse);
        await Assert.ThrowsAsync<ArgumentException>("directory", () =>
            new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, entry, new CollectingSink(), CancellationToken.None));
    }

    [Theory]
    [InlineData(StorageObjectKind.File)]
    [InlineData(StorageObjectKind.Other)]
    public async Task NonDirectoryChildRejected(StorageObjectKind kind)
    {
        using var fixture = new RootFixture();
        var entry = new StorageEntry(fixture.Volume.VolumeIdentity, fixture.Child, kind, ReparseKind.None);
        await Assert.ThrowsAsync<ArgumentException>("directory", () =>
            new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, entry, new CollectingSink(), CancellationToken.None));
    }

    [Theory]
    [InlineData("root")]
    [InlineData("parent")]
    [InlineData("prefix-sibling")]
    [InlineData("escape")]
    public async Task ChildOutsideRootOrRootItselfRejected(string scenario)
    {
        using var fixture = new RootFixture();
        string path = scenario switch
        {
            "root" => fixture.Root,
            "parent" => Path.GetDirectoryName(fixture.Root)!,
            "prefix-sibling" => fixture.Root + "-sibling",
            _ => Path.Combine(fixture.Root, "..", "outside"),
        };
        await Assert.ThrowsAsync<ArgumentException>("directory", () =>
            new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, Candidate(fixture, path), new CollectingSink(), CancellationToken.None));
    }

    [Fact]
    public async Task DoubleDotNameIsNotMistakenForParentSegment()
    {
        using var fixture = new RootFixture();
        string path = Path.Combine(fixture.Root, "..ordinary");
        Directory.CreateDirectory(path);
        try
        {
            var sink = new CollectingSink();
            await new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, Candidate(fixture, path), sink, CancellationToken.None);
            Assert.Empty(sink.Entries);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Fact]
    public async Task ChildBoundaryRejectsNullArgumentsAndPreCancellation()
    {
        using var fixture = new RootFixture();
        var enumerator = new WindowsStorageEnumerator();
        var sink = new CollectingSink();
        await Assert.ThrowsAsync<ArgumentNullException>("systemVolume", () => enumerator.EnumerateChildrenAsync(null!, Candidate(fixture), sink, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("directory", () => enumerator.EnumerateChildrenAsync(fixture.Volume, null!, sink, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("entrySink", () => enumerator.EnumerateChildrenAsync(fixture.Volume, Candidate(fixture), null!, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            enumerator.EnumerateChildrenAsync(fixture.Volume, Candidate(fixture, Path.Combine(fixture.Root, "missing")), sink, cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(sink.Entries);
    }

    private static StorageEntry Candidate(RootFixture fixture, string? path = null) =>
        new(fixture.Volume.VolumeIdentity, path ?? fixture.Child, StorageObjectKind.Directory, ReparseKind.None);

    private static void AssertDirectChild(string root, string path)
    {
        string relative = Path.GetRelativePath(root, path);
        Assert.False(Path.IsPathRooted(relative));
        Assert.NotEqual(".", relative);
        Assert.NotEqual("..", relative);
        Assert.NotEmpty(relative);
        Assert.DoesNotContain(Path.DirectorySeparatorChar, relative);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, relative);
    }

    private sealed class CollectingSink : IStorageEntrySink
    {
        public List<StorageEntry> Entries { get; } = [];

        public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CallbackSink(Func<StorageEntry, CancellationToken, ValueTask> write) : IStorageEntrySink
    {
        public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken) => write(entry, cancellationToken);
    }

    private sealed class RootFixture : IDisposable
    {
        public RootFixture()
        {
            Root = Directory.CreateTempSubdirectory("CDriveSmartClean-F1-09-").FullName;
            try
            {
                Directory.CreateDirectory(Child);
                File.WriteAllText(Ordinary, "fixture");
                File.WriteAllText(Grandchild, "must not be enumerated");
                File.WriteAllText(Hidden, "visible to enumeration");
                File.SetAttributes(Hidden, FileAttributes.Hidden | FileAttributes.System);
                Volume = new SystemVolumeDescriptor(new VolumeIdentity(new Guid("336d3520-fd79-4b49-85df-2f41bf352376")), Root);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public string Root { get; }

        public string Child => Path.Combine(Root, "child");

        public string Ordinary => Path.Combine(Root, "ordinary.bin");

        public string Grandchild => Path.Combine(Child, "grandchild.bin");

        public string Hidden => Path.Combine(Root, "hidden.bin");

        public SystemVolumeDescriptor Volume { get; }

        public void Dispose()
        {
            // Delete only known test-owned paths; never recursively remove a directory tree.
            if (File.Exists(Hidden))
            {
                File.SetAttributes(Hidden, FileAttributes.Normal);
                File.Delete(Hidden);
            }

            File.Delete(Ordinary);
            File.Delete(Grandchild);
            if (Directory.Exists(Child))
            {
                Directory.Delete(Child);
            }

            Directory.Delete(Root);
        }
    }
}
