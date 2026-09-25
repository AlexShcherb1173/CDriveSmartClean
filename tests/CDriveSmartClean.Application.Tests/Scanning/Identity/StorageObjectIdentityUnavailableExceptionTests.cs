using CDriveSmartClean.Application.Scanning.Identity;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Identity;

public sealed class StorageObjectIdentityUnavailableExceptionTests
{
    [Fact]
    public void NullPathRejected() => Assert.Throws<ArgumentNullException>("canonicalPath", () =>
        new StorageObjectIdentityUnavailableException(null!));

    [Theory]
    [InlineData("")]
    [InlineData(" \t")]
    public void BlankPathRejected(string path) => Assert.Throws<ArgumentException>("canonicalPath", () =>
        new StorageObjectIdentityUnavailableException(path));

    [Fact]
    public void ExactImmutablePathAndExceptionContract()
    {
        const string path = " mixed/../Path ";
        var exception = new StorageObjectIdentityUnavailableException(path);
        Assert.IsAssignableFrom<IOException>(exception);
        Assert.Equal(path, exception.CanonicalPath);
        var type = exception.GetType();
        Assert.True(type.IsSealed);
        Assert.False(type.GetProperty("CanonicalPath")!.CanWrite);
        Assert.Equal(typeof(string), Assert.Single(Assert.Single(type.GetConstructors()).GetParameters()).ParameterType);
    }
}
