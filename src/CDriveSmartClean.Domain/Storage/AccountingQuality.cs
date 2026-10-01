namespace CDriveSmartClean.Domain.Storage;

/// <summary>Completeness of the supported observation model, not proof of physical-volume coverage.</summary>
public enum AccountingQuality
{
    Complete = 1,
    Incomplete,
    Inconsistent,
    Unavailable,
}

[Flags]
public enum AccountingReason
{
    None = 0,
    TraversalCoverageGap = 1,
    IdentityUnavailable = 2,
    MeasurementUnavailable = 4,
    ConflictingIdentityEvidence = 8,
    ConflictingPathEvidence = 16,
    UnsupportedAllocationEvidence = 32,
    ResourceLimit = 64,
    ArithmeticOverflow = 128,
    VolumeSnapshotUnavailable = 256,
    VolumeDrift = 512,
}
