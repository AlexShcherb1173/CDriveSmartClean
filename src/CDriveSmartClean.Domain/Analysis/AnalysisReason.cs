namespace CDriveSmartClean.Domain.Analysis;

[Flags]
public enum AnalysisReason
{
    None = 0,
    UpstreamAccountingUnavailable = 1 << 0,
    UpstreamAccountingIncomplete = 1 << 1,
    UpstreamReconciliationInconsistent = 1 << 2,
    ClassificationContextIncomplete = 1 << 3,
    ClassificationRuleConflict = 1 << 4,
    CrossCategoryIdentityConflict = 1 << 5,
    IdentityUnavailable = 1 << 6,
    MeasurementUnavailable = 1 << 7,
    ConflictingIdentityEvidence = 1 << 8,
    ConflictingPathEvidence = 1 << 9,
    UnsupportedAllocationEvidence = 1 << 10,
    ResourceLimit = 1 << 11,
    ArithmeticOverflow = 1 << 12,
    AccountingMismatch = 1 << 13,
}
