using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Scan.Traversal;

namespace CDriveSmartClean.Scan.Accounting;

public sealed class StorageAccountingEngine
{
    private readonly StorageTreeWalker walker;
    private readonly IVolumeSpaceProvider volumeProvider;
    private readonly StorageAccountingOptions options;
    private readonly IReadOnlyDictionary<StorageTraversalIssueKind, long>? initialIssueCounts;

    public StorageAccountingEngine(StorageTreeWalker walker, IVolumeSpaceProvider volumeProvider, StorageAccountingOptions options)
        : this(walker, volumeProvider, options, null)
    {
    }

    private StorageAccountingEngine(StorageTreeWalker walker, IVolumeSpaceProvider volumeProvider, StorageAccountingOptions options,
        IReadOnlyDictionary<StorageTraversalIssueKind, long>? initialIssueCounts)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(volumeProvider);
        ArgumentNullException.ThrowIfNull(options);
        this.walker = walker;
        this.volumeProvider = volumeProvider;
        this.options = options;
        this.initialIssueCounts = initialIssueCounts;
    }

    public async Task<StorageAccountingResult> AccountAsync(SystemVolumeDescriptor systemVolume,
        IStorageEntrySink downstreamEntrySink, IStorageTraversalIssueSink downstreamIssueSink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(downstreamEntrySink);
        ArgumentNullException.ThrowIfNull(downstreamIssueSink);
        cancellationToken.ThrowIfCancellationRequested();
        VolumeSpaceSnapshot start = Snapshot(systemVolume);
        var session = new Session(systemVolume, downstreamEntrySink, downstreamIssueSink, options, initialIssueCounts);
        try
        {
            await walker.WalkAsync(systemVolume, session, session, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var (root, snapshot) = session.Finish(cancellationToken);
            VolumeSpaceSnapshot end = Snapshot(systemVolume);
            cancellationToken.ThrowIfCancellationRequested();
            var summary = new StorageAccountingSummary(root?.Aggregate, session.Reasons);
            return new StorageAccountingResult(summary, root, snapshot,
                new VolumeReconciliation(start, end, summary, true), true, session.Issues,
                session.ResourceLimitDiagnostic);
        }
        finally
        {
            session.Clear();
        }
    }

    private VolumeSpaceSnapshot Snapshot(SystemVolumeDescriptor volume)
    {
        VolumeSpaceSnapshot result = volumeProvider.GetVolumeSpace(volume);
        if (result is null || !result.VolumeIdentity.Equals(volume.VolumeIdentity))
            throw new InvalidOperationException("Volume snapshot identity contradiction.");
        return result;
    }

    private sealed class Session : IStorageEntrySink, IStorageTraversalIssueSink
    {
        private readonly SystemVolumeDescriptor volume;
        private readonly IStorageEntrySink entrySink;
        private readonly IStorageTraversalIssueSink issueSink;
        private StorageIdentityLedger? ledger;
        internal AccountingReason Reasons { get; private set; }
        internal ResourceLimitDiagnostic? ResourceLimitDiagnostic { get; private set; }
        internal Dictionary<StorageTraversalIssueKind, long> Issues { get; }

        internal Session(SystemVolumeDescriptor volume, IStorageEntrySink entrySink, IStorageTraversalIssueSink issueSink,
            StorageAccountingOptions options, IReadOnlyDictionary<StorageTraversalIssueKind, long>? initialIssueCounts)
        {
            this.volume = volume;
            this.entrySink = entrySink;
            this.issueSink = issueSink;
            Issues = Enum.GetValues<StorageTraversalIssueKind>().ToDictionary(kind => kind, _ => 0L);
            if (initialIssueCounts is not null)
            {
                foreach ((StorageTraversalIssueKind kind, long count) in initialIssueCounts)
                {
                    if (!Issues.ContainsKey(kind) || count < 0) throw new ArgumentOutOfRangeException(nameof(initialIssueCounts));
                    Issues[kind] = count;
                }
            }
            try { ledger = new StorageIdentityLedger(options); }
            catch (StorageIdentityLedger.ResourceLimitException exception)
            {
                Disable(AccountingReason.ResourceLimit, exception.Diagnostic);
            }
            catch (OverflowException) { Disable(AccountingReason.ArithmeticOverflow); }
        }

        public async ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!entry.VolumeIdentity.Equals(volume.VolumeIdentity)) throw new InvalidOperationException("Cross-volume entry.");
            // Security validation remains active after accounting degradation.
            string relative = StorageHierarchyAccumulator.ValidatePath(volume, entry.CanonicalPath);
            if (ledger is not null)
            {
                try { ledger.Add(entry, relative); }
                catch (StorageIdentityLedger.ResourceLimitException exception)
                {
                    Disable(AccountingReason.ResourceLimit, exception.Diagnostic);
                }
                catch (OverflowException) { Disable(AccountingReason.ArithmeticOverflow); }
            }
            await entrySink.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!issue.VolumeIdentity.Equals(volume.VolumeIdentity)) throw new InvalidOperationException("Cross-volume issue.");
            StorageHierarchyAccumulator.ValidatePath(volume, issue.CanonicalPath);
            try { Issues[issue.Kind] = checked(Issues[issue.Kind] + 1); }
            catch (OverflowException) { Disable(AccountingReason.ArithmeticOverflow); }
            Reasons |= AccountingReason.TraversalCoverageGap;
            await issueSink.WriteAsync(issue, cancellationToken).ConfigureAwait(false);
        }

        internal (StorageHierarchyNode? Root, CompactAccountingSnapshot? Snapshot) Finish(CancellationToken token)
        {
            if (ledger is not null)
            {
                try
                {
                    var result = ledger.Finish(token);
                    Reasons |= ledger.Reasons;
                    return result;
                }
                catch (StorageIdentityLedger.ResourceLimitException exception)
                {
                    Disable(AccountingReason.ResourceLimit, exception.Diagnostic);
                }
                catch (OverflowException) { Disable(AccountingReason.ArithmeticOverflow); }
            }
            return (null, null);
        }

        private void Disable(AccountingReason reason, ResourceLimitDiagnostic? resourceLimitDiagnostic = null)
        {
            Reasons |= reason;
            ResourceLimitDiagnostic ??= resourceLimitDiagnostic;
            ledger = null;
        }
        internal void Clear() => ledger = null;
    }
}
