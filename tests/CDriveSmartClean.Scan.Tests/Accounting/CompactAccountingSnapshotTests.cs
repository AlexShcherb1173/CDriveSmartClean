using System.Text.Json;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Scan.Accounting;
using Xunit;

namespace CDriveSmartClean.Scan.Tests.Accounting;

public sealed class CompactAccountingSnapshotTests
{
    [Fact]
    public async Task ProductionResultProjectsOnlyRequestedAllocationGroups()
    {
        var harness = new AccountingHarness();
        harness.Entries =
        [
            harness.Entry("three", Identity(harness, 3)),
            harness.Entry("one", Identity(harness, 1)),
            harness.Entry("two", Identity(harness, 2)),
        ];

        StorageAccountingResult result = await harness.Run();

        Assert.Equal(3, result.AllocationGroups.Count);
        Assert.Equal(0, result.ProjectedAllocationGroupCount);
        Assert.Equal(Identity(harness, 1), result.AllocationGroups[0].Identity);
        Assert.Equal(1, result.ProjectedAllocationGroupCount);
        Assert.Equal(Identity(harness, 1), result.AllocationGroups[0].Identity);
        Assert.Equal(2, result.ProjectedAllocationGroupCount);
        Assert.IsType<CompactAccountingSnapshot.CompactAllocationGroupList>(result.AllocationGroups);
    }

    [Fact]
    public void SealTransfersFactsAndReleasesMutableTraversalState()
    {
        var harness = new AccountingHarness();
        var ledger = Ledger();
        StorageObjectIdentity identity = Identity(harness, 1);
        ledger.Add(harness.Entry("alpha", identity), "alpha");
        ledger.Add(harness.Entry("beta", identity, attributes: StorageEntryAttributes.Compressed), "beta");

        var sealedResult = ledger.Finish(CancellationToken.None);
        CompactAccountingSnapshot snapshot = sealedResult.Snapshot;

        Assert.True(ledger.TraversalStateReleased);
        Assert.Equal(2, snapshot.PathCount);
        Assert.Equal(1, snapshot.IdentityCount);
        Assert.Equal(2, snapshot.GetIdentityPaths(0).Length);
        Assert.All(snapshot.GetIdentityPaths(0).ToArray(), pathId =>
            Assert.Contains(0, snapshot.GetPathIdentities(pathId).ToArray()));
        Assert.Contains(snapshot.GetIdentityPaths(0).ToArray(), pathId =>
            snapshot.GetPath(pathId).Attributes.HasFlag(StorageEntryAttributes.Compressed));
        Assert.Throws<InvalidOperationException>(() => ledger.Add(harness.Entry("later", identity), "later"));
    }

    [Fact]
    public void InvalidCrossReferenceCannotPublishSnapshot()
    {
        var harness = new AccountingHarness();
        StorageObjectIdentity identity = Identity(harness, 1);
        StorageEntry entry = harness.Entry("one", identity);
        CompactAccountingSnapshot.PathFact[] paths =
        [
            new("one", entry.Measurement, entry.ObjectKind, entry.ReparseKind, entry.Attributes,
                false, 0, 1),
        ];
        CompactAccountingSnapshot.IdentityFact[] identities =
        [
            new(identity, entry.Measurement, entry.ObjectKind, entry.ReparseKind, entry.Attributes,
                AccountingReason.None, "", 0, 1),
        ];

        Assert.Throws<InvalidOperationException>(() =>
            new CompactAccountingSnapshot(paths, identities, [0], [], CancellationToken.None));
    }

