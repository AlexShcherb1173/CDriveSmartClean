using System.Reflection;
using System.Text.Json;
using CDriveSmartClean.Application.ResourceLimits;
using Xunit;

namespace CDriveSmartClean.Application.Tests.ResourceLimits;

public sealed class ResourceLimitDiagnosticTests
{
    [Fact]
    public void ContractIsImmutableBoundedAndNonSensitive()
    {
        var diagnostic = new ResourceLimitDiagnostic(ResourceLimitStage.Accounting,
            ResourceLimitDimension.MaximumDistinctPaths, 10, 11);
        PropertyInfo[] properties = typeof(ResourceLimitDiagnostic).GetProperties(
            BindingFlags.Instance | BindingFlags.Public);

        Assert.Equal([
            "ConfiguredLimit",
            "Dimension",
            "ObservedOrAttemptedValue",
            "Stage",
        ], properties.Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, property => Assert.Null(property.SetMethod));
        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(string));
        Assert.DoesNotContain(properties, property =>
            property.PropertyType.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
            property.PropertyType.Name.Contains("Identity", StringComparison.OrdinalIgnoreCase) ||
            property.PropertyType.Name.Contains("Dictionary", StringComparison.OrdinalIgnoreCase));
        string json = JsonSerializer.Serialize(diagnostic);
        Assert.DoesNotContain("path", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identity", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("filename", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ConstructorRejectsUnknownEnumsAndNonPositiveValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimitDiagnostic(
            (ResourceLimitStage)99, ResourceLimitDimension.MaximumDistinctPaths, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimitDiagnostic(
            ResourceLimitStage.Accounting, (ResourceLimitDimension)99, 1, 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimitDiagnostic(
            ResourceLimitStage.Accounting, ResourceLimitDimension.MaximumDistinctPaths, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ResourceLimitDiagnostic(
            ResourceLimitStage.Accounting, ResourceLimitDimension.MaximumDistinctPaths, 1, 0));
    }

    [Theory]
    [InlineData(ResourceLimitStage.Accounting, ResourceLimitDimension.MaximumPathStates)]
    [InlineData(ResourceLimitStage.Analysis, ResourceLimitDimension.MaximumDirectories)]
    [InlineData(ResourceLimitStage.Findings, ResourceLimitDimension.AnalysisStateBudget)]
    public void ConstructorRejectsStageDimensionMismatch(
        ResourceLimitStage stage, ResourceLimitDimension dimension) =>
        Assert.Throws<ArgumentException>(() => new ResourceLimitDiagnostic(stage, dimension, 1, 2));
}
