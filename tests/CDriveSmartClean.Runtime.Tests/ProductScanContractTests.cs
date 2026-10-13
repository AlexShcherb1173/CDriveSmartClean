using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Runtime;
using Xunit;

namespace CDriveSmartClean.Runtime.Tests;

public sealed class ProductScanContractTests
{
    [Fact]
    public void RuntimeExportsExactlyFiveRequiredTypes()
    {
        Type[] types = typeof(SystemVolumeScanWorkflow).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "CDriveSmartClean.Runtime")
            .OrderBy(type => type.Name, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal([
            "ProductScanPhase",
            "ProductScanProgress",
            "ProductScanRequest",
            "ProductScanResult",
            "SystemVolumeScanWorkflow",
        ], types.Select(type => type.Name));
        Assert.Equal(5, types.Length);
    }

    [Fact]
    public void RequestIsImmutableSystemVolumeOnlyAndUsesDefaults()
    {
        var request = new ProductScanRequest(Guid.NewGuid());
        Assert.NotNull(request.AccountingOptions);
        Assert.NotNull(request.AnalysisOptions);
        Assert.NotNull(request.FindingOptions);
        Assert.All(typeof(ProductScanRequest).GetProperties(), property => Assert.Null(property.SetMethod));
        string[] names = typeof(ProductScanRequest).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain(names, name => name.Contains("Path", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Drive", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("Cleanup", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, name => name.Contains("AI", StringComparison.OrdinalIgnoreCase));
        Assert.Throws<ArgumentException>(() => new ProductScanRequest(Guid.Empty));
    }

    [Fact]
    public void RequestPreservesExplicitPerCallOptions()
    {
        var accounting = new StorageAccountingOptions(2, 3, 4, 5);
        var analysis = new StorageAnalysisOptions(2, 3, 4, 5);
        var findings = new UniversalFindingOptions(5, 6, 7);
        var request = new ProductScanRequest(Guid.NewGuid(), accounting, analysis, findings);
        Assert.Same(accounting, request.AccountingOptions);
        Assert.Same(analysis, request.AnalysisOptions);
        Assert.Same(findings, request.FindingOptions);
    }

    [Fact]
    public void ProgressValidatesExternalStateAndHasNoSpeculativeMetrics()
    {
        Guid session = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => new ProductScanProgress(Guid.Empty,
            ProductScanPhase.Completed, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductScanProgress(session,
            (ProductScanPhase)99, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductScanProgress(session,
            ProductScanPhase.Completed, -1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductScanProgress(session,
            ProductScanPhase.Completed, 0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProductScanProgress(session,
            ProductScanPhase.Completed, 0, 0, -1));
        string[] names = typeof(ProductScanProgress).GetProperties().Select(property => property.Name).ToArray();
        Assert.DoesNotContain("Percentage", names);
        Assert.DoesNotContain("ETA", names);
        Assert.DoesNotContain("TotalObjects", names);
        Assert.DoesNotContain("RemainingBytes", names);
        Assert.DoesNotContain("ReclaimableProgress", names);
        Assert.All(typeof(ProductScanProgress).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void ResultDefensivelyCopiesCollectionsAndExposesAuthoritativeValues()
    {
        Guid session = Guid.NewGuid();
        var volume = new VolumeIdentity(Guid.NewGuid());
        var descriptor = new SystemVolumeDescriptor(volume, @"C:\");
        var aggregate = new StorageAggregate();
        var summary = new StorageAccountingSummary(aggregate, AccountingReason.None);
        var root = new StorageHierarchyNode(string.Empty, aggregate, []);
        var snapshot = VolumeSpaceSnapshot.Available(volume, DateTimeOffset.UnixEpoch,
            100, 100, 100, 100, 0, 0, 0);
        var sourceCounts = new Dictionary<StorageTraversalIssueKind, long>
        {
            [StorageTraversalIssueKind.Inaccessible] = 2,
        };
        var accounting = new StorageAccountingResult(summary, root, [],
            new VolumeReconciliation(snapshot, snapshot, summary, true), true, sourceCounts);
        CategorySummary[] categories = Enum.GetValues<FindingCategory>().Select(category =>
            new CategorySummary(category, 0, 0, 0, 0, 0,
                AnalysisQuality.Complete, AnalysisReason.None)).ToArray();
        var analysis = new StorageAnalysisResult(AnalysisQuality.Complete, AnalysisReason.None,
            AccountingQuality.Complete, AccountingReason.None, categories, [], [], [], []);
        var findings = new UniversalFindingResult(AnalysisQuality.Complete, UniversalFindingReason.None,
            AnalysisQuality.Complete, AnalysisReason.None, []);

        var result = new ProductScanResult(session, descriptor, accounting, analysis, findings);
        sourceCounts[StorageTraversalIssueKind.Inaccessible] = 99;

        Assert.Equal(2, result.IssueCounts[StorageTraversalIssueKind.Inaccessible]);
        Assert.NotSame(accounting.IssueCounts, result.IssueCounts);
        Assert.NotSame(findings.Findings, result.Findings);
        Assert.Same(snapshot, result.StartSnapshot);
        Assert.Same(snapshot, result.EndSnapshot);
        Assert.Equal(AccountingQuality.Complete, result.AccountingQuality);
        Assert.Equal(AccountingQuality.Complete, result.ReconciliationQuality);
        Assert.True(result.TraversalCompleted);
        Assert.All(typeof(ProductScanResult).GetProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void AllProductContractsAreSealedAndScannerIsNotImplemented()
    {
        Assert.True(typeof(SystemVolumeScanWorkflow).IsSealed);
        Assert.True(typeof(ProductScanRequest).IsSealed);
        Assert.True(typeof(ProductScanProgress).IsSealed);
        Assert.True(typeof(ProductScanResult).IsSealed);
        Assert.DoesNotContain(typeof(SystemVolumeScanWorkflow).Assembly.GetTypes(),
            type => type.GetInterfaces().Any(contract => contract.Name == "IStorageScanner"));
    }

    [Fact]
    public void AccountingPathLimitDiagnosticReachesProductResultUnchanged()
    {
        var diagnostic = new ResourceLimitDiagnostic(ResourceLimitStage.Accounting,
            ResourceLimitDimension.MaximumDistinctPaths, 2, 3);
        Assert.Same(diagnostic, Result(accountingDiagnostic: diagnostic).ResourceLimitDiagnostic);
    }

    [Fact]
    public void AccountingBudgetDiagnosticWinsOverDownstreamDirectDiagnostics()
    {
        var accounting = new ResourceLimitDiagnostic(ResourceLimitStage.Accounting,
            ResourceLimitDimension.AccountingStateBudget, 100, 101);
        var analysis = new ResourceLimitDiagnostic(ResourceLimitStage.Analysis,
            ResourceLimitDimension.MaximumPathStates, 2, 3);
        var findings = new ResourceLimitDiagnostic(ResourceLimitStage.Findings,
            ResourceLimitDimension.MaximumHierarchyNodes, 2, 3);
        Assert.Same(accounting, Result(accounting, analysis, findings).ResourceLimitDiagnostic);
    }

    [Fact]
    public void AnalysisPathLimitDiagnosticReachesProductResult()
    {
        var diagnostic = new ResourceLimitDiagnostic(ResourceLimitStage.Analysis,
            ResourceLimitDimension.MaximumPathStates, 2, 3);
        Assert.Same(diagnostic, Result(analysisDiagnostic: diagnostic).ResourceLimitDiagnostic);
    }

    [Fact]
    public void AnalysisBudgetDiagnosticReachesProductResult()
    {
        var diagnostic = new ResourceLimitDiagnostic(ResourceLimitStage.Analysis,
            ResourceLimitDimension.AnalysisStateBudget, 100, 101);
        Assert.Same(diagnostic, Result(analysisDiagnostic: diagnostic).ResourceLimitDiagnostic);
    }

    [Fact]
    public void FindingHierarchyLimitDiagnosticReachesProductResult()
    {
        var diagnostic = new ResourceLimitDiagnostic(ResourceLimitStage.Findings,
            ResourceLimitDimension.MaximumHierarchyNodes, 2, 3);
        Assert.Same(diagnostic, Result(findingDiagnostic: diagnostic).ResourceLimitDiagnostic);
    }

    [Fact]
    public void ProductResultHasNoDiagnosticWithoutDirectResourceLimit()
    {
        Assert.Null(Result().ResourceLimitDiagnostic);
    }

    private static ProductScanResult Result(
        ResourceLimitDiagnostic? accountingDiagnostic = null,
        ResourceLimitDiagnostic? analysisDiagnostic = null,
        ResourceLimitDiagnostic? findingDiagnostic = null)
    {
        Guid session = Guid.NewGuid();
        var volume = new VolumeIdentity(Guid.NewGuid());
        var descriptor = new SystemVolumeDescriptor(volume, @"C:\");
        var aggregate = new StorageAggregate();
        bool accountingUnavailable = accountingDiagnostic is not null;
        var summary = new StorageAccountingSummary(
            accountingUnavailable ? null : aggregate,
            accountingUnavailable ? AccountingReason.ResourceLimit : AccountingReason.None);
        StorageHierarchyNode? root = accountingUnavailable
            ? null
            : new StorageHierarchyNode(string.Empty, aggregate, []);
        CompactAccountingSnapshot? compactSnapshot = accountingUnavailable
            ? null
            : new CompactAccountingSnapshot([], [], [], [], CancellationToken.None);
        VolumeSpaceSnapshot space = VolumeSpaceSnapshot.Available(
            volume, DateTimeOffset.UnixEpoch, 100, 100, 100, 100, 0, 0, 0);
        var accounting = new StorageAccountingResult(summary, root, compactSnapshot,
            new VolumeReconciliation(space, space, summary, true), true,
            new Dictionary<StorageTraversalIssueKind, long>(), accountingDiagnostic);

        bool analysisUnavailable = accountingUnavailable || analysisDiagnostic is not null;
        AnalysisReason analysisReasons = analysisDiagnostic is not null
            ? AnalysisReason.ResourceLimit
            : accountingUnavailable
                ? AnalysisReason.UpstreamAccountingUnavailable
                : AnalysisReason.None;
        CategorySummary[] categories = analysisUnavailable
            ? []
            : Enum.GetValues<FindingCategory>().Select(category => new CategorySummary(
                category, 0, 0, 0, 0, 0, AnalysisQuality.Complete, AnalysisReason.None)).ToArray();
        var analysis = new StorageAnalysisResult(
            analysisUnavailable ? AnalysisQuality.Unavailable : AnalysisQuality.Complete,
            analysisReasons, summary.Quality, summary.Reasons, categories, [], [], [], [],
            analysisDiagnostic);

        bool findingsUnavailable = analysisUnavailable || findingDiagnostic is not null;
        UniversalFindingReason findingReasons = findingDiagnostic is not null
            ? UniversalFindingReason.ResourceLimit
            : analysisUnavailable
                ? UniversalFindingReason.UpstreamAnalysisUnavailable
                : UniversalFindingReason.None;
        var findings = new UniversalFindingResult(
            findingsUnavailable ? AnalysisQuality.Unavailable : AnalysisQuality.Complete,
            findingReasons, analysis.Quality, analysis.Reasons, [], findingDiagnostic);
        return new ProductScanResult(session, descriptor, accounting, analysis, findings);
    }
}
