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

    public StorageAccountingEngine(StorageTreeWalker walker, IVolumeSpaceProvider volumeProvider, StorageAccountingOptions options)
    {
        ArgumentNullException.ThrowIfNull(walker);
        ArgumentNullException.ThrowIfNull(volumeProvider);
        ArgumentNullException.ThrowIfNull(options);
        this.walker = walker;
        this.volumeProvider = volumeProvider;
        this.options = options;
    }

    public async Task<StorageAccountingResult> AccountAsync(SystemVolumeDescriptor systemVolume,
        IStorageEntrySink downstreamEntrySink, IStorageTraversalIssueSink downstreamIssueSink, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(downstreamEntrySink);
        ArgumentNullException.ThrowIfNull(downstreamIssueSink);
        cancellationToken.ThrowIfCancellationRequested();
        VolumeSpaceSnapshot start = Snapshot(systemVolume);
        var session = new Session(systemVolume, downstreamEntrySink, downstreamIssueSink, options);
        try
        {
            await walker.WalkAsync(systemVolume, session, session, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            var (root, groups) = session.Finish(cancellationToken);
            VolumeSpaceSnapshot end = Snapshot(systemVolume);
            cancellationToken.ThrowIfCancellationRequested();
            var summary = new StorageAccountingSummary(root?.Aggregate, session.Reasons);
            return new StorageAccountingResult(summary, root, groups,
                new VolumeReconciliation(start, end, summary, true), true, session.Issues);
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
        internal Dictionary<StorageTraversalIssueKind, long> Issues { get; } =
            Enum.GetValues<StorageTraversalIssueKind>().ToDictionary(kind => kind, _ => 0L);

        internal Session(SystemVolumeDescriptor volume, IStorageEntrySink entrySink, IStorageTraversalIssueSink issueSink, StorageAccountingOptions options)
        {
            this.volume = volume;
            this.entrySink = entrySink;
            this.issueSink = issueSink;
            try { ledger = new StorageIdentityLedger(options); }
            catch (StorageIdentityLedger.ResourceLimitException) { Disable(AccountingReason.ResourceLimit); }
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
                catch (StorageIdentityLedger.ResourceLimitException) { Disable(AccountingReason.ResourceLimit); }
                catch (OverflowException) { Disable(AccountingReason.ArithmeticOverflow); }
            }
            await entrySink.WriteAsync(entry, cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!issue.VolumeIdentity.Equals(volume.VolumeIdentity)) throw new InvalidOperationException("Cross-volume issue.");
            StorageHierarchyAccumulator.ValidatePath(volume, issue.CanonicalPath);
            Issues[issue.Kind] = checked(Issues[issue.Kind] + 1);
            Reasons |= AccountingReason.TraversalCoverageGap;
            await issueSink.WriteAsync(issue, cancellationToken).ConfigureAwait(false);
        }

        internal (StorageHierarchyNode? Root, AllocationGroup[] Groups) Finish(CancellationToken token)
        {
            if (ledger is not null)
            {
                try
                {
                    var result = ledger.Finish(token);
                    Reasons |= ledger.Reasons;
                    return result;
                }
                catch (StorageIdentityLedger.ResourceLimitException) { Disable(AccountingReason.ResourceLimit); }
                catch (OverflowException) { Disable(AccountingReason.ArithmeticOverflow); }
            }
            return (null, []);
        }

        private void Disable(AccountingReason reason)
        {
            Reasons |= reason;
            ledger = null;
        }
        internal void Clear() => ledger = null;
    }
}
