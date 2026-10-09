using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Analysis;

internal sealed record AnalysisEntryFact(StorageObjectIdentity? Identity, StorageMeasurement Measurement,
    StorageObjectKind Kind, ReparseKind Reparse, StorageEntryAttributes Attributes)
{
    internal AccountingReason Eligibility
    {
        get
        {
            AccountingReason result = AccountingReason.None;
            if (Identity is null) result |= AccountingReason.IdentityUnavailable;
            if (Measurement.Availability != StorageMeasurementAvailability.Available)
                result |= AccountingReason.MeasurementUnavailable;
            if (Kind != StorageObjectKind.File || Reparse != ReparseKind.None ||
                Measurement.Scope != StorageMeasurementScope.FileContent ||
                (Attributes & ~(StorageEntryAttributes.Sparse | StorageEntryAttributes.Compressed)) != 0)
                result |= AccountingReason.UnsupportedAllocationEvidence;
            return result;
        }
    }
}

internal sealed class AnalysisPathState(
    string relativePath,
    AnalysisEntryFact fact,
    DeterministicClassificationEngine.Result classification)
{
    internal string RelativePath { get; } = relativePath;
    internal AnalysisEntryFact Fact { get; } = fact;
    internal DeterministicClassificationEngine.Result Classification { get; } = classification;
    internal HashSet<StorageObjectIdentity> Identities { get; } = [];
    internal bool Poisoned { get; set; }
}

internal sealed class AnalysisIdentityState(AnalysisEntryFact fact)
{
    internal AnalysisEntryFact ReferenceFact { get; } = fact;
    internal HashSet<string> Paths { get; } = new(StringComparer.Ordinal);
    internal AccountingReason Reasons { get; set; } = fact.Eligibility;
}

internal sealed class StorageAnalysisSession : IStorageAnalysisSession
{
    internal static long LegacyCommonCaseCharge(int relativePathLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(relativePathLength);
        return checked(2304L + relativePathLength * 8L);
    }

    private readonly StorageAnalysisRequest request;
    private readonly DeterministicClassificationEngine classifier;
    private readonly AnalysisResourceGuard resourceGuard;
    private readonly Dictionary<string, AnalysisPathState> paths = new(StringComparer.Ordinal);
    private readonly Dictionary<StorageObjectIdentity, AnalysisIdentityState> identities = [];
    private AnalysisReason reasons;
    private ResourceLimitDiagnostic? resourceLimitDiagnostic;
    private bool disabled;
    private bool completed;

    internal StorageAnalysisSession(StorageAnalysisRequest request)
    {
        this.request = request;
        classifier = new DeterministicClassificationEngine(request.ClassificationContext);
        resourceGuard = new AnalysisResourceGuard(request.Options.AnalysisStateBudget);
        if (!request.ClassificationContext.IsComplete)
            reasons |= AnalysisReason.ClassificationContextIncomplete;
    }

    public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(entry);
        if (completed) throw new InvalidOperationException("Analysis session is already complete.");
        if (!entry.VolumeIdentity.Equals(request.SystemVolume.VolumeIdentity))
            throw new InvalidOperationException("Cross-volume analysis entry.");
        string relative = RelativePath(entry.CanonicalPath);
        if (disabled) return ValueTask.CompletedTask;

        var fact = new AnalysisEntryFact(entry.ObjectIdentity, entry.Measurement, entry.ObjectKind,
            entry.ReparseKind, entry.Attributes);
        bool newPath = !paths.TryGetValue(relative, out AnalysisPathState? existing);
        bool newIdentity = entry.ObjectIdentity is { } identity && !identities.ContainsKey(identity);
        bool newAssociation = entry.ObjectIdentity is { } associatedIdentity &&
            (newIdentity || !identities[associatedIdentity].Paths.Contains(relative));
        if (newPath && paths.Count >= request.Options.MaximumPathStates)
        {
            Disable(AnalysisReason.ResourceLimit, new ResourceLimitDiagnostic(
                ResourceLimitStage.Analysis, ResourceLimitDimension.MaximumPathStates,
                request.Options.MaximumPathStates, paths.Count + 1L));
            return ValueTask.CompletedTask;
        }
        if (newIdentity && identities.Count >= request.Options.MaximumIdentityStates)
        {
            Disable(AnalysisReason.ResourceLimit, new ResourceLimitDiagnostic(
                ResourceLimitStage.Analysis, ResourceLimitDimension.MaximumIdentityStates,
                request.Options.MaximumIdentityStates, identities.Count + 1L));
            return ValueTask.CompletedTask;
        }

        long charge;
        try
        {
            charge = checked((newPath ? 768L + relative.Length * 8L : 0L) +
                (newIdentity ? 768L : 0L) + (newPath ? 256L : 0L) +
                (newAssociation ? 128L : 0L));
        }
        catch (OverflowException)
        {
            Disable(AnalysisReason.ArithmeticOverflow);
            return ValueTask.CompletedTask;
        }
        if (!resourceGuard.TryCharge(charge, out bool overflow, out long attempted))
        {
            Disable(overflow ? AnalysisReason.ArithmeticOverflow : AnalysisReason.ResourceLimit,
                overflow
                    ? null
                    : new ResourceLimitDiagnostic(ResourceLimitStage.Analysis,
                        ResourceLimitDimension.AnalysisStateBudget,
                        request.Options.AnalysisStateBudget, attempted));
            return ValueTask.CompletedTask;
        }

