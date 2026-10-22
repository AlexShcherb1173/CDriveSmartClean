using System.Globalization;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Desktop.Resources;
using CDriveSmartClean.Desktop.ViewModels;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Runtime;

namespace CDriveSmartClean.Desktop.Presentation;

internal sealed class PresentationLocalizer
{
    private static readonly (AccountingReason Flag, string Key)[] AccountingReasonMappings =
    [
        (AccountingReason.TraversalCoverageGap, nameof(Strings.ReasonTraversalCoverageGap)),
        (AccountingReason.IdentityUnavailable, nameof(Strings.ReasonIdentityUnavailable)),
        (AccountingReason.MeasurementUnavailable, nameof(Strings.ReasonMeasurementUnavailable)),
        (AccountingReason.ConflictingIdentityEvidence, nameof(Strings.ReasonConflictingIdentityEvidence)),
        (AccountingReason.ConflictingPathEvidence, nameof(Strings.ReasonConflictingPathEvidence)),
        (AccountingReason.UnsupportedAllocationEvidence, nameof(Strings.ReasonUnsupportedAllocationEvidence)),
        (AccountingReason.ResourceLimit, nameof(Strings.ReasonResourceLimit)),
        (AccountingReason.ArithmeticOverflow, nameof(Strings.ReasonArithmeticOverflow)),
        (AccountingReason.VolumeSnapshotUnavailable, nameof(Strings.ReasonVolumeSnapshotUnavailable)),
        (AccountingReason.VolumeDrift, nameof(Strings.ReasonVolumeDrift)),
    ];

    private static readonly (AnalysisReason Flag, string Key)[] AnalysisReasonMappings =
    [
        (AnalysisReason.UpstreamAccountingUnavailable, nameof(Strings.ReasonUpstreamAccountingUnavailable)),
        (AnalysisReason.UpstreamAccountingIncomplete, nameof(Strings.ReasonUpstreamAccountingIncomplete)),
        (AnalysisReason.UpstreamReconciliationInconsistent, nameof(Strings.ReasonUpstreamReconciliationInconsistent)),
        (AnalysisReason.ClassificationContextIncomplete, nameof(Strings.ReasonClassificationContextIncomplete)),
        (AnalysisReason.ClassificationRuleConflict, nameof(Strings.ReasonClassificationRuleConflict)),
        (AnalysisReason.CrossCategoryIdentityConflict, nameof(Strings.ReasonCrossCategoryIdentityConflict)),
        (AnalysisReason.IdentityUnavailable, nameof(Strings.ReasonIdentityUnavailable)),
        (AnalysisReason.MeasurementUnavailable, nameof(Strings.ReasonMeasurementUnavailable)),
        (AnalysisReason.ConflictingIdentityEvidence, nameof(Strings.ReasonConflictingIdentityEvidence)),
        (AnalysisReason.ConflictingPathEvidence, nameof(Strings.ReasonConflictingPathEvidence)),
        (AnalysisReason.UnsupportedAllocationEvidence, nameof(Strings.ReasonUnsupportedAllocationEvidence)),
        (AnalysisReason.ResourceLimit, nameof(Strings.ReasonResourceLimit)),
        (AnalysisReason.ArithmeticOverflow, nameof(Strings.ReasonArithmeticOverflow)),
        (AnalysisReason.AccountingMismatch, nameof(Strings.ReasonAccountingMismatch)),
    ];

    private static readonly (UniversalFindingReason Flag, string Key)[] UniversalFindingReasonMappings =
    [
        (UniversalFindingReason.UpstreamAnalysisIncomplete, nameof(Strings.ReasonUpstreamAnalysisIncomplete)),
        (UniversalFindingReason.UpstreamAnalysisUnavailable, nameof(Strings.ReasonUpstreamAnalysisUnavailable)),
        (UniversalFindingReason.UpstreamAccountingUnavailable, nameof(Strings.ReasonUpstreamAccountingUnavailable)),
        (UniversalFindingReason.UpstreamAccountingIncomplete, nameof(Strings.ReasonUpstreamAccountingIncomplete)),
        (UniversalFindingReason.UpstreamReconciliationInconsistent, nameof(Strings.ReasonUpstreamReconciliationInconsistent)),
        (UniversalFindingReason.VolumeCapacityUnavailable, nameof(Strings.ReasonVolumeCapacityUnavailable)),
        (UniversalFindingReason.InputMismatch, nameof(Strings.ReasonInputMismatch)),
        (UniversalFindingReason.ResourceLimit, nameof(Strings.ReasonResourceLimit)),
        (UniversalFindingReason.ArithmeticOverflow, nameof(Strings.ReasonArithmeticOverflow)),
    ];

    private readonly CultureInfo culture;

    internal PresentationLocalizer(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        this.culture = culture;
    }

    internal string Get(string key) => Strings.ResourceManager.GetString(key, culture)
        ?? Strings.ResourceManager.GetString(key, CultureInfo.GetCultureInfo("en"))
        ?? Strings.Unavailable;

