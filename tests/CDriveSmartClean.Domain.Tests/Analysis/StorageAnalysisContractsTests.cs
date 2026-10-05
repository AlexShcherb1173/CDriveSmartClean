using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Domain.Tests.Analysis;

public sealed class StorageAnalysisContractsTests
{
    private static readonly VolumeIdentity Volume = new(new Guid("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void SafeDefaultsAndFlagsAreStable()
    {
        Assert.Equal(AnalysisQuality.Unavailable, default);
        AnalysisReason reasons = AnalysisReason.IdentityUnavailable | AnalysisReason.MeasurementUnavailable;
        Assert.True(reasons.HasFlag(AnalysisReason.IdentityUnavailable));
        Assert.True(reasons.HasFlag(AnalysisReason.MeasurementUnavailable));
    }

    [Fact]
    public void SizeEvidenceUsesNullForUnavailableAndAcceptsMeasuredZero()
    {
        var unavailable = new AnalysisSizeEvidence(null, null, null, null);
        Assert.Null(unavailable.ObservedAttributedAllocatedBytes);
        Assert.Equal(0, new AnalysisSizeEvidence(0, 0, 0, 0).RawReportedAllocatedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new AnalysisSizeEvidence(-1, null, null, null));
    }

    [Fact]
    public void CandidateCopiesSortsAndDeduplicates()
    {
        var paths = new List<string> { "b", "a", "a" };
        var facets = new List<FindingFacet> { FindingFacet.Sparse, FindingFacet.Compressed, FindingFacet.Sparse };
        var evidence = new List<Evidence>
        {
            new("z", "z", Confidence.High),
            new("a", "first", Confidence.Verified),
            new("a", "first", Confidence.Verified),
        };
        var candidate = new StorageAnalysisCandidate(AnalysisCandidateScope.HierarchyNode, FindingCategory.Unknown,
            facets, Confidence.Unknown, evidence, new(null, null, null, null), paths, null, 0, 1);
        paths.Clear();
        facets.Clear();
        evidence.Clear();
        Assert.Equal(["a", "b"], candidate.RelativePaths);
        Assert.Equal([FindingFacet.Compressed, FindingFacet.Sparse], candidate.Facets);
        Assert.Equal(["a", "z"], candidate.Evidence.Select(item => item.Code));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)candidate.RelativePaths).Add("c"));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CandidateRejectsConflictingSameCodeEvidenceRegardlessOfOrder(
        bool differentDescription, bool reverse)
    {
        var first = new Evidence("stable.code", "description", Confidence.Verified);
        var second = new Evidence("stable.code",
            differentDescription ? "other description" : "description",
            differentDescription ? Confidence.Verified : Confidence.Low);
        Evidence[] evidence = reverse ? [second, first] : [first, second];

        Assert.Throws<ArgumentException>(() => new StorageAnalysisCandidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.Unknown, [], Confidence.Unknown,
            evidence, new(null, null, null, null), ["a"], null, 0, 1));
    }

    [Theory]
    [InlineData(AnalysisCandidateScope.IdentityGroup)]
    [InlineData(AnalysisCandidateScope.File)]
    public void IdentityScopesRequireIdentity(AnalysisCandidateScope scope) =>
        Assert.Throws<ArgumentException>(() => new StorageAnalysisCandidate(scope, FindingCategory.Unknown,
            [], Confidence.Unknown, [], new(null, null, null, null), ["a"], null, 0, 0));

    [Fact]
    public void FileScopeRequiresExactlyOnePath()
    {
        var identity = new StorageObjectIdentity(Volume, Guid.NewGuid());
        Assert.Throws<ArgumentException>(() => new StorageAnalysisCandidate(AnalysisCandidateScope.File,
            FindingCategory.Unknown, [], Confidence.Unknown, [], new(null, null, null, null),
            ["a", "b"], identity, 1, 0));
    }

    [Fact]
    public void CandidateRejectsInvalidEnumsAndCounts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAnalysisCandidate((AnalysisCandidateScope)0,
            FindingCategory.Unknown, [], Confidence.Unknown, [], new(null, null, null, null), ["a"], null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAnalysisCandidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.Unknown, [(FindingFacet)0],
            Confidence.Unknown, [], new(null, null, null, null), ["a"], null, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new StorageAnalysisCandidate(
            AnalysisCandidateScope.HierarchyNode, FindingCategory.Unknown, [], Confidence.Unknown,
            [], new(null, null, null, null), ["a"], null, -1, 0));
    }

    [Fact]
    public void CategorySummaryPreservesNullablePhysicalAllocation()
    {
        var summary = new CategorySummary(FindingCategory.Unknown, null, 0, 0, 0, 0,
            AnalysisQuality.Incomplete, AnalysisReason.UpstreamAccountingUnavailable);
        Assert.Null(summary.DeduplicatedObservedAllocatedBytes);
        Assert.Throws<ArgumentOutOfRangeException>(() => new CategorySummary(FindingCategory.Unknown,
            -1, 0, 0, 0, 0, AnalysisQuality.Complete, AnalysisReason.None));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CategorySummary(FindingCategory.Unknown,
            0, 0, 0, 0, 0, AnalysisQuality.Complete, (AnalysisReason)(1 << 30)));
    }

    [Fact]
    public void StagedCandidateHasNoLaterStageProperties()
    {
        string[] forbidden = ["Reclaim", "Risk", "Protection", "Exclusive", "Cleanup"];
        Assert.DoesNotContain(typeof(StorageAnalysisCandidate).GetProperties(),
            property => forbidden.Any(term => property.Name.Contains(term, StringComparison.Ordinal)));
    }
}
