namespace BlockiumLauncher.Application.UseCases.Install;

public enum UpdatePlanStepKind
{
    VerifyInstance = 1,
    RepairStructure = 2,
    UpdateManagedContent = 3,
    PersistMetadata = 4,
    NoOp = 5
}