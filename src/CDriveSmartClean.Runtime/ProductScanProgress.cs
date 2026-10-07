namespace CDriveSmartClean.Runtime;

public sealed class ProductScanProgress
{
    public ProductScanProgress(Guid scanSessionId, ProductScanPhase phase, long objectsObserved,
        long rawReportedAllocatedBytesObserved, long traversalIssuesObserved)
    {
        if (scanSessionId == Guid.Empty)
            throw new ArgumentException("Scan session identifier cannot be empty.", nameof(scanSessionId));
        if (!Enum.IsDefined(phase)) throw new ArgumentOutOfRangeException(nameof(phase));
        ArgumentOutOfRangeException.ThrowIfNegative(objectsObserved);
        ArgumentOutOfRangeException.ThrowIfNegative(rawReportedAllocatedBytesObserved);
        ArgumentOutOfRangeException.ThrowIfNegative(traversalIssuesObserved);
        ScanSessionId = scanSessionId;
        Phase = phase;
        ObjectsObserved = objectsObserved;
        RawReportedAllocatedBytesObserved = rawReportedAllocatedBytesObserved;
        TraversalIssuesObserved = traversalIssuesObserved;
    }

    public Guid ScanSessionId { get; }
    public ProductScanPhase Phase { get; }
    public long ObjectsObserved { get; }
    public long RawReportedAllocatedBytesObserved { get; }
    public long TraversalIssuesObserved { get; }
}