        if (entry.ObjectIdentity is null) reasons |= AnalysisReason.IdentityUnavailable;
        if (entry.Measurement.Availability != StorageMeasurementAvailability.Available)
            reasons |= AnalysisReason.MeasurementUnavailable;
        if (IsUnsupported(fact)) reasons |= AnalysisReason.UnsupportedAllocationEvidence;

        if (newPath)
        {
            DeterministicClassificationEngine.Result classification = classifier.Classify(entry.CanonicalPath, entry.Attributes);
            reasons |= classification.Reasons;
            existing = new AnalysisPathState(relative, fact, classification);
            paths.Add(relative, existing);
        }
        else if (existing!.Fact != fact)
        {
            existing.Poisoned = true;
            reasons |= AnalysisReason.ConflictingPathEvidence;
            foreach (StorageObjectIdentity previous in existing.Identities)
                identities[previous].Reasons |= AccountingReason.ConflictingPathEvidence;
        }

        if (entry.ObjectIdentity is { } objectIdentity)
        {
            if (!identities.TryGetValue(objectIdentity, out AnalysisIdentityState? identityState))
            {
                identityState = new AnalysisIdentityState(fact);
                identities.Add(objectIdentity, identityState);
            }
            else if (identityState.ReferenceFact != fact)
            {
                identityState.Reasons |= AccountingReason.ConflictingIdentityEvidence;
            }
            identityState.Reasons |= fact.Eligibility;
            identityState.Paths.Add(relative);
            existing!.Identities.Add(objectIdentity);
            if (existing.Poisoned)
                identityState.Reasons |= AccountingReason.ConflictingPathEvidence;
        }
        return ValueTask.CompletedTask;
    }

    public StorageAnalysisResult Complete(StorageAccountingResult accountingResult, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(accountingResult);
        if (completed) throw new InvalidOperationException("Analysis session is already complete.");
        completed = true;
        ValidateVolume(accountingResult);
        try
        {
            if (disabled)
                return Unavailable(accountingResult, reasons);
            long finalizationCharge;
            try
            {
                finalizationCharge = checked(
                    paths.Count * 128L +
                    accountingResult.AllocationGroups.Count * 256L +
                    request.Options.CandidateLimit * 4L * 512L);
            }
            catch (OverflowException)
            {
                return Unavailable(accountingResult, reasons | AnalysisReason.ArithmeticOverflow);
            }
            if (!resourceGuard.TryCharge(finalizationCharge, out bool overflow, out long attempted))
                return Unavailable(accountingResult, reasons |
                    (overflow ? AnalysisReason.ArithmeticOverflow : AnalysisReason.ResourceLimit),
                    overflow
                        ? null
                        : new ResourceLimitDiagnostic(ResourceLimitStage.Analysis,
                            ResourceLimitDimension.AnalysisStateBudget,
                            request.Options.AnalysisStateBudget, attempted));
            return CategoryAccountingBuilder.Build(
                request, paths, identities, accountingResult, reasons, cancellationToken);
        }
        catch (OverflowException)
        {
            return Unavailable(accountingResult, reasons | AnalysisReason.ArithmeticOverflow);
        }
        finally
        {
            paths.Clear();
            identities.Clear();
            resourceGuard.Clear();
        }
    }

    private void Disable(AnalysisReason reason, ResourceLimitDiagnostic? diagnostic = null)
    {
        reasons |= reason;
        resourceLimitDiagnostic ??= diagnostic;
        disabled = true;
        paths.Clear();
        identities.Clear();
        resourceGuard.Clear();
    }

    private string RelativePath(string canonicalPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalPath);
        string root = request.SystemVolume.RootPath.TrimEnd('\\') + "\\";
        if (root.Contains('/') || !canonicalPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Analysis path is outside the authorized root.");
        string relative = canonicalPath[root.Length..];
        if (relative.Length == 0 || relative.Contains('/') || relative.Contains(':') || relative.Contains('\0') ||
            relative.Split('\\').Any(component => component.Length == 0 || component is "." or ".."))
            throw new InvalidOperationException("Malformed canonical analysis path.");
        return relative;
    }

    private void ValidateVolume(StorageAccountingResult result)
    {
        VolumeIdentity expected = request.SystemVolume.VolumeIdentity;
        if (!result.Reconciliation.StartSnapshot.VolumeIdentity.Equals(expected) ||
            !result.Reconciliation.EndSnapshot.VolumeIdentity.Equals(expected) ||
            result.AllocationGroups.Any(group => !group.Identity.VolumeIdentity.Equals(expected)))
            throw new InvalidOperationException("Accounting result volume identity contradiction.");
    }

    private static bool IsUnsupported(AnalysisEntryFact fact) =>
        fact.Kind != StorageObjectKind.File || fact.Reparse != ReparseKind.None ||
        fact.Measurement.Scope != StorageMeasurementScope.FileContent ||
        (fact.Attributes & ~(StorageEntryAttributes.Sparse | StorageEntryAttributes.Compressed)) != 0;

    private StorageAnalysisResult Unavailable(StorageAccountingResult accounting, AnalysisReason reason,
        ResourceLimitDiagnostic? diagnostic = null) =>
        new(AnalysisQuality.Unavailable, reason, accounting.Summary.Quality, accounting.Summary.Reasons,
            [], [], [], [], [], diagnostic ?? resourceLimitDiagnostic);
}
