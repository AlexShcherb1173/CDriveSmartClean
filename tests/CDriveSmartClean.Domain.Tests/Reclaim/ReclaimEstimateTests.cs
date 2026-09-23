using CDriveSmartClean.Domain.Reclaim;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Reclaim;

public sealed class ReclaimEstimateTests
{
    [Fact]
    public void ExactProducesVerifiedEqualValuesAndRetainsFlags()
    {
        ReclaimEstimate estimate = ReclaimEstimate.Exact(42, "  measured  ", true, true);

        Assert.Equal(ReclaimKind.Exact, estimate.Kind);
        Assert.Equal(42, estimate.MinimumBytes);
        Assert.Equal(42, estimate.ExpectedBytes);
        Assert.Equal(42, estimate.MaximumBytes);
        Assert.Equal("measured", estimate.Basis);
        Assert.Equal(Confidence.Verified, estimate.Confidence);
        Assert.True(estimate.RequiresRestart);
        Assert.True(estimate.RequiresCompaction);
    }

    [Fact]
    public void ExactRejectsNegativeBytes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ReclaimEstimate.Exact(-1, "basis"));
    }

    [Fact]
    public void UnknownHasNoByteValuesAndUnknownConfidence()
    {
        ReclaimEstimate estimate = ReclaimEstimate.Unknown("unknown source");

        Assert.Equal(ReclaimKind.Unknown, estimate.Kind);
        Assert.Null(estimate.MinimumBytes);
        Assert.Null(estimate.ExpectedBytes);
        Assert.Null(estimate.MaximumBytes);
        Assert.Equal(Confidence.Unknown, estimate.Confidence);
        Assert.False(estimate.RequiresRestart);
        Assert.False(estimate.RequiresCompaction);
    }

    [Fact]
    public void NoneHasZeroByteValuesAndVerifiedConfidence()
    {
        ReclaimEstimate estimate = ReclaimEstimate.None("not reclaimable");

        Assert.Equal(ReclaimKind.None, estimate.Kind);
        Assert.Equal(0, estimate.MinimumBytes);
        Assert.Equal(0, estimate.ExpectedBytes);
        Assert.Equal(0, estimate.MaximumBytes);
        Assert.Equal(Confidence.Verified, estimate.Confidence);
    }

    [Fact]
    public void BoundedFactoriesAcceptValidRanges()
    {
        ReclaimEstimate estimated = ReclaimEstimate.Estimated(1, 2, 3, Confidence.Medium, "basis");
        ReclaimEstimate conditional = ReclaimEstimate.Conditional(
            2,
            3,
            4,
            Confidence.High,
            "basis",
            ["condition"]);
        ReclaimEstimate userDecision = ReclaimEstimate.UserDecision(
            3,
            4,
            5,
            Confidence.Low,
            "basis");

        Assert.Equal(ReclaimKind.Estimated, estimated.Kind);
        Assert.Equal(ReclaimKind.Conditional, conditional.Kind);
        Assert.Equal(ReclaimKind.UserDecision, userDecision.Kind);
    }

    [Theory]
    [InlineData(2, 1, 3)]
    [InlineData(1, 3, 2)]
    [InlineData(-1, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, 0, -1)]
    public void InvalidBoundedRangesAreRejected(long minimum, long expected, long maximum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReclaimEstimate.Estimated(
                minimum,
                expected,
                maximum,
                Confidence.Medium,
                "basis"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void InvalidBoundedConfidenceIsRejected(int confidence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReclaimEstimate.Estimated(1, 2, 3, (Confidence)confidence, "basis"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidBasisIsRejected(string? basis)
    {
        Assert.ThrowsAny<ArgumentException>(() => ReclaimEstimate.None(basis!));
    }

    [Fact]
    public void ConditionalRequiresAPrecondition()
    {
        Assert.Throws<ArgumentException>(
            () => ReclaimEstimate.Conditional(
                1,
                2,
                3,
                Confidence.High,
                "basis",
                Array.Empty<string>()));
    }

    [Fact]
    public void PreconditionsAreNormalizedDeduplicatedAndDefensivelyCopied()
    {
        var source = new List<string> { "  first  ", "second", "first" };
        ReclaimEstimate estimate = ReclaimEstimate.Conditional(
            1,
            2,
            3,
            Confidence.High,
            "basis",
            source);

        source.Add("third");

        Assert.Equal(["first", "second"], estimate.Preconditions);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void InvalidPreconditionItemIsRejected(string? value)
    {
        Assert.Throws<ArgumentException>(
            () => ReclaimEstimate.Conditional(
                1,
                2,
                3,
                Confidence.High,
                "basis",
                [value!]));
    }

    [Fact]
    public void ReclaimKindDefaultIsUnknown()
    {
        Assert.Equal(ReclaimKind.Unknown, default);
    }

    [Fact]
    public void DefaultOverlapGroupsAreEmpty()
    {
        Assert.Empty(ReclaimEstimate.Exact(1, "basis").OverlapGroupIds);
    }

    [Fact]
    public void OverlapGroupsAreDeduplicatedInStableOrderAndDefensivelyCopied()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var source = new List<Guid> { first, second, first };
        ReclaimEstimate estimate = ReclaimEstimate.Exact(
            1,
            "basis",
            overlapGroupIds: source);

        source.Add(Guid.NewGuid());

        Assert.Equal([first, second], estimate.OverlapGroupIds);
    }

    [Fact]
    public void EmptyOverlapGroupIdIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => ReclaimEstimate.Exact(1, "basis", overlapGroupIds: [Guid.Empty]));
    }

    [Fact]
    public void BoundedFactoriesRetainOverlapGroups()
    {
        Guid estimatedId = Guid.NewGuid();
        Guid conditionalId = Guid.NewGuid();
        Guid userDecisionId = Guid.NewGuid();

        ReclaimEstimate estimated = ReclaimEstimate.Estimated(
            1,
            2,
            3,
            Confidence.High,
            "basis",
            overlapGroupIds: [estimatedId]);
        ReclaimEstimate conditional = ReclaimEstimate.Conditional(
            1,
            2,
            3,
            Confidence.High,
            "basis",
            ["condition"],
            overlapGroupIds: [conditionalId]);
        ReclaimEstimate userDecision = ReclaimEstimate.UserDecision(
            1,
            2,
            3,
            Confidence.High,
            "basis",
            overlapGroupIds: [userDecisionId]);

        Assert.Equal([estimatedId], estimated.OverlapGroupIds);
        Assert.Equal([conditionalId], conditional.OverlapGroupIds);
        Assert.Equal([userDecisionId], userDecision.OverlapGroupIds);
    }

    [Fact]
    public void UnknownAndNoneRetainOverlapGroupsWithoutInventingBytes()
    {
        Guid unknownId = Guid.NewGuid();
        Guid noneId = Guid.NewGuid();
        ReclaimEstimate unknown = ReclaimEstimate.Unknown("basis", [unknownId]);
        ReclaimEstimate none = ReclaimEstimate.None("basis", [noneId]);

        Assert.Equal([unknownId], unknown.OverlapGroupIds);
        Assert.Null(unknown.MaximumBytes);
        Assert.Equal([noneId], none.OverlapGroupIds);
        Assert.Equal(0, none.MaximumBytes);
    }
}
