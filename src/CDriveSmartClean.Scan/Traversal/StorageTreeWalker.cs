using System.Runtime.ExceptionServices;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Identity;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;

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
        var routing = new RoutingSink(systemVolume, entrySink, issueSink, traversalPolicy, pending, cancellationToken);
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
                catch (StorageObjectIdentityUnavailableException)
                {
                    issueKind = StorageTraversalIssueKind.IdentityUnavailable;
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
        catch (DownstreamSinkFailureException failure)
        {
            failure.Original.Throw();
            throw;
        }
    }

    private sealed class RoutingSink(
        SystemVolumeDescriptor systemVolume,
        IStorageEntrySink downstream,
        IStorageTraversalIssueSink issueSink,
        StorageTraversalPolicy policy,
        Stack<StorageEntry> pending,
        CancellationToken traversalToken) : IStorageEntrySink
    {
        // Deduplicate directory scheduling, never visibility or file allocation accounting.
        private readonly HashSet<StorageObjectIdentity> scheduled = [];

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
                throw new DownstreamSinkFailureException(exception);
            }

            traversalToken.ThrowIfCancellationRequested();
            if (policy.Evaluate(entry) != TraversalDecision.TraverseChildren)
            {
                return;
            }

            if (entry.ObjectIdentity is null)
            {
                var issue = new StorageTraversalIssue(systemVolume.VolumeIdentity, entry.CanonicalPath, StorageTraversalIssueKind.IdentityUnavailable);
                try
                {
                    await issueSink.WriteAsync(issue, traversalToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    throw new DownstreamSinkFailureException(exception);
                }

                traversalToken.ThrowIfCancellationRequested();
            }
            else if (scheduled.Add(entry.ObjectIdentity))
            {
                pending.Push(entry);
            }
        }
    }

    private sealed class DownstreamSinkFailureException(Exception original) : Exception("Downstream sink failed.", original)
    {
        public ExceptionDispatchInfo Original { get; } = ExceptionDispatchInfo.Capture(original);
    }
}
