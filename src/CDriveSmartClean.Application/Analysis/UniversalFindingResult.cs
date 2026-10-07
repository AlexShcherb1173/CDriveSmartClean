using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;

namespace CDriveSmartClean.Application.Analysis;

public sealed class UniversalFindingResult
{
    public UniversalFindingResult(
        AnalysisQuality quality,
        UniversalFindingReason reasons,
        AnalysisQuality upstreamAnalysisQuality,
        AnalysisReason upstreamAnalysisReasons,
        IEnumerable<Finding> findings)
    {
        if (!Enum.IsDefined(quality)) throw new ArgumentOutOfRangeException(nameof(quality));
        if (!Enum.IsDefined(upstreamAnalysisQuality))
            throw new ArgumentOutOfRangeException(nameof(upstreamAnalysisQuality));
        ValidateFlags(reasons, nameof(reasons));
        ValidateAnalysisFlags(upstreamAnalysisReasons, nameof(upstreamAnalysisReasons));
        ArgumentNullException.ThrowIfNull(findings);
        Finding[] copy = findings.ToArray();
        if (copy.Any(finding => finding is null))
            throw new ArgumentException("Findings cannot contain null.", nameof(findings));
        if (quality == AnalysisQuality.Unavailable && copy.Length != 0)
            throw new ArgumentException("Unavailable results cannot publish findings.", nameof(findings));

        Array.Sort(copy, CompareFindings);
        Quality = quality;
        Reasons = reasons;
        UpstreamAnalysisQuality = upstreamAnalysisQuality;
        UpstreamAnalysisReasons = upstreamAnalysisReasons;
        Findings = Array.AsReadOnly(copy);
    }

    public AnalysisQuality Quality { get; }
    public UniversalFindingReason Reasons { get; }
    public AnalysisQuality UpstreamAnalysisQuality { get; }
    public AnalysisReason UpstreamAnalysisReasons { get; }
    public IReadOnlyList<Finding> Findings { get; }

    private static void ValidateFlags(UniversalFindingReason reasons, string name)
    {
        UniversalFindingReason known = Enum.GetValues<UniversalFindingReason>()
            .Aggregate(UniversalFindingReason.None, (current, value) => current | value);
        if ((reasons & ~known) != 0) throw new ArgumentOutOfRangeException(name);
    }

    private static void ValidateAnalysisFlags(AnalysisReason reasons, string name)
    {
        AnalysisReason known = Enum.GetValues<AnalysisReason>()
            .Aggregate(AnalysisReason.None, (current, value) => current | value);
        if ((reasons & ~known) != 0) throw new ArgumentOutOfRangeException(name);
    }

    private static int CompareFindings(Finding left, Finding right)
    {
        int result = left.Scope.CompareTo(right.Scope);
        if (result != 0) return result;
        result = CompareAllocatedDescending(left.SizeMetrics.AllocatedBytes, right.SizeMetrics.AllocatedBytes);
        if (result != 0) return result;
        result = left.PrimaryCategory.CompareTo(right.PrimaryCategory);
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(FirstPath(left), FirstPath(right));
        if (result != 0) return result;
        result = Nullable.Compare(left.ObjectIdentity?.VolumeIdentity.Id, right.ObjectIdentity?.VolumeIdentity.Id);
        if (result != 0) return result;
        result = Nullable.Compare(left.ObjectIdentity?.ObjectId, right.ObjectIdentity?.ObjectId);
        if (result != 0) return result;
        return left.Id.CompareTo(right.Id);
    }

    private static int CompareAllocatedDescending(long? left, long? right)
    {
        if (left is null) return right is null ? 0 : 1;
        if (right is null) return -1;
        return right.Value.CompareTo(left.Value);
    }

    private static string FirstPath(Finding finding) =>
        finding.RelativePaths.Count == 0 ? string.Empty : finding.RelativePaths[0];
}
