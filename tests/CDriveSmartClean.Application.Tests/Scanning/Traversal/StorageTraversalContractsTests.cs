using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Traversal;

public sealed class StorageTraversalContractsTests
{
    [Fact]
    public void StorageTraversalIssueKindExactValues()
    {
        Assert.Equal(["Inaccessible", "Disappeared", "TargetChanged", "IoFailure"], Enum.GetNames<StorageTraversalIssueKind>());
        Assert.Equal([1, 2, 3, 4], Enum.GetValues<StorageTraversalIssueKind>().Select(k => (int)k));
    }

    [Fact]
    public void DefaultStorageTraversalIssueKindIsUndefined() => Assert.False(Enum.IsDefined(default(StorageTraversalIssueKind)));

    [Fact]
    public void StorageTraversalIssueValidValuesAccepted()
    {
        var identity = Identity();
        foreach (var kind in Enum.GetValues<StorageTraversalIssueKind>())
        {
            var issue = new StorageTraversalIssue(identity, "path", kind);
            Assert.Same(identity, issue.VolumeIdentity);
            Assert.Equal(kind, issue.Kind);
        }
    }

    [Fact]
    public void NullIssueVolumeIdentityRejected() =>
        Assert.Throws<ArgumentNullException>("volumeIdentity", () => new StorageTraversalIssue(null!, "path", StorageTraversalIssueKind.IoFailure));

    [Fact]
    public void NullIssuePathRejected() => Assert.Throws<ArgumentNullException>("canonicalPath", () => Issue(null!));

    [Fact]
    public void EmptyIssuePathRejected() => Assert.Throws<ArgumentException>("canonicalPath", () => Issue(""));

    [Fact]
    public void WhitespaceIssuePathRejected() => Assert.Throws<ArgumentException>("canonicalPath", () => Issue(" \t"));

    [Fact]
    public void IssuePathRetainedExactly()
    {
        const string path = " mixed/../Path ";
        Assert.Equal(path, Issue(path).CanonicalPath);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void InvalidIssueKindRejected(int value) =>
        Assert.Throws<ArgumentOutOfRangeException>("kind", () => new StorageTraversalIssue(Identity(), "path", (StorageTraversalIssueKind)value));

    [Fact]
    public void StorageTraversalIssuePropertiesImmutable()
    {
        Assert.True(typeof(StorageTraversalIssue).IsSealed);
        var properties = typeof(StorageTraversalIssue).GetProperties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(["CanonicalPath", "Kind", "VolumeIdentity"], properties.Select(p => p.Name));
        Assert.Equal([typeof(string), typeof(StorageTraversalIssueKind), typeof(VolumeIdentity)], properties.Select(p => p.PropertyType));
        Assert.All(properties, p => Assert.False(p.CanWrite));
        Assert.Equal([typeof(VolumeIdentity), typeof(string), typeof(StorageTraversalIssueKind)],
            Assert.Single(typeof(StorageTraversalIssue).GetConstructors()).GetParameters().Select(p => p.ParameterType));
    }

    [Fact]
    public void TraversalIssueSinkIsInterface() => Assert.True(typeof(IStorageTraversalIssueSink).IsInterface);

    [Fact]
    public void TraversalIssueSinkHasExactlyOneMethod() =>
        Assert.Equal("WriteAsync", Assert.Single(typeof(IStorageTraversalIssueSink).GetMethods()).Name);

    [Fact]
    public void TraversalIssueSinkWriteAsyncReturnsValueTask() =>
        Assert.Equal(typeof(ValueTask), Assert.Single(typeof(IStorageTraversalIssueSink).GetMethods()).ReturnType);

    [Fact]
    public void TraversalIssueSinkHasExactParameters()
    {
        var method = Assert.Single(typeof(IStorageTraversalIssueSink).GetMethods());
        Assert.True(method.IsAbstract);
        Assert.Equal([typeof(StorageTraversalIssue), typeof(CancellationToken)], method.GetParameters().Select(p => p.ParameterType));
        Assert.All(method.GetParameters(), p => Assert.False(p.IsOptional));
    }

    [Fact]
    public void TargetChangedExceptionRejectsNullPath() =>
        Assert.Throws<ArgumentNullException>("canonicalPath", () => new StorageTraversalTargetChangedException(null!));

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void TargetChangedExceptionRejectsBlankPath(string path) =>
        Assert.Throws<ArgumentException>("canonicalPath", () => new StorageTraversalTargetChangedException(path));

    [Fact]
    public void TargetChangedExceptionRetainsPath()
    {
        const string path = " mixed/../Path ";
        Assert.Equal(path, new StorageTraversalTargetChangedException(path).CanonicalPath);
        Assert.False(typeof(StorageTraversalTargetChangedException).GetProperty("CanonicalPath")!.CanWrite);
        Assert.True(typeof(StorageTraversalTargetChangedException).IsSealed);
        Assert.Equal(typeof(string), Assert.Single(Assert.Single(typeof(StorageTraversalTargetChangedException).GetConstructors()).GetParameters()).ParameterType);
    }

    [Fact]
    public void TargetChangedExceptionDerivesFromIOException() =>
        Assert.IsAssignableFrom<IOException>(new StorageTraversalTargetChangedException("path"));

    private static VolumeIdentity Identity() => new(Guid.NewGuid());

    private static StorageTraversalIssue Issue(string path) => new(Identity(), path, StorageTraversalIssueKind.IoFailure);
}
