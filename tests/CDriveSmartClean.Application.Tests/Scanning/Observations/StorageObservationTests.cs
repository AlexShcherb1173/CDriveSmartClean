using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;
using Xunit;

namespace CDriveSmartClean.Application.Tests.Scanning.Observations;

public sealed class StorageObservationTests
{
    [Fact]
    public void ValidObservationAccepted()
    {
        Guid session = Guid.NewGuid();
        var volume = new VolumeIdentity(Guid.NewGuid());
        var identity = new StorageObjectIdentity(volume, Guid.NewGuid());
        var target = new VolumeIdentity(Guid.NewGuid());
        var observation = new StorageObservation(session, volume, identity, "/entry", StorageObjectKind.Directory, 12, 16, ReparseKind.Junction, target);
        Assert.Equal(session, observation.ScanSessionId);
        Assert.Same(volume, observation.VolumeIdentity);
        Assert.Same(identity, observation.ObjectIdentity);
        Assert.Equal("/entry", observation.CanonicalPath);
        Assert.Equal(StorageObjectKind.Directory, observation.ObjectKind);
        Assert.Equal(12, observation.LogicalBytes);
        Assert.Equal(16, observation.AllocatedBytes);
        Assert.Equal(ReparseKind.Junction, observation.ReparseKind);
        Assert.Same(target, observation.ReparseTargetVolumeIdentity);
    }

    [Fact]
    public void EmptyScanSessionRejected()
    {
        Assert.Throws<ArgumentException>("scanSessionId", () => Create(scanSessionId: Guid.Empty));
    }

    [Fact]
    public void NullVolumeRejected()
    {
        Assert.Throws<ArgumentNullException>("volumeIdentity", () => new StorageObservation(Guid.NewGuid(), null!, null, "/entry", StorageObjectKind.File, 0, 0, ReparseKind.None, null));
    }

    [Fact]
    public void BlankCanonicalPathRejected()
    {
        Assert.Throws<ArgumentNullException>("canonicalPath", () => Create(canonicalPath: null!));
        Assert.Throws<ArgumentException>("canonicalPath", () => Create(canonicalPath: ""));
        Assert.Throws<ArgumentException>("canonicalPath", () => Create(canonicalPath: " \t\r\n"));
    }

    [Fact]
    public void DefaultObjectKindRejected()
    {
        Assert.False(Enum.IsDefined(default(StorageObjectKind)));
        Assert.Throws<ArgumentOutOfRangeException>("objectKind", () => Create(objectKind: default));
    }

