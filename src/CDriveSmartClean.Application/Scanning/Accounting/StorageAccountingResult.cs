using System.Collections.ObjectModel;
using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning.Accounting;

public sealed class StorageAccountingResult
{
    public StorageAccountingResult(StorageAccountingSummary summary, StorageHierarchyNode? root,
        IEnumerable<AllocationGroup> allocationGroups, VolumeReconciliation reconciliation,
        bool traversalCompleted, IReadOnlyDictionary<StorageTraversalIssueKind, long> issueCounts)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(allocationGroups);
        ArgumentNullException.ThrowIfNull(reconciliation);
        ArgumentNullException.ThrowIfNull(issueCounts);
        if ((summary.Quality == AccountingQuality.Unavailable) != (root is null))
            throw new ArgumentException("Unavailable accounting has no authoritative hierarchy.");
        ReadOnlyDictionary<StorageTraversalIssueKind, long> counts = CopyIssueCounts(issueCounts);
        AllocationGroup[] groups = allocationGroups.OrderBy(g => g.Identity.VolumeIdentity.Id).ThenBy(g => g.Identity.ObjectId).ToArray();
        if (root is null && groups.Length != 0) throw new ArgumentException("Unavailable accounting cannot publish prefix groups.");
        Summary = summary;
        Root = root;
        AllocationGroups = Array.AsReadOnly(groups);
        Reconciliation = reconciliation;
        TraversalCompleted = traversalCompleted;
        IssueCounts = counts;
    }

    internal StorageAccountingResult(StorageAccountingSummary summary, StorageHierarchyNode? root,
        CompactAccountingSnapshot? compactSnapshot, VolumeReconciliation reconciliation,
        bool traversalCompleted, IReadOnlyDictionary<StorageTraversalIssueKind, long> issueCounts,
        ResourceLimitDiagnostic? resourceLimitDiagnostic = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(reconciliation);
        ArgumentNullException.ThrowIfNull(issueCounts);
        if ((summary.Quality == AccountingQuality.Unavailable) != (root is null))
            throw new ArgumentException("Unavailable accounting has no authoritative hierarchy.");
        if ((root is null) != (compactSnapshot is null))
            throw new ArgumentException("Authoritative accounting requires a compact snapshot.", nameof(compactSnapshot));
        ValidateDiagnostic(summary, resourceLimitDiagnostic);
        Summary = summary;
        Root = root;
        CompactSnapshot = compactSnapshot;
        AllocationGroups = compactSnapshot is null
            ? Array.AsReadOnly(Array.Empty<AllocationGroup>())
            : compactSnapshot.CreateAllocationGroups();
        Reconciliation = reconciliation;
        TraversalCompleted = traversalCompleted;
        IssueCounts = CopyIssueCounts(issueCounts);
        ResourceLimitDiagnostic = resourceLimitDiagnostic;
    }
    public StorageAccountingSummary Summary { get; }
    public StorageHierarchyNode? Root { get; }
    public IReadOnlyList<AllocationGroup> AllocationGroups { get; }
    public VolumeReconciliation Reconciliation { get; }
    public bool TraversalCompleted { get; }
    public IReadOnlyDictionary<StorageTraversalIssueKind, long> IssueCounts { get; }
    internal CompactAccountingSnapshot? CompactSnapshot { get; }
    internal ResourceLimitDiagnostic? ResourceLimitDiagnostic { get; }
    internal int ProjectedAllocationGroupCount =>
        (AllocationGroups as CompactAccountingSnapshot.CompactAllocationGroupList)?.MaterializedCount ?? AllocationGroups.Count;

    private static ReadOnlyDictionary<StorageTraversalIssueKind, long> CopyIssueCounts(
        IReadOnlyDictionary<StorageTraversalIssueKind, long> issueCounts)
    {
        var counts = new Dictionary<StorageTraversalIssueKind, long>();
        foreach (var pair in issueCounts)
        {
            if (!Enum.IsDefined(pair.Key)) throw new ArgumentException("Unknown issue kind.", nameof(issueCounts));
            ArgumentOutOfRangeException.ThrowIfNegative(pair.Value);
            counts.Add(pair.Key, pair.Value);
        }
        return new ReadOnlyDictionary<StorageTraversalIssueKind, long>(counts);
    }

    private static void ValidateDiagnostic(StorageAccountingSummary summary,
        ResourceLimitDiagnostic? resourceLimitDiagnostic)
    {
        if (resourceLimitDiagnostic is null) return;
        if (resourceLimitDiagnostic.Stage != ResourceLimitStage.Accounting ||
            !summary.Reasons.HasFlag(AccountingReason.ResourceLimit))
            throw new ArgumentException("Accounting diagnostic requires a direct accounting resource limit.",
                nameof(resourceLimitDiagnostic));
    }
}
