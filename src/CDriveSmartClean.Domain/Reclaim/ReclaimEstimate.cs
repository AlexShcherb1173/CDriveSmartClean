using System.Collections.ObjectModel;

namespace CDriveSmartClean.Domain.Reclaim;

public sealed class ReclaimEstimate
{
    private ReclaimEstimate(
        ReclaimKind kind,
        long? minimumBytes,
        long? expectedBytes,
        long? maximumBytes,
        string basis,
        IReadOnlyList<string> preconditions,
        IReadOnlyList<Guid> overlapGroupIds,
        Confidence confidence,
        bool requiresRestart,
        bool requiresCompaction)
    {
        Kind = kind;
        MinimumBytes = minimumBytes;
        ExpectedBytes = expectedBytes;
        MaximumBytes = maximumBytes;
        Basis = basis;
        Preconditions = preconditions;
        OverlapGroupIds = overlapGroupIds;
        Confidence = confidence;
        RequiresRestart = requiresRestart;
        RequiresCompaction = requiresCompaction;
    }

    public ReclaimKind Kind { get; }

    public long? MinimumBytes { get; }

    public long? ExpectedBytes { get; }

    public long? MaximumBytes { get; }

    public string Basis { get; }

    public IReadOnlyList<string> Preconditions { get; }

    public IReadOnlyList<Guid> OverlapGroupIds { get; }

    public Confidence Confidence { get; }

    public bool RequiresRestart { get; }

    public bool RequiresCompaction { get; }

    public static ReclaimEstimate Exact(
        long bytes,
        string basis,
        bool requiresRestart = false,
        bool requiresCompaction = false,
        IEnumerable<Guid>? overlapGroupIds = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);

        return new ReclaimEstimate(
            ReclaimKind.Exact,
            bytes,
            bytes,
            bytes,
            NormalizeBasis(basis),
            Array.Empty<string>(),
            NormalizeOverlapGroupIds(overlapGroupIds),
            Confidence.Verified,
            requiresRestart,
            requiresCompaction);
    }

    public static ReclaimEstimate Estimated(
        long minimumBytes,
        long expectedBytes,
        long maximumBytes,
        Confidence confidence,
        string basis,
        IEnumerable<string>? preconditions = null,
        bool requiresRestart = false,
        bool requiresCompaction = false,
        IEnumerable<Guid>? overlapGroupIds = null)
    {
        return CreateBounded(
            ReclaimKind.Estimated,
            minimumBytes,
            expectedBytes,
            maximumBytes,
            confidence,
            basis,
            preconditions,
            false,
            requiresRestart,
            requiresCompaction,
            overlapGroupIds);
    }

    public static ReclaimEstimate Conditional(
        long minimumBytes,
        long expectedBytes,
        long maximumBytes,
        Confidence confidence,
        string basis,
        IEnumerable<string> preconditions,
        bool requiresRestart = false,
        bool requiresCompaction = false,
        IEnumerable<Guid>? overlapGroupIds = null)
    {
        ArgumentNullException.ThrowIfNull(preconditions);

        return CreateBounded(
            ReclaimKind.Conditional,
            minimumBytes,
            expectedBytes,
            maximumBytes,
            confidence,
            basis,
            preconditions,
            true,
            requiresRestart,
            requiresCompaction,
            overlapGroupIds);
    }

    public static ReclaimEstimate UserDecision(
        long minimumBytes,
        long expectedBytes,
        long maximumBytes,
        Confidence confidence,
        string basis,
        IEnumerable<string>? preconditions = null,
        bool requiresRestart = false,
        bool requiresCompaction = false,
        IEnumerable<Guid>? overlapGroupIds = null)
    {
        return CreateBounded(
            ReclaimKind.UserDecision,
            minimumBytes,
            expectedBytes,
            maximumBytes,
            confidence,
            basis,
            preconditions,
            false,
            requiresRestart,
            requiresCompaction,
            overlapGroupIds);
    }

    public static ReclaimEstimate Unknown(
        string basis,
        IEnumerable<Guid>? overlapGroupIds = null)
    {
        return new ReclaimEstimate(
            ReclaimKind.Unknown,
            null,
            null,
            null,
            NormalizeBasis(basis),
            Array.Empty<string>(),
            NormalizeOverlapGroupIds(overlapGroupIds),
            Confidence.Unknown,
            false,
            false);
    }

    public static ReclaimEstimate None(
        string basis,
        IEnumerable<Guid>? overlapGroupIds = null)
    {
        return new ReclaimEstimate(
            ReclaimKind.None,
            0,
            0,
            0,
            NormalizeBasis(basis),
            Array.Empty<string>(),
            NormalizeOverlapGroupIds(overlapGroupIds),
            Confidence.Verified,
            false,
            false);
    }

    private static ReclaimEstimate CreateBounded(
        ReclaimKind kind,
        long minimumBytes,
        long expectedBytes,
        long maximumBytes,
        Confidence confidence,
        string basis,
        IEnumerable<string>? preconditions,
        bool requiresPrecondition,
        bool requiresRestart,
        bool requiresCompaction,
        IEnumerable<Guid>? overlapGroupIds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumBytes);

        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumBytes, expectedBytes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(expectedBytes, maximumBytes);

        if (!Enum.IsDefined(confidence) || confidence == Confidence.Unknown)
        {
            throw new ArgumentOutOfRangeException(nameof(confidence));
        }

        IReadOnlyList<string> normalizedPreconditions = NormalizeStrings(
            preconditions,
            nameof(preconditions));

        if (requiresPrecondition && normalizedPreconditions.Count == 0)
        {
            throw new ArgumentException(
                "At least one precondition is required.",
                nameof(preconditions));
        }

        return new ReclaimEstimate(
            kind,
            minimumBytes,
            expectedBytes,
            maximumBytes,
            NormalizeBasis(basis),
            normalizedPreconditions,
            NormalizeOverlapGroupIds(overlapGroupIds),
            confidence,
            requiresRestart,
            requiresCompaction);
    }

    private static string NormalizeBasis(string basis)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(basis);
        return basis.Trim();
    }

    private static IReadOnlyList<string> NormalizeStrings(
        IEnumerable<string>? values,
        string parameterName)
    {
        if (values is null)
        {
            return Array.Empty<string>();
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (string? value in values)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException(
                    "Collection items cannot be null or whitespace.",
                    parameterName);
            }

            string trimmed = value.Trim();
            if (seen.Add(trimmed))
            {
                normalized.Add(trimmed);
            }
        }

        return new ReadOnlyCollection<string>(normalized);
    }

    private static ReadOnlyCollection<Guid> NormalizeOverlapGroupIds(
        IEnumerable<Guid>? overlapGroupIds)
    {
        if (overlapGroupIds is null)
        {
            return Array.AsReadOnly(Array.Empty<Guid>());
        }

        var normalized = new List<Guid>();
        var seen = new HashSet<Guid>();

        foreach (Guid overlapGroupId in overlapGroupIds)
        {
            if (overlapGroupId == Guid.Empty)
            {
                throw new ArgumentException(
                    "Overlap group identifiers cannot be empty.",
                    nameof(overlapGroupIds));
            }

            if (seen.Add(overlapGroupId))
            {
                normalized.Add(overlapGroupId);
            }
        }

        return new ReadOnlyCollection<Guid>(normalized);
    }
}
