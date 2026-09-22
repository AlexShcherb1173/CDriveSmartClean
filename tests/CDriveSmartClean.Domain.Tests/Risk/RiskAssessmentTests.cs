using CDriveSmartClean.Domain.Risk;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Risk;

public sealed class RiskAssessmentTests
{
    [Fact]
    public void ValidAssessmentNormalizesAndDeduplicatesReasons()
    {
        var assessment = new RiskAssessment(
            RiskLevel.Medium,
            Confidence.High,
            ["  first  ", "second", "first"]);

        Assert.Equal(RiskLevel.Medium, assessment.Level);
        Assert.Equal(Confidence.High, assessment.Confidence);
        Assert.Equal(["first", "second"], assessment.Reasons);
    }

    [Fact]
    public void SourceMutationDoesNotAffectAssessment()
    {
        var source = new List<string> { "reason" };
        var assessment = new RiskAssessment(RiskLevel.Low, Confidence.Medium, source);

        source.Add("later");

        Assert.Equal(["reason"], assessment.Reasons);
    }

    [Fact]
    public void NullReasonsAreRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => new RiskAssessment(RiskLevel.Low, Confidence.Medium, null!));
    }

    [Fact]
    public void EmptyReasonsAreRejected()
    {
        Assert.Throws<ArgumentException>(
            () => new RiskAssessment(RiskLevel.Low, Confidence.Medium, Array.Empty<string>()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidReasonItemsAreRejected(string? reason)
    {
        Assert.Throws<ArgumentException>(
            () => new RiskAssessment(RiskLevel.Low, Confidence.Medium, [reason!]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void UndefinedRiskLevelIsRejected(int level)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RiskAssessment((RiskLevel)level, Confidence.Medium, ["reason"]));
    }

    [Fact]
    public void UndefinedConfidenceIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RiskAssessment(RiskLevel.Low, (Confidence)99, ["reason"]));
    }

    [Fact]
    public void DefaultRiskLevelIsNotAccepted()
    {
        Assert.False(Enum.IsDefined(default(RiskLevel)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new RiskAssessment(default, Confidence.Medium, ["reason"]));
    }
}
