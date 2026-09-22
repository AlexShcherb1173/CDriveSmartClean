using System.Collections.ObjectModel;

namespace CDriveSmartClean.Domain.Risk;

public sealed class RiskAssessment
{
    public RiskAssessment(RiskLevel level, Confidence confidence, IEnumerable<string> reasons)
    {
        if (!Enum.IsDefined(level))
        {
            throw new ArgumentOutOfRangeException(nameof(level));
        }

        if (!Enum.IsDefined(confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        ArgumentNullException.ThrowIfNull(reasons);

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? reason in reasons)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new ArgumentException(
                    "Reasons cannot contain null or whitespace items.",
                    nameof(reasons));
            }

            string trimmed = reason.Trim();
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        if (normalized.Count == 0)
        {
            throw new ArgumentException("At least one reason is required.", nameof(reasons));
        }

        Level = level;
        Confidence = confidence;
        Reasons = new ReadOnlyCollection<string>(normalized);
    }

    public RiskLevel Level { get; }

    public Confidence Confidence { get; }

    public IReadOnlyList<string> Reasons { get; }
}
