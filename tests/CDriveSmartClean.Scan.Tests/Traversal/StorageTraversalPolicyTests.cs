using CDriveSmartClean.Application.Scanning.Observations;
using CDriveSmartClean.Domain.Storage;
using CDriveSmartClean.Scan.Traversal;
using Xunit;

namespace CDriveSmartClean.Scan.Tests.Traversal;

public sealed class StorageTraversalPolicyTests
{
    private readonly StorageTraversalPolicy policy = new();

    [Fact]
    public void NullObservationRejected()
    {
        Assert.Throws<ArgumentNullException>("observation", () => policy.Evaluate(null!));
    }

    [Fact]
    public void OrdinaryDirectoryTraversesChildren()
    {
        StorageObservation observation = CreateObservation(StorageObjectKind.Directory);

        Assert.Equal(TraversalDecision.TraverseChildren, policy.Evaluate(observation));
    }

    [Fact]
    public void OrdinaryFileIsObserveOnly()
    {
        StorageObservation observation = CreateObservation(StorageObjectKind.File);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void OtherObjectIsObserveOnly()
    {
        StorageObservation observation = CreateObservation(StorageObjectKind.Other);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void SymbolicLinkDirectoryIsObserveOnly()
    {
        StorageObservation observation = CreateObservation(StorageObjectKind.Directory, ReparseKind.SymbolicLink);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void JunctionDirectoryIsObserveOnly()
    {
        StorageObservation observation = CreateObservation(StorageObjectKind.Directory, ReparseKind.Junction);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void MountPointDirectoryIsObserveOnly()
    {
        StorageObservation observation = CreateObservation(StorageObjectKind.Directory, ReparseKind.MountPoint);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void OtherReparseDirectoryIsObserveOnly()
    {
        StorageObservation observation = CreateObservation(StorageObjectKind.Directory, ReparseKind.Other);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void UnknownReparseTargetIsObserveOnly()
    {
        StorageObservation observation = CreateObservation(
            StorageObjectKind.Directory,
            ReparseKind.Junction,
            reparseTargetVolumeIdentity: null);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void SameVolumeJunctionIsObserveOnly()
    {
        var sourceVolume = new VolumeIdentity(Guid.NewGuid());
        var equalTargetVolume = new VolumeIdentity(sourceVolume.Id);
        StorageObservation observation = CreateObservation(
            StorageObjectKind.Directory,
            ReparseKind.Junction,
            sourceVolume,
            equalTargetVolume);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void CrossVolumeMountPointIsObserveOnly()
    {
        var sourceVolume = new VolumeIdentity(Guid.NewGuid());
        var targetVolume = new VolumeIdentity(Guid.NewGuid());
        StorageObservation observation = CreateObservation(
            StorageObjectKind.Directory,
            ReparseKind.MountPoint,
            sourceVolume,
            targetVolume);

        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(observation));
    }

    [Fact]
    public void DefaultTraversalDecisionIsUndefinedAndValuesAreExact()
    {
        Assert.False(Enum.IsDefined(default(TraversalDecision)));
        Assert.Equal(
            [TraversalDecision.TraverseChildren, TraversalDecision.ObserveOnly],
            Enum.GetValues<TraversalDecision>());
        Assert.Equal(1, (int)TraversalDecision.TraverseChildren);
        Assert.Equal(2, (int)TraversalDecision.ObserveOnly);
    }

    [Fact]
    public void AllReparseKindsAreObserveOnlyForDirectories()
    {
        ReparseKind[] reparseKinds =
        [
            ReparseKind.SymbolicLink,
            ReparseKind.Junction,
            ReparseKind.MountPoint,
            ReparseKind.Other,
        ];

        Assert.All(
            reparseKinds,
            reparseKind => Assert.Equal(
                TraversalDecision.ObserveOnly,
                policy.Evaluate(CreateObservation(StorageObjectKind.Directory, reparseKind))));
    }

    [Fact]
    public void TraversalDecisionIsIndependentOfCanonicalPath()
    {
        StorageObservation firstDirectory = CreateObservation(
            StorageObjectKind.Directory,
            canonicalPath: @"C:\Windows\Something");
        StorageObservation secondDirectory = CreateObservation(
            StorageObjectKind.Directory,
            canonicalPath: @"X:\CompletelyDifferent\Name");
        StorageObservation firstReparse = CreateObservation(
            StorageObjectKind.Directory,
            ReparseKind.SymbolicLink,
            canonicalPath: @"C:\Windows\Something");
        StorageObservation secondReparse = CreateObservation(
            StorageObjectKind.Directory,
            ReparseKind.MountPoint,
            canonicalPath: @"X:\CompletelyDifferent\Name");

        Assert.Equal(TraversalDecision.TraverseChildren, policy.Evaluate(firstDirectory));
        Assert.Equal(TraversalDecision.TraverseChildren, policy.Evaluate(secondDirectory));
        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(firstReparse));
        Assert.Equal(TraversalDecision.ObserveOnly, policy.Evaluate(secondReparse));
    }

    private static StorageObservation CreateObservation(
        StorageObjectKind objectKind,
        ReparseKind reparseKind = ReparseKind.None,
        VolumeIdentity? sourceVolumeIdentity = null,
        VolumeIdentity? reparseTargetVolumeIdentity = null,
        string canonicalPath = @"C:\Observed\Entry")
    {
        return new StorageObservation(
            Guid.NewGuid(),
            sourceVolumeIdentity ?? new VolumeIdentity(Guid.NewGuid()),
            objectIdentity: null,
            canonicalPath,
            objectKind,
            logicalBytes: 0,
            allocatedBytes: 0,
            reparseKind,
            reparseTargetVolumeIdentity);
    }
}
