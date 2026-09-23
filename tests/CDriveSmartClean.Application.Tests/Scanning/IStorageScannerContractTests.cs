using System.Reflection;
using CDriveSmartClean.Application.Scanning;
using CDriveSmartClean.Application.Scanning.Observations;
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
            [
                typeof(ScanRequest),
                typeof(IStorageObservationSink),
                typeof(IProgress<ScanProgress>),
                typeof(CancellationToken),
            ],
            parameterTypes);
    }

    [Fact]
    public void ObservationSinkIsRequiredAndProgressIsOptional()
    {
        ParameterInfo[] parameters = Assert.Single(GetPublicInstanceMethods()).GetParameters();
        var nullabilityContext = new NullabilityInfoContext();

        Assert.Equal(NullabilityState.NotNull, nullabilityContext.Create(parameters[1]).ReadState);
        Assert.Equal(NullabilityState.Nullable, nullabilityContext.Create(parameters[2]).ReadState);
        Assert.False(parameters[1].HasDefaultValue);
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

    [Fact]
    public void ScanAsyncExposesNoObservationInventoryCollection()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());
        Type[] exposedTypes = method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType)
            .ToArray();

        Assert.DoesNotContain(exposedTypes, IsObservationInventoryType);
        Assert.DoesNotContain(
            exposedTypes,
            type => type.FullName?.StartsWith("System.Threading.Channels.", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void ScannerAndSinkApisAreAwaitableAndNotCallbacks()
    {
        MethodInfo scanMethod = Assert.Single(GetPublicInstanceMethods());
        MethodInfo sinkMethod = Assert.Single(
            typeof(IStorageObservationSink).GetMethods(BindingFlags.Public | BindingFlags.Instance));

        Assert.Equal(typeof(Task<ScanResult>), scanMethod.ReturnType);
        Assert.Equal(typeof(ValueTask), sinkMethod.ReturnType);
        Assert.DoesNotContain(
            scanMethod.GetParameters(),
            parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
        Assert.DoesNotContain(
            sinkMethod.GetParameters(),
            parameter => typeof(Delegate).IsAssignableFrom(parameter.ParameterType));
    }

    [Fact]
    public void ScanResultRemainsSummaryOnly()
    {
        PropertyInfo[] properties = typeof(ScanResult).GetProperties();

        Assert.Equal(
            ["Completion", "Mode", "ScanSessionId", "VolumeAccounting"],
            properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.DoesNotContain(properties, property => IsObservationInventoryType(property.PropertyType));
    }

    private static bool IsObservationInventoryType(Type type)
    {
        if (type == typeof(StorageObservation))
        {
            return true;
        }

        if (type.IsArray)
        {
            return type.GetElementType() == typeof(StorageObservation);
        }

        return type.IsGenericType &&
            type.GetGenericArguments().Contains(typeof(StorageObservation));
    }

    private static MethodInfo[] GetPublicInstanceMethods()
    {
        return typeof(IStorageScanner).GetMethods(BindingFlags.Public | BindingFlags.Instance);
    }
}
