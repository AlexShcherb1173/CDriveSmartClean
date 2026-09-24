using System.Reflection;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Scan.Traversal;
using Xunit;

namespace CDriveSmartClean.Scan.Tests.Traversal;

public sealed class StorageTreeWalkerTests
{
    [Fact]
    public async Task RecursiveTreeWalkDeliversEachEntryOnceAndNeverTraversesFiles()
    {
        var h = new Harness();
        var dirA = h.Entry("dir-a", StorageObjectKind.Directory);
        var dirB = h.Entry("dir-b", StorageObjectKind.Directory);
        h.Enumerator.Root = [h.Entry("file-a"), dirA, h.Entry("file-d")];
        h.Enumerator.Children["dir-a"] = [h.Entry("file-b"), dirB];
        h.Enumerator.Children["dir-b"] = [h.Entry("file-c")];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Equal(["dir-a", "dir-b", "file-a", "file-b", "file-c", "file-d"], h.Entries.Values.Select(e => e.CanonicalPath).Order(StringComparer.Ordinal));
        Assert.Equal(["dir-a", "dir-b"], h.Enumerator.ChildCalls);
        Assert.Equal(1, h.Enumerator.RootCalls);
        Assert.Empty(h.Issues.Values);
    }

    [Theory]
    [InlineData(StorageObjectKind.Directory, ReparseKind.SymbolicLink)]
    [InlineData(StorageObjectKind.Directory, ReparseKind.Junction)]
    [InlineData(StorageObjectKind.Directory, ReparseKind.MountPoint)]
    [InlineData(StorageObjectKind.Directory, ReparseKind.Other)]
    [InlineData(StorageObjectKind.File, ReparseKind.None)]
    [InlineData(StorageObjectKind.Other, ReparseKind.None)]
    public async Task ObserveOnlyEntriesAreVisibleButNeverTraversed(StorageObjectKind kind, ReparseKind reparse)
    {
        var h = new Harness();
        var entry = h.Entry("observe-only", kind, reparse);
        h.Enumerator.Root = [entry];
        h.Enumerator.Children[entry.CanonicalPath] = [h.Entry("forbidden-child")];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Same(entry, Assert.Single(h.Entries.Values));
        Assert.Empty(h.Enumerator.ChildCalls);
        Assert.Empty(h.Issues.Values);
    }

