using System.Reflection;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Analysis;

public sealed class UniversalFindingContractsTests
{
    [Fact]
    public void OptionsHaveBoundedDefaults()
    {
        var options = new UniversalFindingOptions();
        Assert.Equal(25, options.MaximumFindingsPerView);
        Assert.Equal(100, options.MaximumTotalFindings);
        Assert.Equal(100_000, options.MaximumHierarchyNodes);
    }

    [Theory]
    [InlineData(0, 100, 100)]
    [InlineData(251, 251, 100)]
    [InlineData(5, 4, 100)]
    [InlineData(101, 100, 100)]
    [InlineData(1, 1001, 100)]
    [InlineData(1, 5, 0)]
    [InlineData(1, 5, 1000001)]
    public void InvalidOptionsAreRejected(int perView, int total, int nodes) =>
        Assert.ThrowsAny<ArgumentException>(() => new UniversalFindingOptions(perView, total, nodes));

    [Fact]
    public void RequestRejectsEmptySessionAndNullInputs()
    {
        (StorageAnalysisRequest analysisRequest, StorageAnalysisResult analysisResult,
            StorageAccountingResult accounting) = Inputs();
        Assert.Throws<ArgumentException>(() =>
            new UniversalFindingRequest(Guid.Empty, analysisRequest, analysisResult, accounting));
        Assert.Throws<ArgumentNullException>(() =>
            new UniversalFindingRequest(Guid.NewGuid(), null!, analysisResult, accounting));
        Assert.Throws<ArgumentNullException>(() =>
            new UniversalFindingRequest(Guid.NewGuid(), analysisRequest, null!, accounting));
        Assert.Throws<ArgumentNullException>(() =>
            new UniversalFindingRequest(Guid.NewGuid(), analysisRequest, analysisResult, null!));
    }

    [Fact]
    public void CrossResultVolumeMismatchRemainsAvailableForBuilderValidation()
    {
        (StorageAnalysisRequest analysisRequest, StorageAnalysisResult analysisResult,
            StorageAccountingResult accounting) = Inputs(
                new VolumeIdentity(Guid.Parse("22222222-2222-2222-2222-222222222222")));
        var request = new UniversalFindingRequest(Guid.NewGuid(), analysisRequest, analysisResult, accounting);
        Assert.NotEqual(request.AnalysisRequest.SystemVolume.VolumeIdentity,
            request.AccountingResult.Reconciliation.StartSnapshot.VolumeIdentity);
    }

    [Fact]
    public void ResultRejectsUnknownReasonBits() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new UniversalFindingResult(
            AnalysisQuality.Complete, (UniversalFindingReason)(1 << 20), AnalysisQuality.Complete,
            AnalysisReason.None, []));

    [Fact]
    public void UnavailableResultCannotPublishFindings() =>
        Assert.Throws<ArgumentException>(() => new UniversalFindingResult(
            AnalysisQuality.Unavailable, UniversalFindingReason.ResourceLimit, AnalysisQuality.Complete,
            AnalysisReason.None, [Finding("path", 1)]));

    [Fact]
    public void ResultCopiesAndDeterministicallyOrdersFindings()
    {
        Finding small = Finding("z", 1);
        Finding large = Finding("a", 2);
        var source = new List<Finding> { small, large };
        var result = new UniversalFindingResult(AnalysisQuality.Complete, UniversalFindingReason.None,
            AnalysisQuality.Complete, AnalysisReason.None, source);
        source.Clear();
        Assert.Equal([large.Id, small.Id], result.Findings.Select(item => item.Id));
    }

    [Fact]
    public void BuilderInterfaceHasExactMethodContract()
    {
        MethodInfo method = Assert.Single(typeof(IUniversalFindingBuilder).GetMethods());
        Assert.Equal("Build", method.Name);
        Assert.Equal(typeof(UniversalFindingResult), method.ReturnType);
        Assert.Equal([typeof(UniversalFindingRequest), typeof(CancellationToken)],
            method.GetParameters().Select(parameter => parameter.ParameterType));
    }

    [Fact]
    public void ExportedTypeCountsRemainExact()
    {
        Assert.Equal(44, typeof(IUniversalFindingBuilder).Assembly.GetExportedTypes().Length);
        Assert.Equal(30, typeof(FindingScope).Assembly.GetExportedTypes().Length);
    }

    private static Finding Finding(string path, long allocated) => new(
        Guid.Parse(allocated == 1
            ? "00000000-0000-0000-0000-000000000001"
            : "00000000-0000-0000-0000-000000000002"),
        Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), path,
        FindingScope.HierarchyArea, FindingCategory.Unknown, [path], null, 1, 0, [],
        new SizeMetrics(null, allocated, null), ReclaimEstimate.Unknown("unknown"),
        new RiskAssessment(RiskLevel.High, Confidence.Verified, ["risk"]),
        Confidence.Unknown, ProtectionState.ReviewRequired,
        [new Evidence("evidence", "evidence", Confidence.Unknown)]);

    private static (StorageAnalysisRequest, StorageAnalysisResult, StorageAccountingResult) Inputs(
        VolumeIdentity? accountingVolume = null)
    {
        var volume = new VolumeIdentity(Guid.Parse("11111111-1111-1111-1111-111111111111"));
        var descriptor = new SystemVolumeDescriptor(volume, @"C:\");
        var context = new StorageClassificationContext(volume, @"C:\Windows", [@"C:\Program Files"],
            @"C:\ProgramData", @"C:\Users\Current", [@"C:\Users\Current\AppData\Local"],
            @"C:\Users\Public", @"C:\Users", []);
        var request = new StorageAnalysisRequest(descriptor, context);
        var summary = new StorageAccountingSummary(null, AccountingReason.ResourceLimit);
        VolumeSpaceSnapshot snapshot = VolumeSpaceSnapshot.Unavailable(
            accountingVolume ?? volume, DateTimeOffset.UnixEpoch, VolumeSpaceFailure.VolumeUnavailable);
        var accounting = new StorageAccountingResult(summary, null, [],
            new VolumeReconciliation(snapshot, snapshot, summary, true), true,
            new Dictionary<StorageTraversalIssueKind, long>());
        var analysis = new StorageAnalysisResult(AnalysisQuality.Unavailable,
            AnalysisReason.UpstreamAccountingUnavailable, AccountingQuality.Unavailable,
            AccountingReason.ResourceLimit, [], [], [], [], []);
        return (request, analysis, accounting);
    }
}
