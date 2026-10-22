namespace CDriveSmartClean.Desktop.Presentation;

internal sealed record VolumeSummaryRow(string RootPath, string Capacity, string Used, string Free)
{
    internal static VolumeSummaryRow Empty(PresentationLocalizer localizer) => new(
        localizer.Get(nameof(Resources.Strings.NotScanned)),
        localizer.Get(nameof(Resources.Strings.Unavailable)),
        localizer.Get(nameof(Resources.Strings.Unavailable)),
        localizer.Get(nameof(Resources.Strings.Unavailable)));
}
internal sealed record QualitySummaryRow(string Name, string Value, string Reason);
internal sealed record CategorySummaryRow(string Category, string Allocated);
internal sealed record IssueSummaryRow(string Kind, long Count);
