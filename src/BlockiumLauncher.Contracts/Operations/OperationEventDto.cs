using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Contracts.Operations;

public sealed record OperationEventDto(
    string OperationId,
    OperationEventKind Kind,
    DateTimeOffset TimestampUtc,
    string Message,
    OperationProgressDto? Progress,
    Error? Error);
