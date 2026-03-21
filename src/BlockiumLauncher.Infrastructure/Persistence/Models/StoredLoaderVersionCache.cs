using System;
using BlockiumLauncher.Domain.Enums;

namespace BlockiumLauncher.Infrastructure.Persistence.Models;

public sealed class StoredLoaderVersionCache
{
    public List<StoredLoaderVersionSummary> LoaderVersions { get; set; } = [];
}

public sealed class StoredLoaderVersionSummary
{
    public LoaderType LoaderType { get; set; }
    public string GameVersion { get; set; } = string.Empty;
    public string LoaderVersion { get; set; } = string.Empty;
    public bool IsStable { get; set; }
}