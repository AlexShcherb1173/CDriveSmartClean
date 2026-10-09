namespace CDriveSmartClean.Domain.Storage;

public sealed class AllocationGroup
{
    public AllocationGroup(StorageObjectIdentity identity, IEnumerable<string> paths, long? eligibleReportedAllocatedBytes,
        AccountingReason reasons, string attributionPath)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(attributionPath);
        if (eligibleReportedAllocatedBytes < 0) throw new ArgumentOutOfRangeException(nameof(eligibleReportedAllocatedBytes));
        string[] ordered;
        if (paths is string[] { Length: 1 } single)
        {
            ordered = [single[0]];
        }
        else
        {
            ordered = paths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        }
        if (ordered.Length == 0 || ordered.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Paths are required.", nameof(paths));
        if (reasons != AccountingReason.None && eligibleReportedAllocatedBytes is not null)
            throw new ArgumentException("Ineligible evidence cannot have authoritative allocation.");
        Identity = identity;
        Paths = Array.AsReadOnly(ordered);
        EligibleReportedAllocatedBytes = eligibleReportedAllocatedBytes;
        Reasons = reasons;
        AttributionPath = attributionPath;
    }
    public StorageObjectIdentity Identity { get; }
    public IReadOnlyList<string> Paths { get; }
    public long AliasPathCount => checked((long)Paths.Count - 1);
    public long? EligibleReportedAllocatedBytes { get; }
    public AccountingReason Reasons { get; }
    public bool IsConflicted => (Reasons & (AccountingReason.ConflictingIdentityEvidence | AccountingReason.ConflictingPathEvidence)) != 0;
    public string AttributionPath { get; }
}
