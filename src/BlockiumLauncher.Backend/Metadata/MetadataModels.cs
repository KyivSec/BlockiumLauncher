using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Application.UseCases.Metadata
{
    public sealed class GetAvailableVersionsRequest
    {
        public bool IncludeSnapshots { get; }
        public LoaderType? LoaderTypeFilter { get; }

        public GetAvailableVersionsRequest(bool IncludeSnapshots = false, LoaderType? LoaderTypeFilter = null)
        {
            this.IncludeSnapshots = IncludeSnapshots;
            this.LoaderTypeFilter = LoaderTypeFilter;
        }
    }

    public sealed class ResolveInstallPlanRequest
    {
        public VersionId GameVersion { get; }
        public LoaderType LoaderType { get; }
        public string? LoaderVersion { get; }

        public ResolveInstallPlanRequest(VersionId GameVersion, LoaderType LoaderType, string? LoaderVersion)
        {
            this.GameVersion = GameVersion;
            this.LoaderType = LoaderType;
            this.LoaderVersion = NormalizeOptional(LoaderVersion);

            ValidateLoader();
        }

        private void ValidateLoader()
        {
            if (LoaderType == LoaderType.Vanilla && LoaderVersion is not null)
            {
                throw new ArgumentException("Vanilla install plans must not specify a loader version.", nameof(LoaderVersion));
            }

            if (LoaderType != LoaderType.Vanilla && LoaderVersion is null)
            {
                throw new ArgumentException("Non-vanilla install plans must specify a loader version.", nameof(LoaderVersion));
            }
        }

        private static string? NormalizeOptional(string? Value)
        {
            return string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();
        }
    }

    public sealed class SyncMetadataRequest
    {
        public bool ForceRefresh { get; }

        public SyncMetadataRequest(bool ForceRefresh = false)
        {
            this.ForceRefresh = ForceRefresh;
        }
    }
}
