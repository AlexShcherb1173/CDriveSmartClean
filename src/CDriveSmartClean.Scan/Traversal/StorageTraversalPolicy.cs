using CDriveSmartClean.Application.Scanning.Enumeration;
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

        return EvaluateKinds(observation.ObjectKind, observation.ReparseKind, StorageEntryAttributes.None);
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Performance", "CA1822:Mark members as static",
        Justification = "Preserves the instance policy boundary for composition.")]
    public TraversalDecision Evaluate(StorageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return EvaluateKinds(entry.ObjectKind, entry.ReparseKind, entry.Attributes);
    }

    private static TraversalDecision EvaluateKinds(StorageObjectKind objectKind, ReparseKind reparseKind,
        StorageEntryAttributes attributes) =>
        objectKind == StorageObjectKind.Directory && reparseKind == ReparseKind.None &&
        !StorageEntryAttributePolicy.IsRecallSensitive(attributes)
            ? TraversalDecision.TraverseChildren
            : TraversalDecision.ObserveOnly;
}
