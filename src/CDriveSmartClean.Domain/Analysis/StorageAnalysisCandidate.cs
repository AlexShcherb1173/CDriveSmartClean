using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Domain.Analysis;

public sealed class StorageAnalysisCandidate
{
    public StorageAnalysisCandidate(AnalysisCandidateScope scope, FindingCategory primaryCategory,
        IEnumerable<FindingFacet> facets, Confidence classificationConfidence, IEnumerable<Evidence> evidence,
        AnalysisSizeEvidence sizeEvidence, IEnumerable<string> relativePaths, StorageObjectIdentity? objectIdentity,
        long fileCount, long directoryCount)
    {
        if (!Enum.IsDefined(scope)) throw new ArgumentOutOfRangeException(nameof(scope));
        if (!Enum.IsDefined(primaryCategory)) throw new ArgumentOutOfRangeException(nameof(primaryCategory));
        if (!Enum.IsDefined(classificationConfidence)) throw new ArgumentOutOfRangeException(nameof(classificationConfidence));
        ArgumentNullException.ThrowIfNull(facets);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(sizeEvidence);
        ArgumentNullException.ThrowIfNull(relativePaths);
        ArgumentOutOfRangeException.ThrowIfNegative(fileCount);
        ArgumentOutOfRangeException.ThrowIfNegative(directoryCount);
        FindingFacet[] orderedFacets = facets.ToArray();
        if (orderedFacets.Any(facet => !Enum.IsDefined(facet)))
            throw new ArgumentOutOfRangeException(nameof(facets));
        orderedFacets = orderedFacets.Distinct().Order().ToArray();
        Evidence[] orderedEvidence = evidence.ToArray();
        if (orderedEvidence.Any(item => item is null))
            throw new ArgumentException("Evidence cannot contain null.", nameof(evidence));
        IGrouping<string, Evidence>[] evidenceGroups = orderedEvidence
            .GroupBy(item => item.Code, StringComparer.Ordinal).ToArray();
        if (evidenceGroups.Any(group => group.Skip(1).Any(item =>
                !item.Description.Equals(group.First().Description, StringComparison.Ordinal) ||
                item.Confidence != group.First().Confidence)))
            throw new ArgumentException("Evidence sharing a code must be semantically identical.", nameof(evidence));
        orderedEvidence = evidenceGroups.Select(group => group.First())
            .OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        string[] orderedPaths = relativePaths.ToArray();
        if (orderedPaths.Length == 0 || orderedPaths.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty path is required.", nameof(relativePaths));
        orderedPaths = orderedPaths.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if (scope == AnalysisCandidateScope.IdentityGroup && objectIdentity is null)
            throw new ArgumentException("Identity-group candidates require native identity.", nameof(objectIdentity));
        if (scope == AnalysisCandidateScope.File && (objectIdentity is null || orderedPaths.Length != 1))
            throw new ArgumentException("File candidates require one path and native identity.", nameof(objectIdentity));
        Scope = scope;
        PrimaryCategory = primaryCategory;
        Facets = Array.AsReadOnly(orderedFacets);
        ClassificationConfidence = classificationConfidence;
        Evidence = Array.AsReadOnly(orderedEvidence);
        SizeEvidence = sizeEvidence;
        RelativePaths = Array.AsReadOnly(orderedPaths);
        ObjectIdentity = objectIdentity;
        FileCount = fileCount;
        DirectoryCount = directoryCount;
    }

    public AnalysisCandidateScope Scope { get; }
    public FindingCategory PrimaryCategory { get; }
    public IReadOnlyList<FindingFacet> Facets { get; }
    public Confidence ClassificationConfidence { get; }
    public IReadOnlyList<Evidence> Evidence { get; }
    public AnalysisSizeEvidence SizeEvidence { get; }
    public IReadOnlyList<string> RelativePaths { get; }
    public StorageObjectIdentity? ObjectIdentity { get; }
    public long FileCount { get; }
    public long DirectoryCount { get; }
}
