using CDriveSmartClean.Domain.Findings;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Findings;

public sealed class EvidenceTests
{
    [Fact]
    public void ValidEvidenceIsAcceptedAndNormalized()
    {
        var evidence = new Evidence("  code  ", "  description  ", Confidence.High);

        Assert.Equal("code", evidence.Code);
        Assert.Equal("description", evidence.Description);
        Assert.Equal(Confidence.High, evidence.Confidence);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidCodeIsRejected(string? code)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new Evidence(code!, "description", Confidence.High));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidDescriptionIsRejected(string? description)
    {
        Assert.ThrowsAny<ArgumentException>(
            () => new Evidence("code", description!, Confidence.High));
    }

    [Fact]
    public void UndefinedConfidenceIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Evidence("code", "description", (Confidence)99));
    }

    [Fact]
    public void ConfidenceDefaultIsUnknown()
    {
        Assert.Equal(Confidence.Unknown, default);
    }
}
