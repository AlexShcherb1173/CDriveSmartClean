using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Scan.Accounting;

internal sealed class StorageHierarchyAccumulator
{
    internal sealed class Node(string path, int parent, int depth)
    {
        internal readonly string Path = path;
        internal readonly int Parent = parent;
        internal readonly int Depth = depth;
        internal readonly List<int> Children = [];
        internal readonly Dictionary<string, int> ChildIndexes = new(StringComparer.Ordinal);
        internal readonly long[] Values = new long[15];
    }

    internal readonly List<Node> Nodes = [new("", 0, 0)];
    private readonly Action<long> charge;
    private readonly int maximumDirectories;

    internal StorageHierarchyAccumulator(Action<long> charge, int maximumDirectories)
    {
        this.charge = charge;
        this.maximumDirectories = maximumDirectories;
        charge(1024);
    }

    internal static string ValidatePath(SystemVolumeDescriptor volume, string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string root = volume.RootPath.TrimEnd('\\') + "\\";
        if (root.Contains('/') || !path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Path is outside the authorized root.");
        string relative = path[root.Length..];
        if (relative.Length == 0 || relative.Contains('/') || relative.Contains(':') || relative.Contains('\0'))
            throw new InvalidOperationException("Malformed canonical descendant path.");
        foreach (string component in relative.Split('\\'))
            if (component.Length == 0 || component is "." or "..")
                throw new InvalidOperationException("Malformed canonical path component.");
        return relative;
    }

    internal int Directory(string path)
    {
        int parent = 0;
        foreach (string component in path.Split('\\', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Nodes[parent].ChildIndexes.TryGetValue(component, out int found))
            {
                parent = found;
                continue;
            }
            if (Nodes.Count >= maximumDirectories) throw new StorageIdentityLedger.ResourceLimitException();
            string next = Nodes[parent].Path.Length == 0 ? component : Nodes[parent].Path + "\\" + component;
            charge(checked(1024L + 4L * next.Length));
            int index = Nodes.Count;
            Nodes.Add(new Node(next, parent, checked(Nodes[parent].Depth + 1)));
            Nodes[parent].Children.Add(index);
            Nodes[parent].ChildIndexes.Add(component, index);
            parent = index;
        }
        return parent;
    }

    internal int[][] Ancestors(CancellationToken token)
    {
        int levels = 1;
        while ((1L << levels) <= Nodes.Count) levels++;
        charge(checked((long)Nodes.Count * levels * 8 + Nodes.Count * 64L));
        var ancestors = new int[levels][];
        ancestors[0] = Nodes.Select(n => n.Parent).ToArray();
        for (int level = 1; level < levels; level++)
        {
            token.ThrowIfCancellationRequested();
            ancestors[level] = new int[Nodes.Count];
            for (int i = 0; i < Nodes.Count; i++) ancestors[level][i] = ancestors[level - 1][ancestors[level - 1][i]];
        }
        return ancestors;
    }

    internal int CommonAncestor(int left, int right, int[][] ancestors)
    {
        if (Nodes[left].Depth < Nodes[right].Depth) (left, right) = (right, left);
        int difference = Nodes[left].Depth - Nodes[right].Depth;
        for (int level = 0; difference != 0; level++, difference >>= 1)
            if ((difference & 1) != 0) left = ancestors[level][left];
        if (left == right) return left;
        for (int level = ancestors.Length - 1; level >= 0; level--)
            if (ancestors[level][left] != ancestors[level][right])
                (left, right) = (ancestors[level][left], ancestors[level][right]);
        return Nodes[left].Parent;
    }

    internal StorageHierarchyNode Finish(CancellationToken token)
    {
        charge(checked(Nodes.Count * 1024L));
        var result = new StorageHierarchyNode[Nodes.Count];
        for (int i = Nodes.Count - 1; i >= 0; i--)
        {
            token.ThrowIfCancellationRequested();
            Node node = Nodes[i];
            node.Values[4] = node.Values[3];
            foreach (int child in node.Children)
                for (int field = 0; field < node.Values.Length; field++)
                    if (field != 3) node.Values[field] = checked(node.Values[field] + Nodes[child].Values[field]);
            long[] v = node.Values;
            result[i] = new StorageHierarchyNode(node.Path,
                new StorageAggregate(v[0], v[1], v[2], v[3], v[4], v[5], v[6], v[7],
                    v[8], v[9], v[10], v[11], v[12], v[13], v[14]), node.Children.Select(child => result[child]));
        }
        return result[0];
    }
}
