namespace CDriveSmartClean.Application.Analysis;

public sealed class StorageAnalysisOptions
{
    public const int MaximumCandidateLimit = 1000;
    public const long DefaultAnalysisStateBudget = 256L * 1024 * 1024;

    public StorageAnalysisOptions(int maximumPathStates = 1_000_000, int maximumIdentityStates = 1_000_000,
        int candidateLimit = 100, long analysisStateBudget = DefaultAnalysisStateBudget)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumPathStates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumIdentityStates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(candidateLimit);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(candidateLimit, MaximumCandidateLimit);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(analysisStateBudget);
        MaximumPathStates = maximumPathStates;
        MaximumIdentityStates = maximumIdentityStates;
        CandidateLimit = candidateLimit;
        AnalysisStateBudget = analysisStateBudget;
    }

    public int MaximumPathStates { get; }
    public int MaximumIdentityStates { get; }
    public int CandidateLimit { get; }
    public long AnalysisStateBudget { get; }
}
