namespace BlockiumLauncher.Contracts.Operations;

public enum OperationEventKind
{
    Started = 0,
    Progress = 1,
    Message = 2,
    Warning = 3,
    Error = 4,
    Completed = 5
}
