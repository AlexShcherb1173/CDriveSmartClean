namespace CDriveSmartClean.Application.Scanning.Traversal;

public interface IStorageTraversalIssueSink
{
    /// <summary>
    /// Receives issues individually. Writes must be awaited, with at most one unfinished
    /// issue write per traversal. Sink failure and cancellation propagate to the caller.
    /// </summary>
    ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken cancellationToken);
}
