using System.Reflection;
using CDriveSmartClean.Application.Scanning;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning;

public sealed class IStorageScannerContractTests
{
    [Fact]
    public void StorageScannerIsAnInterface()
    {
        Assert.True(typeof(IStorageScanner).IsInterface);
    }

    [Fact]
    public void ExactlyOnePublicInstanceMethodExists()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());

        Assert.Equal("ScanAsync", method.Name);
    }

    [Fact]
    public void ScanAsyncReturnsTaskOfScanResult()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());

        Assert.Equal(typeof(Task<ScanResult>), method.ReturnType);
    }

    [Fact]
    public void ScanAsyncHasExactParameters()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());
        Type[] parameterTypes = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        Assert.Equal(
            [typeof(ScanRequest), typeof(IProgress<ScanProgress>), typeof(CancellationToken)],
            parameterTypes);
    }

    [Fact]
    public void ScanAsyncHasNoImplementation()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());

        Assert.True(method.IsAbstract);
        Assert.Null(method.GetMethodBody());
    }

    [Fact]
    public void ScanAsyncHasNoPathOrStringParameter()
    {
        ParameterInfo[] parameters = Assert.Single(GetPublicInstanceMethods()).GetParameters();

        Assert.DoesNotContain(parameters, parameter => parameter.ParameterType == typeof(string));
    }

    [Fact]
    public void ScanAsyncExposesNoPlatformOrStreamTypes()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());
        Type[] forbiddenTypes = [typeof(FileInfo), typeof(DirectoryInfo), typeof(DriveInfo), typeof(Stream)];
        Type[] exposedTypes = method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType)
            .ToArray();

        Assert.DoesNotContain(exposedTypes, forbiddenTypes.Contains);
    }

    [Fact]
    public void ScanAsyncExposesNoFindingType()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());
        Type[] exposedTypes = method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType)
            .ToArray();

        Assert.DoesNotContain(exposedTypes, type => type.Name.Contains("Finding", StringComparison.Ordinal));
    }

    private static MethodInfo[] GetPublicInstanceMethods()
    {
        return typeof(IStorageScanner).GetMethods(BindingFlags.Public | BindingFlags.Instance);
    }
}
