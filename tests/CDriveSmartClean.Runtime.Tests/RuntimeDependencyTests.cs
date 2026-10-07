using System.Xml.Linq;
using CDriveSmartClean.Runtime;
using Xunit;

namespace CDriveSmartClean.Runtime.Tests;

public sealed class RuntimeDependencyTests
{
    [Fact]
    public void ProductionDependencyGraphRemainsOutwardOnly()
    {
        string root = FindRepositoryRoot();
        AssertReferences(root, "src/CDriveSmartClean.Domain/CDriveSmartClean.Domain.csproj", []);
        AssertReferences(root, "src/CDriveSmartClean.Application/CDriveSmartClean.Application.csproj",
            ["CDriveSmartClean.Domain"]);
        AssertReferences(root, "src/CDriveSmartClean.Scan/CDriveSmartClean.Scan.csproj",
            ["CDriveSmartClean.Application"]);
        AssertReferences(root, "src/CDriveSmartClean.Analysis/CDriveSmartClean.Analysis.csproj",
            ["CDriveSmartClean.Application", "CDriveSmartClean.Domain"]);
        AssertReferences(root, "src/CDriveSmartClean.Platform.Windows/CDriveSmartClean.Platform.Windows.csproj",
            ["CDriveSmartClean.Application"]);
        AssertReferences(root, "src/CDriveSmartClean.Runtime/CDriveSmartClean.Runtime.csproj",
            ["CDriveSmartClean.Analysis", "CDriveSmartClean.Application", "CDriveSmartClean.Platform.Windows",
                "CDriveSmartClean.Scan"]);
    }

    [Fact]
    public void RuntimeProjectHasRequiredWindowsContractAndNoProductionPackages()
    {
        string root = FindRepositoryRoot();
        XDocument project = XDocument.Load(Path.Combine(root,
            "src/CDriveSmartClean.Runtime/CDriveSmartClean.Runtime.csproj"));
        Assert.Equal("net10.0-windows", Value(project, "TargetFramework"));
        Assert.Equal("x64", Value(project, "PlatformTarget"));
        Assert.Empty(project.Descendants("PackageReference"));

        XDocument tests = XDocument.Load(Path.Combine(root,
            "tests/CDriveSmartClean.Runtime.Tests/CDriveSmartClean.Runtime.Tests.csproj"));
        Assert.Equal("net10.0-windows", Value(tests, "TargetFramework"));
        Assert.Equal("x64", Value(tests, "PlatformTarget"));
        AssertReferences(root, "tests/CDriveSmartClean.Runtime.Tests/CDriveSmartClean.Runtime.Tests.csproj",
            ["CDriveSmartClean.Runtime"]);
    }

    [Fact]
    public void RuntimeIsReadOnlyAndInfrastructureIndependent()
    {
        string runtime = Path.Combine(FindRepositoryRoot(), "src/CDriveSmartClean.Runtime");
        string source = string.Join('\n', Directory.EnumerateFiles(runtime, "*.cs")
            .Select(File.ReadAllText));
        string[] forbidden =
        [
            "File.Delete", "File.Move", "File.Write", "Directory.Delete", "Registry", "Process",
            "PowerShell", "cmd.exe", "FileStream", "Directory.Enumerate", "DllImport", "LibraryImport",
            "cdrivesmartc.tech", "HOMESERVER", "WEB01", "Docker", "Caddy", "Tailscale", "Yandex Cloud",
            "REG.RU", "GitHub Releases",
        ];
        Assert.All(forbidden, value => Assert.DoesNotContain(value, source, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(5, typeof(SystemVolumeScanWorkflow).Assembly.GetExportedTypes()
            .Count(type => type.Namespace == "CDriveSmartClean.Runtime"));
    }

    private static void AssertReferences(string root, string relativeProject, string[] expected)
    {
        XDocument project = XDocument.Load(Path.Combine(root, relativeProject.Replace('/', Path.DirectorySeparatorChar)));
        string[] actual = project.Descendants("ProjectReference")
            .Select(element => Path.GetFileNameWithoutExtension(element.Attribute("Include")!.Value))
            .Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
    }

    private static string Value(XDocument project, string name) =>
        project.Descendants(name).Single().Value;

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null;
             directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CDriveSmartClean.sln"))) return directory.FullName;
        }
        throw new InvalidOperationException("Repository root was not found.");
    }
}
