using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Accounting;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Analysis.Tests;

internal static class UniversalFindingTestData
{
    internal static readonly VolumeIdentity Volume =
        new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    internal static readonly Guid SessionId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    internal static UniversalFindingRequest Request(
        IReadOnlyDictionary<FindingCategory, long?>? categoryAllocations = null,
        IEnumerable<StorageAnalysisCandidate>? hierarchy = null,
        IEnumerable<StorageAnalysisCandidate>? identities = null,
        IEnumerable<StorageAnalysisCandidate>? files = null,
        IEnumerable<StorageAnalysisCandidate>? unknown = null,
        IEnumerable<StorageHierarchyNode>? hierarchyChildren = null,
        long? capacityBytes = 200L * 1_073_741_824,
        AnalysisQuality analysisQuality = AnalysisQuality.Complete,
        AnalysisReason analysisReasons = AnalysisReason.None,
        UniversalFindingOptions? options = null,
        VolumeIdentity? accountingVolume = null)
    {
        categoryAllocations ??= new Dictionary<FindingCategory, long?>();
        CategorySummary[] summaries = Enum.GetValues<FindingCategory>().Select(category =>
        {
            bool present = categoryAllocations.TryGetValue(category, out long? allocated);
            return new CategorySummary(category, present ? allocated : 0, present ? allocated ?? 0 : 0,
                0, present ? 1 : 0, present ? 1 : 0, analysisQuality, analysisReasons);
        }).ToArray();
        long total = categoryAllocations.Values.Where(value => value is not null).Sum(value => value!.Value);
        var aggregate = new StorageAggregate(rawReportedAllocatedBytes: total,
            inclusiveAttributedObservedAllocatedBytes: total,
            fileCount: categoryAllocations.Count);
        var root = new StorageHierarchyNode("", aggregate, hierarchyChildren ?? []);
        var summary = new StorageAccountingSummary(aggregate, AccountingReason.None);
        VolumeIdentity snapshotVolume = accountingVolume ?? Volume;
        VolumeSpaceSnapshot start;
        VolumeSpaceSnapshot end;
        if (capacityBytes is { } capacity)
        {
            if (capacity < total) capacity = total;
            start = VolumeSpaceSnapshot.Available(snapshotVolume, DateTimeOffset.UnixEpoch,
                capacity, capacity - total, capacity, capacity - total, total, 0, 0);
            end = start;
        }
        else
        {
            start = VolumeSpaceSnapshot.Unavailable(snapshotVolume, DateTimeOffset.UnixEpoch,
                VolumeSpaceFailure.VolumeUnavailable);
            end = start;
        }
        var accounting = new StorageAccountingResult(summary, root, [],
            new VolumeReconciliation(start, end, summary, true), true,
            new Dictionary<StorageTraversalIssueKind, long>());
        var analysis = analysisQuality == AnalysisQuality.Unavailable
            ? new StorageAnalysisResult(analysisQuality, analysisReasons, summary.Quality, summary.Reasons,
                [], [], [], [], [])
            : new StorageAnalysisResult(analysisQuality, analysisReasons, summary.Quality, summary.Reasons,
                summaries, hierarchy ?? [], identities ?? [], files ?? [], unknown ?? []);
        return new UniversalFindingRequest(SessionId, AnalysisRequest(), analysis, accounting, options);
    }

    internal static StorageAnalysisRequest AnalysisRequest() => new(
        new SystemVolumeDescriptor(Volume, @"C:\"),
        new StorageClassificationContext(Volume, @"C:\Windows",
            [@"C:\Program Files", @"C:\Program Files (x86)"], @"C:\ProgramData",
            @"C:\Users\Current", [@"C:\Users\Current\AppData\Local", @"C:\Users\Current\AppData\Roaming"],
            @"C:\Users\Public", @"C:\Users", []));

    internal static StorageAnalysisCandidate Candidate(
        AnalysisCandidateScope scope,
        FindingCategory category,
        string path,
        long logicalBytes,
        long allocatedBytes,
        IEnumerable<FindingFacet>? facets = null,
        StorageObjectIdentity? identity = null,
        IEnumerable<string>? paths = null,
        Confidence? confidence = null)
    {
        identity ??= scope is AnalysisCandidateScope.File or AnalysisCandidateScope.IdentityGroup
            ? new StorageObjectIdentity(Volume, StableObjectId(path)) : null;
        string[] relativePaths = (paths ?? [path]).ToArray();
        long fileCount = scope == AnalysisCandidateScope.HierarchyNode ? 2 : 1;
        long directoryCount = scope == AnalysisCandidateScope.HierarchyNode ? 1 : 0;
        var evidence = new List<Evidence>
        {
            new("classification.test", "Deterministic test classification.", confidence ?? CategoryConfidence(category))
        };
        foreach (FindingFacet facet in facets ?? [])
        {
            string code = facet switch
            {
                FindingFacet.Compressed => "facet.compressed",
                FindingFacet.Sparse => "facet.sparse",
                FindingFacet.HardLinked => "facet.hardlink_observed_alias",
                FindingFacet.CloudPlaceholder => "facet.cloud_placeholder",
                _ => "facet.test"
            };
            evidence.Add(new Evidence(code, $"Test evidence for {facet}.", Confidence.Verified));
        }
        return new StorageAnalysisCandidate(scope, category, facets ?? [], confidence ?? CategoryConfidence(category),
            evidence, new AnalysisSizeEvidence(logicalBytes, allocatedBytes, allocatedBytes, 0),
            relativePaths, identity, fileCount, directoryCount);
    }

    internal static StorageHierarchyNode HierarchyNode(
        string relativePath, long allocatedBytes, IEnumerable<StorageHierarchyNode>? children = null) =>
        new(relativePath, new StorageAggregate(visibleLogicalMeasuredBytes: allocatedBytes,
            rawReportedAllocatedBytes: allocatedBytes,
            directAttributedObservedAllocatedBytes: allocatedBytes,
            inclusiveAttributedObservedAllocatedBytes: allocatedBytes,
            fileCount: 1, directoryCount: children?.Any() == true ? 1 : 0), children ?? []);

    private static Confidence CategoryConfidence(FindingCategory category) => category switch
    {
        FindingCategory.System => Confidence.Verified,
        FindingCategory.Unknown => Confidence.Unknown,
        _ => Confidence.High
    };

    private static Guid StableObjectId(string value)
    {
        byte[] bytes = new byte[16];
        for (int index = 0; index < value.Length; index++) bytes[index % 16] ^= (byte)value[index];
        if (bytes.All(item => item == 0)) bytes[0] = 1;
        return new Guid(bytes);
    }
}
