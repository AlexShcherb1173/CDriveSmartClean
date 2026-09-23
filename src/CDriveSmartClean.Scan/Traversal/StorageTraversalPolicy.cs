using CDriveSmartClean.Application.Scanning.Observations;

namespace CDriveSmartClean.Scan.Traversal;

public sealed class StorageTraversalPolicy
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance",
        "CA1822:Mark members as static",
        Justification = "The policy is an instance boundary so future policy composition does not change its public contract.")]
    public TraversalDecision Evaluate(StorageObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        return observation.ObjectKind == StorageObjectKind.Directory &&
            observation.ReparseKind == ReparseKind.None
            ? TraversalDecision.TraverseChildren
            : TraversalDecision.ObserveOnly;
    }
}
