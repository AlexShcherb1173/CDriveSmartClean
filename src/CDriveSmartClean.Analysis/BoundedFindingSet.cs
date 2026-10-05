namespace CDriveSmartClean.Analysis;

internal sealed class BoundedFindingSet<T>
{
    private readonly int limit;
    private readonly SortedSet<T> values;

    internal BoundedFindingSet(int limit, IComparer<T> comparer)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        this.limit = limit;
        values = new SortedSet<T>(comparer ?? throw new ArgumentNullException(nameof(comparer)));
    }

    internal void Add(T value)
    {
        values.Add(value);
        if (values.Count > limit) values.Remove(values.Max!);
    }

    internal T[] ToArray() => values.ToArray();
}