    internal string Localize(DesktopScanState value) => Get(value switch
    {
        DesktopScanState.Idle => nameof(Strings.DesktopScanStateIdle),
        DesktopScanState.Scanning => nameof(Strings.DesktopScanStateScanning),
        DesktopScanState.Cancelling => nameof(Strings.DesktopScanStateCancelling),
        DesktopScanState.Completed => nameof(Strings.DesktopScanStateCompleted),
        DesktopScanState.Cancelled => nameof(Strings.DesktopScanStateCancelled),
        DesktopScanState.Failed => nameof(Strings.DesktopScanStateFailed),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(ProductScanPhase value) => Get(value switch
    {
        ProductScanPhase.DiscoveringSystemVolume => nameof(Strings.ProductScanPhaseDiscoveringSystemVolume),
        ProductScanPhase.ResolvingClassificationContext => nameof(Strings.ProductScanPhaseResolvingClassificationContext),
        ProductScanPhase.TraversingAndAccounting => nameof(Strings.ProductScanPhaseTraversingAndAccounting),
        ProductScanPhase.CompletingAnalysis => nameof(Strings.ProductScanPhaseCompletingAnalysis),
        ProductScanPhase.BuildingFindings => nameof(Strings.ProductScanPhaseBuildingFindings),
        ProductScanPhase.Completed => nameof(Strings.ProductScanPhaseCompleted),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(FindingCategory value) => Get(value switch
    {
        FindingCategory.Unknown => nameof(Strings.FindingCategoryUnknown),
        FindingCategory.System => nameof(Strings.FindingCategorySystem),
        FindingCategory.Application => nameof(Strings.FindingCategoryApplication),
        FindingCategory.ApplicationData => nameof(Strings.FindingCategoryApplicationData),
        FindingCategory.UserData => nameof(Strings.FindingCategoryUserData),
        FindingCategory.Temporary => nameof(Strings.FindingCategoryTemporary),
        FindingCategory.Cache => nameof(Strings.FindingCategoryCache),
        FindingCategory.LogDiagnostic => nameof(Strings.FindingCategoryLogDiagnostic),
        FindingCategory.InstallerArchive => nameof(Strings.FindingCategoryInstallerArchive),
        FindingCategory.BackupSnapshot => nameof(Strings.FindingCategoryBackupSnapshot),
        FindingCategory.VirtualStorage => nameof(Strings.FindingCategoryVirtualStorage),
        FindingCategory.CloudBacked => nameof(Strings.FindingCategoryCloudBacked),
        FindingCategory.RecycleBin => nameof(Strings.FindingCategoryRecycleBin),
        FindingCategory.FilesystemOverhead => nameof(Strings.FindingCategoryFilesystemOverhead),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(FindingScope value) => Get(value switch
    {
        FindingScope.CategoryAggregate => nameof(Strings.FindingScopeCategoryAggregate),
        FindingScope.HierarchyArea => nameof(Strings.FindingScopeHierarchyArea),
        FindingScope.IdentityGroup => nameof(Strings.FindingScopeIdentityGroup),
        FindingScope.File => nameof(Strings.FindingScopeFile),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(RiskLevel value) => Get(value switch
    {
        RiskLevel.Safe => nameof(Strings.RiskLevelSafe),
        RiskLevel.Low => nameof(Strings.RiskLevelLow),
        RiskLevel.Medium => nameof(Strings.RiskLevelMedium),
        RiskLevel.High => nameof(Strings.RiskLevelHigh),
        RiskLevel.Critical => nameof(Strings.RiskLevelCritical),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(Confidence value) => Get(value switch
    {
        Confidence.Unknown => nameof(Strings.ConfidenceUnknown),
        Confidence.Low => nameof(Strings.ConfidenceLow),
        Confidence.Medium => nameof(Strings.ConfidenceMedium),
        Confidence.High => nameof(Strings.ConfidenceHigh),
        Confidence.Verified => nameof(Strings.ConfidenceVerified),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(ProtectionState value) => Get(value switch
    {
        ProtectionState.Blocked => nameof(Strings.ProtectionStateBlocked),
        ProtectionState.Normal => nameof(Strings.ProtectionStateNormal),
        ProtectionState.ReviewRequired => nameof(Strings.ProtectionStateReviewRequired),
        ProtectionState.Protected => nameof(Strings.ProtectionStateProtected),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(StorageTraversalIssueKind value) => Get(value switch
    {
        StorageTraversalIssueKind.Inaccessible => nameof(Strings.StorageTraversalIssueKindInaccessible),
        StorageTraversalIssueKind.Disappeared => nameof(Strings.StorageTraversalIssueKindDisappeared),
        StorageTraversalIssueKind.TargetChanged => nameof(Strings.StorageTraversalIssueKindTargetChanged),
        StorageTraversalIssueKind.IoFailure => nameof(Strings.StorageTraversalIssueKindIoFailure),
        StorageTraversalIssueKind.IdentityUnavailable => nameof(Strings.StorageTraversalIssueKindIdentityUnavailable),
        StorageTraversalIssueKind.RecallSensitive => nameof(Strings.StorageTraversalIssueKindRecallSensitive),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(AccountingQuality value) => Get(value switch
    {
        AccountingQuality.Complete => nameof(Strings.AccountingQualityComplete),
        AccountingQuality.Incomplete => nameof(Strings.AccountingQualityIncomplete),
        AccountingQuality.Inconsistent => nameof(Strings.AccountingQualityInconsistent),
        AccountingQuality.Unavailable => nameof(Strings.AccountingQualityUnavailable),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(AnalysisQuality value) => Get(value switch
    {
        AnalysisQuality.Unavailable => nameof(Strings.AnalysisQualityUnavailable),
        AnalysisQuality.Incomplete => nameof(Strings.AnalysisQualityIncomplete),
        AnalysisQuality.Complete => nameof(Strings.AnalysisQualityComplete),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(AccountingReason value) => LocalizeFlags(value, AccountingReasonMappings);
    internal string Localize(AnalysisReason value) => LocalizeFlags(value, AnalysisReasonMappings);
    internal string Localize(UniversalFindingReason value) => LocalizeFlags(value, UniversalFindingReasonMappings);

    internal string Localize(FindingFacet value) => Get(value switch
    {
        FindingFacet.Large => nameof(Strings.FindingFacetLarge),
        FindingFacet.Old => nameof(Strings.FindingFacetOld),
        FindingFacet.Recent => nameof(Strings.FindingFacetRecent),
        FindingFacet.Growing => nameof(Strings.FindingFacetGrowing),
        FindingFacet.DuplicateCandidate => nameof(Strings.FindingFacetDuplicateCandidate),
        FindingFacet.OrphanCandidate => nameof(Strings.FindingFacetOrphanCandidate),
        FindingFacet.CacheLike => nameof(Strings.FindingFacetCacheLike),
        FindingFacet.TempLike => nameof(Strings.FindingFacetTempLike),
        FindingFacet.UserContent => nameof(Strings.FindingFacetUserContent),
        FindingFacet.Compressed => nameof(Strings.FindingFacetCompressed),
        FindingFacet.Sparse => nameof(Strings.FindingFacetSparse),
        FindingFacet.HardLinked => nameof(Strings.FindingFacetHardLinked),
        FindingFacet.CloudPlaceholder => nameof(Strings.FindingFacetCloudPlaceholder),
        FindingFacet.Virtualized => nameof(Strings.FindingFacetVirtualized),
        FindingFacet.Active => nameof(Strings.FindingFacetActive),
        FindingFacet.SystemProtected => nameof(Strings.FindingFacetSystemProtected),
        FindingFacet.UnknownOwner => nameof(Strings.FindingFacetUnknownOwner),
        _ => nameof(Strings.Unavailable),
    });

    internal string LocalizeFacets(IReadOnlyList<FindingFacet> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return string.Join(", ", values.Select(Localize));
    }

    internal string Localize(ReclaimKind value) => Get(value switch
    {
        ReclaimKind.Unknown => nameof(Strings.NotEstimated),
        ReclaimKind.None => nameof(Strings.None),
        ReclaimKind.Exact => nameof(Strings.EstimateAvailable),
        ReclaimKind.Estimated => nameof(Strings.Estimated),
        ReclaimKind.Conditional => nameof(Strings.ConditionalEstimate),
        ReclaimKind.UserDecision => nameof(Strings.RequiresUserDecision),
        _ => nameof(Strings.Unavailable),
    });

    internal string Localize(ReclaimEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return Localize(estimate.Kind);
    }

    internal string FormatAdditionalPaths(int count) =>
        string.Format(CultureInfo.InvariantCulture, Get(nameof(Strings.AdditionalPaths)), count);

    internal string LocalizeFindingDisplayName(FindingScope scope, FindingCategory category, string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        return scope == FindingScope.CategoryAggregate ? Localize(category) : displayName;
    }

    private string LocalizeFlags<T>(T value, IReadOnlyList<(T Flag, string Key)> mappings) where T : struct, Enum
    {
        ulong numeric = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        if (numeric == 0) return Get(nameof(Strings.None));

        ulong known = 0;
        var parts = new List<string>(mappings.Count + 1);
        foreach ((T flag, string key) in mappings)
        {
            ulong bit = Convert.ToUInt64(flag, CultureInfo.InvariantCulture);
            known |= bit;
            if ((numeric & bit) == bit) parts.Add(Get(key));
        }
        if ((numeric & ~known) != 0) parts.Add(Get(nameof(Strings.Unavailable)));
        return parts.Count == 0 ? Get(nameof(Strings.Unavailable)) : string.Join(", ", parts);
    }
}
