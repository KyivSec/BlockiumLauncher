namespace BlockiumLauncher.Contracts.Operations;

public sealed record OperationProgressDto(
    double? Percentage,
    long? Current,
    long? Total,
    string? Unit);
