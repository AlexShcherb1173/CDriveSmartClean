using System.Collections;
using System.Globalization;
using System.Resources;
using CDriveSmartClean.Desktop.Resources;
using Xunit;

namespace CDriveSmartClean.Desktop.Tests;

public sealed class ResourceParityTests
{
    [Fact]
    public void NeutralAndRussianResourcesHaveIdenticalNonemptyKeys()
    {
        Dictionary<string, string> english = Read(CultureInfo.GetCultureInfo("en"));
        Dictionary<string, string> russian = Read(CultureInfo.GetCultureInfo("ru"));
        Assert.Equal(145, english.Count);
        Assert.Equal(english.Keys.Order(StringComparer.Ordinal), russian.Keys.Order(StringComparer.Ordinal));
        Assert.All(english, item => Assert.False(string.IsNullOrWhiteSpace(item.Value), item.Key));
        Assert.All(russian, item => Assert.False(string.IsNullOrWhiteSpace(item.Value), item.Key));
    }

    [Fact]
    public void NeutralAndRussianLookupsResolveExpectedText()
    {
        Assert.Equal("Analysis only — CDriveSmartClean does not delete files in this version.",
            Strings.ResourceManager.GetString(nameof(Strings.AnalysisOnlyBanner), CultureInfo.GetCultureInfo("en")));
        Assert.Equal("Только анализ — эта версия CDriveSmartClean не удаляет файлы.",
            Strings.ResourceManager.GetString(nameof(Strings.AnalysisOnlyBanner), CultureInfo.GetCultureInfo("ru")));
    }

    [Fact]
    public void UnsupportedCultureFallsBackToNeutralEnglish() => Assert.Equal("The scan failed.",
        Strings.ResourceManager.GetString(nameof(Strings.ScanFailed), CultureInfo.GetCultureInfo("fr-FR")));

    [Fact]
    public void ResourceAccessorIsPublicAndStronglyTypedForFutureXaml()
    {
        Assert.True(typeof(Strings).IsPublic);
        Assert.Equal("Language", Strings.ResourceManager.GetString(nameof(Strings.Language), CultureInfo.GetCultureInfo("en")));
    }

    private static Dictionary<string, string> Read(CultureInfo culture)
    {
        ResourceSet set = Assert.IsAssignableFrom<ResourceSet>(Strings.ResourceManager.GetResourceSet(culture, true, true));
        return set.Cast<DictionaryEntry>().ToDictionary(item => Assert.IsType<string>(item.Key),
            item => Assert.IsType<string>(item.Value), StringComparer.Ordinal);
    }
}
