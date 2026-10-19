namespace CDriveSmartClean.Application.Analysis;

public sealed class UniversalFindingOptions
{
    public UniversalFindingOptions(
        int maximumFindingsPerView = 25,
        int maximumTotalFindings = 100,
        int maximumHierarchyNodes = 250_000)
    {
        if (maximumFindingsPerView is <= 0 or > 250)
            throw new ArgumentOutOfRangeException(nameof(maximumFindingsPerView));
        if (maximumTotalFindings is < 5 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(maximumTotalFindings));
        if (maximumFindingsPerView > maximumTotalFindings)
            throw new ArgumentException(
                "Per-view limit cannot exceed the total finding limit.",
                nameof(maximumFindingsPerView));
        if (maximumHierarchyNodes is <= 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(maximumHierarchyNodes));

        MaximumFindingsPerView = maximumFindingsPerView;
        MaximumTotalFindings = maximumTotalFindings;
        MaximumHierarchyNodes = maximumHierarchyNodes;
    }

    public int MaximumFindingsPerView { get; }
    public int MaximumTotalFindings { get; }
    public int MaximumHierarchyNodes { get; }
}
