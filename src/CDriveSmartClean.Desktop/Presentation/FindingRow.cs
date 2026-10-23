using CDriveSmartClean.Domain.Findings;

namespace CDriveSmartClean.Desktop.Presentation;

internal sealed record FindingRow(string DisplayName, string Category, string Scope,
    string Allocated, string Logical, string Reclaim, string Risk, string RiskConfidence,
    string Confidence, string ProtectionState, string Facets, string RelativePathSummary)
{
    internal static FindingRow FromFinding(Finding finding, PresentationLocalizer localizer)
    {
        ArgumentNullException.ThrowIfNull(finding);
        ArgumentNullException.ThrowIfNull(localizer);
        string unavailable = localizer.Get(nameof(Resources.Strings.Unavailable));
        return new(localizer.LocalizeFindingDisplayName(finding.Scope, finding.PrimaryCategory, finding.DisplayName),
            localizer.Localize(finding.PrimaryCategory), localizer.Localize(finding.Scope),
            PresentationFormatter.FormatBytes(finding.SizeMetrics.AllocatedBytes, unavailable),
            PresentationFormatter.FormatBytes(finding.SizeMetrics.LogicalBytes, unavailable),
            localizer.Localize(finding.ReclaimEstimate), localizer.Localize(finding.RiskAssessment.Level),
            localizer.Localize(finding.RiskAssessment.Confidence), localizer.Localize(finding.Confidence),
            localizer.Localize(finding.ProtectionState), localizer.LocalizeFacets(finding.Facets),
            PresentationFormatter.FormatPathSummary(finding.RelativePaths, localizer.FormatAdditionalPaths));
    }
}
