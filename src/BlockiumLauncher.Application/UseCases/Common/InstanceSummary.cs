using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Common;

public sealed record InstanceSummary(
    InstanceId InstanceId,
    string Name,
    VersionId GameVersion,
    LoaderType LoaderType,
    string? LoaderVersion,
    InstanceState State,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastPlayedAtUtc,
    string InstallLocation,
    string? IconKey);
