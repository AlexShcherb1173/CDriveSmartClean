using System.Runtime.CompilerServices;
using CDriveSmartClean.Application.ResourceLimits;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;

namespace CDriveSmartClean.Scan.Accounting;

internal sealed class StorageHierarchyAccumulator
{
    [InlineArray(15)]
    private struct NodeValues
    {
        private long first;
    }

    private sealed class Node(string path, int parent, int depth, int nextSibling)
    {
        internal readonly string Path = path;
        internal readonly int Parent = parent;
        internal readonly int Depth = depth;
        internal readonly int NextSibling = nextSibling;
        internal int FirstChild = -1;
        internal NodeValues Values;
    }

    private readonly List<Node> nodes = [new("", 0, 0, -1)];
    private readonly Dictionary<string, int> directoryIndexes = new(StringComparer.Ordinal) { [""] = 0 };
    private readonly Action<long> charge;
    private readonly int maximumDirectories;

    internal int DirectoryCount => nodes.Count;

    internal StorageHierarchyAccumulator(Action<long> charge, int maximumDirectories)
    {
        this.charge = charge;
        this.maximumDirectories = maximumDirectories;
        charge(256);
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
        if (path.Length == 0) return 0;
        int parent = 0;
        int start = 0;
        while (start < path.Length)
        {
            int separator = path.IndexOf('\\', start);
            int end = separator < 0 ? path.Length : separator;
            string next = end == path.Length ? path : path[..end];
            if (directoryIndexes.TryGetValue(next, out int found)) parent = found;
            else
            {
                if (nodes.Count >= maximumDirectories)
                    throw new StorageIdentityLedger.ResourceLimitException(
                        ResourceLimitDimension.MaximumDirectories, maximumDirectories, nodes.Count + 1L);
                charge(checked(256L + 2L * next.Length));
                int index = nodes.Count;
                var node = new Node(next, parent, checked(nodes[parent].Depth + 1), nodes[parent].FirstChild);
                nodes.Add(node);
                nodes[parent].FirstChild = index;
                directoryIndexes.Add(next, index);
                parent = index;
            }
            start = end + 1;
        }
        return parent;
    }

    internal int CommonAncestor(int left, int right, CancellationToken token)
    {
        while (nodes[left].Depth > nodes[right].Depth)
        {
            token.ThrowIfCancellationRequested();
            left = nodes[left].Parent;
        }
        while (nodes[right].Depth > nodes[left].Depth)
        {
            token.ThrowIfCancellationRequested();
            right = nodes[right].Parent;
        }
        while (left != right)
        {
            token.ThrowIfCancellationRequested();
            left = nodes[left].Parent;
            right = nodes[right].Parent;
        }
        return left;
    }

    internal string Path(int node) => nodes[node].Path;
    internal bool IsValidNode(int node) => (uint)node < (uint)nodes.Count;

    internal void AddValue(int node, int field, long value)
    {
        nodes[node].Values[field] = checked(nodes[node].Values[field] + value);
    }

    internal StorageHierarchyNode Finish(CancellationToken token)
    {
        charge(checked(nodes.Count * 1024L));
        var result = new StorageHierarchyNode[nodes.Count];
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            token.ThrowIfCancellationRequested();
            Node node = nodes[i];
            node.Values[4] = node.Values[3];
            for (int child = node.FirstChild; child >= 0; child = nodes[child].NextSibling)
            {
                for (int field = 0; field < 15; field++)
                    if (field != 3) node.Values[field] = checked(node.Values[field] + nodes[child].Values[field]);
            }
            result[i] = new StorageHierarchyNode(node.Path,
                new StorageAggregate(node.Values[0], node.Values[1], node.Values[2], node.Values[3], node.Values[4],
                    node.Values[5], node.Values[6], node.Values[7], node.Values[8], node.Values[9], node.Values[10],
                    node.Values[11], node.Values[12], node.Values[13], node.Values[14]), Children(node, result));
        }
        return result[0];
    }

    private IEnumerable<StorageHierarchyNode> Children(Node node, StorageHierarchyNode[] result)
    {
        for (int child = node.FirstChild; child >= 0; child = nodes[child].NextSibling)
            yield return result[child];
    }
}
