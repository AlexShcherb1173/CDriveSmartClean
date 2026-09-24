using System.Reflection;
using CDriveSmartClean.Application.Scanning.Volumes;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Volumes;

public sealed class SystemVolumeContractsTests
{
    [Fact]
    public void SystemVolumeDescriptorValidValuesAccepted()
    {
        var identity = new VolumeIdentity(Guid.NewGuid());
        var descriptor = new SystemVolumeDescriptor(identity, @"Z:\");
        Assert.Same(identity, descriptor.VolumeIdentity);
    }

    [Fact]
    public void NullVolumeIdentityRejected()
    {
        Assert.Throws<ArgumentNullException>("volumeIdentity", () => new SystemVolumeDescriptor(null!, "root"));
    }

    [Fact]
    public void NullRootPathRejected()
    {
        Assert.Throws<ArgumentNullException>("rootPath", () => new SystemVolumeDescriptor(Identity(), null!));
    }

    [Fact]
    public void EmptyRootPathRejected()
    {
        Assert.Throws<ArgumentException>("rootPath", () => new SystemVolumeDescriptor(Identity(), ""));
    }

    [Fact]
    public void WhitespaceRootPathRejected()
    {
        Assert.Throws<ArgumentException>("rootPath", () => new SystemVolumeDescriptor(Identity(), " \t"));
    }

    [Fact]
    public void RootPathRetainedExactly()
    {
        const string root = @" z:\MixedCase\..\mount\ ";
        Assert.Equal(root, new SystemVolumeDescriptor(Identity(), root).RootPath);
    }

    [Fact]
    public void DescriptorPropertiesAreImmutable()
    {
        var properties = typeof(SystemVolumeDescriptor).GetProperties();
        Assert.Equal(["RootPath", "VolumeIdentity"], properties.Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.All(properties, p => Assert.False(p.CanWrite));
        Assert.True(typeof(SystemVolumeDescriptor).IsSealed);
    }

    [Fact]
    public void SystemVolumeProviderIsInterface()
    {
        Assert.True(typeof(ISystemVolumeProvider).IsInterface);
    }

    [Fact]
    public void SystemVolumeProviderHasExactlyOneMethod()
    {
        Assert.Equal("GetSystemVolume", Method().Name);
    }

    [Fact]
    public void GetSystemVolumeHasNoParameters()
    {
        Assert.Empty(Method().GetParameters());
    }

    [Fact]
    public void GetSystemVolumeReturnsSystemVolumeDescriptor()
    {
        Assert.Equal(typeof(SystemVolumeDescriptor), Method().ReturnType);
    }

    [Fact]
    public void GetSystemVolumeHasNoImplementation()
    {
        Assert.True(Method().IsAbstract);
        Assert.Null(Method().GetMethodBody());
    }

    [Fact]
    public void ContractExposesOnlyPortableTypes()
    {
        Assert.Equal(typeof(SystemVolumeDescriptor), Method().ReturnType);
        Assert.Equal(
            [typeof(VolumeIdentity), typeof(string)],
            Assert.Single(typeof(SystemVolumeDescriptor).GetConstructors()).GetParameters().Select(p => p.ParameterType));
        Assert.Equal(typeof(VolumeIdentity), typeof(SystemVolumeDescriptor).GetProperty("VolumeIdentity")!.PropertyType);
        Assert.Equal(typeof(string), typeof(SystemVolumeDescriptor).GetProperty("RootPath")!.PropertyType);
        Assert.All(
            typeof(ISystemVolumeProvider).GetMethods(),
            method => Assert.False((method.Attributes & MethodAttributes.PinvokeImpl) != 0));
    }

    private static VolumeIdentity Identity() => new(Guid.NewGuid());

    private static MethodInfo Method() =>
        Assert.Single(typeof(ISystemVolumeProvider).GetMethods(BindingFlags.Public | BindingFlags.Instance));
}
