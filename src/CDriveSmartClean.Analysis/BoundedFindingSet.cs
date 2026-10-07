namespace CDriveSmartClean.Analysis;

internal sealed class BoundedFindingSet<T>
{
    private readonly int limit;
    private readonly Func<T, string> keySelector;
    private readonly HashSet<string> seenKeys = new(StringComparer.Ordinal);
    private readonly SortedSet<T> values;

    internal BoundedFindingSet(int limit, IComparer<T> comparer, Func<T, string> keySelector)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        this.limit = limit;
        this.keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
        values = new SortedSet<T>(comparer ?? throw new ArgumentNullException(nameof(comparer)));
    }

    internal void Add(T value)
    {
        string key = keySelector(value);
        if (!seenKeys.Add(key)) throw new InvalidOperationException("Bounded findings require unique stable keys.");
        if (!values.Add(value)) throw new InvalidOperationException("Finding comparer is not a total order.");
        if (values.Count <= limit) return;
        T removed = values.Max!;
        values.Remove(removed);
    }

    internal T[] ToArray() => values.ToArray();
}
