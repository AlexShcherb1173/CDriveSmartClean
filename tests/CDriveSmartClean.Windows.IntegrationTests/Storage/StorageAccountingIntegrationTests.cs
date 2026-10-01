using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Storage;
using CDriveSmartClean.Scan.Accounting;
using CDriveSmartClean.Scan.Traversal;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

[Collection("WindowsNativeTraversal")]
public sealed class StorageAccountingIntegrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeHardLinksDeduplicateAndPreservePaths(bool crossDirectory)
    {
        using var fixture = new WindowsStorageFixture();
        string alias = crossDirectory ? "child\\alias.bin" : "alias.bin";
        fixture.AddHardLink(alias, "ordinary.bin");
        var sink = new Sink();
        var engine = new StorageAccountingEngine(new StorageTreeWalker(fixture.Enumerator, new StorageTraversalPolicy()), new FixtureSnapshot(), new());
        var result = await engine.AccountAsync(fixture.Volume, sink, sink, TestContext.Current.CancellationToken);
        StorageEntry ordinary = Assert.Single(sink.Entries, e => e.CanonicalPath == fixture.Ordinary);
        StorageEntry link = Assert.Single(sink.Entries, e => e.CanonicalPath == fixture.Owned(alias));
        Assert.Equal(ordinary.ObjectIdentity, link.ObjectIdentity);
        Assert.Equal(ordinary.Measurement, link.Measurement);
        AllocationGroup group = Assert.Single(result.AllocationGroups, g => g.Identity.Equals(ordinary.ObjectIdentity));
        Assert.Equal(2, group.Paths.Count);
        Assert.Equal("", group.AttributionPath);
        Assert.Equal(ordinary.Measurement.ReportedAllocatedBytes, group.EligibleReportedAllocatedBytes);
        long expected = sink.Entries.Where(e => e.ObjectKind == Application.Scanning.Observations.StorageObjectKind.File)
            .GroupBy(e => e.ObjectIdentity).Sum(g => g.First().Measurement.ReportedAllocatedBytes!.Value);
        Assert.Equal(expected, result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Empty(sink.Issues);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SparseAndCompressedEvidenceIsNotReplaced(bool compressed)
    {
        using var fixture = new WindowsStorageFixture();
        bool supported = compressed ? fixture.TryAddCompressedFile("special.bin", out _, out _) :
            fixture.TryAddSparseFile("special.bin", out _, out _);
        Assert.True(supported, "This integration gate requires the disposable NTFS sparse/compression capability.");
        var sink = new Sink();
        var result = await new StorageAccountingEngine(new StorageTreeWalker(fixture.Enumerator, new StorageTraversalPolicy()), new FixtureSnapshot(), new())
            .AccountAsync(fixture.Volume, sink, sink, TestContext.Current.CancellationToken);
        StorageEntry special = Assert.Single(sink.Entries, e => e.CanonicalPath == fixture.Owned("special.bin"));
        AllocationGroup group = Assert.Single(result.AllocationGroups, g => g.Identity.Equals(special.ObjectIdentity));
        Assert.Equal(special.Measurement.ReportedAllocatedBytes, group.EligibleReportedAllocatedBytes);
        Assert.True(special.Measurement.LogicalBytes > 0);
    }

    private sealed class FixtureSnapshot : IVolumeSpaceProvider
    {
        public VolumeSpaceSnapshot GetVolumeSpace(SystemVolumeDescriptor volume) =>
            VolumeSpaceSnapshot.Available(volume.VolumeIdentity, DateTimeOffset.UnixEpoch, 1_000_000_000, 0, 1_000_000_000, 0, 1_000_000_000, 0, 0);
    }
    private sealed class Sink : IStorageEntrySink, IStorageTraversalIssueSink
    {
        internal readonly List<StorageEntry> Entries = [];
        internal readonly List<StorageTraversalIssue> Issues = [];
        public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken) { Entries.Add(entry); return ValueTask.CompletedTask; }
        public ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken cancellationToken) { Issues.Add(issue); return ValueTask.CompletedTask; }
    }
}
