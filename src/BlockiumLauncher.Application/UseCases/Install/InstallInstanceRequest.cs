using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class InstallInstanceRequest
{
    public string InstanceName { get; init; } = string.Empty;
    public string GameVersion { get; init; } = string.Empty;
    public LoaderType LoaderType { get; init; }
    public string? LoaderVersion { get; init; }
    public string? TargetDirectory { get; init; }
    public bool OverwriteIfExists { get; init; }
    public bool DownloadRuntime { get; init; }
}