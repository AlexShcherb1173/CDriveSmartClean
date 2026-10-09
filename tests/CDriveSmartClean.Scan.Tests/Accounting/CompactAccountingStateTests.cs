using System.Reflection;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Scan.Accounting;
using Xunit;

namespace CDriveSmartClean.Scan.Tests.Accounting;

public sealed class CompactAccountingStateTests
{
    [Fact]
    public void CommonCaseUsesInlineIdsWithoutExceptionalCollections()
    {
        var harness = new AccountingHarness();
        var ledger = Ledger();
        StorageObjectIdentity identity = Identity(harness, 1);

        ledger.Add(harness.Entry("one", identity), "one");

        Assert.Equal(1, ledger.PathCount);
        Assert.Equal(1, ledger.IdentityCount);
        Assert.Equal(0, ledger.ExceptionalIdentityPathEdgeCount);
        Assert.Equal(0, ledger.ExceptionalPathIdentityEdgeCount);
        Assert.True(typeof(StorageIdentityLedger.PathId).IsValueType);
        Assert.True(typeof(StorageIdentityLedger.IdentityId).IsValueType);
        Assert.Equal(256 + StorageIdentityLedger.CompactCommonCaseCharge(3), ledger.ChargedState);
    }

    [Fact]
    public void AliasAndConflictEdgesAreAllocatedOnlyOnTransition()
    {
        var harness = new AccountingHarness();
        var aliasLedger = Ledger();
        StorageObjectIdentity aliasIdentity = Identity(harness, 1);
        aliasLedger.Add(harness.Entry("one", aliasIdentity), "one");
        aliasLedger.Add(harness.Entry("two", aliasIdentity), "two");
        aliasLedger.Add(harness.Entry("three", aliasIdentity), "three");

        Assert.Equal(2, aliasLedger.ExceptionalIdentityPathEdgeCount);
        Assert.Equal(0, aliasLedger.ExceptionalPathIdentityEdgeCount);
        AllocationGroup aliasGroup = Assert.Single(aliasLedger.Finish(CancellationToken.None).Snapshot.CreateAllocationGroups());
        Assert.Equal(["one", "three", "two"], aliasGroup.Paths);

        var conflictLedger = Ledger();
        conflictLedger.Add(harness.Entry("same", Identity(harness, 2)), "same");
        conflictLedger.Add(harness.Entry("same", Identity(harness, 3)), "same");

        Assert.Equal(0, conflictLedger.ExceptionalIdentityPathEdgeCount);
        Assert.Equal(1, conflictLedger.ExceptionalPathIdentityEdgeCount);
        Assert.All(conflictLedger.Finish(CancellationToken.None).Snapshot.CreateAllocationGroups(),
            group => Assert.True(group.Reasons.HasFlag(AccountingReason.ConflictingPathEvidence)));
    }

    [Fact]
    public void MeasurementConflictUsesInlineConflictStateWithoutAssociationGrowth()
    {
        var harness = new AccountingHarness();
        var ledger = Ledger();
        StorageObjectIdentity identity = Identity(harness, 1);
        ledger.Add(harness.Entry("same", identity), "same");
        ledger.Add(harness.Entry("same", identity, bytes: 20), "same");

        Assert.Equal(0, ledger.ExceptionalIdentityPathEdgeCount);
        Assert.Equal(0, ledger.ExceptionalPathIdentityEdgeCount);
        AllocationGroup group = Assert.Single(ledger.Finish(CancellationToken.None).Snapshot.CreateAllocationGroups());
        Assert.True(group.Reasons.HasFlag(AccountingReason.ConflictingPathEvidence));
        Assert.Null(group.EligibleReportedAllocatedBytes);
    }

    [Fact]
    public void DeepHierarchyFinalizesIterativelyWithoutGlobalAncestorTable()
    {
        long charged = 0;
        var hierarchy = new StorageHierarchyAccumulator(value => charged = checked(charged + value), 5_000);
        string path = string.Join('\\', Enumerable.Range(0, 2_048).Select(index => $"d{index}"));

        hierarchy.Directory(path);
        StorageHierarchyNode root = hierarchy.Finish(CancellationToken.None);

        Assert.Equal(2_049, hierarchy.DirectoryCount);
        Assert.Null(typeof(StorageHierarchyAccumulator).GetMethod("Ancestors",
            BindingFlags.Instance | BindingFlags.NonPublic));
        int depth = 0;
        StorageHierarchyNode node = root;
        while (node.Children.Count != 0)
        {
            node = Assert.Single(node.Children);
            depth++;
        }
        Assert.Equal(2_048, depth);
        Assert.True(charged > 0);
    }

    [Fact]
    public void HighAliasCountUsesBoundedParentWalkForLca()
    {
        var harness = new AccountingHarness();
        var ledger = Ledger(maximumPaths: 5_000, maximumDirectories: 10);
        StorageObjectIdentity identity = Identity(harness, 1);
        for (int index = 0; index < 2_000; index++)
        {
            string path = index % 2 == 0 ? $"left\\f{index}" : $"right\\f{index}";
            ledger.Add(harness.Entry(path, identity), path);
        }

        Assert.Equal(1_999, ledger.ExceptionalIdentityPathEdgeCount);
        AllocationGroup group = Assert.Single(ledger.Finish(CancellationToken.None).Snapshot.CreateAllocationGroups());

        Assert.Equal(2_000, group.Paths.Count);
        Assert.Equal(string.Empty, group.AttributionPath);
    }

    [Fact]
    public void CancellationRemainsAuthoritativeDuringFinalization()
    {
        var harness = new AccountingHarness();
        var ledger = Ledger(maximumPaths: 2_000);
        for (int index = 0; index < 1_000; index++)
        {
            string path = $"f{index}";
            ledger.Add(harness.Entry(path, Identity(harness, index + 1)), path);
        }
        using var source = new CancellationTokenSource();
        source.Cancel();

        Assert.Throws<OperationCanceledException>(() => ledger.Finish(source.Token));
    }

    [Fact]
    public void CapacityProbeReportsDeterministicCompactCharges()
    {
        const int pathLength = 19;
        const int maximum = 500_000;
        var harness = new AccountingHarness();
        var ledger = Ledger(maximumIdentities: maximum, maximumPaths: maximum, budget: long.MaxValue);
        long commonCharge = StorageIdentityLedger.CompactCommonCaseCharge(pathLength);
        long legacyCharge = StorageIdentityLedger.LegacyCommonCaseCharge(pathLength);
        var observations = new Dictionary<int, long>();

        for (int index = 0; index < maximum; index++)
        {
            string path = $"file-{index:D10}.bin";
            ledger.Add(harness.Entry(path, Identity(harness, index + 1)), path);
            int count = index + 1;
            if (count is 100_000 or 250_000 or 500_000) observations.Add(count, ledger.ChargedState);
        }

        Assert.Equal(256 + 100_000L * commonCharge, observations[100_000]);
        Assert.Equal(256 + 250_000L * commonCharge, observations[250_000]);
        Assert.Equal(256 + 500_000L * commonCharge, observations[500_000]);
        Assert.True(commonCharge * 100 <= legacyCharge * 50);
    }

    private static StorageIdentityLedger Ledger(int maximumIdentities = 10_000, int maximumPaths = 10_000,
        int maximumDirectories = 10_000, long budget = 512L * 1024 * 1024) =>
        new(new StorageAccountingOptions(maximumIdentities, maximumPaths, maximumDirectories, budget));

    private static StorageObjectIdentity Identity(AccountingHarness harness, int value) =>
        new(harness.Volume.VolumeIdentity, new Guid(value, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
}
