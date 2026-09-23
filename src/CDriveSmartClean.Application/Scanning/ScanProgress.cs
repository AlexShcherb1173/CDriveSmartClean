namespace CDriveSmartClean.Application.Scanning;

public sealed class ScanProgress
{
    public ScanProgress(Guid scanSessionId, long objectsObserved, long allocatedBytesObserved)
    {
        if (scanSessionId == Guid.Empty)
        {
            throw new ArgumentException("Scan session ID cannot be empty.", nameof(scanSessionId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(objectsObserved);
        ArgumentOutOfRangeException.ThrowIfNegative(allocatedBytesObserved);

        ScanSessionId = scanSessionId;
        ObjectsObserved = objectsObserved;
        AllocatedBytesObserved = allocatedBytesObserved;
    }

    public Guid ScanSessionId { get; }

    public long ObjectsObserved { get; }

    public long AllocatedBytesObserved { get; }
}
