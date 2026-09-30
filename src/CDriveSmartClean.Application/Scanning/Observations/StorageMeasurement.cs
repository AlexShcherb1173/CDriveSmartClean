namespace CDriveSmartClean.Application.Scanning.Observations;

/// <summary>Raw per-entry filesystem measurement. Reported allocation is not exclusive or reclaimable space.</summary>
public sealed class StorageMeasurement : IEquatable<StorageMeasurement>
{
    public StorageMeasurement(
        long? logicalBytes,
        long? reportedAllocatedBytes,
        StorageMeasurementAvailability availability,
        StorageMeasurementQuality quality,
        StorageMeasurementSource source,
        StorageMeasurementScope scope,
        StorageMeasurementFreshness freshness)
    {
        if (!Enum.IsDefined(availability)) throw new ArgumentOutOfRangeException(nameof(availability));
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        if (!Enum.IsDefined(source)) throw new ArgumentOutOfRangeException(nameof(source));
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!Enum.IsDefined(freshness)) throw new ArgumentOutOfRangeException(nameof(freshness));

        if (availability == StorageMeasurementAvailability.Available)
        {
            if (logicalBytes is null) throw new ArgumentNullException(nameof(logicalBytes));
            if (reportedAllocatedBytes is null) throw new ArgumentNullException(nameof(reportedAllocatedBytes));
            ArgumentOutOfRangeException.ThrowIfNegative(logicalBytes.Value, nameof(logicalBytes));
            ArgumentOutOfRangeException.ThrowIfNegative(reportedAllocatedBytes.Value, nameof(reportedAllocatedBytes));
        }
        else if (logicalBytes is not null || reportedAllocatedBytes is not null)
        {
            throw new ArgumentException("Unavailable and not-applicable measurements cannot contain byte values.");
        }

        if (availability == StorageMeasurementAvailability.Available &&
            (quality != StorageMeasurementQuality.FileSystemReported ||
             source != StorageMeasurementSource.WindowsFileIdExtendedDirectoryInfo ||
             freshness != StorageMeasurementFreshness.LivePointInTime))
        {
            throw new ArgumentException("Available measurements require filesystem-reported live native evidence.");
        }

        if (availability != StorageMeasurementAvailability.Available &&
            (quality != StorageMeasurementQuality.Unknown || source != StorageMeasurementSource.None ||
             freshness != StorageMeasurementFreshness.Unknown))
        {
            throw new ArgumentException("Non-available measurements require unknown quality/freshness and no source.");
        }

        LogicalBytes = logicalBytes;
        ReportedAllocatedBytes = reportedAllocatedBytes;
        Availability = availability;
        Quality = quality;
        Source = source;
        Scope = scope;
        Freshness = freshness;
    }

    public long? LogicalBytes { get; }
    public long? ReportedAllocatedBytes { get; }
    public StorageMeasurementAvailability Availability { get; }
    public StorageMeasurementQuality Quality { get; }
    public StorageMeasurementSource Source { get; }
    public StorageMeasurementScope Scope { get; }
    public StorageMeasurementFreshness Freshness { get; }

    public bool Equals(StorageMeasurement? other) =>
        ReferenceEquals(this, other) ||
        other is not null &&
        LogicalBytes == other.LogicalBytes &&
        ReportedAllocatedBytes == other.ReportedAllocatedBytes &&
        Availability == other.Availability &&
        Quality == other.Quality &&
        Source == other.Source &&
        Scope == other.Scope &&
        Freshness == other.Freshness;

    public override bool Equals(object? obj) => obj is StorageMeasurement other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        LogicalBytes, ReportedAllocatedBytes, Availability, Quality, Source, Scope, Freshness);

    public static StorageMeasurement Unavailable(StorageMeasurementScope scope) => new(
        null, null, StorageMeasurementAvailability.Unavailable, StorageMeasurementQuality.Unknown,
        StorageMeasurementSource.None, scope, StorageMeasurementFreshness.Unknown);
}
