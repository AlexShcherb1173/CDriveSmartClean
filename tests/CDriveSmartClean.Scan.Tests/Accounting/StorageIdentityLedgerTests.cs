using System.Text.Json;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Scan.Accounting;
using CDriveSmartClean.Scan.Traversal;
using Xunit;

namespace CDriveSmartClean.Scan.Tests.Accounting;

public sealed class StorageIdentityLedgerTests
{
    [Fact]
    public async Task ConflictingMeasurementAtSamePathIsExcluded()
    {
        var h = new AccountingHarness();
        var id = h.Id();
        var first = h.Entry("same", id);
        var second = h.Entry("same", id, bytes: 20);
        h.Entries = [first, second];
        var result = await h.Run();
        Assert.Equal(0, result.Root!.Aggregate.RawReportedAllocatedBytes);
        Assert.True(result.Summary.Reasons.HasFlag(AccountingReason.ConflictingPathEvidence));
        h.Entries = [second, first];
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(await h.Run()));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AliasesCountOnce(int count)
    {
        var h = new AccountingHarness();
        var id = h.Id();
        h.Entries = Enumerable.Range(0, count).Select(i => h.Entry("a" + i, id)).ToArray();
        var result = await h.Run();
        Assert.Equal(10, result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(count - 1, result.Summary.AliasPathCount);
        Assert.Equal(count, h.Forwarded.Count);
        Assert.Equal(count * 10, result.Root!.Aggregate.RawReportedAllocatedBytes);
    }

    [Fact]
    public async Task IndependentIdentitiesCountSeparately()
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry("a", h.Id()), h.Entry("b", h.Id())];
        Assert.Equal(20, (await h.Run()).Summary.DeduplicatedObservedAllocatedBytes);
    }

    [Fact]
    public async Task SamePathIsIdempotent()
    {
        var h = new AccountingHarness();
        StorageEntry entry = h.Entry("a", h.Id());
        h.Entries = [entry, entry];
        var result = await h.Run();
        Assert.Equal(1, result.Root!.Aggregate.FileCount);
        Assert.Equal(0, result.Summary.AliasPathCount);
        Assert.Equal(2, h.Forwarded.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task IdentityConflictsExcludedAndPermutationIndependent(int variation)
    {
        var h = new AccountingHarness();
        var id = h.Id();
        StorageEntry first = h.Entry("a", id);
        StorageEntry second = h.Entry("b", id, bytes: variation == 0 ? 11 : 10,
            logical: variation == 1 ? 11 : 10, attributes: variation == 2 ? StorageEntryAttributes.Compressed : StorageEntryAttributes.None,
            kind: variation == 3 ? StorageObjectKind.Other : StorageObjectKind.File,
            reparse: variation == 4 ? ReparseKind.Other : ReparseKind.None);
        h.Entries = [first, second];
        var result = await h.Run();
        Assert.Equal(0, result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(1, result.Summary.ConflictedIdentityCount);
        h.Entries = [second, first];
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(await h.Run()));
    }

    [Fact]
    public async Task ConflictingPathInvalidatesAllAffectedGroups()
    {
        var h = new AccountingHarness();
        var a = h.Id();
        var b = h.Id();
        StorageEntry[] entries = [h.Entry("same", a), h.Entry("same", b), h.Entry("alias", a)];
        string? expected = null;
        for (int seed = 0; seed < 12; seed++)
        {
            var random = new Random(seed);
            h.Entries = entries.OrderBy(_ => random.Next()).ToArray();
            var result = await h.Run();
            Assert.Equal(0, result.Summary.DeduplicatedObservedAllocatedBytes);
            Assert.Equal(2, result.Summary.ConflictedIdentityCount);
            Assert.Equal(10, result.Root!.Aggregate.RawReportedAllocatedBytes);
            string json = JsonSerializer.Serialize(result);
            expected ??= json;
            Assert.Equal(expected, json);
        }
    }

    [Theory]
    [InlineData(StorageEntryAttributes.Sparse, true)]
    [InlineData(StorageEntryAttributes.Compressed, true)]
    [InlineData(StorageEntryAttributes.Offline, false)]
    [InlineData(StorageEntryAttributes.RecallOnOpen, false)]
    [InlineData(StorageEntryAttributes.RecallOnDataAccess, false)]
    [InlineData(StorageEntryAttributes.Pinned, false)]
    [InlineData(StorageEntryAttributes.Unpinned, false)]
    public async Task AttributesControlEligibility(StorageEntryAttributes attributes, bool eligible)
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry("a", h.Id(), logical: 1000, attributes: attributes)];
        var result = await h.Run();
        Assert.Equal(eligible ? 10 : 0, result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(eligible ? 0 : 10, result.Summary.UncertainMeasuredAllocatedBytes);
        Assert.Equal(1000, result.Root!.Aggregate.VisibleLogicalMeasuredBytes);
    }

    [Theory]
    [InlineData(StorageObjectKind.Directory, ReparseKind.None)]
    [InlineData(StorageObjectKind.File, ReparseKind.Other)]
    public async Task UnsupportedObjectsRetainRawEvidenceOnly(StorageObjectKind kind, ReparseKind reparse)
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry("object", h.Id(), kind: kind, reparse: reparse)];
        var result = await h.Run();
        Assert.Equal(0, result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(10, result.Summary.UncertainMeasuredAllocatedBytes);
        Assert.Equal(10, result.Root!.Aggregate.RawReportedAllocatedBytes);
        Assert.True(result.Summary.Reasons.HasFlag(AccountingReason.UnsupportedAllocationEvidence));
    }

    [Theory]
    [InlineData(StorageMeasurementAvailability.Available)]
    [InlineData(StorageMeasurementAvailability.Unavailable)]
    [InlineData(StorageMeasurementAvailability.NotApplicable)]
    public async Task AvailabilityAndIdentityAbsenceAreNotAssumedUnique(StorageMeasurementAvailability availability)
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry("a", null, availability: availability)];
        var result = await h.Run();
        Assert.Equal(0, result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(1, result.Root!.Aggregate.IdentityUnavailableCount);
        Assert.Equal(availability == StorageMeasurementAvailability.Available ? 10 : 0, result.Summary.UncertainMeasuredAllocatedBytes);
        Assert.Equal(AccountingQuality.Incomplete, result.Summary.Quality);
    }

    [Fact]
    public async Task MeasuredZeroIsAvailable()
    {
        var h = new AccountingHarness();
        h.Entries = [h.Entry("a", h.Id(), bytes: 0)];
        var result = await h.Run();
        Assert.Equal(0, result.Summary.DeduplicatedObservedAllocatedBytes);
        Assert.Equal(1, result.Root!.Aggregate.AvailableMeasurementCount);
        Assert.Equal(0, Assert.Single(result.AllocationGroups).EligibleReportedAllocatedBytes);
    }
}

