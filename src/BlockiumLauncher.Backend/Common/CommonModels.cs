using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Common
{
    public sealed record AccountSummary(
        AccountId AccountId,
        string DisplayName,
        AccountProvider Provider,
        AccountState State,
        bool IsDefault,
        DateTimeOffset? LastValidatedAtUtc);

    public enum CatalogContentType
    {
        Mod = 0,
        Modpack = 1,
        ResourcePack = 2,
        Shader = 3
    }

    public sealed class CatalogProjectSummary
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
        public CatalogContentType ContentType { get; init; }
        public string ProjectId { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public long Downloads { get; init; }
        public long Follows { get; init; }
        public DateTimeOffset? PublishedAtUtc { get; init; }
        public DateTimeOffset? UpdatedAtUtc { get; init; }
        public string? IconUrl { get; init; }
        public string? ProjectUrl { get; init; }
        public IReadOnlyList<string> Categories { get; init; } = [];
        public IReadOnlyList<string> GameVersions { get; init; } = [];
        public IReadOnlyList<string> Loaders { get; init; } = [];
    }

    public enum CatalogDescriptionFormat
    {
        PlainText = 0,
        Markdown = 1,
        Html = 2
    }

    public sealed class CatalogProjectDetails
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
        public CatalogContentType ContentType { get; init; }
        public string ProjectId { get; init; } = string.Empty;
        public string Slug { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Summary { get; init; } = string.Empty;
        public string Author { get; init; } = string.Empty;
        public long Downloads { get; init; }
        public long Follows { get; init; }
        public DateTimeOffset? PublishedAtUtc { get; init; }
        public DateTimeOffset? UpdatedAtUtc { get; init; }
        public string? IconUrl { get; init; }
        public string? ProjectUrl { get; init; }
        public CatalogDescriptionFormat DescriptionFormat { get; init; } = CatalogDescriptionFormat.PlainText;
        public string DescriptionContent { get; init; } = string.Empty;
        public IReadOnlyList<string> Categories { get; init; } = [];
        public IReadOnlyList<string> GameVersions { get; init; } = [];
        public IReadOnlyList<string> Loaders { get; init; } = [];
    }

    public sealed class CatalogProviderMetadata
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
        public CatalogContentType ContentType { get; init; }
        public IReadOnlyList<CatalogSearchSort> SortOptions { get; init; } = [];
        public IReadOnlyList<string> Categories { get; init; } = [];
        public IReadOnlyList<string> GameVersions { get; init; } = [];
        public IReadOnlyList<string> Loaders { get; init; } = [];
    }

    public sealed class CatalogFileSummary
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
        public CatalogContentType ContentType { get; init; }
        public string ProjectId { get; init; } = string.Empty;
        public string FileId { get; init; } = string.Empty;
        public string DisplayName { get; init; } = string.Empty;
        public string FileName { get; init; } = string.Empty;
        public string? DownloadUrl { get; init; }
        public string? ProjectUrl { get; init; }
        public string? FilePageUrl { get; init; }
        public string? Sha1 { get; init; }
        public long SizeBytes { get; init; }
        public DateTimeOffset? PublishedAtUtc { get; init; }
        public IReadOnlyList<string> GameVersions { get; init; } = [];
        public IReadOnlyList<string> Loaders { get; init; } = [];
        public bool IsServerPack { get; init; }
        public bool RequiresManualDownload { get; init; }
    }

    public enum CatalogProvider
    {
        Modrinth = 0,
        CurseForge = 1
    }

    public sealed class CatalogSearchQuery
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
        public CatalogContentType ContentType { get; init; }
        public string? Query { get; init; }
        public string? GameVersion { get; init; }
        public IReadOnlyList<string> GameVersions { get; init; } = [];
        public string? Loader { get; init; }
        public IReadOnlyList<string> Loaders { get; init; } = [];
        public IReadOnlyList<string> Categories { get; init; } = [];
        public CatalogSearchSort Sort { get; init; } = CatalogSearchSort.Relevance;
        public int Limit { get; init; } = 20;
        public int Offset { get; init; }
    }

    public sealed class CatalogFileQuery
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
        public CatalogContentType ContentType { get; init; }
        public string ProjectId { get; init; } = string.Empty;
        public string? GameVersion { get; init; }
        public string? Loader { get; init; }
        public int Limit { get; init; } = 20;
        public int Offset { get; init; }
    }

    public sealed class CatalogFileResolutionQuery
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.CurseForge;
        public CatalogContentType ContentType { get; init; }
        public string ProjectId { get; init; } = string.Empty;
        public string? FileId { get; init; }
        public string? GameVersion { get; init; }
        public string? Loader { get; init; }
    }

    public sealed class CatalogProjectDetailsQuery
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
        public CatalogContentType ContentType { get; init; }
        public string ProjectId { get; init; } = string.Empty;
    }

    public sealed class CatalogProviderMetadataQuery
    {
        public CatalogProvider Provider { get; init; } = CatalogProvider.Modrinth;
        public CatalogContentType ContentType { get; init; }
    }

    public enum CatalogSearchSort
    {
        Relevance = 0,
        Downloads = 1,
        Follows = 2,
        Newest = 3,
        Updated = 4
    }

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

    public sealed record InstanceBrowserSummary(
        InstanceId InstanceId,
        string Name,
        string GameVersion,
        LoaderType LoaderType,
        string? LoaderVersion,
        InstanceState State,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset? LastPlayedAtUtc,
        long TotalPlaytimeSeconds,
        string InstallLocation,
        string? IconPath);

    public sealed record JavaInstallationSummary(
        JavaInstallationId JavaInstallationId,
        string ExecutablePath,
        string Version,
        JavaArchitecture Architecture,
        string Vendor,
        bool IsValid);

    public sealed class LaunchPlan
    {
        public string ExecutablePath { get; }
        public string WorkingDirectory { get; }
        public IReadOnlyList<string> Arguments { get; }
        public IReadOnlyDictionary<string, string> EnvironmentVariables { get; }

        public LaunchPlan(
            string ExecutablePath,
            string WorkingDirectory,
            IReadOnlyList<string> Arguments,
            IReadOnlyDictionary<string, string> EnvironmentVariables)
        {
            this.ExecutablePath = NormalizeRequired(ExecutablePath, nameof(ExecutablePath));
            this.WorkingDirectory = NormalizeRequired(WorkingDirectory, nameof(WorkingDirectory));
            this.Arguments = Arguments ?? throw new ArgumentNullException(nameof(Arguments));
            this.EnvironmentVariables = EnvironmentVariables ?? throw new ArgumentNullException(nameof(EnvironmentVariables));
        }

        private static string NormalizeRequired(string Value, string ParamName)
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", ParamName);
            }

            return Value.Trim();
        }
    }

    public sealed record LoaderVersionSummary(
        LoaderType LoaderType,
        VersionId GameVersion,
        string LoaderVersion,
        bool IsStable);

    public sealed record ProcessLaunchResult(
        int ProcessId,
        DateTimeOffset StartedAtUtc);

    public sealed record VersionSummary(
        VersionId VersionId,
        string DisplayName,
        bool IsRelease,
        DateTimeOffset ReleasedAtUtc);
}
