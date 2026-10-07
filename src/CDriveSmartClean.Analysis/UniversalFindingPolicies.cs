using System.Security.Cryptography;
using System.Text;
using CDriveSmartClean.Domain;
using CDriveSmartClean.Domain.Findings;
using CDriveSmartClean.Domain.Risk;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Analysis;

internal static class UniversalFindingPolicies
{
    internal const long OneGibibyte = 1_073_741_824;
    internal const long SixteenGibibytes = 17_179_869_184;

    internal static long LargeThreshold(long capacityBytes) =>
        Math.Clamp(capacityBytes / 100, OneGibibyte, SixteenGibibytes);

    internal static bool IsLarge(long? allocatedBytes, long? capacityBytes) =>
        allocatedBytes is { } allocated && capacityBytes is { } capacity &&
        allocated >= LargeThreshold(capacity);

    internal static RiskLevel Risk(FindingCategory category) =>
        category == FindingCategory.System ? RiskLevel.Critical : RiskLevel.High;

    internal static ProtectionState Protection(FindingCategory category) =>
        category == FindingCategory.System ? ProtectionState.Protected : ProtectionState.ReviewRequired;

    internal static Confidence CategoryConfidence(FindingCategory category) => category switch
    {
        FindingCategory.System => Confidence.Verified,
        FindingCategory.Application => Confidence.High,
        FindingCategory.ApplicationData => Confidence.High,
        FindingCategory.UserData => Confidence.High,
        FindingCategory.Unknown => Confidence.Unknown,
        _ => Confidence.Unknown
    };

    internal static Confidence Minimum(params Confidence[] values) => values.Min();

    internal static Confidence QualityCap(Confidence confidence, bool incomplete) =>
        incomplete && confidence > Confidence.Medium ? Confidence.Medium : confidence;

    internal static string CategoryEvidenceCode(FindingCategory category) => category switch
    {
        FindingCategory.System => "finding.category.system",
        FindingCategory.Application => "finding.category.application",
        FindingCategory.ApplicationData => "finding.category.application_data",
        FindingCategory.UserData => "finding.category.user_data",
        FindingCategory.Unknown => "finding.category.unknown",
        _ => throw new ArgumentOutOfRangeException(nameof(category))
    };

    internal static string CategoryRiskReason(FindingCategory category) => category switch
    {
        FindingCategory.System => "risk.system_data",
        FindingCategory.Application => "risk.application_files",
        FindingCategory.ApplicationData => "risk.application_data",
        FindingCategory.UserData => "risk.user_data",
        FindingCategory.Unknown => "risk.unknown_purpose",
        _ => "risk.unknown_purpose"
    };

    internal static Guid FindingId(Guid scanSessionId, string stableKey)
    {
        byte[] namespaceBytes = scanSessionId.ToByteArray();
        SwapGuidByteOrder(namespaceBytes);
        byte[] nameBytes = Encoding.UTF8.GetBytes("f1-15/v1/" + stableKey);
        byte[] input = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, input, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, input, namespaceBytes.Length, nameBytes.Length);
#pragma warning disable CA5350 // RFC 4122 UUIDv5 requires SHA-1; this is an identifier, not a security primitive.
        byte[] hash = SHA1.HashData(input);
#pragma warning restore CA5350
        byte[] result = hash[..16];
        result[6] = (byte)((result[6] & 0x0f) | 0x50);
        result[8] = (byte)((result[8] & 0x3f) | 0x80);
        SwapGuidByteOrder(result);
        return new Guid(result);
    }

    internal static bool TryGetVolumeRelativePath(
        string absolutePath, VolumeIdentity? expectedVolume, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(absolutePath) || absolutePath.Contains('/') || absolutePath.Contains('\0'))
            return false;

        int rootLength;
        if (absolutePath.Length >= 3 && IsAsciiLetter(absolutePath[0]) &&
            absolutePath[1] == ':' && absolutePath[2] == '\\')
        {
            rootLength = 3;
        }
        else
        {
            const string volumePrefix = @"\\?\Volume";
            const int guidWithBracesLength = 38;
            int separatorIndex = volumePrefix.Length + guidWithBracesLength;
            if (!absolutePath.StartsWith(volumePrefix, StringComparison.OrdinalIgnoreCase) ||
                absolutePath.Length <= separatorIndex || absolutePath[separatorIndex] != '\\' ||
                !Guid.TryParseExact(absolutePath.AsSpan(volumePrefix.Length, guidWithBracesLength), "B", out Guid id) ||
                id == Guid.Empty || expectedVolume is not null && id != expectedVolume.Id)
                return false;
            rootLength = separatorIndex + 1;
        }

        string remainder = absolutePath[rootLength..].TrimEnd('\\');
        if (remainder.Length != 0 && !IsCanonicalRelativePath(remainder)) return false;
        relativePath = remainder;
        return true;
    }

    internal static bool IsCanonicalRelativePath(string path) =>
        path.Length != 0 && !path.StartsWith('\\') && !path.EndsWith('\\') &&
        !path.Contains('/') && !path.Contains('\0') && !path.Contains(':') &&
        path.Split('\\').All(component => component.Length != 0 && component is not "." and not "..");

    internal static bool IsDescendantPath(string path, string root)
    {
        if (path.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
        return root.Length == 0 || path.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsCacheLikeRelativePath(string relativePath, IEnumerable<string> acceptedRoots)
    {
        foreach (string root in acceptedRoots)
        {
            if (!IsDescendantPath(relativePath, root)) continue;
            string descendant = root.Length == 0 ? relativePath : relativePath.Length == root.Length
                ? string.Empty : relativePath[(root.Length + 1)..];
            if (descendant.Split('\\', StringSplitOptions.RemoveEmptyEntries)
                .Any(component => component.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
                                  component.Equals("caches", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }

    internal static bool IsCacheLikePath(string absolutePath, IEnumerable<string> acceptedRoots)
    {
        if (!TryGetVolumeRelativePath(absolutePath, null, out string relativePath)) return false;
        var relativeRoots = new List<string>();
        foreach (string root in acceptedRoots)
        {
            if (!TryGetVolumeRelativePath(root, null, out string relativeRoot)) return false;
            relativeRoots.Add(relativeRoot);
        }
        return IsCacheLikeRelativePath(relativePath, relativeRoots);
    }

    private static bool IsAsciiLetter(char value) => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static void SwapGuidByteOrder(byte[] guid)
    {
        (guid[0], guid[3]) = (guid[3], guid[0]);
        (guid[1], guid[2]) = (guid[2], guid[1]);
        (guid[4], guid[5]) = (guid[5], guid[4]);
        (guid[6], guid[7]) = (guid[7], guid[6]);
    }
}
