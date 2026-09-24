namespace CDriveSmartClean.Application.Scanning.Enumeration;

/// <summary>Receives discovered entries individually, before measurement.</summary>
public interface IStorageEntrySink
{
    /// <summary>
    /// Receives one entry. The enumerator must await the returned value task;
    /// at most one unfinished write may exist per enumeration.
    /// Sink cancellation and failure propagate through the active enumeration.
    /// </summary>
    ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken);
}
