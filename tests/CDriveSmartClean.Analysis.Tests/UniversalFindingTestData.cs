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
        VolumeIdentity? accountingVolume = null,
        IEnumerable<AllocationGroup>? allocationGroups = null,
        IEnumerable<CategorySummary>? categorySummaries = null,
        StorageAggregate? accountingAggregate = null,
        AccountingReason accountingReasons = AccountingReason.None,
        StorageClassificationContext? classificationContext = null,
        string systemRoot = @"C:\",
        bool deriveHierarchyNodes = true)
    {
        categoryAllocations ??= new Dictionary<FindingCategory, long?>();
        CategorySummary[] summaries = categorySummaries?.ToArray() ?? Enum.GetValues<FindingCategory>().Select(category =>
        {
            bool present = categoryAllocations.TryGetValue(category, out long? allocated);
            return new CategorySummary(category, present ? allocated : 0, present ? allocated ?? 0 : 0,
                0, present ? 1 : 0, present ? 1 : 0, analysisQuality, analysisReasons);
        }).ToArray();
        long total = categoryAllocations.Values.Where(value => value is not null).Sum(value => value!.Value);
        var aggregate = accountingAggregate ?? new StorageAggregate(rawReportedAllocatedBytes: total,
            inclusiveAttributedObservedAllocatedBytes: total,
            fileCount: categoryAllocations.Count);
        var childNodes = (hierarchyChildren ?? []).ToDictionary(item => item.RelativePath, StringComparer.Ordinal);
        foreach (StorageAnalysisCandidate candidate in (hierarchy ?? []).Concat(unknown ?? [])
                     .Where(item => deriveHierarchyNodes && item.Scope == AnalysisCandidateScope.HierarchyNode))
        {
            if (childNodes.ContainsKey(candidate.RelativePaths[0])) continue;
            childNodes.Add(candidate.RelativePaths[0], new StorageHierarchyNode(candidate.RelativePaths[0],
                new StorageAggregate(
                    visibleLogicalMeasuredBytes: candidate.SizeEvidence.VisibleLogicalBytes ?? 0,
                    rawReportedAllocatedBytes: candidate.SizeEvidence.RawReportedAllocatedBytes ?? 0,
                    uncertainMeasuredAllocatedBytes: candidate.SizeEvidence.UncertainMeasuredAllocatedBytes ?? 0,
                    inclusiveAttributedObservedAllocatedBytes:
                        candidate.SizeEvidence.ObservedAttributedAllocatedBytes ?? 0,
                    fileCount: candidate.FileCount,
                    directoryCount: candidate.DirectoryCount), []));
        }
        StorageHierarchyNode? root = accountingReasons.HasFlag(AccountingReason.ResourceLimit) ||
            accountingReasons.HasFlag(AccountingReason.ArithmeticOverflow)
            ? null : new StorageHierarchyNode("", aggregate, childNodes.Values);
        var summary = new StorageAccountingSummary(root is null ? null : aggregate, accountingReasons);
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
        IEnumerable<StorageAnalysisCandidate> identityCandidates = (identities ?? []).Concat(files ?? [])
            .Concat(unknown ?? []).Where(item => item.Scope is AnalysisCandidateScope.File or AnalysisCandidateScope.IdentityGroup);
        AllocationGroup[] groups = allocationGroups?.ToArray() ?? identityCandidates
            .GroupBy(item => item.ObjectIdentity!)
            .Select(group =>
            {
                StorageAnalysisCandidate candidate = group.First();
                return new AllocationGroup(candidate.ObjectIdentity!, candidate.RelativePaths,
                    candidate.SizeEvidence.ObservedAttributedAllocatedBytes, AccountingReason.None,
                    candidate.RelativePaths[0]);
            }).ToArray();
        var accounting = new StorageAccountingResult(summary, root, groups,
            new VolumeReconciliation(start, end, summary, true), true,
            new Dictionary<StorageTraversalIssueKind, long>());
        var analysis = analysisQuality == AnalysisQuality.Unavailable
            ? new StorageAnalysisResult(analysisQuality, analysisReasons, summary.Quality, summary.Reasons,
                [], [], [], [], [])
            : new StorageAnalysisResult(analysisQuality, analysisReasons, summary.Quality, summary.Reasons,
                summaries, hierarchy ?? [], identities ?? [], files ?? [], unknown ?? []);
        return new UniversalFindingRequest(SessionId, AnalysisRequest(classificationContext, systemRoot),
            analysis, accounting, options);
    }

    internal static StorageAnalysisRequest AnalysisRequest(
        StorageClassificationContext? context = null, string systemRoot = @"C:\") => new(
        new SystemVolumeDescriptor(Volume, systemRoot),
        context ?? new StorageClassificationContext(Volume, @"C:\Windows",
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
        Confidence? confidence = null,
        long? rawAllocatedBytes = null,
        long? observedAllocatedBytes = null,
        long? uncertainAllocatedBytes = null,
        long? fileCount = null,
        long? directoryCount = null,
        IEnumerable<Evidence>? additionalEvidence = null)
    {
        identity ??= scope is AnalysisCandidateScope.File or AnalysisCandidateScope.IdentityGroup
            ? new StorageObjectIdentity(Volume, StableObjectId(path)) : null;
        string[] relativePaths = (paths ?? [path]).ToArray();
        var evidence = new List<Evidence>
        {
            new("classification.test", "Deterministic test classification.", confidence ?? CategoryConfidence(category))
        };
        evidence.AddRange(additionalEvidence ?? []);
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
            evidence, new AnalysisSizeEvidence(logicalBytes, rawAllocatedBytes ?? allocatedBytes,
                observedAllocatedBytes ?? allocatedBytes, uncertainAllocatedBytes ?? 0),
            relativePaths, identity,
            fileCount ?? (scope == AnalysisCandidateScope.HierarchyNode ? 2 : 1),
            directoryCount ?? (scope == AnalysisCandidateScope.HierarchyNode ? 1 : 0));
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
