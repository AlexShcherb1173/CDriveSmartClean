namespace CDriveSmartClean.Application.Analysis;

public interface IUniversalFindingBuilder
{
    UniversalFindingResult Build(
        UniversalFindingRequest request,
        CancellationToken cancellationToken);
}
