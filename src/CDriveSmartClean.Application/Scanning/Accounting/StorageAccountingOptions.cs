namespace CDriveSmartClean.Application.Scanning.Accounting;

public sealed class StorageAccountingOptions
{
    public StorageAccountingOptions(int maximumIdentities = 1_500_000, int maximumDistinctPaths = 1_500_000,
        int maximumDirectories = 250_000, long accountingStateBudget = 1536L * 1024 * 1024)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumIdentities);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDistinctPaths);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumDirectories);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accountingStateBudget);
        MaximumIdentities = maximumIdentities;
        MaximumDistinctPaths = maximumDistinctPaths;
        MaximumDirectories = maximumDirectories;
        AccountingStateBudget = accountingStateBudget;
    }
    public int MaximumIdentities { get; }
    public int MaximumDistinctPaths { get; }
    public int MaximumDirectories { get; }
    /// <summary>Deterministic estimated state budget, not a managed-heap measurement.</summary>
    public long AccountingStateBudget { get; }
}
