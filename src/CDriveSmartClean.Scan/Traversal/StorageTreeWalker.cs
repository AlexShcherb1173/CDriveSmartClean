using System.Runtime.ExceptionServices;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;

namespace CDriveSmartClean.Scan.Traversal;

public sealed class StorageTreeWalker
{
    private readonly IStorageEnumerator storageEnumerator;
    private readonly StorageTraversalPolicy traversalPolicy;

    public StorageTreeWalker(IStorageEnumerator storageEnumerator, StorageTraversalPolicy traversalPolicy)
    {
        ArgumentNullException.ThrowIfNull(storageEnumerator);
        ArgumentNullException.ThrowIfNull(traversalPolicy);
        this.storageEnumerator = storageEnumerator;
        this.traversalPolicy = traversalPolicy;
    }

    public async Task WalkAsync(
        SystemVolumeDescriptor systemVolume,
        IStorageEntrySink entrySink,
        IStorageTraversalIssueSink issueSink,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(systemVolume);
        ArgumentNullException.ThrowIfNull(entrySink);
        ArgumentNullException.ThrowIfNull(issueSink);
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new Stack<StorageEntry>();
        var routing = new RoutingSink(systemVolume, entrySink, traversalPolicy, pending, cancellationToken);
        try
        {
            // A root failure is fatal: only child enumeration has coverage-issue handling.
            await storageEnumerator.EnumerateRootAsync(systemVolume, routing, cancellationToken).ConfigureAwait(false);
            while (pending.TryPop(out StorageEntry? directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                StorageTraversalIssueKind? issueKind = null;
                try
                {
                    await storageEnumerator.EnumerateChildrenAsync(systemVolume, directory, routing, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException)
                {
                    issueKind = StorageTraversalIssueKind.Inaccessible;
                }
                catch (StorageTraversalTargetChangedException)
                {
                    issueKind = StorageTraversalIssueKind.TargetChanged;
                }
                catch (DirectoryNotFoundException)
                {
                    issueKind = StorageTraversalIssueKind.Disappeared;
                }
                catch (FileNotFoundException)
                {
                    issueKind = StorageTraversalIssueKind.Disappeared;
                }
                catch (IOException)
                {
                    issueKind = StorageTraversalIssueKind.IoFailure;
                }

                if (issueKind is { } kind)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var issue = new StorageTraversalIssue(systemVolume.VolumeIdentity, directory.CanonicalPath, kind);
                    await issueSink.WriteAsync(issue, cancellationToken).ConfigureAwait(false);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (EntrySinkFailureException failure)
        {
            failure.Original.Throw();
            throw;
        }
    }

    private sealed class RoutingSink(
        SystemVolumeDescriptor systemVolume,
        IStorageEntrySink downstream,
        StorageTraversalPolicy policy,
        Stack<StorageEntry> pending,
        CancellationToken traversalToken) : IStorageEntrySink
    {
        // Prevent scheduling the same supplied entry twice; this is not physical-object deduplication.
        private readonly HashSet<StorageEntry> scheduled = [];

        public async ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            traversalToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(entry);
            if (!entry.VolumeIdentity.Equals(systemVolume.VolumeIdentity))
            {
                throw new InvalidOperationException("Enumerator emitted an entry from another volume.");
            }

            try
            {
                await downstream.WriteAsync(entry, traversalToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Wrap only the downstream call, never enumerator or policy failures.
                throw new EntrySinkFailureException(exception);
            }

            traversalToken.ThrowIfCancellationRequested();
            if (policy.Evaluate(entry) == TraversalDecision.TraverseChildren && scheduled.Add(entry))
            {
                pending.Push(entry);
            }
        }
    }

    private sealed class EntrySinkFailureException(Exception original) : Exception("Entry sink failed.", original)
    {
        public ExceptionDispatchInfo Original { get; } = ExceptionDispatchInfo.Capture(original);
    }
}
