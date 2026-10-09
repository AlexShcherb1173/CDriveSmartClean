namespace CDriveSmartClean.Application.ResourceLimits;

public sealed class ResourceLimitDiagnostic
{
    public ResourceLimitDiagnostic(ResourceLimitStage stage, ResourceLimitDimension dimension,
        long configuredLimit, long observedOrAttemptedValue)
    {
        if (!Enum.IsDefined(stage)) throw new ArgumentOutOfRangeException(nameof(stage));
        if (!Enum.IsDefined(dimension)) throw new ArgumentOutOfRangeException(nameof(dimension));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(configuredLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(observedOrAttemptedValue);
        if (!Matches(stage, dimension))
            throw new ArgumentException("Resource-limit dimension does not belong to the specified stage.",
                nameof(dimension));

        Stage = stage;
        Dimension = dimension;
        ConfiguredLimit = configuredLimit;
        ObservedOrAttemptedValue = observedOrAttemptedValue;
    }

    public ResourceLimitStage Stage { get; }
    public ResourceLimitDimension Dimension { get; }
    public long ConfiguredLimit { get; }
    public long ObservedOrAttemptedValue { get; }

    private static bool Matches(ResourceLimitStage stage, ResourceLimitDimension dimension) => stage switch
    {
        ResourceLimitStage.Accounting => dimension is ResourceLimitDimension.MaximumDistinctPaths or
            ResourceLimitDimension.MaximumIdentities or ResourceLimitDimension.MaximumDirectories or
            ResourceLimitDimension.AccountingStateBudget,
        ResourceLimitStage.Analysis => dimension is ResourceLimitDimension.MaximumPathStates or
            ResourceLimitDimension.MaximumIdentityStates or ResourceLimitDimension.AnalysisStateBudget,
        ResourceLimitStage.Findings => dimension == ResourceLimitDimension.MaximumHierarchyNodes,
        _ => false
    };
}
