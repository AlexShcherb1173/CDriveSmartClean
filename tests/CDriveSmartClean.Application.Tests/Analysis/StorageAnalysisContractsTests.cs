using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Analysis;

public sealed class StorageAnalysisContractsTests
{
    private static readonly VolumeIdentity Volume = new(new Guid("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public void OptionsHaveBoundedDefaults()
    {
        var options = new StorageAnalysisOptions();
        Assert.Equal(1_000_000, options.MaximumPathStates);
        Assert.Equal(1_000_000, options.MaximumIdentityStates);
        Assert.Equal(100, options.CandidateLimit);
        Assert.Equal(256L * 1024 * 1024, options.AnalysisStateBudget);
    }

    [Theory]
    [InlineData(0, 1, 1, 1)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, 1, 0, 1)]
    [InlineData(1, 1, 1001, 1)]
    [InlineData(1, 1, 1, 0)]
    public void OptionsRejectInvalidLimits(int paths, int identities, int candidates, long budget) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new StorageAnalysisOptions(paths, identities, candidates, budget));

    [Fact]
    public void ContextCopiesSortsAndReportsCompleteness()
    {
        var roots = new List<string> { @"C:\Z", @"C:\A", @"c:\a" };
        var keys = new List<string> { "Z", "A", "A" };
        var context = new StorageClassificationContext(Volume, @"C:\Windows", roots, null, null,
            [], null, @"C:\Users", keys);
        roots.Clear();
        keys.Clear();
        Assert.Equal([@"C:\A", @"C:\Z"], context.ProgramFilesRoots);
        Assert.Equal(["A", "Z"], context.UnavailableContextKeys);
        Assert.False(context.IsComplete);
    }

    [Theory]
    [InlineData(@"C:folder")]
    [InlineData(@"C:")]
    [InlineData(@"C:\foo\..\bar")]
    [InlineData(@"C:\foo\.\bar")]
    [InlineData("C:\\foo\\\\bar")]
    [InlineData("C:/foo")]
    [InlineData(@"C:\foo:ads")]
    [InlineData(@"\\?\VolumeNotAGuid\foo")]
    public void ContextRejectsNoncanonicalWindowsRoots(string path) =>
        Assert.Throws<ArgumentException>(() => new StorageClassificationContext(
            Volume, path, [], null, null, [], null, null, []));

    [Fact]
    public void ContextRejectsNulContainingRoot() =>
        Assert.Throws<ArgumentException>(() => new StorageClassificationContext(
            Volume, "C:\\foo\0bar", [], null, null, [], null, null, []));

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:\Windows")]
    [InlineData(@"\\?\Volume{11111111-1111-1111-1111-111111111111}\")]
    [InlineData(@"\\?\Volume{11111111-1111-1111-1111-111111111111}\Windows")]
    public void ContextAcceptsCanonicalDriveAndGuidVolumeRoots(string path)
    {
        var context = new StorageClassificationContext(
            Volume, path, [], null, null, [], null, null, []);
        Assert.Equal(path, context.WindowsDirectory);
    }

    [Fact]
    public void ContextCaseVariantDeduplicationIsInputOrderIndependent()
    {
        var left = new StorageClassificationContext(Volume, @"C:\Windows", [@"C:\A", @"c:\a"], null,
            null, [@"C:\Users\Current\AppData", @"c:\users\current\appdata"], null, null, []);
        var right = new StorageClassificationContext(Volume, @"C:\Windows", [@"c:\a", @"C:\A"], null,
            null, [@"c:\users\current\appdata", @"C:\Users\Current\AppData"], null, null, []);

        Assert.Equal(left.ProgramFilesRoots, right.ProgramFilesRoots);
        Assert.Equal(left.CurrentUserAppDataRoots, right.CurrentUserAppDataRoots);
        Assert.Equal([@"C:\A"], left.ProgramFilesRoots);
        Assert.Equal([@"C:\Users\Current\AppData"], left.CurrentUserAppDataRoots);
    }

    [Fact]
    public void RequestRejectsVolumeMismatch()
    {
        var context = Context(Volume);
        var other = new SystemVolumeDescriptor(new(Guid.NewGuid()), @"D:\");
        Assert.Throws<ArgumentException>(() => new StorageAnalysisRequest(other, context));
    }

    [Fact]
    public void ResultCopiesAndOrdersCollections()
    {
        CategorySummary[] summaries = Enum.GetValues<FindingCategory>().Reverse()
            .Select(category => new CategorySummary(category, 0, 0, 0, 0, 0,
                AnalysisQuality.Complete, AnalysisReason.None)).ToArray();
        var candidate = new StorageAnalysisCandidate(AnalysisCandidateScope.HierarchyNode,
            FindingCategory.Unknown, [], Confidence.Unknown, [], new(0, 0, 0, 0), ["a"], null, 0, 0);
        var result = new StorageAnalysisResult(AnalysisQuality.Complete, AnalysisReason.None,
            AccountingQuality.Complete, AccountingReason.None, summaries, [candidate], [], [], [candidate]);
        Array.Clear(summaries);
        Assert.Equal(Enum.GetValues<FindingCategory>(), result.CategorySummaries.Select(item => item.Category));
        Assert.Throws<NotSupportedException>(() => ((IList<CategorySummary>)result.CategorySummaries).Clear());
    }

    [Fact]
    public void UnavailableResultCannotExposePartialOutput()
    {
        var candidate = new StorageAnalysisCandidate(AnalysisCandidateScope.HierarchyNode,
            FindingCategory.Unknown, [], Confidence.Unknown, [], new(null, null, null, null), ["a"], null, 0, 0);
        Assert.Throws<ArgumentException>(() => new StorageAnalysisResult(AnalysisQuality.Unavailable,
            AnalysisReason.ResourceLimit, AccountingQuality.Complete, AccountingReason.None,
            [], [candidate], [], [], []));
    }

    [Fact]
    public void SessionContractIsAnEntrySink() =>
        Assert.True(typeof(IStorageEntrySink).IsAssignableFrom(typeof(IStorageAnalysisSession)));

    [Fact]
    public void ApplicationContractsDoNotLeakPlatformTypes() =>
        Assert.DoesNotContain(typeof(StorageClassificationContext).Assembly.GetReferencedAssemblies(),
            assembly => assembly.Name == "CDriveSmartClean.Platform.Windows");

    private static StorageClassificationContext Context(VolumeIdentity volume) =>
        new(volume, @"C:\Windows", [@"C:\Program Files"], @"C:\ProgramData",
            @"C:\Users\Current", [@"C:\Users\Current\AppData"], @"C:\Users\Public",
            @"C:\Users", []);
}
