namespace CDriveSmartClean.Domain.Analysis;

/// <summary>Observed size evidence; no value denotes exclusive or reclaimable allocation.</summary>
public sealed class AnalysisSizeEvidence
{
    public AnalysisSizeEvidence(long? visibleLogicalBytes, long? rawReportedAllocatedBytes,
        long? observedAttributedAllocatedBytes, long? uncertainMeasuredAllocatedBytes)
    {
        Validate(visibleLogicalBytes, nameof(visibleLogicalBytes));
        Validate(rawReportedAllocatedBytes, nameof(rawReportedAllocatedBytes));
        Validate(observedAttributedAllocatedBytes, nameof(observedAttributedAllocatedBytes));
        Validate(uncertainMeasuredAllocatedBytes, nameof(uncertainMeasuredAllocatedBytes));
        VisibleLogicalBytes = visibleLogicalBytes;
        RawReportedAllocatedBytes = rawReportedAllocatedBytes;
        ObservedAttributedAllocatedBytes = observedAttributedAllocatedBytes;
        UncertainMeasuredAllocatedBytes = uncertainMeasuredAllocatedBytes;
    }

    public long? VisibleLogicalBytes { get; }
    public long? RawReportedAllocatedBytes { get; }
    public long? ObservedAttributedAllocatedBytes { get; }
    public long? UncertainMeasuredAllocatedBytes { get; }

    private static void Validate(long? value, string name)
    {
        if (value < 0) throw new ArgumentOutOfRangeException(name);
    }
}
