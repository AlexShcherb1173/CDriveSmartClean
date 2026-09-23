using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Application.Scanning;

public sealed class ScanResult
{
    public ScanResult(Guid scanSessionId, ScanMode mode, ScanCompletion completion, VolumeAccounting volumeAccounting)
    {
        if (scanSessionId == Guid.Empty)
        {
            throw new ArgumentException("Scan session ID cannot be empty.", nameof(scanSessionId));
        }

        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        if (!Enum.IsDefined(completion))
        {
            throw new ArgumentOutOfRangeException(nameof(completion));
        }

        ArgumentNullException.ThrowIfNull(volumeAccounting);

        ScanSessionId = scanSessionId;
        Mode = mode;
        Completion = completion;
        VolumeAccounting = volumeAccounting;
    }

    public Guid ScanSessionId { get; }

    public ScanMode Mode { get; }

    public ScanCompletion Completion { get; }

    public VolumeAccounting VolumeAccounting { get; }
}
