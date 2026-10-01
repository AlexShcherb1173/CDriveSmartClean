using CDriveSmartClean.Application.Analysis;
using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Analysis;
using CDriveSmartClean.Domain.Findings;

namespace CDriveSmartClean.Analysis;

internal sealed class DeterministicClassificationEngine(StorageClassificationContext context)
{
    private sealed record Match(FindingCategory Category, Evidence Evidence, int Authority, int Specificity);
    internal sealed record Result(FindingCategory Category, FindingFacet[] Facets, Confidence Confidence,
        Evidence[] Evidence, AnalysisReason Reasons);

    internal Result Classify(string canonicalPath, StorageEntryAttributes attributes)
    {
        var matches = new List<Match>();
        Add(matches, canonicalPath, context.WindowsDirectory, FindingCategory.System,
            "category.system.windows_directory", "Path is under the authoritative Windows directory.",
            Confidence.Verified, 3, 40);
        foreach (string root in context.ProgramFilesRoots)
            Add(matches, canonicalPath, root, FindingCategory.Application,
                "category.application.program_files", "Path is under an authoritative Program Files root.",
                Confidence.High, 2, 30);
        Add(matches, canonicalPath, context.ProgramDataRoot, FindingCategory.ApplicationData,
            "category.application_data.program_data", "Path is under the authoritative ProgramData root.",
            Confidence.High, 2, 40);
        foreach (string root in context.CurrentUserAppDataRoots)
            Add(matches, canonicalPath, root, FindingCategory.ApplicationData,
                "category.application_data.app_data", "Path is under an authoritative current-user AppData root.",
                Confidence.High, 2, 50);
        Add(matches, canonicalPath, context.CurrentUserProfileRoot, FindingCategory.UserData,
            "category.user_data.current_profile", "Path is under the authoritative current-user profile root.",
            Confidence.High, 2, 20);
        Add(matches, canonicalPath, context.PublicRoot, FindingCategory.UserData,
            "category.user_data.public", "Path is under the authoritative Public profile root.",
            Confidence.High, 2, 30);

        AnalysisReason reasons = AnalysisReason.None;
        FindingCategory category;
        Confidence confidence;
        Evidence[] categoryEvidence;
        if (matches.Count == 0)
        {
            category = FindingCategory.Unknown;
            confidence = Confidence.Unknown;
            categoryEvidence =
            [
                new Evidence("classification.unknown.no_supported_rule",
                    "No supported deterministic primary-category rule matched.", Confidence.Unknown),
            ];
        }
        else
        {
            int authority = matches.Max(match => match.Authority);
            int specificity = matches.Where(match => match.Authority == authority).Max(match => match.Specificity);
            Match[] strongest = matches.Where(match => match.Authority == authority && match.Specificity == specificity)
                .OrderBy(match => match.Category).ThenBy(match => match.Evidence.Code, StringComparer.Ordinal).ToArray();
            if (strongest.Select(match => match.Category).Distinct().Count() != 1)
            {
                category = FindingCategory.Unknown;
                confidence = Confidence.Unknown;
                reasons |= AnalysisReason.ClassificationRuleConflict;
                categoryEvidence =
                [
                    new Evidence("classification_rule_conflict",
                        "Equal-strength deterministic rules disagreed on the primary category.", Confidence.Verified),
                ];
            }
            else
            {
                category = strongest[0].Category;
                confidence = strongest.Max(match => match.Evidence.Confidence);
                categoryEvidence = strongest.Select(match => match.Evidence).ToArray();
            }
        }

        var facets = new List<FindingFacet>();
        var evidence = new List<Evidence>(categoryEvidence);
        if ((attributes & StorageEntryAttributes.Compressed) != 0)
        {
            facets.Add(FindingFacet.Compressed);
            evidence.Add(new Evidence("facet.compressed", "Native entry evidence reports compression.", Confidence.Verified));
        }
        if ((attributes & StorageEntryAttributes.Sparse) != 0)
        {
            facets.Add(FindingFacet.Sparse);
            evidence.Add(new Evidence("facet.sparse", "Native entry evidence reports a sparse object.", Confidence.Verified));
        }
        if (StorageEntryAttributePolicy.IsRecallSensitive(attributes))
        {
            facets.Add(FindingFacet.CloudPlaceholder);
            evidence.Add(new Evidence("facet.cloud_placeholder",
                "Native entry attributes indicate recall-sensitive placeholder behavior.", Confidence.Verified));
        }
        return new Result(category, facets.Distinct().Order().ToArray(), confidence,
            evidence.OrderBy(item => item.Code, StringComparer.Ordinal).ToArray(), reasons);
    }

    private static void Add(List<Match> matches, string path, string? root, FindingCategory category,
        string code, string description, Confidence confidence, int authority, int specificity)
    {
        if (root is not null && Contains(root, path))
            matches.Add(new Match(category, new Evidence(code, description, confidence), authority, specificity));
    }

    private static bool Contains(string root, string path)
    {
        string normalized = root.TrimEnd('\\');
        return path.Equals(normalized, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(normalized + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
