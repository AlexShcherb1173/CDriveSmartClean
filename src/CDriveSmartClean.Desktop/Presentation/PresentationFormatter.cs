using System.Globalization;
using CDriveSmartClean.Domain.Reclaim;

namespace CDriveSmartClean.Desktop.Presentation;

internal static class PresentationFormatter
{
    private static readonly string[] ByteUnits = ["B", "KiB", "MiB", "GiB", "TiB", "PiB"];
    internal static string FormatBytes(long? value)
    {
        if (value is null) return "Unavailable";
        if (value == 0) return "0 B";
        double scaled = value.Value;
        int unit = 0;
        while (scaled >= 1024 && unit < ByteUnits.Length - 1) { scaled /= 1024; unit++; }
        return $"{scaled.ToString("0.##", CultureInfo.InvariantCulture)} {ByteUnits[unit]}";
    }
    internal static string FormatPercentage(decimal? value) => value is null
        ? "Unavailable" : $"{value.Value.ToString("0.##", CultureInfo.InvariantCulture)}%";
    internal static string FormatReclaim(ReclaimEstimate estimate)
    {
        ArgumentNullException.ThrowIfNull(estimate);
        return estimate.Kind switch
        {
            ReclaimKind.Unknown => "Not estimated",
            ReclaimKind.None => "None",
            ReclaimKind.Exact => "Estimate available",
            ReclaimKind.Estimated => "Estimated",
            ReclaimKind.Conditional => "Conditional estimate",
            ReclaimKind.UserDecision => "Requires user decision",
            _ => "Unavailable",
        };
    }
}
