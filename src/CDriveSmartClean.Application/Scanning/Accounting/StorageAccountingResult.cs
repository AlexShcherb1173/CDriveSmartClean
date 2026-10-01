using System.Collections.ObjectModel;
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
        var counts = new Dictionary<StorageTraversalIssueKind, long>();
        foreach (var pair in issueCounts)
        {
            if (!Enum.IsDefined(pair.Key)) throw new ArgumentException("Unknown issue kind.", nameof(issueCounts));
            ArgumentOutOfRangeException.ThrowIfNegative(pair.Value);
            counts.Add(pair.Key, pair.Value);
        }
        AllocationGroup[] groups = allocationGroups.OrderBy(g => g.Identity.VolumeIdentity.Id).ThenBy(g => g.Identity.ObjectId).ToArray();
        if (root is null && groups.Length != 0) throw new ArgumentException("Unavailable accounting cannot publish prefix groups.");
        Summary = summary;
        Root = root;
        AllocationGroups = Array.AsReadOnly(groups);
        Reconciliation = reconciliation;
        TraversalCompleted = traversalCompleted;
        IssueCounts = new ReadOnlyDictionary<StorageTraversalIssueKind, long>(counts);
    }
    public StorageAccountingSummary Summary { get; }
    public StorageHierarchyNode? Root { get; }
    public IReadOnlyList<AllocationGroup> AllocationGroups { get; }
    public VolumeReconciliation Reconciliation { get; }
    public bool TraversalCompleted { get; }
    public IReadOnlyDictionary<StorageTraversalIssueKind, long> IssueCounts { get; }
}