    [Fact]
    public void UndefinedObjectKindRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>("objectKind", () => Create(objectKind: (StorageObjectKind)99));
    }

    [Fact]
    public void DefaultReparseKindRejected()
    {
        Assert.False(Enum.IsDefined(default(ReparseKind)));
        Assert.Throws<ArgumentOutOfRangeException>("reparseKind", () => Create(reparseKind: default));
    }

    [Fact]
    public void UndefinedReparseKindRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>("reparseKind", () => Create(reparseKind: (ReparseKind)99));
    }

    [Fact]
    public void NegativeLogicalBytesRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>("logicalBytes", () => Create(logicalBytes: -1));
    }

    [Fact]
    public void NegativeAllocatedBytesRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>("allocatedBytes", () => Create(allocatedBytes: -1));
    }

    [Fact]
    public void ZeroSizesAccepted()
    {
        StorageObservation observation = Create();
        Assert.Equal(0, observation.LogicalBytes);
        Assert.Equal(0, observation.AllocatedBytes);
    }

    [Fact]
    public void LogicalMayExceedAllocated()
    {
        StorageObservation observation = Create(logicalBytes: long.MaxValue, allocatedBytes: 1);
        Assert.Equal(long.MaxValue, observation.LogicalBytes);
        Assert.Equal(1, observation.AllocatedBytes);
    }

    [Fact]
    public void AllocatedMayExceedLogical()
    {
        StorageObservation observation = Create(logicalBytes: 1, allocatedBytes: long.MaxValue);
        Assert.Equal(1, observation.LogicalBytes);
        Assert.Equal(long.MaxValue, observation.AllocatedBytes);
    }

    [Fact]
    public void PropertiesAreImmutable()
    {
        Assert.All(typeof(StorageObservation).GetProperties(), property => Assert.False(property.CanWrite));
    }

    [Fact]
    public void CanonicalPathRetainedExactly()
    {
        const string path = "  X:\\MiXeD\\..\\é\\entry  ";
        Assert.Equal(path, Create(canonicalPath: path).CanonicalPath);
    }

    [Fact]
    public void NullObjectIdentityAccepted()
    {
        Assert.Null(Create(objectIdentity: null).ObjectIdentity);
    }

    [Fact]
    public void MatchingObjectVolumeAccepted()
    {
        var volume = new VolumeIdentity(Guid.NewGuid());
        var identity = new StorageObjectIdentity(new VolumeIdentity(volume.Id), Guid.NewGuid());
        Assert.Same(identity, Create(volumeIdentity: volume, objectIdentity: identity).ObjectIdentity);
    }

    [Fact]
    public void ObjectIdentityFromDifferentVolumeRejected()
    {
        var identity = new StorageObjectIdentity(new VolumeIdentity(Guid.NewGuid()), Guid.NewGuid());
        Assert.Throws<ArgumentException>("objectIdentity", () => Create(volumeIdentity: new VolumeIdentity(Guid.NewGuid()), objectIdentity: identity));
    }

    [Fact]
    public void NonReparseHasIsReparsePointFalse()
    {
        Assert.False(Create(reparseKind: ReparseKind.None).IsReparsePoint);
    }

    [Fact]
    public void ReparseHasIsReparsePointTrue()
    {
        foreach (ReparseKind kind in new[] { ReparseKind.SymbolicLink, ReparseKind.Junction, ReparseKind.MountPoint, ReparseKind.Other })
        {
            Assert.True(Create(reparseKind: kind).IsReparsePoint);
        }
    }

    [Fact]
    public void NonReparseRejectsTargetVolume()
    {
        Assert.Throws<ArgumentException>("reparseTargetVolumeIdentity", () => Create(reparseTargetVolumeIdentity: new VolumeIdentity(Guid.NewGuid())));
    }

    [Fact]
    public void SameVolumeReparseTargetAccepted()
    {
        var volume = new VolumeIdentity(Guid.NewGuid());
        var target = new VolumeIdentity(volume.Id);
        StorageObservation observation = Create(volumeIdentity: volume, reparseKind: ReparseKind.Junction, reparseTargetVolumeIdentity: target);
        Assert.Same(target, observation.ReparseTargetVolumeIdentity);
        Assert.Equal(observation.VolumeIdentity, observation.ReparseTargetVolumeIdentity);
    }

    [Fact]
    public void CrossVolumeReparseTargetAccepted()
    {
        var volume = new VolumeIdentity(Guid.NewGuid());
        var target = new VolumeIdentity(Guid.NewGuid());
        StorageObservation observation = Create(volumeIdentity: volume, reparseKind: ReparseKind.MountPoint, reparseTargetVolumeIdentity: target);
        Assert.Same(target, observation.ReparseTargetVolumeIdentity);
        Assert.NotEqual(observation.VolumeIdentity, observation.ReparseTargetVolumeIdentity);
    }

    [Fact]
    public void ReparseWithUnknownTargetAccepted()
    {
        StorageObservation observation = Create(reparseKind: ReparseKind.Junction);
        Assert.True(observation.IsReparsePoint);
        Assert.Null(observation.ReparseTargetVolumeIdentity);
    }

    [Fact]
    public void DifferentPathsCanShareObjectIdentity()
    {
        var volume = new VolumeIdentity(Guid.NewGuid());
        Guid id = Guid.NewGuid();
        StorageObservation first = Create(volumeIdentity: volume, objectIdentity: new StorageObjectIdentity(volume, id), canonicalPath: "/first");
        StorageObservation second = Create(volumeIdentity: volume, objectIdentity: new StorageObjectIdentity(new VolumeIdentity(volume.Id), id), canonicalPath: "/second");
        Assert.NotEqual(first.CanonicalPath, second.CanonicalPath);
        Assert.NotNull(first.ObjectIdentity);
        Assert.True(first.ObjectIdentity.Equals(second.ObjectIdentity));
    }

    [Fact]
    public void NoExclusiveAllocationOrAnalysisClaims()
    {
        Assert.Equal(
            ["AllocatedBytes", "CanonicalPath", "IsReparsePoint", "LogicalBytes", "ObjectIdentity", "ObjectKind", "ReparseKind", "ReparseTargetVolumeIdentity", "ScanSessionId", "VolumeIdentity"],
            typeof(StorageObservation).GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public void EnumsHaveExactlyTheRequiredValues()
    {
        Assert.Equal([1, 2, 3], Enum.GetValues<StorageObjectKind>().Select(value => (int)value));
        Assert.Equal(["File", "Directory", "Other"], Enum.GetNames<StorageObjectKind>());
        Assert.Equal([1, 2, 3, 4, 5], Enum.GetValues<ReparseKind>().Select(value => (int)value));
        Assert.Equal(["None", "SymbolicLink", "Junction", "MountPoint", "Other"], Enum.GetNames<ReparseKind>());
    }

    private static StorageObservation Create(
        Guid? scanSessionId = null,
        VolumeIdentity? volumeIdentity = null,
        StorageObjectIdentity? objectIdentity = null,
        string canonicalPath = "/entry",
        StorageObjectKind objectKind = StorageObjectKind.File,
        long logicalBytes = 0,
        long allocatedBytes = 0,
        ReparseKind reparseKind = ReparseKind.None,
        VolumeIdentity? reparseTargetVolumeIdentity = null)
    {
        return new StorageObservation(
            scanSessionId ?? Guid.NewGuid(),
            volumeIdentity ?? new VolumeIdentity(Guid.NewGuid()),
            objectIdentity,
            canonicalPath,
            objectKind,
            logicalBytes,
            allocatedBytes,
            reparseKind,
            reparseTargetVolumeIdentity);
    }
}
