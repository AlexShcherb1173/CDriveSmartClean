namespace CDriveSmartClean.Runtime;

public enum ProductScanPhase
{
    DiscoveringSystemVolume = 0,
    ResolvingClassificationContext = 1,
    TraversingAndAccounting = 2,
    CompletingAnalysis = 3,
    BuildingFindings = 4,
    Completed = 5,
}
