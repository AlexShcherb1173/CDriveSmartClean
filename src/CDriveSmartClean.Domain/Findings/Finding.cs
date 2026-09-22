using System.Collections.ObjectModel;
using CDriveSmartClean.Domain.Reclaim;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Domain.Findings;

public sealed class Finding
{
    public Finding(
        Guid id,
        Guid scanSessionId,
        string displayName,
        FindingCategory primaryCategory,
        IEnumerable<FindingFacet> facets,
        SizeMetrics sizeMetrics,
        ReclaimEstimate reclaimEstimate,
        RiskAssessment riskAssessment,
        Confidence confidence,
        ProtectionState protectionState,
        IEnumerable<Evidence> evidence)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("The finding identifier cannot be empty.", nameof(id));
        }

        if (scanSessionId == Guid.Empty)
        {
            throw new ArgumentException(
                "The scan session identifier cannot be empty.",
                nameof(scanSessionId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        if (!Enum.IsDefined(primaryCategory))
        {
            throw new ArgumentOutOfRangeException(nameof(primaryCategory));
        }

        ArgumentNullException.ThrowIfNull(facets);
        ArgumentNullException.ThrowIfNull(sizeMetrics);
        ArgumentNullException.ThrowIfNull(reclaimEstimate);
        ArgumentNullException.ThrowIfNull(riskAssessment);

        if (!Enum.IsDefined(confidence))
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        if (!Enum.IsDefined(protectionState))
        {
            throw new ArgumentOutOfRangeException(nameof(protectionState));
        }

        ArgumentNullException.ThrowIfNull(evidence);

        Id = id;
        ScanSessionId = scanSessionId;
        DisplayName = displayName.Trim();
        PrimaryCategory = primaryCategory;
        Facets = CopyFacets(facets);
        SizeMetrics = sizeMetrics;
        ReclaimEstimate = reclaimEstimate;
        RiskAssessment = riskAssessment;
        Confidence = confidence;
        ProtectionState = protectionState;
        Evidence = CopyEvidence(evidence);
    }

    public Guid Id { get; }

    public Guid ScanSessionId { get; }

    public string DisplayName { get; }

    public FindingCategory PrimaryCategory { get; }

    public IReadOnlyList<FindingFacet> Facets { get; }

    public SizeMetrics SizeMetrics { get; }

    public ReclaimEstimate ReclaimEstimate { get; }

    public RiskAssessment RiskAssessment { get; }

    public Confidence Confidence { get; }

    public ProtectionState ProtectionState { get; }

    public IReadOnlyList<Evidence> Evidence { get; }

    private static ReadOnlyCollection<FindingFacet> CopyFacets(IEnumerable<FindingFacet> facets)
    {
        var copy = new List<FindingFacet>();
        var seen = new HashSet<FindingFacet>();

        foreach (FindingFacet facet in facets)
        {
            if (!Enum.IsDefined(facet))
            {
                throw new ArgumentOutOfRangeException(nameof(facets));
            }

            if (seen.Add(facet))
            {
                copy.Add(facet);
            }
        }

        return new ReadOnlyCollection<FindingFacet>(copy);
    }

    private static ReadOnlyCollection<Evidence> CopyEvidence(IEnumerable<Evidence> evidence)
    {
        var copy = new List<Evidence>();

        foreach (Evidence? item in evidence)
        {
            if (item is null)
            {
                throw new ArgumentException(
                    "Evidence cannot contain null items.",
                    nameof(evidence));
            }

            copy.Add(item);
        }

        if (copy.Count == 0)
        {
            throw new ArgumentException("At least one evidence item is required.", nameof(evidence));
        }

        return new ReadOnlyCollection<Evidence>(copy);
    }
}
