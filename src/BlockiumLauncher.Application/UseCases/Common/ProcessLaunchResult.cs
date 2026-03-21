namespace BlockiumLauncher.Application.UseCases.Common;

public sealed record ProcessLaunchResult(
    int ProcessId,
    DateTimeOffset StartedAtUtc);
