using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
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
        var entry = new StorageEntry(new VolumeIdentity(Guid.NewGuid()), null, Path.Combine(fixture.Root, "missing"), StorageObjectKind.Directory, ReparseKind.None);
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
        var entry = new StorageEntry(fixture.Volume.VolumeIdentity, null, fixture.Child, StorageObjectKind.Directory, reparse);
        await Assert.ThrowsAsync<ArgumentException>("directory", () =>
            new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, entry, new CollectingSink(), CancellationToken.None));
    }

    [Theory]
    [InlineData(StorageObjectKind.File)]
    [InlineData(StorageObjectKind.Other)]
    public async Task NonDirectoryChildRejected(StorageObjectKind kind)
    {
        using var fixture = new RootFixture();
        var entry = new StorageEntry(fixture.Volume.VolumeIdentity, null, fixture.Child, kind, ReparseKind.None);
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

    [Fact]
    public async Task NativeIdentityStableAcrossReadsAndDistinctAcrossFiles()
    {
        using var fixture = new RootFixture();
        var first = await Observe(fixture);
        var second = await Observe(fixture);
        Assert.All(first, entry => Assert.NotNull(entry.ObjectIdentity));
        foreach (var entry in first)
        {
            Assert.Equal(entry.ObjectIdentity, Assert.Single(second, e => e.CanonicalPath == entry.CanonicalPath).ObjectIdentity);
        }

        Assert.NotEqual(Assert.Single(first, e => e.CanonicalPath == fixture.Ordinary).ObjectIdentity,
            Assert.Single(first, e => e.CanonicalPath == fixture.Hidden).ObjectIdentity);
    }

    [Fact]
    public async Task HardLinkIdentityEquivalentAndBothPathsVisible()
    {
        using var fixture = new RootFixture();
        string link = Path.Combine(fixture.Root, "hard-link.bin");
        try
        {
            bool created = CreateHardLinkW(link, fixture.Ordinary, 0);
            int error = Marshal.GetLastPInvokeError();
            Assert.True(created, $"Test hard-link creation failed: {error}");
            var entries = await Observe(fixture);
            var original = Assert.Single(entries, e => e.CanonicalPath == fixture.Ordinary);
            var alias = Assert.Single(entries, e => e.CanonicalPath == link);
            Assert.NotNull(original.ObjectIdentity);
            Assert.NotNull(alias.ObjectIdentity);
            Assert.Equal(original.ObjectIdentity, alias.ObjectIdentity);
            Assert.NotEqual(original.CanonicalPath, alias.CanonicalPath);
        }
        finally
        {
            File.Delete(link);
        }
    }

    [Fact]
    public async Task SamePathDirectoryReplacementIsDetected()
    {
        using var fixture = new RootFixture();
        string path = Path.Combine(fixture.Root, "candidate");
        Directory.CreateDirectory(path);
        try
        {
            var original = Assert.Single(await Observe(fixture), e => e.CanonicalPath == path);
            Assert.NotNull(original.ObjectIdentity);
            Directory.Delete(path);
            Directory.CreateDirectory(path);
            var replacement = Assert.Single(await Observe(fixture), e => e.CanonicalPath == path);
            Assert.NotNull(replacement.ObjectIdentity);
            Assert.NotEqual(original.ObjectIdentity, replacement.ObjectIdentity);
            var sink = new CollectingSink();
            var error = await Assert.ThrowsAsync<StorageTraversalTargetChangedException>(() =>
                new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, original, sink, TestContext.Current.CancellationToken));
            Assert.Equal(path, error.CanonicalPath);
            Assert.Empty(sink.Entries);
        }
        finally
        {
            Directory.Delete(path);
        }
    }

    [Fact]
    public async Task PinnedHandlePreventsTargetReplacementUntilSinkCompletes()
    {
        using var fixture = new RootFixture();
        var candidate = Assert.Single(await Observe(fixture), e => e.CanonicalPath == fixture.Child);
        Assert.NotNull(candidate.ObjectIdentity);
        string moved = Path.Combine(fixture.Root, "moved-child");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sink = new CallbackSink((_, _) =>
        {
            entered.TrySetResult();
            return new ValueTask(release.Task);
        });
        Task enumeration = new WindowsStorageEnumerator().EnumerateChildrenAsync(
            fixture.Volume, candidate, sink, TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(enumeration.IsCompleted);
            Assert.Throws<IOException>(() => Directory.Move(fixture.Child, moved));
            Assert.True(Directory.Exists(fixture.Child));
            Assert.False(Directory.Exists(moved));
        }
        finally
        {
            release.TrySetResult();
            await enumeration.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            // Restore only the fixture's exact path if a failed assertion exposed a pinning defect.
            if (Directory.Exists(moved))
            {
                Directory.Move(moved, fixture.Child);
            }
        }

        Directory.Move(fixture.Child, moved);
        Directory.Move(moved, fixture.Child);
    }

    [Fact]
    public async Task NullDirectoryIdentityFailsClosed()
    {
        using var fixture = new RootFixture();
        var candidate = new StorageEntry(fixture.Volume.VolumeIdentity, null, fixture.Child, StorageObjectKind.Directory, ReparseKind.None);
        var sink = new CollectingSink();
        var error = await Assert.ThrowsAsync<StorageObjectIdentityUnavailableException>(() =>
            new WindowsStorageEnumerator().EnumerateChildrenAsync(fixture.Volume, candidate, sink, TestContext.Current.CancellationToken));
        Assert.Equal(fixture.Child, error.CanonicalPath);
        Assert.Empty(sink.Entries);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PinnedHandleReleasedWhenChildSinkFailsOrCancels(bool cancel)
    {
        using var fixture = new RootFixture();
        var candidate = Assert.Single(await Observe(fixture), e => e.CanonicalPath == fixture.Child);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception expected = cancel ? new OperationCanceledException(cancellation.Token) : new IOException("sink failure");
        var sink = new CallbackSink((_, _) => ValueTask.FromException(expected));
        var actual = await Record.ExceptionAsync(() => new WindowsStorageEnumerator().EnumerateChildrenAsync(
            fixture.Volume, candidate, sink, TestContext.Current.CancellationToken));
        Assert.Same(expected, actual);
        string moved = Path.Combine(fixture.Root, "released-child");
        try
        {
            Directory.Move(fixture.Child, moved);
        }
        finally
        {
            if (Directory.Exists(moved))
            {
                Directory.Move(moved, fixture.Child);
            }
        }
    }

    [Fact]
    public async Task ObservationDoesNotPinButStrictTraversalReportsSharingConflict()
    {
        using var fixture = new RootFixture();
        // DELETE access with share-all makes observation possible but conflicts with a traversal pin.
        using var locked = TestCreateFile(fixture.Child, 0x00010000, 7, 0, 3, 0x02200000, 0);
        Assert.False(locked.IsInvalid);
        var candidate = Assert.Single(await Observe(fixture), e => e.CanonicalPath == fixture.Child);
        Assert.NotNull(candidate.ObjectIdentity);
        await Assert.ThrowsAsync<IOException>(() => new WindowsStorageEnumerator().EnumerateChildrenAsync(
            fixture.Volume, candidate, new CollectingSink(), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void BestEffortMissingIdentityReturnsNull()
    {
        using var fixture = new RootFixture();
        Assert.Null(ReadIdentity(fixture.Volume.VolumeIdentity, Path.Combine(fixture.Root, "absent")));
    }

    [Fact]
    public void NativeInteropSurfaceLayoutsAndNoFollowFlagsAreExact()
    {
        Type native = typeof(WindowsStorageEnumerator).Assembly.GetType(
            "CDriveSmartClean.Platform.Windows.Interop.Kernel32FileIdentityNative", true)!;
        Assert.False(native.IsPublic);
        Assert.False(IdentityReader.IsPublic);
        var methods = native.GetMethods(BindingFlags.NonPublic | BindingFlags.Static);
        var imports = methods.Select(m => m.GetCustomAttribute<DllImportAttribute>()).OfType<DllImportAttribute>().ToArray();
        Assert.Equal(["CreateFileW", "GetFileInformationByHandleEx"], imports.Select(i => i.EntryPoint).Distinct().Order(StringComparer.Ordinal));
        Assert.All(imports, import =>
        {
            Assert.Equal("kernel32.dll", import.Value);
            Assert.True(import.SetLastError);
            Assert.True(import.ExactSpelling);
        });
        Assert.Equal(typeof(Microsoft.Win32.SafeHandles.SafeFileHandle), native.GetMethod("CreateFileW", BindingFlags.NonPublic | BindingFlags.Static)!.ReturnType);
        uint flags = (uint)native.GetField("IdentityOpenFlags", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        Assert.Equal(0x02200000u, flags);
        Assert.Equal(7u, native.GetField("ObservationShare", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.Equal(3u, native.GetField("TraversalShare", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.Equal(1u, native.GetField("DirectoryListAccess", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Assert.Equal(3u, native.GetField("OpenExisting", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue());
        Type id128 = native.GetNestedType("FileId128", BindingFlags.NonPublic)!;
        Type idInfo = native.GetNestedType("FileIdInfo", BindingFlags.NonPublic)!;
        Type tagInfo = native.GetNestedType("FileAttributeTagInfo", BindingFlags.NonPublic)!;
        Assert.Equal(16, Marshal.SizeOf(id128));
        Assert.Equal(24, Marshal.SizeOf(idInfo));
        Assert.Equal(8, Marshal.SizeOf(tagInfo));
        Assert.Equal((nint)8, Marshal.OffsetOf(idInfo, "FileId"));
        Assert.Equal((nint)4, Marshal.OffsetOf(tagInfo, "ReparseTag"));
        Type infoClass = native.GetNestedType("FileInfoByHandleClass", BindingFlags.NonPublic)!;
        Assert.Equal(0x12, Convert.ToInt32(Enum.Parse(infoClass, "FileIdInfo"), System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal(9, Convert.ToInt32(Enum.Parse(infoClass, "FileAttributeTagInfo"), System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void FileIdAll128BitsArePreservedWithoutUuidInterpretation()
    {
        Type id = typeof(WindowsStorageEnumerator).Assembly.GetType(
            "CDriveSmartClean.Platform.Windows.Interop.Kernel32FileIdentityNative+FileId128", true)!;
        object native = Activator.CreateInstance(id)!;
        id.GetField("Low", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(native, 0x0706050403020100ul);
        id.GetField("High", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(native, 0x0f0e0d0c0b0a0908ul);
        var convert = id.GetMethod("ToOpaqueGuid", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var actual = (Guid)convert.Invoke(native, null)!;
        Assert.Equal(Enumerable.Range(0, 16).Select(i => (byte)i), actual.ToByteArray());
        Assert.Equal(actual, (Guid)convert.Invoke(native, null)!);
        id.GetField("High", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(native, 0x8f0e0d0c0b0a0908ul);
        Assert.NotEqual(actual, (Guid)convert.Invoke(native, null)!);
    }

    [Fact]
    public void EmptyNativeIdFailsClosed()
    {
        var method = IdentityReader.GetMethod("CreateIdentity", BindingFlags.NonPublic | BindingFlags.Static)!;
        var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [new VolumeIdentity(Guid.NewGuid()), Guid.Empty, "path"]));
        Assert.Equal("path", Assert.IsType<StorageObjectIdentityUnavailableException>(error.InnerException).CanonicalPath);
    }

    [Theory]
    [InlineData(2, typeof(FileNotFoundException))]
    [InlineData(3, typeof(DirectoryNotFoundException))]
    [InlineData(5, typeof(UnauthorizedAccessException))]
    [InlineData(1, typeof(StorageObjectIdentityUnavailableException))]
    [InlineData(50, typeof(StorageObjectIdentityUnavailableException))]
    [InlineData(87, typeof(StorageObjectIdentityUnavailableException))]
    [InlineData(32, typeof(IOException))]
    public void NativeErrorsMappedWithoutLosingIoEvidence(int code, Type expected)
    {
        var method = IdentityReader.GetMethod("NativeFailure", BindingFlags.NonPublic | BindingFlags.Static)!;
        var error = Assert.IsAssignableFrom<Exception>(method.Invoke(null, [code, "path"]));
        Assert.Equal(expected, error.GetType());
        if (code == 32)
        {
            Assert.Equal(code, Assert.IsType<System.ComponentModel.Win32Exception>(error.InnerException).NativeErrorCode);
        }
    }

    private static async Task<List<StorageEntry>> Observe(RootFixture fixture)
    {
        var sink = new CollectingSink();
        await new WindowsStorageEnumerator().EnumerateRootAsync(fixture.Volume, sink, TestContext.Current.CancellationToken);
        return sink.Entries;
    }

    // Test-only mutations are restricted to RootFixture's Directory.CreateTempSubdirectory storage.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string fileName, string existingFileName, nint securityAttributes);

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle TestCreateFile(
        string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);

    private static StorageEntry Candidate(RootFixture fixture, string? path = null) =>
        new(fixture.Volume.VolumeIdentity, ReadIdentity(fixture.Volume.VolumeIdentity, path ?? fixture.Child), path ?? fixture.Child, StorageObjectKind.Directory, ReparseKind.None);

    private static Type IdentityReader => typeof(WindowsStorageEnumerator).Assembly.GetType(
        "CDriveSmartClean.Platform.Windows.Storage.WindowsStorageObjectIdentityReader", true)!;

    private static StorageObjectIdentity? ReadIdentity(VolumeIdentity volume, string path) =>
        (StorageObjectIdentity?)IdentityReader.GetMethod("TryRead", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [volume, path]);

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
