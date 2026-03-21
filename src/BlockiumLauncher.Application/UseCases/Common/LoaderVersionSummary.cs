using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Common;

public sealed record LoaderVersionSummary(
    LoaderType LoaderType,
    VersionId GameVersion,
    string LoaderVersion,
    bool IsStable);
