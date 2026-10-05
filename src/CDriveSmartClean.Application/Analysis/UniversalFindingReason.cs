namespace CDriveSmartClean.Application.Analysis;

[Flags]
public enum UniversalFindingReason
{
    None = 0,
    UpstreamAnalysisIncomplete = 1 << 0,
    UpstreamAnalysisUnavailable = 1 << 1,
    UpstreamAccountingUnavailable = 1 << 2,
    UpstreamAccountingIncomplete = 1 << 3,
    UpstreamReconciliationInconsistent = 1 << 4,
    VolumeCapacityUnavailable = 1 << 5,
    InputMismatch = 1 << 6,
    ResourceLimit = 1 << 7,
    ArithmeticOverflow = 1 << 8
}
