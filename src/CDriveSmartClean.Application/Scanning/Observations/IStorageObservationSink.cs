namespace CDriveSmartClean.Application.Scanning.Observations;

public interface IStorageObservationSink
{
    /// <summary>
    /// Accepts one observation. Scanner implementations must await the returned value task before
    /// delivering the next observation, with at most one unfinished write per scan. Cancellation or
    /// failure must propagate through the active scan.
    /// </summary>
    ValueTask WriteAsync(
        StorageObservation observation,
        CancellationToken cancellationToken);
}
