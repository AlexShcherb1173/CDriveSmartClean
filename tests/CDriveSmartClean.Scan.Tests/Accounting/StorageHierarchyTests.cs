using System.Text.Json;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Scan.Tests.Accounting;

public sealed class StorageHierarchyTests
{
    [Theory]
    [InlineData("A\\one", "B\\two", "")]
    [InlineData("A\\one", "A\\two", "A")]
    [InlineData("A\\B\\one", "A\\C\\two", "A")]
    [InlineData("A\\one", "A\\B\\two", "A")]
    public async Task AttributionUsesCommonAncestor(string first, string second, string lca)
    {
        var h = new AccountingHarness();
        var id = h.Id();
        h.Entries = [h.Entry(first, id), h.Entry(second, id)];
        var result = await h.Run();
        Assert.Equal(lca, Assert.Single(result.AllocationGroups).AttributionPath);
        Assert.Equal(10, result.Root!.Aggregate.InclusiveAttributedObservedAllocatedBytes);
        Assert.Equal(20, result.Root.Aggregate.RawReportedAllocatedBytes);
        Assert.Equal(0, result.Root.Aggregate.DirectoryCount);
    }

    [Fact]
    public async Task HierarchyPreservesCaseAndUnicodeAndIsOrderIndependent()
    {
        var h = new AccountingHarness();
        var id = h.Id();
        var entries = new[] { h.Entry("A\\a", id), h.Entry("a\\b", id), h.Entry("é\\x", h.Id()), h.Entry("e\u0301\\y", h.Id()),
            h.Entry("A", h.Id(), kind: StorageObjectKind.Directory) };
        string? expected = null;
        for (int seed = 0; seed < 20; seed++)
        {
            var random = new Random(seed);
            h.Entries = entries.OrderBy(_ => random.Next()).ToArray();
            var result = await h.Run();
            Assert.Equal(4, result.Root!.Children.Count);
            Assert.Equal(1, result.Root.Aggregate.DirectoryCount);
            Assert.Equal(30, result.Summary.DeduplicatedObservedAllocatedBytes);
            string json = JsonSerializer.Serialize(result);
            expected ??= json;
            Assert.Equal(expected, json);
        }
    }

    [Theory]
    [InlineData("..\\escape")]
    [InlineData(".\\a")]
    [InlineData("A\\\\a")]
    [InlineData("A/a")]
    [InlineData("A:stream")]
    [InlineData("")]
    public async Task MalformedPathsFailEvenWhenAccountingDisabled(string path)
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry(path, h.Id())];
        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Run(new(accountingStateBudget: 1)));
    }

    [Fact]
    public async Task DeepHierarchyFinalizesIteratively()
    {
        var h = new AccountingHarness();
        string path = string.Join("\\", Enumerable.Repeat("d", 2000)) + "\\file";
        h.Entries = [h.Entry(path, h.Id())];
        var result = await h.Run();
        Assert.Equal(10, result.Summary.DeduplicatedObservedAllocatedBytes);
        StorageHierarchyNode node = result.Root!;
        int depth = 0;
        while (node.Children.Count != 0) { node = Assert.Single(node.Children); depth++; }
        Assert.Equal(2000, depth);
    }
}
