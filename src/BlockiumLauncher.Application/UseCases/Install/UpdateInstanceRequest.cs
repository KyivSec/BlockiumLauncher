using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class UpdateInstanceRequest
{
    public InstanceId InstanceId { get; init; } = default!;
    public VersionId TargetGameVersion { get; init; }
    public LoaderType TargetLoaderType { get; init; }
    public VersionId? TargetLoaderVersion { get; init; }
}