using System.Globalization;

namespace CDriveSmartClean.Desktop.Localization;

internal static class DesktopCulture
{
    internal const string English = "en";
    internal const string Russian = "ru";

    internal static CultureInfo Select(string? persistedPreference, CultureInfo windowsUiCulture)
    {
        ArgumentNullException.ThrowIfNull(windowsUiCulture);
        string? normalized = Normalize(persistedPreference);
        if (normalized is not null) return CultureInfo.GetCultureInfo(normalized);

        return CultureInfo.GetCultureInfo(
            string.Equals(windowsUiCulture.TwoLetterISOLanguageName, Russian, StringComparison.OrdinalIgnoreCase)
                ? Russian
                : English);
    }

    internal static string? Normalize(string? cultureName)
    {
        if (string.Equals(cultureName?.Trim(), English, StringComparison.OrdinalIgnoreCase)) return English;
        if (string.Equals(cultureName?.Trim(), Russian, StringComparison.OrdinalIgnoreCase)) return Russian;
        return null;
    }

    internal static void Apply(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}
