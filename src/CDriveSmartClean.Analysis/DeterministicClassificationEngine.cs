using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;

namespace CDriveSmartClean.Analysis;

internal sealed class DeterministicClassificationEngine(StorageClassificationContext context)
{
    [Flags]
    internal enum EvidenceCode : ushort
    {
        None = 0,
        Unknown = 1 << 0,
        RuleConflict = 1 << 1,
        WindowsDirectory = 1 << 2,
        ProgramFiles = 1 << 3,
        ProgramData = 1 << 4,
        AppData = 1 << 5,
        CurrentProfile = 1 << 6,
        PublicProfile = 1 << 7,
        Compressed = 1 << 8,
        Sparse = 1 << 9,
        CloudPlaceholder = 1 << 10,
    }

    internal readonly record struct CompactResult(FindingCategory Category, uint FacetBits,
        Confidence Confidence, EvidenceCode EvidenceCodes, AnalysisReason Reasons);

    internal sealed record Result(FindingCategory Category, FindingFacet[] Facets, Confidence Confidence,
        Evidence[] Evidence, AnalysisReason Reasons);

    internal Result Classify(string canonicalPath, StorageEntryAttributes attributes) =>
        Materialize(ClassifyCompact(canonicalPath, attributes));

    internal CompactResult ClassifyCompact(string canonicalPath, StorageEntryAttributes attributes)
    {
        int authority = -1;
        int specificity = -1;
        ulong categoryMask = 0;
        EvidenceCode categoryEvidence = EvidenceCode.None;
        Confidence confidence = Confidence.Unknown;

        Add(canonicalPath, context.WindowsDirectory, FindingCategory.System, EvidenceCode.WindowsDirectory,
            Confidence.Verified, 3, 40, ref authority, ref specificity, ref categoryMask,
            ref categoryEvidence, ref confidence);
        foreach (string root in context.ProgramFilesRoots)
            Add(canonicalPath, root, FindingCategory.Application, EvidenceCode.ProgramFiles,
                Confidence.High, 2, 30, ref authority, ref specificity, ref categoryMask,
                ref categoryEvidence, ref confidence);
        Add(canonicalPath, context.ProgramDataRoot, FindingCategory.ApplicationData, EvidenceCode.ProgramData,
            Confidence.High, 2, 40, ref authority, ref specificity, ref categoryMask,
            ref categoryEvidence, ref confidence);
        foreach (string root in context.CurrentUserAppDataRoots)
            Add(canonicalPath, root, FindingCategory.ApplicationData, EvidenceCode.AppData,
                Confidence.High, 2, 50, ref authority, ref specificity, ref categoryMask,
                ref categoryEvidence, ref confidence);
        Add(canonicalPath, context.CurrentUserProfileRoot, FindingCategory.UserData, EvidenceCode.CurrentProfile,
            Confidence.High, 2, 20, ref authority, ref specificity, ref categoryMask,
            ref categoryEvidence, ref confidence);
        Add(canonicalPath, context.PublicRoot, FindingCategory.UserData, EvidenceCode.PublicProfile,
            Confidence.High, 2, 30, ref authority, ref specificity, ref categoryMask,
            ref categoryEvidence, ref confidence);

        FindingCategory category;
        AnalysisReason reasons = AnalysisReason.None;
        if (categoryMask == 0)
        {
            category = FindingCategory.Unknown;
            confidence = Confidence.Unknown;
            categoryEvidence = EvidenceCode.Unknown;
        }
        else if ((categoryMask & (categoryMask - 1)) != 0)
        {
            category = FindingCategory.Unknown;
            confidence = Confidence.Unknown;
            categoryEvidence = EvidenceCode.RuleConflict;
            reasons = AnalysisReason.ClassificationRuleConflict;
        }
        else
        {
            category = (FindingCategory)BitPosition(categoryMask);
        }

        uint facets = 0;
        if ((attributes & StorageEntryAttributes.Compressed) != 0)
        {
            facets |= FacetBit(FindingFacet.Compressed);
            categoryEvidence |= EvidenceCode.Compressed;
        }
        if ((attributes & StorageEntryAttributes.Sparse) != 0)
        {
            facets |= FacetBit(FindingFacet.Sparse);
            categoryEvidence |= EvidenceCode.Sparse;
        }
        if (StorageEntryAttributePolicy.IsRecallSensitive(attributes))
        {
            facets |= FacetBit(FindingFacet.CloudPlaceholder);
            categoryEvidence |= EvidenceCode.CloudPlaceholder;
        }
        return new CompactResult(category, facets, confidence, categoryEvidence, reasons);
    }

