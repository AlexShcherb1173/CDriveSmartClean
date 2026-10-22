using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CDriveSmartClean.Desktop.Localization;

internal sealed class LanguagePreferenceStore
{
    private const string SettingsFileName = "settings.json";
    private readonly string settingsFilePath;

    internal LanguagePreferenceStore()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CDriveSmartClean", SettingsFileName))
    {
    }

    internal LanguagePreferenceStore(string settingsFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(settingsFilePath);
        this.settingsFilePath = settingsFilePath;
    }

    internal string? Load()
    {
        try
        {
            if (!File.Exists(settingsFilePath)) return null;
            string json = File.ReadAllText(settingsFilePath, Encoding.UTF8);
            Settings? settings = JsonSerializer.Deserialize<Settings>(json);
            return DesktopCulture.Normalize(settings?.UiCulture);
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return null;
        }
    }

    internal bool TrySave(string cultureName)
    {
        string? normalized = DesktopCulture.Normalize(cultureName);
        if (normalized is null) return false;

        try
        {
            string? directory = Path.GetDirectoryName(settingsFilePath);
            if (string.IsNullOrWhiteSpace(directory)) return false;
            Directory.CreateDirectory(directory);
            string json = JsonSerializer.Serialize(new Settings { UiCulture = normalized });
            File.WriteAllText(settingsFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return true;
        }
        catch (Exception exception) when (IsStorageFailure(exception))
        {
            return false;
        }
    }

    private static bool IsStorageFailure(Exception exception) => exception is
        IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException;

    private sealed class Settings
    {
        [JsonPropertyName("uiCulture")]
        public string? UiCulture { get; init; }
    }
}