internal sealed class AccountingHarness : IStorageEnumerator, IStorageEntrySink, IStorageTraversalIssueSink, IVolumeSpaceProvider
{
    internal SystemVolumeDescriptor Volume { get; } = new(new VolumeIdentity(new Guid("11111111-1111-1111-1111-111111111111")), @"C:\");
    internal StorageEntry[] Entries = [];
    internal readonly List<StorageEntry> Forwarded = [];
    internal readonly List<StorageTraversalIssue> ForwardedIssues = [];
    internal Exception? ChildError;
    internal Exception? EntryError;
    internal Exception? IssueError;
    internal Action? AfterEntry;
    internal bool WrongSnapshotIdentity;
    internal long StartUsed = 100;
    internal long EndUsed = 100;
    internal bool SnapshotUnavailable;
    internal int Snapshots;
    internal StorageObjectIdentity Id() => new(Volume.VolumeIdentity, Guid.NewGuid());
    internal StorageEntry Entry(string relative, StorageObjectIdentity? id, long bytes = 10, long logical = 10,
        StorageEntryAttributes attributes = StorageEntryAttributes.None, StorageObjectKind kind = StorageObjectKind.File,
        ReparseKind reparse = ReparseKind.None, StorageMeasurementAvailability availability = StorageMeasurementAvailability.Available)
    {
        var scope = reparse != ReparseKind.None ? StorageMeasurementScope.ReparseEntryMetadata :
            kind == StorageObjectKind.Directory ? StorageMeasurementScope.DirectoryEntryMetadata : StorageMeasurementScope.FileContent;
        var measurement = availability == StorageMeasurementAvailability.Available
            ? new StorageMeasurement(logical, bytes, availability, StorageMeasurementQuality.FileSystemReported,
                StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo, scope, StorageMeasurementFreshness.LivePointInTime)
            : new StorageMeasurement(null, null, availability, StorageMeasurementQuality.Unknown,
                StorageMeasurementSource.None, scope, StorageMeasurementFreshness.Unknown);
        return new StorageEntry(Volume.VolumeIdentity, id, Volume.RootPath + relative, kind, reparse, measurement, attributes);
    }
    internal Task<StorageAccountingResult> Run(StorageAccountingOptions? options = null, CancellationToken? token = null)
    {
        Snapshots = 0;
        Forwarded.Clear();
        ForwardedIssues.Clear();
        return new StorageAccountingEngine(new StorageTreeWalker(this, new StorageTraversalPolicy()), this, options ?? new())
            .AccountAsync(Volume, this, this, token ?? TestContext.Current.CancellationToken);
    }
    public async Task EnumerateRootAsync(SystemVolumeDescriptor systemVolume, IStorageEntrySink sink, CancellationToken token)
    {
        Assert.Equal(1, Snapshots);
        foreach (StorageEntry entry in Entries) await sink.WriteAsync(entry, token);
    }
    public Task EnumerateChildrenAsync(SystemVolumeDescriptor systemVolume, StorageEntry directory, IStorageEntrySink sink, CancellationToken token)
        => ChildError is null ? Task.CompletedTask : Task.FromException(ChildError);
    public ValueTask WriteAsync(StorageEntry entry, CancellationToken token)
    {
        if (EntryError is not null) throw EntryError;
        Forwarded.Add(entry);
        AfterEntry?.Invoke();
        return ValueTask.CompletedTask;
    }
    public ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken token)
    {
        if (IssueError is not null) throw IssueError;
        ForwardedIssues.Add(issue);
        return ValueTask.CompletedTask;
    }
    public VolumeSpaceSnapshot GetVolumeSpace(SystemVolumeDescriptor volume)
    {
        if (WrongSnapshotIdentity)
            return VolumeSpaceSnapshot.Unavailable(new(Guid.NewGuid()), DateTimeOffset.UnixEpoch, VolumeSpaceFailure.NativeFailure);
        long used = Snapshots++ == 0 ? StartUsed : EndUsed;
        return SnapshotUnavailable ? VolumeSpaceSnapshot.Unavailable(volume.VolumeIdentity, DateTimeOffset.UnixEpoch, VolumeSpaceFailure.NativeFailure) :
            VolumeSpaceSnapshot.Available(volume.VolumeIdentity, DateTimeOffset.UnixEpoch, 1000, 1000 - used, 1000, 1000 - used, used, 0, 0);
    }
}
