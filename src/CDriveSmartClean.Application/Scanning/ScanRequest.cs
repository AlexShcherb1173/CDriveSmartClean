namespace CDriveSmartClean.Application.Scanning;

public sealed class ScanRequest
{
    public ScanRequest(Guid scanSessionId, ScanMode mode)
    {
        if (scanSessionId == Guid.Empty)
        {
            throw new ArgumentException("Scan session ID cannot be empty.", nameof(scanSessionId));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ScanSessionId = scanSessionId;
        Mode = mode;
    }

    public Guid ScanSessionId { get; }

    public ScanMode Mode { get; }
}