    [Theory]
    [InlineData(0, StorageTraversalIssueKind.Inaccessible)]
    [InlineData(1, StorageTraversalIssueKind.Disappeared)]
    [InlineData(2, StorageTraversalIssueKind.Disappeared)]
    [InlineData(3, StorageTraversalIssueKind.TargetChanged)]
    [InlineData(4, StorageTraversalIssueKind.IoFailure)]
    [InlineData(5, StorageTraversalIssueKind.IdentityUnavailable)]
    public async Task ChildFailuresReportExactlyOneIssueAndContinuePendingSiblings(int failure, StorageTraversalIssueKind kind)
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("good", StorageObjectKind.Directory), h.Entry("bad", StorageObjectKind.Directory)];
        h.Enumerator.Children["good"] = [h.Entry("surviving-file")];
        h.Enumerator.Errors["bad"] = Failure(failure);
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Equal(["bad", "good"], h.Enumerator.ChildCalls);
        Assert.Contains(h.Entries.Values, e => e.CanonicalPath == "surviving-file");
        var issue = Assert.Single(h.Issues.Values);
        Assert.Equal(kind, issue.Kind);
        Assert.Equal("bad", issue.CanonicalPath);
        Assert.Same(h.Volume.VolumeIdentity, issue.VolumeIdentity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task RootFailureIsFatalAndNotDowngraded(int failure)
    {
        var h = new Harness();
        var expected = Failure(failure);
        h.Enumerator.RootError = expected;
        Assert.Same(expected, await Record.ExceptionAsync(() => h.Walk(TestContext.Current.CancellationToken)));
        Assert.Empty(h.Issues.Values);
        Assert.Empty(h.Enumerator.ChildCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedChildFailuresPropagate(bool argument)
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("bad", StorageObjectKind.Directory)];
        Exception expected = argument ? new ArgumentException("bug") : new InvalidOperationException("bug");
        h.Enumerator.Errors["bad"] = expected;
        Assert.Same(expected, await Record.ExceptionAsync(() => h.Walk(TestContext.Current.CancellationToken)));
        Assert.Empty(h.Issues.Values);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public async Task EntrySinkFailuresKeepTheirOriginAndOriginalInstance(bool unauthorized, bool asynchronous, bool atRoot)
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("parent", StorageObjectKind.Directory)];
        h.Enumerator.Children["parent"] = [h.Entry("child"), h.Entry("later")];
        Exception expected = unauthorized ? new UnauthorizedAccessException("sink") : new IOException("sink");
        h.Entries.Callback = (entry, _) =>
        {
            if (atRoot || entry.CanonicalPath == "child")
            {
                if (!asynchronous)
                {
                    throw expected;
                }

                return ValueTask.FromException(expected);
            }

            return ValueTask.CompletedTask;
        };
        Assert.Same(expected, await Record.ExceptionAsync(() => h.Walk(TestContext.Current.CancellationToken)));
        Assert.Empty(h.Issues.Values);
        Assert.DoesNotContain(h.Entries.Values, e => e.CanonicalPath == "later");
        Assert.Equal(atRoot ? 1 : 2, h.Entries.Values.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IssueSinkFailureStopsTraversal(bool cancel)
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("later", StorageObjectKind.Directory), h.Entry("bad", StorageObjectKind.Directory)];
        h.Enumerator.Errors["bad"] = new IOException("enumeration");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Exception expected = cancel ? new OperationCanceledException(cancellation.Token) : new IOException("issue sink");
        h.Issues.Callback = (_, _) => ValueTask.FromException(expected);
        Assert.Same(expected, await Record.ExceptionAsync(() => h.Walk(TestContext.Current.CancellationToken)));
        Assert.Equal(["bad"], h.Enumerator.ChildCalls);
        Assert.Single(h.Issues.Values);
    }

    [Fact]
    public async Task PreCancellationPreventsRootEnumeration()
    {
        var h = new Harness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Walk(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Equal(0, h.Enumerator.RootCalls);
        Assert.Empty(h.Entries.Values);
    }

    [Fact]
    public async Task CancellationAfterDeliveryStopsBeforeDirectoryDescent()
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("parent", StorageObjectKind.Directory), h.Entry("later")];
        using var cancellation = new CancellationTokenSource();
        h.Entries.Callback = (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            return ValueTask.CompletedTask;
        };
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Walk(cancellation.Token));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Single(h.Entries.Values);
        Assert.Empty(h.Enumerator.ChildCalls);
        Assert.Empty(h.Issues.Values);
    }

    [Fact]
    public async Task EntrySinkCancellationIsNeverAnIssue()
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("parent", StorageObjectKind.Directory)];
        h.Enumerator.Children["parent"] = [h.Entry("child")];
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        h.Entries.Callback = (entry, _) => entry.CanonicalPath == "child"
            ? ValueTask.FromCanceled(cancellation.Token) : ValueTask.CompletedTask;
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Walk(TestContext.Current.CancellationToken));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Empty(h.Issues.Values);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossVolumeOutputFailsClosedBeforeForwardingOrScheduling(bool child)
    {
        var h = new Harness();
        var foreign = new StorageEntry(new VolumeIdentity(Guid.NewGuid()), null, "foreign", StorageObjectKind.Directory, ReparseKind.None);
        h.Enumerator.Root = child ? [h.Entry("parent", StorageObjectKind.Directory)] : [foreign];
        h.Enumerator.Children["parent"] = [foreign];
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Walk(TestContext.Current.CancellationToken));
        Assert.DoesNotContain(h.Entries.Values, e => e == foreign);
        Assert.DoesNotContain("foreign", h.Enumerator.ChildCalls);
        Assert.Empty(h.Issues.Values);
    }

    [Fact]
    public async Task SameTokenReachesAllBoundaries()
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("bad", StorageObjectKind.Directory)];
        h.Enumerator.Errors["bad"] = new IOException("failed");
        using var cancellation = new CancellationTokenSource();
        await h.Walk(cancellation.Token);
        Assert.All(h.Enumerator.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.All(h.Entries.Tokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(cancellation.Token, Assert.Single(h.Issues.Tokens));
    }

    [Fact]
    public async Task EntryBackpressurePreventsNextDeliveryAndDescent()
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("parent", StorageObjectKind.Directory), h.Entry("later")];
        var entered = Signal();
        var release = Signal();
        h.Entries.Callback = (entry, _) =>
        {
            if (entry.CanonicalPath == "parent")
            {
                entered.SetResult();
                return new ValueTask(release.Task);
            }

            Assert.True(release.Task.IsCompletedSuccessfully);
            return ValueTask.CompletedTask;
        };
        Task walk = h.Walk(TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Single(h.Entries.Values);
            Assert.Empty(h.Enumerator.ChildCalls);
            Assert.False(walk.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await walk.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        Assert.Equal(["parent"], h.Enumerator.ChildCalls);
        Assert.Equal(2, h.Entries.Values.Count);
    }

    [Fact]
    public async Task IssueBackpressurePreventsNextBranchAndIssue()
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("later", StorageObjectKind.Directory), h.Entry("first", StorageObjectKind.Directory)];
        h.Enumerator.Errors["first"] = new IOException("first");
        h.Enumerator.Errors["later"] = new IOException("later");
        var entered = Signal();
        var release = Signal();
        h.Issues.Callback = (issue, _) =>
        {
            if (issue.CanonicalPath == "first")
            {
                entered.SetResult();
                return new ValueTask(release.Task);
            }

            Assert.True(release.Task.IsCompletedSuccessfully);
            return ValueTask.CompletedTask;
        };
        Task walk = h.Walk(TestContext.Current.CancellationToken);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Single(h.Issues.Values);
            Assert.Equal(["first"], h.Enumerator.ChildCalls);
            Assert.False(walk.IsCompleted);
        }
        finally
        {
            release.TrySetResult();
            await walk.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }

        Assert.Equal(2, h.Issues.Values.Count);
    }

    [Fact]
    public async Task SameDirectoryEntryIsNotScheduledTwice()
    {
        var h = new Harness();
        var directory = h.Entry("directory", StorageObjectKind.Directory);
        h.Enumerator.Root = [directory, directory];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Equal(["directory"], h.Enumerator.ChildCalls);
    }

    [Fact]
    public async Task DeepTreeUsesIterativeDescentAndWalkStateIsPerCall()
    {
        var h = new Harness();
        h.Enumerator.Root = [h.Entry("0", StorageObjectKind.Directory)];
        for (int i = 0; i < 2000; i++)
        {
            h.Enumerator.Children[i.ToString(System.Globalization.CultureInfo.InvariantCulture)] =
                [h.Entry((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StorageObjectKind.Directory)];
        }

        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Equal(2001, h.Enumerator.ChildCalls.Count);
        Assert.Equal(2001, h.Entries.Values.Count);
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Equal(4002, h.Enumerator.ChildCalls.Count);
        Assert.Equal(2, h.Enumerator.RootCalls);
    }

    [Fact]
    public async Task NullArgumentsAndDependenciesAreRejected()
    {
        var h = new Harness();
        Assert.Throws<ArgumentNullException>("storageEnumerator", () => new StorageTreeWalker(null!, new StorageTraversalPolicy()));
        Assert.Throws<ArgumentNullException>("traversalPolicy", () => new StorageTreeWalker(h.Enumerator, null!));
        await Assert.ThrowsAsync<ArgumentNullException>("systemVolume", () => h.Walker.WalkAsync(null!, h.Entries, h.Issues, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("entrySink", () => h.Walker.WalkAsync(h.Volume, null!, h.Issues, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>("issueSink", () => h.Walker.WalkAsync(h.Volume, h.Entries, null!, CancellationToken.None));
    }

    [Fact]
    public void WalkerPublicContractIsExact()
    {
        var type = typeof(StorageTreeWalker);
        Assert.True(type.IsSealed);
        Assert.Equal(3, type.Assembly.GetExportedTypes().Length);
        Assert.Equal([typeof(IStorageEnumerator), typeof(StorageTraversalPolicy)],
            Assert.Single(type.GetConstructors()).GetParameters().Select(p => p.ParameterType));
        var method = Assert.Single(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Equal("WalkAsync", method.Name);
        Assert.Equal(typeof(Task), method.ReturnType);
        Assert.Equal([typeof(SystemVolumeDescriptor), typeof(IStorageEntrySink), typeof(IStorageTraversalIssueSink), typeof(CancellationToken)],
            method.GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public async Task IdentitylessDirectoryIsVisibleReportedAndNotTraversed()
    {
        var h = new Harness();
        var entry = new StorageEntry(h.Volume.VolumeIdentity, null, "unknown", StorageObjectKind.Directory, ReparseKind.None);
        h.Enumerator.Root = [entry];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Same(entry, Assert.Single(h.Entries.Values));
        var issue = Assert.Single(h.Issues.Values);
        Assert.Equal(StorageTraversalIssueKind.IdentityUnavailable, issue.Kind);
        Assert.Equal(entry.CanonicalPath, issue.CanonicalPath);
        Assert.Same(entry.VolumeIdentity, issue.VolumeIdentity);
        Assert.Empty(h.Enumerator.ChildCalls);
    }

    [Fact]
    public async Task IdentitylessFileRemainsVisibleWithoutTraversalIssue()
    {
        var h = new Harness();
        var entry = new StorageEntry(h.Volume.VolumeIdentity, null, "file", StorageObjectKind.File, ReparseKind.None);
        h.Enumerator.Root = [entry];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Same(entry, Assert.Single(h.Entries.Values));
        Assert.Empty(h.Issues.Values);
        Assert.Empty(h.Enumerator.ChildCalls);
    }

    [Theory]
    [InlineData(ReparseKind.SymbolicLink)]
    [InlineData(ReparseKind.Junction)]
    [InlineData(ReparseKind.MountPoint)]
    [InlineData(ReparseKind.Other)]
    public async Task IdentitylessReparseRemainsObserveOnly(ReparseKind reparse)
    {
        var h = new Harness();
        var entry = new StorageEntry(h.Volume.VolumeIdentity, null, "reparse", StorageObjectKind.Directory, reparse);
        h.Enumerator.Root = [entry];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Same(entry, Assert.Single(h.Entries.Values));
        Assert.Empty(h.Issues.Values);
        Assert.Empty(h.Enumerator.ChildCalls);
    }

    [Fact]
    public async Task SameDirectoryIdentityIsScheduledOnlyOnce()
    {
        var h = new Harness();
        var first = h.Entry("first", StorageObjectKind.Directory);
        var second = new StorageEntry(first.VolumeIdentity,
            new StorageObjectIdentity(first.VolumeIdentity, first.ObjectIdentity!.ObjectId), "second", StorageObjectKind.Directory, ReparseKind.None);
        h.Enumerator.Root = [first, second];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Equal([first, second], h.Entries.Values);
        Assert.Equal(["first"], h.Enumerator.ChildCalls);
        Assert.Empty(h.Issues.Values);
    }

    [Fact]
    public async Task HardLinkFilePathsBothRemainVisible()
    {
        var h = new Harness();
        var first = h.Entry("first");
        var second = new StorageEntry(first.VolumeIdentity, first.ObjectIdentity, "second", StorageObjectKind.File, ReparseKind.None);
        h.Enumerator.Root = [first, second];
        await h.Walk(TestContext.Current.CancellationToken);
        Assert.Equal([first, second], h.Entries.Values);
        Assert.Empty(h.Issues.Values);
    }

    [Fact]
    public Task IdentityUnavailableChildExceptionReportsIssueAndContinuesSibling() =>
        ChildFailuresReportExactlyOneIssueAndContinuePendingSiblings(5, StorageTraversalIssueKind.IdentityUnavailable);

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task IssueSinkFailureDuringIdentityUnavailableRoutingPreservesOrigin(bool unauthorized, bool asynchronous, bool atRoot)
    {
        var h = new Harness();
        var unknown = new StorageEntry(h.Volume.VolumeIdentity, null, "unknown", StorageObjectKind.Directory, ReparseKind.None);
        h.Enumerator.Root = atRoot ? [unknown, h.Entry("later")] : [h.Entry("parent", StorageObjectKind.Directory)];
        h.Enumerator.Children["parent"] = [unknown, h.Entry("later")];
        Exception expected = unauthorized ? new UnauthorizedAccessException("issue sink") : new IOException("issue sink");
        h.Issues.Callback = (_, _) =>
        {
            if (!asynchronous)
            {
                throw expected;
            }

            return ValueTask.FromException(expected);
        };
        Assert.Same(expected, await Record.ExceptionAsync(() => h.Walk(TestContext.Current.CancellationToken)));
        Assert.Equal(StorageTraversalIssueKind.IdentityUnavailable, Assert.Single(h.Issues.Values).Kind);
        Assert.DoesNotContain(h.Entries.Values, e => e.CanonicalPath == "later");
    }

    [Fact]
    public async Task IdentityUnavailableRoutingHonorsIssueBackpressureAndCancellation()
    {
        var h = new Harness();
        var unknown = new StorageEntry(h.Volume.VolumeIdentity, null, "unknown", StorageObjectKind.Directory, ReparseKind.None);
        h.Enumerator.Root = [unknown, h.Entry("later")];
        var entered = Signal();
        var release = Signal();
        using var cancellation = new CancellationTokenSource();
        h.Issues.Callback = (_, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            entered.SetResult();
            return new ValueTask(release.Task);
        };
        Task walk = h.Walk(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.False(walk.IsCompleted);
            Assert.Single(h.Entries.Values);
            cancellation.Cancel();
        }
        finally
        {
            release.TrySetResult();
        }

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => walk);
        Assert.Equal(cancellation.Token, error.CancellationToken);
        Assert.Single(h.Issues.Values);
        Assert.Empty(h.Enumerator.ChildCalls);
    }

    private static Exception Failure(int value) => value switch
    {
        0 => new UnauthorizedAccessException("enumerator"),
        1 => new DirectoryNotFoundException("enumerator"),
        2 => new FileNotFoundException("enumerator"),
        3 => new StorageTraversalTargetChangedException("bad"),
        5 => new StorageObjectIdentityUnavailableException("bad"),
        _ => new IOException("enumerator"),
    };

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Harness
    {
        public SystemVolumeDescriptor Volume { get; } = new(new VolumeIdentity(Guid.NewGuid()), "root");
        public FakeEnumerator Enumerator { get; } = new();
        public EntrySink Entries { get; } = new();
        public IssueSink Issues { get; } = new();
        public StorageTreeWalker Walker { get; }

        public Harness() => Walker = new StorageTreeWalker(Enumerator, new StorageTraversalPolicy());

        public StorageEntry Entry(string path, StorageObjectKind kind = StorageObjectKind.File, ReparseKind reparse = ReparseKind.None) =>
            new(Volume.VolumeIdentity, new StorageObjectIdentity(Volume.VolumeIdentity, Guid.NewGuid()), path, kind, reparse);

        public Task Walk(CancellationToken cancellationToken = default) => Walker.WalkAsync(Volume, Entries, Issues, cancellationToken);
    }

    private sealed class FakeEnumerator : IStorageEnumerator
    {
        public StorageEntry[] Root { get; set; } = [];
        public Dictionary<string, StorageEntry[]> Children { get; } = [];
        public Dictionary<string, Exception> Errors { get; } = [];
        public Exception? RootError { get; set; }
        public int RootCalls { get; private set; }
        public List<string> ChildCalls { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public async Task EnumerateRootAsync(SystemVolumeDescriptor systemVolume, IStorageEntrySink entrySink, CancellationToken cancellationToken)
        {
            RootCalls++;
            Tokens.Add(cancellationToken);
            if (RootError is { } error)
            {
                throw error;
            }

            foreach (var entry in Root)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await entrySink.WriteAsync(entry, cancellationToken);
            }
        }

        public async Task EnumerateChildrenAsync(SystemVolumeDescriptor systemVolume, StorageEntry directory, IStorageEntrySink entrySink, CancellationToken cancellationToken)
        {
            ChildCalls.Add(directory.CanonicalPath);
            Tokens.Add(cancellationToken);
            if (Errors.TryGetValue(directory.CanonicalPath, out var error))
            {
                throw error;
            }

            if (Children.TryGetValue(directory.CanonicalPath, out var children))
            {
                foreach (var entry in children)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await entrySink.WriteAsync(entry, cancellationToken);
                }
            }
        }
    }

    private sealed class EntrySink : IStorageEntrySink
    {
        public List<StorageEntry> Values { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Func<StorageEntry, CancellationToken, ValueTask>? Callback { get; set; }

        public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            Values.Add(entry);
            Tokens.Add(cancellationToken);
            return Callback?.Invoke(entry, cancellationToken) ?? ValueTask.CompletedTask;
        }
    }

    private sealed class IssueSink : IStorageTraversalIssueSink
    {
        public List<StorageTraversalIssue> Values { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];
        public Func<StorageTraversalIssue, CancellationToken, ValueTask>? Callback { get; set; }

        public ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken cancellationToken)
        {
            Values.Add(issue);
            Tokens.Add(cancellationToken);
            return Callback?.Invoke(issue, cancellationToken) ?? ValueTask.CompletedTask;
        }
    }
}
