using System.Text;
using CDriveSmartClean.Desktop.Localization;
using Xunit;

namespace CDriveSmartClean.Desktop.Tests;

public sealed class LanguagePreferenceStoreTests
{
    [Fact]
    public void MissingFileReturnsNoPreference()
    {
        using var temporary = new TemporaryDirectory();
        Assert.Null(new LanguagePreferenceStore(temporary.SettingsPath).Load());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void ValidPreferenceLoadsNormalized(string culture)
    {
        using var temporary = new TemporaryDirectory();
        File.WriteAllText(temporary.SettingsPath, $"{{\"uiCulture\":\"{culture.ToUpperInvariant()}\"}}", Encoding.UTF8);
        Assert.Equal(culture, new LanguagePreferenceStore(temporary.SettingsPath).Load());
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    public void UnsupportedPreferenceIsIgnored(string culture)
    {
        using var temporary = new TemporaryDirectory();
        File.WriteAllText(temporary.SettingsPath, $"{{\"uiCulture\":\"{culture}\"}}", Encoding.UTF8);
        Assert.Null(new LanguagePreferenceStore(temporary.SettingsPath).Load());
    }

    [Fact]
    public void MalformedJsonIsIgnored()
    {
        using var temporary = new TemporaryDirectory();
        File.WriteAllText(temporary.SettingsPath, "{not-json", Encoding.UTF8);
        Assert.Null(new LanguagePreferenceStore(temporary.SettingsPath).Load());
    }

    [Fact]
    public void UnreadableFileIsIgnored()
    {
        using var temporary = new TemporaryDirectory();
        File.WriteAllText(temporary.SettingsPath, "{\"uiCulture\":\"ru\"}", Encoding.UTF8);
        using var locked = new FileStream(temporary.SettingsPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.Null(new LanguagePreferenceStore(temporary.SettingsPath).Load());
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ru")]
    public void SupportedPreferenceRoundTripsAsMinimalUtf8Json(string culture)
    {
        using var temporary = new TemporaryDirectory();
        var store = new LanguagePreferenceStore(temporary.SettingsPath);
        Assert.True(store.TrySave(culture));
        Assert.Equal(culture, store.Load());
        Assert.Equal($"{{\"uiCulture\":\"{culture}\"}}", File.ReadAllText(temporary.SettingsPath, Encoding.UTF8));
        byte[] content = File.ReadAllBytes(temporary.SettingsPath);
        Assert.False(content.Length >= 3 && content.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }));
    }

    [Fact]
    public void UnsupportedPreferenceIsNotPersisted()
    {
        using var temporary = new TemporaryDirectory();
        var store = new LanguagePreferenceStore(temporary.SettingsPath);
        Assert.False(store.TrySave("fr"));
        Assert.False(File.Exists(temporary.SettingsPath));
    }

    [Fact]
    public void WriteFailureIsReportedWithoutThrowing()
    {
        using var temporary = new TemporaryDirectory();
        string blockingFile = Path.Combine(temporary.Path, "blocking-file");
        File.WriteAllText(blockingFile, "block");
        var store = new LanguagePreferenceStore(Path.Combine(blockingFile, "settings.json"));
        Assert.False(store.TrySave("en"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        internal TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CDriveSmartClean-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
            SettingsPath = System.IO.Path.Combine(Path, "settings.json");
        }
        internal string Path { get; }
        internal string SettingsPath { get; }
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
