namespace CDriveSmartClean.Domain.Storage;

public sealed class StorageHierarchyNode
{
    public StorageHierarchyNode(string relativePath, StorageAggregate aggregate, IEnumerable<StorageHierarchyNode> children)
    {
        ArgumentNullException.ThrowIfNull(relativePath);
        ArgumentNullException.ThrowIfNull(aggregate);
        ArgumentNullException.ThrowIfNull(children);
        RelativePath = relativePath;
        Aggregate = aggregate;
        StorageHierarchyNode[] ordered = children.OrderBy(child => child.RelativePath, StringComparer.Ordinal).ToArray();
        if (ordered.Select(child => child.RelativePath).Distinct(StringComparer.Ordinal).Count() != ordered.Length)
            throw new ArgumentException("Duplicate hierarchy nodes.", nameof(children));
        Children = Array.AsReadOnly(ordered);
    }
    public string RelativePath { get; }
    public StorageAggregate Aggregate { get; }
    public IReadOnlyList<StorageHierarchyNode> Children { get; }
}
