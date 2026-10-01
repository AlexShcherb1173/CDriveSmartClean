using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Analysis;

internal sealed record AnalysisEntryFact(StorageObjectIdentity? Identity, StorageMeasurement Measurement,
    StorageObjectKind Kind, ReparseKind Reparse, StorageEntryAttributes Attributes);

internal sealed class AnalysisPathState(
    string relativePath,
    AnalysisEntryFact fact,
    DeterministicClassificationEngine.Result classification)
{
    internal string RelativePath { get; } = relativePath;
    internal AnalysisEntryFact Fact { get; } = fact;
    internal DeterministicClassificationEngine.Result Classification { get; } = classification;
    internal bool Poisoned { get; set; }
}

internal sealed class StorageAnalysisSession : IStorageAnalysisSession
{
    private readonly StorageAnalysisRequest request;
    private readonly DeterministicClassificationEngine classifier;
    private readonly AnalysisResourceGuard resourceGuard;
    private readonly Dictionary<string, AnalysisPathState> paths = new(StringComparer.Ordinal);
    private readonly HashSet<StorageObjectIdentity> identities = [];
    private AnalysisReason reasons;
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
        bool newIdentity = entry.ObjectIdentity is { } identity && !identities.Contains(identity);
        if (newPath && paths.Count >= request.Options.MaximumPathStates ||
            newIdentity && identities.Count >= request.Options.MaximumIdentityStates)
        {
            Disable(AnalysisReason.ResourceLimit);
            return ValueTask.CompletedTask;
        }

        long charge;
        try
        {
            charge = checked((newPath ? 768L + relative.Length * 8L : 0L) +
                (newIdentity ? 768L : 0L) + (newPath ? 256L : 0L));
        }
        catch (OverflowException)
        {
            Disable(AnalysisReason.ArithmeticOverflow);
            return ValueTask.CompletedTask;
        }
        if (!resourceGuard.TryCharge(charge, out bool overflow))
        {
            Disable(overflow ? AnalysisReason.ArithmeticOverflow : AnalysisReason.ResourceLimit);
            return ValueTask.CompletedTask;
        }

        if (newPath)
        {
            DeterministicClassificationEngine.Result classification = classifier.Classify(entry.CanonicalPath, entry.Attributes);
            reasons |= classification.Reasons;
            existing = new AnalysisPathState(relative, fact, classification);
            paths.Add(relative, existing);
            if (entry.ObjectIdentity is null) reasons |= AnalysisReason.IdentityUnavailable;
            if (entry.Measurement.Availability != StorageMeasurementAvailability.Available)
                reasons |= AnalysisReason.MeasurementUnavailable;
            if (IsUnsupported(fact)) reasons |= AnalysisReason.UnsupportedAllocationEvidence;
        }
        else if (existing!.Fact != fact)
        {
            existing.Poisoned = true;
            reasons |= AnalysisReason.ConflictingPathEvidence;
        }

        if (entry.ObjectIdentity is { } objectIdentity) identities.Add(objectIdentity);
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
            if (!resourceGuard.TryCharge(finalizationCharge, out bool overflow))
                return Unavailable(accountingResult, reasons |
                    (overflow ? AnalysisReason.ArithmeticOverflow : AnalysisReason.ResourceLimit));
            return CategoryAccountingBuilder.Build(request, paths, accountingResult, reasons, cancellationToken);
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

    private void Disable(AnalysisReason reason)
    {
        reasons |= reason;
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

    private static StorageAnalysisResult Unavailable(StorageAccountingResult accounting, AnalysisReason reason) =>
        new(AnalysisQuality.Unavailable, reason, accounting.Summary.Quality, accounting.Summary.Reasons,
            [], [], [], [], []);
}
