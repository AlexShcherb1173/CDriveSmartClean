using System.Globalization;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Desktop.Presentation;
using CDriveSmartClean.Desktop.Resources;
using CDriveSmartClean.Desktop.ViewModels;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Runtime;
using Xunit;

namespace CDriveSmartClean.Desktop.Tests;

public sealed class PresentationLocalizerTests
{
    private static readonly PresentationLocalizer English = new(CultureInfo.GetCultureInfo("en"));
    private static readonly PresentationLocalizer Russian = new(CultureInfo.GetCultureInfo("ru"));

    [Fact]
    public void EnglishAndRussianMappingsResolveExpectedText()
    {
        Assert.Equal("Scanning", English.Localize(DesktopScanState.Scanning));
        Assert.Equal("Сканирование", Russian.Localize(DesktopScanState.Scanning));
        Assert.Equal("Application data", English.Localize(FindingCategory.ApplicationData));
        Assert.Equal("Данные приложений", Russian.Localize(FindingCategory.ApplicationData));
        Assert.Equal("Review required", English.Localize(ProtectionState.ReviewRequired));
        Assert.Equal("Требует проверки", Russian.Localize(ProtectionState.ReviewRequired));
    }

    [Fact]
    public void EveryDefinedNonFlagEnumValueHasAResult()
    {
        AssertAll(Enum.GetValues<DesktopScanState>(), English.Localize);
        AssertAll(Enum.GetValues<ProductScanPhase>(), English.Localize);
        AssertAll(Enum.GetValues<FindingCategory>(), English.Localize);
        AssertAll(Enum.GetValues<FindingScope>(), English.Localize);
        AssertAll(Enum.GetValues<RiskLevel>(), English.Localize);
        AssertAll(Enum.GetValues<Confidence>(), English.Localize);
        AssertAll(Enum.GetValues<ProtectionState>(), English.Localize);
        AssertAll(Enum.GetValues<StorageTraversalIssueKind>(), English.Localize);
        AssertAll(Enum.GetValues<AccountingQuality>(), English.Localize);
        AssertAll(Enum.GetValues<AnalysisQuality>(), English.Localize);
        AssertAll(Enum.GetValues<FindingFacet>(), English.Localize);
        AssertAll(Enum.GetValues<ReclaimKind>(), English.Localize);
    }

    [Fact]
    public void EveryDefinedFlagValueHasAResult()
    {
        AssertAll(Enum.GetValues<AccountingReason>(), English.Localize);
        AssertAll(Enum.GetValues<AnalysisReason>(), English.Localize);
        AssertAll(Enum.GetValues<UniversalFindingReason>(), English.Localize);
    }

    [Fact]
    public void FlagCombinationsUseDeterministicEnumOrder()
    {
        AccountingReason value = AccountingReason.VolumeDrift | AccountingReason.TraversalCoverageGap |
                                 AccountingReason.ResourceLimit;
        Assert.Equal("Traversal coverage gap, Resource limit, Volume drift", English.Localize(value));
        Assert.Equal("Неполный обход, Ограничение ресурсов, Изменение состояния тома", Russian.Localize(value));
    }

    [Fact]
    public void UnknownFlagBitsAppendSafeFallback()
    {
        AccountingReason value = AccountingReason.TraversalCoverageGap | (AccountingReason)(1 << 20);
        Assert.Equal("Traversal coverage gap, Unavailable", English.Localize(value));
    }

    [Fact]
    public void UnknownNumericEnumValuesFailSafe()
    {
        Assert.Equal("Unavailable", English.Localize((DesktopScanState)999));
        Assert.Equal("Unavailable", English.Localize((ProductScanPhase)999));
        Assert.Equal("Unavailable", English.Localize((FindingCategory)999));
        Assert.Equal("Unavailable", English.Localize((FindingScope)999));
        Assert.Equal("Unavailable", English.Localize((RiskLevel)999));
        Assert.Equal("Unavailable", English.Localize((Confidence)999));
        Assert.Equal("Unavailable", English.Localize((ProtectionState)999));
        Assert.Equal("Unavailable", English.Localize((StorageTraversalIssueKind)999));
        Assert.Equal("Unavailable", English.Localize((AccountingQuality)999));
        Assert.Equal("Unavailable", English.Localize((AnalysisQuality)999));
        Assert.Equal("Unavailable", English.Localize((FindingFacet)999));
        Assert.Equal("Unavailable", English.Localize((ReclaimKind)999));
    }

    [Fact]
    public void ReclaimEstimateUsesLocalizedKind()
    {
        Assert.Equal("Not estimated", English.Localize(ReclaimEstimate.Unknown("test")));
        Assert.Equal("Не оценено", Russian.Localize(ReclaimEstimate.Unknown("test")));
    }

    [Fact]
    public void FacetsAreLocalizedWithoutChangingInputOrder()
    {
        Assert.Equal("Large, Hard-linked", English.LocalizeFacets([FindingFacet.Large, FindingFacet.HardLinked]));
        Assert.Equal("Крупный, Жёсткие ссылки", Russian.LocalizeFacets([FindingFacet.Large, FindingFacet.HardLinked]));
    }

    [Fact]
    public void AdditionalPathCountUsesLocalizedInvariantFormatting()
    {
        Assert.Equal("… (+12 more)", English.FormatAdditionalPaths(12));
        Assert.Equal("… (ещё 12)", Russian.FormatAdditionalPaths(12));
    }

    [Fact]
    public void CategoryAggregateDisplayNameCanBeLocalizedWhilePathsRemainInvariant()
    {
        Assert.Equal("Данные пользователя",
            Russian.LocalizeFindingDisplayName(FindingScope.CategoryAggregate, FindingCategory.UserData, "UserData"));
        Assert.Equal(@"Users\Alex\file.bin",
            Russian.LocalizeFindingDisplayName(FindingScope.File, FindingCategory.UserData, @"Users\Alex\file.bin"));
    }

    [Fact]
    public void LocalizedErrorResourcesDoNotRequireFrameworkExceptionText()
    {
        Assert.Equal("The scan failed.", English.Get(nameof(Strings.ScanFailed)));
        Assert.Equal("Сканирование завершилось ошибкой.", Russian.Get(nameof(Strings.ScanFailed)));
        Assert.Equal("Сканирование вернуло результат для другого сеанса.", Russian.Get(nameof(Strings.ScanWrongSession)));
        Assert.Equal("Не удалось сохранить выбранный язык.", Russian.Get(nameof(Strings.LanguagePreferenceSaveFailed)));
    }

    [Fact]
    public void UnsupportedLocalizerCultureUsesNeutralEnglish()
    {
        var unsupported = new PresentationLocalizer(CultureInfo.GetCultureInfo("fr-FR"));
        Assert.Equal("Findings", unsupported.Get(nameof(Strings.Findings)));
    }

    private static void AssertAll<T>(IEnumerable<T> values, Func<T, string> localize)
    {
        foreach (T value in values) Assert.False(string.IsNullOrWhiteSpace(localize(value)), value?.ToString());
    }
}
