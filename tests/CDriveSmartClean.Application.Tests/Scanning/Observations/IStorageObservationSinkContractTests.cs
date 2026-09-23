using System.Reflection;
using CDriveSmartClean.Application.Scanning.Observations;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Observations;

public sealed class IStorageObservationSinkContractTests
{
    [Fact]
    public void StorageObservationSinkIsAnInterface()
    {
        Assert.True(typeof(IStorageObservationSink).IsInterface);
    }

    [Fact]
    public void ExactlyOnePublicInstanceMethodExists()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());

        Assert.Equal("WriteAsync", method.Name);
    }

    [Fact]
    public void WriteAsyncReturnsNonGenericValueTask()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());

        Assert.Equal(typeof(ValueTask), method.ReturnType);
        Assert.False(method.ReturnType.IsGenericType);
        Assert.NotEqual(typeof(void), method.ReturnType);
    }

    [Fact]
    public void WriteAsyncHasExactParameters()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());
        Type[] parameterTypes = method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.Equal([typeof(StorageObservation), typeof(CancellationToken)], parameterTypes);
    }

    [Fact]
    public void ObservationParameterIsNotNullable()
    {
        ParameterInfo observationParameter = Assert.Single(GetPublicInstanceMethods()).GetParameters()[0];
        var nullability = new NullabilityInfoContext().Create(observationParameter);

        Assert.Equal(NullabilityState.NotNull, nullability.ReadState);
    }

    [Fact]
    public void WriteAsyncHasNoImplementation()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());

        Assert.True(method.IsAbstract);
        Assert.Null(method.GetMethodBody());
    }

    [Fact]
    public void WriteAsyncExposesNoPlatformTypes()
    {
        string[] forbiddenTypeNames =
        [
            "System.String",
            "System.IO.FileInfo",
            "System.IO.DirectoryInfo",
            "System.IO.DriveInfo",
            "System.IO.Stream",
            "Microsoft.Win32.SafeHandles.SafeFileHandle",
        ];

        Assert.DoesNotContain(
            GetExposedTypes(),
            type => forbiddenTypeNames.Contains(type.FullName, StringComparer.Ordinal));
    }

    [Fact]
    public void WriteAsyncExposesNoAnalysisOrCleanupTypes()
    {
        string[] forbiddenTypeNames =
        [
            "Finding",
            "Evidence",
            "ReclaimEstimate",
            "RiskAssessment",
            "CleanupAction",
            "CleanupPlan",
            "ActionManifest",
        ];

        Assert.DoesNotContain(
            GetExposedTypes(),
            type => forbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal));
    }

    private static MethodInfo[] GetPublicInstanceMethods()
    {
        return typeof(IStorageObservationSink).GetMethods(BindingFlags.Public | BindingFlags.Instance);
    }

    private static Type[] GetExposedTypes()
    {
        MethodInfo method = Assert.Single(GetPublicInstanceMethods());

        return method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType)
            .ToArray();
    }
}
