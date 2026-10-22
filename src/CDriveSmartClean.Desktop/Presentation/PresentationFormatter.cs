using System.Globalization;
namespace CDriveSmartClean.Desktop.Presentation;

internal static class PresentationFormatter
{
    internal const int MaxDisplayedPaths = 3;
    internal const int MaxPathCharacters = 128;
    private static readonly string[] ByteUnits = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
    internal static string FormatBytes(long? value, string unavailable)
    {
        ArgumentNullException.ThrowIfNull(unavailable);
        if (value is null) return unavailable;
        if (value == 0) return "0 B";
        double scaled = value.Value;
        int unit = 0;
        while (scaled >= 1024 && unit < ByteUnits.Length - 1) { scaled /= 1024; unit++; }
        return $"{scaled.ToString("0.##", CultureInfo.InvariantCulture)} {ByteUnits[unit]}";
    }
    internal static string FormatPercentage(decimal? value, string unavailable)
    {
        ArgumentNullException.ThrowIfNull(unavailable);
        return value is null ? unavailable : $"{value.Value.ToString("0.##", CultureInfo.InvariantCulture)}%";
    }

    internal static string FormatPathSummary(IReadOnlyList<string> paths, Func<int, string> formatAdditionalPaths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(formatAdditionalPaths);
        int pathCount = paths.Count;
        int displayedCount = Math.Min(pathCount, MaxDisplayedPaths);
        if (displayedCount == 0) return string.Empty;

        var displayedPaths = new string[displayedCount];
        for (int index = 0; index < displayedCount; index++)
        {
            string path = paths[index];
            displayedPaths[index] = path.Length <= MaxPathCharacters
                ? path
                : string.Concat(path.AsSpan(0, MaxPathCharacters - 1), "…");
        }

        string summary = string.Join("; ", displayedPaths);
        int omittedCount = pathCount - displayedCount;
        return omittedCount == 0
            ? summary
            : string.Concat(summary, " ", formatAdditionalPaths(omittedCount));
    }
}
