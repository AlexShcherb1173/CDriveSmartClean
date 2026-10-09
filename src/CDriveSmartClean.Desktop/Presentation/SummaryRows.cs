using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Domain.Findings;

namespace CDriveSmartClean.Desktop.Presentation;

internal sealed record VolumeSummaryRow(string RootPath, string Capacity, string Used, string Free)
{
    internal static VolumeSummaryRow Empty { get; } = new("Not scanned", "Unavailable", "Unavailable", "Unavailable");
}
internal sealed record QualitySummaryRow(string Name, string Value, string Reason);
internal sealed record CategorySummaryRow(FindingCategory Category, string Allocated);
internal sealed record IssueSummaryRow(StorageTraversalIssueKind Kind, long Count);
