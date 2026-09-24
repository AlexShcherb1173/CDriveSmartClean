using CDriveSmartClean.Application.Scanning.Enumeration;
using CDriveSmartClean.Application.Scanning.Traversal;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Platform.Windows.Storage;
using CDriveSmartClean.Scan.Traversal;
using Xunit;

namespace CDriveSmartClean.Windows.IntegrationTests.Storage;

public sealed class StorageTreeWalkerIntegrationTests
{
    [Fact]
    public async Task RealCompositionDiscoversNestedFixtureWithoutDuplicatesOrIssues()
    {
        string root = Directory.CreateTempSubdirectory("CDriveSmartClean-F1-10-").FullName;
        string level1 = Path.Combine(root, "level-1");
        string level2 = Path.Combine(level1, "level-2");
        string rootFile = Path.Combine(root, "root-file.bin");
        string level1File = Path.Combine(level1, "level-1-file.bin");
        string level2File = Path.Combine(level2, "level-2-file.bin");
        try
        {
            Directory.CreateDirectory(level2);
            File.WriteAllText(rootFile, "root");
            File.WriteAllText(level1File, "child");
            File.WriteAllText(level2File, "grandchild");
            var volume = new SystemVolumeDescriptor(new VolumeIdentity(Guid.NewGuid()), root);
            var entries = new EntrySink();
            var issues = new IssueSink();
            var walker = new StorageTreeWalker(new WindowsStorageEnumerator(), new StorageTraversalPolicy());
            await walker.WalkAsync(volume, entries, issues, TestContext.Current.CancellationToken);

            string[] expected = [level1, level2, rootFile, level1File, level2File];
            Assert.Equal(expected.Order(StringComparer.OrdinalIgnoreCase), entries.Entries.Select(e => e.CanonicalPath).Order(StringComparer.OrdinalIgnoreCase));
            Assert.Equal(5, entries.Entries.Select(e => e.CanonicalPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(entries.Entries, entry =>
            {
                Assert.Same(volume.VolumeIdentity, entry.VolumeIdentity);
                Assert.True(Path.IsPathFullyQualified(entry.CanonicalPath));
            });
            Assert.Empty(issues.Issues);
        }
        finally
        {
            // Explicit fixture-owned paths only; no recursive deletion.
            File.Delete(rootFile);
            File.Delete(level1File);
            File.Delete(level2File);
            if (Directory.Exists(level2))
            {
                Directory.Delete(level2);
            }

            if (Directory.Exists(level1))
            {
                Directory.Delete(level1);
            }

            Directory.Delete(root);
        }
    }

    private sealed class EntrySink : IStorageEntrySink
    {
        public List<StorageEntry> Entries { get; } = [];

        public ValueTask WriteAsync(StorageEntry entry, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Entries.Add(entry);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class IssueSink : IStorageTraversalIssueSink
    {
        public List<StorageTraversalIssue> Issues { get; } = [];

        public ValueTask WriteAsync(StorageTraversalIssue issue, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Issues.Add(issue);
            return ValueTask.CompletedTask;
        }
    }
}
