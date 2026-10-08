using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Risk;

namespace CDriveSmartClean.Desktop.Presentation;

internal sealed record FindingRow(string DisplayName, FindingCategory Category, FindingScope Scope,
    string Allocated, string Logical, string Reclaim, RiskLevel Risk, Confidence RiskConfidence,
    Confidence Confidence, ProtectionState ProtectionState, string Facets, string RelativePathSummary)
{
    internal static FindingRow FromFinding(Finding finding)
    {
        ArgumentNullException.ThrowIfNull(finding);
        return new(finding.DisplayName, finding.PrimaryCategory, finding.Scope,
            PresentationFormatter.FormatBytes(finding.SizeMetrics.AllocatedBytes),
            PresentationFormatter.FormatBytes(finding.SizeMetrics.LogicalBytes),
            PresentationFormatter.FormatReclaim(finding.ReclaimEstimate), finding.RiskAssessment.Level,
            finding.RiskAssessment.Confidence, finding.Confidence, finding.ProtectionState,
            string.Join(", ", finding.Facets), string.Join("; ", finding.RelativePaths));
    }
}
