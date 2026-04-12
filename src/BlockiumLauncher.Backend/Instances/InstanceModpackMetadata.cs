using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Application.Abstractions.Instances;

public sealed class InstanceModpackMetadata
{
    public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
    public string ProjectId { get; init; } = string.Empty;
    public string FileId { get; init; } = string.Empty;
    public string PackName { get; init; } = string.Empty;
    public string PackVersionLabel { get; init; } = string.Empty;
    public string? ProjectUrl { get; init; }
    public string MinecraftVersion { get; init; } = string.Empty;
    public LoaderType LoaderType { get; init; }
    public string? LoaderVersion { get; init; }
    public DateTimeOffset InstalledAtUtc { get; init; }
}
