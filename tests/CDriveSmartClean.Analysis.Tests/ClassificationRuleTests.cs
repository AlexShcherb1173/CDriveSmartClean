using System.Reflection;
using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using Xunit;

namespace CDriveSmartClean.Analysis.Tests;

public sealed class ClassificationRuleTests
{
    [Theory]
    [InlineData("ordinary.bin", FindingCategory.Unknown)]
    [InlineData(@"Windows\System32\a.dll", FindingCategory.System)]
    [InlineData(@"Other\Windows\a.dll", FindingCategory.Unknown)]
    [InlineData(@"Program Files\App\a.dll", FindingCategory.Application)]
    [InlineData(@"ProgramData\App\a.dat", FindingCategory.ApplicationData)]
    [InlineData(@"Users\Current\document.txt", FindingCategory.UserData)]
    [InlineData(@"Users\Current\AppData\Local\App\a.dat", FindingCategory.ApplicationData)]
    [InlineData(@"Users\Public\document.txt", FindingCategory.UserData)]
    [InlineData(@"Users\Other\document.txt", FindingCategory.Unknown)]
    [InlineData(@"Program FilesX\a.dll", FindingCategory.Unknown)]
    public async Task PrimaryCategoriesUseOnlyAuthoritativeBoundarySafeRoots(
        string path, FindingCategory expected)
    {
        StorageAnalysisCandidate candidate = await Candidate(path);
        Assert.Equal(expected, candidate.PrimaryCategory);
    }

    [Fact]
    public async Task WindowsMatchingIsCaseInsensitiveAndPreservesUnicodePath()
    {
        StorageAnalysisCandidate candidate = await Candidate(@"windows\Данные\Файл.bin");
        Assert.Equal(FindingCategory.System, candidate.PrimaryCategory);
        Assert.Equal(@"windows\Данные\Файл.bin", candidate.RelativePaths[0]);
    }

    [Theory]
    [InlineData(StorageEntryAttributes.Compressed, FindingFacet.Compressed)]
    [InlineData(StorageEntryAttributes.Sparse, FindingFacet.Sparse)]
    [InlineData(StorageEntryAttributes.Offline, FindingFacet.CloudPlaceholder)]
    [InlineData(StorageEntryAttributes.RecallOnOpen, FindingFacet.CloudPlaceholder)]
    [InlineData(StorageEntryAttributes.RecallOnDataAccess, FindingFacet.CloudPlaceholder)]
    [InlineData(StorageEntryAttributes.Unpinned, FindingFacet.CloudPlaceholder)]
    public void SupportedFacetsComeFromNativeEntryEvidence(
        StorageEntryAttributes attributes, FindingFacet expected)
    {
        (FindingCategory _, FindingFacet[] facets) = Classify("object.bin", attributes);
        Assert.Contains(expected, facets);
    }

    [Fact]
    public void PinnedAloneIsNotCloudPlaceholder()
    {
        (FindingCategory category, FindingFacet[] facets) = Classify("object.bin", StorageEntryAttributes.Pinned);
        Assert.DoesNotContain(FindingFacet.CloudPlaceholder, facets);
        Assert.NotEqual(FindingCategory.CloudBacked, category);
    }

    [Fact]
    public void WeakCloudEvidenceNeverSelectsCloudBacked()
    {
        (FindingCategory category, FindingFacet[] facets) = Classify(
            @"Users\Current\cloud.bin", StorageEntryAttributes.Offline);
        Assert.Equal(FindingCategory.UserData, category);
        Assert.Contains(FindingFacet.CloudPlaceholder, facets);
    }

    [Fact]
    public async Task DeferredCategoriesAreNeverEmitted()
    {
        string[] names = ["cache", "temp", "archive.zip", "disk.vhdx", "setup.msi", "$Recycle.Bin"];
        foreach (string name in names)
        {
            StorageAnalysisCandidate candidate = await Candidate(name);
            Assert.Contains(candidate.PrimaryCategory, new[]
            {
                FindingCategory.Unknown, FindingCategory.System, FindingCategory.Application,
                FindingCategory.ApplicationData, FindingCategory.UserData,
            });
        }
    }

    [Fact]
    public async Task EqualStrengthPrimaryRulesConflictToUnknown()
    {
        var context = new StorageClassificationContext(TestData.Volume, @"C:\Windows",
            [@"C:\Overlap"], null, null, [], @"C:\Overlap", @"C:\Users", []);
        StorageAnalysisCandidate candidate = await Candidate(@"Overlap\a.bin", context: context);
        Assert.Equal(FindingCategory.Unknown, candidate.PrimaryCategory);
        Assert.Contains(candidate.Evidence, evidence => evidence.Code == "classification_rule_conflict");
    }

    [Fact]
    public async Task InputOrderDoesNotChangeClassification()
    {
        var first = TestData.Entry(@"Windows\a", TestData.Identity(), 1);
        var second = TestData.Entry(@"ProgramData\b", TestData.Identity(), 2);
        StorageAnalysisResult left = await TestData.Analyze([first, second]);
        StorageAnalysisResult right = await TestData.Analyze([second, first]);
        Assert.Equal(left.CategorySummaries.Select(Snapshot), right.CategorySummaries.Select(Snapshot));
    }

    private static object Snapshot(CategorySummary summary) => new
    {
        summary.Category,
        summary.DeduplicatedObservedAllocatedBytes,
        summary.RawVisibleAllocatedBytes,
        summary.UncertainMeasuredAllocatedBytes,
    };

    private static async Task<StorageAnalysisCandidate> Candidate(
        string path, StorageClassificationContext? context = null)
    {
        StorageEntry entry = TestData.Entry(path, TestData.Identity(), 1);
        StorageAnalysisResult result = await TestData.Analyze([entry], context: context);
        return Assert.Single(result.LargestFileCandidates);
    }

    private static (FindingCategory Category, FindingFacet[] Facets) Classify(
        string path, StorageEntryAttributes attributes)
    {
        Assembly assembly = typeof(UniversalStorageAnalyzer).Assembly;
        Type engineType = assembly.GetType(
            "CDriveSmartClean.Analysis.DeterministicClassificationEngine", throwOnError: true)!;
        object engine = Activator.CreateInstance(engineType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null, [TestData.Context()], culture: null)!;
        MethodInfo method = engineType.GetMethod("Classify",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        object result = method.Invoke(engine, [@"C:\" + path, attributes])!;
        Type resultType = result.GetType();
        return (
            (FindingCategory)resultType.GetProperty("Category")!.GetValue(result)!,
            (FindingFacet[])resultType.GetProperty("Facets")!.GetValue(result)!);
    }
}