    [Fact]
    public async Task CompactProductionResultMatchesPublicEagerContract()
    {
        var harness = new AccountingHarness();
        StorageObjectIdentity alias = Identity(harness, 4);
        harness.Entries =
        [
            harness.Entry("zeta", Identity(harness, 8)),
            harness.Entry("Alias\\beta", alias),
            harness.Entry("Alias\\Alpha", alias),
            harness.Entry("conflict", Identity(harness, 6)),
            harness.Entry("conflict", Identity(harness, 7)),
            harness.Entry("unknown", null),
            harness.Entry("offline", Identity(harness, 5), attributes: StorageEntryAttributes.Offline),
            harness.Entry("Юникод\\файл", Identity(harness, 2)),
            harness.Entry("case", Identity(harness, 3)),
        ];

        StorageAccountingResult compact = await harness.Run();
        var eager = new StorageAccountingResult(compact.Summary, compact.Root,
            compact.AllocationGroups.Select(Clone), compact.Reconciliation,
            compact.TraversalCompleted, compact.IssueCounts);

        var options = new JsonSerializerOptions { MaxDepth = 2_048 };
        Assert.Equal(JsonSerializer.Serialize(eager, options), JsonSerializer.Serialize(compact, options));
    }

    [Fact]
    public async Task DeepHierarchyCompactResultMatchesPublicEagerContract()
    {
        var harness = new AccountingHarness();
        string path = string.Join('\\', Enumerable.Range(0, 512).Select(index => $"d{index}")) + "\\file";
        harness.Entries = [harness.Entry(path, Identity(harness, 1))];

        StorageAccountingResult compact = await harness.Run();
        var eager = new StorageAccountingResult(compact.Summary, compact.Root,
            compact.AllocationGroups.Select(Clone), compact.Reconciliation,
            compact.TraversalCompleted, compact.IssueCounts);

        var options = new JsonSerializerOptions { MaxDepth = 2_048 };
        Assert.Equal(JsonSerializer.Serialize(eager, options), JsonSerializer.Serialize(compact, options));
    }

    [Fact]
    public void FinalizationCapacityProbeSealsWithoutMaterializingGroups()
    {
        (long Pre, long Post) at100K = Probe(100_000);
        (long Pre, long Post) at250K = Probe(250_000);
        (long Pre, long Post) at500K = Probe(500_000);

        Assert.Equal((29_400_256, 47_801_664), at100K);
        Assert.Equal((73_500_256, 119_501_664), at250K);
        Assert.Equal((147_000_256, 239_001_664), at500K);
        Assert.Equal(184, (at500K.Post - at500K.Pre - 1_408) / 500_000);
    }

    [Fact]
    public void CancellationPreventsSnapshotPublication()
    {
        var harness = new AccountingHarness();
        var ledger = Ledger();
        for (int index = 0; index < 1_000; index++)
        {
            string path = $"f{index}";
            ledger.Add(harness.Entry(path, Identity(harness, index + 1)), path);
        }
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => ledger.Finish(source.Token));
        Assert.False(ledger.TraversalStateReleased);
    }

    private static (long Pre, long Post) Probe(int count)
    {
        var harness = new AccountingHarness();
        var ledger = Ledger(maximum: count, budget: long.MaxValue);
        for (int index = 0; index < count; index++)
        {
            string path = $"file-{index:D10}.bin";
            ledger.Add(harness.Entry(path, Identity(harness, index + 1)), path);
        }
        long pre = ledger.ChargedState;

        var sealedResult = ledger.Finish(CancellationToken.None);
        CompactAccountingSnapshot.CompactAllocationGroupList groups = sealedResult.Snapshot.CreateAllocationGroups();

        Assert.Equal(count, groups.Count);
        Assert.Equal(0, groups.MaterializedCount);
        Assert.True(ledger.TraversalStateReleased);
        return (pre, ledger.ChargedState);
    }

    private static AllocationGroup Clone(AllocationGroup group) =>
        new(group.Identity, group.Paths, group.EligibleReportedAllocatedBytes, group.Reasons, group.AttributionPath);

    private static StorageIdentityLedger Ledger(int maximum = 10_000, long budget = 512L * 1024 * 1024) =>
        new(new StorageAccountingOptions(maximum, maximum, maximum, budget));

    private static StorageObjectIdentity Identity(AccountingHarness harness, int value) =>
        new(harness.Volume.VolumeIdentity, new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
}