    internal static Result Materialize(CompactResult compact)
    {
        FindingFacet[] facets = Enum.GetValues<FindingFacet>()
            .Where(facet => (compact.FacetBits & FacetBit(facet)) != 0).ToArray();
        Evidence[] evidence = Enum.GetValues<EvidenceCode>()
            .Where(code => code != EvidenceCode.None && (compact.EvidenceCodes & code) != 0)
            .Select(CreateEvidence).OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        return new Result(compact.Category, facets, compact.Confidence, evidence, compact.Reasons);
    }

    private static void Add(string path, string? root, FindingCategory category, EvidenceCode evidence,
        Confidence matchConfidence, int matchAuthority, int matchSpecificity, ref int authority,
        ref int specificity, ref ulong categoryMask, ref EvidenceCode evidenceCodes, ref Confidence confidence)
    {
        if (root is null || !Contains(root, path)) return;
        if (matchAuthority < authority || matchAuthority == authority && matchSpecificity < specificity) return;
        if (matchAuthority > authority || matchSpecificity > specificity)
        {
            authority = matchAuthority;
            specificity = matchSpecificity;
            categoryMask = 0;
            evidenceCodes = EvidenceCode.None;
            confidence = Confidence.Unknown;
        }
        categoryMask |= 1UL << (int)category;
        evidenceCodes |= evidence;
        if (matchConfidence > confidence) confidence = matchConfidence;
    }

    private static int BitPosition(ulong value)
    {
        int position = 0;
        while ((value >>= 1) != 0) position++;
        return position;
    }

    private static uint FacetBit(FindingFacet facet) => 1U << (int)facet;

    private static Evidence CreateEvidence(EvidenceCode code) => code switch
    {
        EvidenceCode.Unknown => new Evidence("classification.unknown.no_supported_rule",
            "No supported deterministic primary-category rule matched.", Confidence.Unknown),
        EvidenceCode.RuleConflict => new Evidence("classification_rule_conflict",
            "Equal-strength deterministic rules disagreed on the primary category.", Confidence.Verified),
        EvidenceCode.WindowsDirectory => new Evidence("category.system.windows_directory",
            "Path is under the authoritative Windows directory.", Confidence.Verified),
        EvidenceCode.ProgramFiles => new Evidence("category.application.program_files",
            "Path is under an authoritative Program Files root.", Confidence.High),
        EvidenceCode.ProgramData => new Evidence("category.application_data.program_data",
            "Path is under the authoritative ProgramData root.", Confidence.High),
        EvidenceCode.AppData => new Evidence("category.application_data.app_data",
            "Path is under an authoritative current-user AppData root.", Confidence.High),
        EvidenceCode.CurrentProfile => new Evidence("category.user_data.current_profile",
            "Path is under the authoritative current-user profile root.", Confidence.High),
        EvidenceCode.PublicProfile => new Evidence("category.user_data.public",
            "Path is under the authoritative Public profile root.", Confidence.High),
        EvidenceCode.Compressed => new Evidence("facet.compressed",
            "Native entry evidence reports compression.", Confidence.Verified),
        EvidenceCode.Sparse => new Evidence("facet.sparse",
            "Native entry evidence reports a sparse object.", Confidence.Verified),
        EvidenceCode.CloudPlaceholder => new Evidence("facet.cloud_placeholder",
            "Native entry attributes indicate recall-sensitive placeholder behavior.", Confidence.Verified),
        _ => throw new ArgumentOutOfRangeException(nameof(code)),
    };

    private static bool Contains(string root, string path)
    {
        string normalized = root.TrimEnd('\\');
        return path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
