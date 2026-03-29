using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Contracts.Operations;

public sealed record OperationEventDto(
    string OperationId,
    OperationEventKind Kind,
    DateTimeOffset TimestampUtc,
    string Message,
    OperationProgressDto? Progress,
    Error? Error);

public enum OperationEventKind
{
    Started = 0,
    Progress = 1,
    Message = 2,
    Warning = 3,
    Error = 4,
    Completed = 5
}

public sealed record OperationProgressDto(
    double? Percentage,
    long? Current,
    long? Total,
    string? Unit);
