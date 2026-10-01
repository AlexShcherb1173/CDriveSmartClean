using System.Text.Json;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
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
    public async Task SameFolderAliasesHaveOneDirectAllocationAndTwoRawPaths()
    {
        var h = new AccountingHarness();
        var identity = h.Id();
        h.Entries = [h.Entry("A\\one.bin", identity), h.Entry("A\\two.bin", identity)];

        StorageAccountingResult result = await h.Run();
        StorageHierarchyNode root = result.Root!;
        StorageHierarchyNode a = Child(root, "A");

        Assert.Equal(0, root.Aggregate.DirectAttributedObservedAllocatedBytes);
        Assert.Equal(10, root.Aggregate.InclusiveAttributedObservedAllocatedBytes);
        Assert.Equal(20, root.Aggregate.RawReportedAllocatedBytes);
        Assert.Equal(2, root.Aggregate.FileCount);
        Assert.Equal(10, a.Aggregate.DirectAttributedObservedAllocatedBytes);
        Assert.Equal(10, a.Aggregate.InclusiveAttributedObservedAllocatedBytes);
        Assert.Equal(20, a.Aggregate.RawReportedAllocatedBytes);
        Assert.Equal(2, a.Aggregate.FileCount);
        Assert.Equal("A", Assert.Single(result.AllocationGroups).AttributionPath);
    }

    [Fact]
    public async Task SiblingFolderAliasesAreAttributedOnlyToRoot()
    {
        var h = new AccountingHarness();
        var identity = h.Id();
        h.Entries = [h.Entry("A\\one.bin", identity), h.Entry("B\\two.bin", identity)];

        StorageAccountingResult result = await h.Run();
        StorageHierarchyNode root = result.Root!;
        StorageHierarchyNode a = Child(root, "A");
        StorageHierarchyNode b = Child(root, "B");

        Assert.Equal(10, root.Aggregate.DirectAttributedObservedAllocatedBytes);
        Assert.Equal(10, root.Aggregate.InclusiveAttributedObservedAllocatedBytes);
        Assert.Equal(20, root.Aggregate.RawReportedAllocatedBytes);
        Assert.Equal(2, root.Aggregate.FileCount);
        AssertLeafEvidence(a, 10, 1);
        AssertLeafEvidence(b, 10, 1);
        Assert.Equal("", Assert.Single(result.AllocationGroups).AttributionPath);
    }

    [Fact]
    public async Task NestedSiblingAliasesRollUpRawEvidenceButAttributeToParent()
    {
        var h = new AccountingHarness();
        var identity = h.Id();
        h.Entries = [h.Entry("A\\B\\one.bin", identity), h.Entry("A\\C\\two.bin", identity)];

        StorageAccountingResult result = await h.Run();
        StorageHierarchyNode root = result.Root!;
        StorageHierarchyNode a = Child(root, "A");
        StorageHierarchyNode b = Child(a, "A\\B");
        StorageHierarchyNode c = Child(a, "A\\C");

        Assert.Equal(0, root.Aggregate.DirectAttributedObservedAllocatedBytes);
        Assert.Equal(10, root.Aggregate.InclusiveAttributedObservedAllocatedBytes);
        Assert.Equal(20, root.Aggregate.RawReportedAllocatedBytes);
        Assert.Equal(2, root.Aggregate.FileCount);
        Assert.Equal(10, a.Aggregate.DirectAttributedObservedAllocatedBytes);
        Assert.Equal(10, a.Aggregate.InclusiveAttributedObservedAllocatedBytes);
        Assert.Equal(20, a.Aggregate.RawReportedAllocatedBytes);
        Assert.Equal(2, a.Aggregate.FileCount);
        AssertLeafEvidence(b, 10, 1);
        AssertLeafEvidence(c, 10, 1);
        Assert.Equal("A", Assert.Single(result.AllocationGroups).AttributionPath);
    }

    [Fact]
    public async Task ParentDirectAndChildInclusiveAllocationsRollUpOnceInEveryOrder()
    {
        var h = new AccountingHarness();
        var parentIdentity = h.Id();
        var childIdentity = h.Id();
        StorageEntry[] observations =
        [
            h.Entry("A\\one.bin", parentIdentity),
            h.Entry("A\\two.bin", parentIdentity),
            h.Entry("A\\B\\three.bin", childIdentity, bytes: 20),
            h.Entry("A\\B\\four.bin", childIdentity, bytes: 20),
            h.Entry("A\\B", h.Id(), kind: StorageObjectKind.Directory,
                availability: StorageMeasurementAvailability.NotApplicable),
        ];
        string? expected = null;

        for (int seed = 0; seed < 12; seed++)
        {
            var random = new Random(seed);
            h.Entries = observations.OrderBy(_ => random.Next()).ToArray();
            StorageAccountingResult result = await h.Run();
            StorageHierarchyNode root = result.Root!;
            StorageHierarchyNode a = Child(root, "A");
            StorageHierarchyNode b = Child(a, "A\\B");

            Assert.Equal(30, root.Aggregate.InclusiveAttributedObservedAllocatedBytes);
            Assert.Equal(60, root.Aggregate.RawReportedAllocatedBytes);
            Assert.Equal(4, root.Aggregate.FileCount);
            Assert.Equal(1, root.Aggregate.DirectoryCount);
            Assert.Equal(10, a.Aggregate.DirectAttributedObservedAllocatedBytes);
            Assert.Equal(30, a.Aggregate.InclusiveAttributedObservedAllocatedBytes);
            Assert.Equal(60, a.Aggregate.RawReportedAllocatedBytes);
            Assert.Equal(4, a.Aggregate.FileCount);
            Assert.Equal(1, a.Aggregate.DirectoryCount);
            Assert.Equal(20, b.Aggregate.DirectAttributedObservedAllocatedBytes);
            Assert.Equal(20, b.Aggregate.InclusiveAttributedObservedAllocatedBytes);
            Assert.Equal(40, b.Aggregate.RawReportedAllocatedBytes);
            Assert.Equal(2, b.Aggregate.FileCount);
            Assert.Equal(1, b.Aggregate.DirectoryCount);
            Assert.Equal(a.Aggregate.DirectAttributedObservedAllocatedBytes + b.Aggregate.InclusiveAttributedObservedAllocatedBytes,
                a.Aggregate.InclusiveAttributedObservedAllocatedBytes);
            Assert.Equal("A", Assert.Single(result.AllocationGroups, group => group.Identity.Equals(parentIdentity)).AttributionPath);
            Assert.Equal("A\\B", Assert.Single(result.AllocationGroups, group => group.Identity.Equals(childIdentity)).AttributionPath);
            string json = JsonSerializer.Serialize(result);
            expected ??= json;
            Assert.Equal(expected, json);
        }
    }

    private static StorageHierarchyNode Child(StorageHierarchyNode parent, string relativePath) =>
        Assert.Single(parent.Children, child => child.RelativePath == relativePath);

    private static void AssertLeafEvidence(StorageHierarchyNode node, long rawBytes, long fileCount)
    {
        Assert.Equal(0, node.Aggregate.DirectAttributedObservedAllocatedBytes);
        Assert.Equal(0, node.Aggregate.InclusiveAttributedObservedAllocatedBytes);
        Assert.Equal(rawBytes, node.Aggregate.RawReportedAllocatedBytes);
        Assert.Equal(fileCount, node.Aggregate.FileCount);
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
