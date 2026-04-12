using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances
{
    public sealed class CreateInstanceRequest
    {
        public string Name { get; }
        public VersionId GameVersion { get; }
        public LoaderType LoaderType { get; }
        public string? LoaderVersion { get; }
        public string InstallLocation { get; }
        public LaunchProfile LaunchProfile { get; }
        public string? IconKey { get; }

        public CreateInstanceRequest(
            string Name,
            VersionId GameVersion,
            LoaderType LoaderType,
            string? LoaderVersion,
            string InstallLocation,
            LaunchProfile? LaunchProfile = null,
            string? IconKey = null)
        {
            this.Name = NormalizeRequired(Name, nameof(Name));
            this.GameVersion = GameVersion;
            this.LoaderType = LoaderType;
            this.LoaderVersion = NormalizeOptional(LoaderVersion);
            this.InstallLocation = NormalizeRequired(InstallLocation, nameof(InstallLocation));
            this.LaunchProfile = LaunchProfile ?? LaunchProfile.CreateDefault();
            this.IconKey = NormalizeOptional(IconKey);

            ValidateLoader();
        }

        private void ValidateLoader()
        {
            if (LoaderType == LoaderType.Vanilla && LoaderVersion is not null)
            {
                throw new ArgumentException("Vanilla instances must not specify a loader version.", nameof(LoaderVersion));
            }

            if (LoaderType != LoaderType.Vanilla && LoaderVersion is null)
            {
                throw new ArgumentException("Non-vanilla instances must specify a loader version.", nameof(LoaderVersion));
            }
        }

        private static string NormalizeRequired(string Value, string ParamName)
        {
            if (string.IsNullOrWhiteSpace(Value))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", ParamName);
            }

            return Value.Trim();
        }

        private static string? NormalizeOptional(string? Value)
        {
            return string.IsNullOrWhiteSpace(Value) ? null : Value.Trim();
        }
    }

    public sealed class DeleteInstanceRequest
    {
        public InstanceId InstanceId { get; }

        public DeleteInstanceRequest(InstanceId InstanceId)
        {
            this.InstanceId = InstanceId;
        }
    }

    public sealed class GetInstanceDetailsRequest
    {
        public InstanceId InstanceId { get; }

        public GetInstanceDetailsRequest(InstanceId InstanceId)
        {
            this.InstanceId = InstanceId;
        }
    }

    public static class InstanceContentErrors
    {
        public static readonly Error InvalidRequest = new("Instances.InvalidRequest", "The instance content request is invalid.");
        public static readonly Error InstanceNotFound = new("Instances.InstanceNotFound", "The requested instance was not found.");
        public static readonly Error ContentNotFound = new("Instances.ContentNotFound", "The requested content file was not found.");
    }

    public sealed class ListInstanceContentRequest
    {
        public InstanceId InstanceId { get; init; }
    }

    public sealed class ListInstancesRequest
    {
        public bool IncludeDeleted { get; }

        public ListInstancesRequest(bool IncludeDeleted = false)
        {
            this.IncludeDeleted = IncludeDeleted;
        }
    }

    public sealed class RepairInstanceRequest
    {
        public InstanceId InstanceId { get; }
        public bool ForceFullRepair { get; }

        public RepairInstanceRequest(InstanceId InstanceId, bool ForceFullRepair = false)
        {
            this.InstanceId = InstanceId;
            this.ForceFullRepair = ForceFullRepair;
        }
    }

    public sealed class RescanInstanceContentRequest
    {
        public InstanceId InstanceId { get; init; }
    }

    public sealed class SetModEnabledRequest
    {
        public InstanceId InstanceId { get; init; }
        public string ModReference { get; init; } = string.Empty;
        public bool Enabled { get; init; }
    }

    public sealed class SetInstanceContentEnabledRequest
    {
        public InstanceId InstanceId { get; init; }
        public InstanceContentCategory Category { get; init; }
        public string ContentReference { get; init; } = string.Empty;
        public bool Enabled { get; init; }
    }

    public sealed class DeleteInstanceContentRequest
    {
        public InstanceId InstanceId { get; init; }
        public InstanceContentCategory Category { get; init; }
        public string ContentReference { get; init; } = string.Empty;
    }
}

namespace BlockiumLauncher.Application.UseCases.Instances
{
    public sealed class ListInstanceBrowserSummariesUseCase
    {
        private readonly IInstanceRepository instanceRepository;
        private readonly IInstanceContentMetadataService instanceContentMetadataService;

        public ListInstanceBrowserSummariesUseCase(
            IInstanceRepository instanceRepository,
            IInstanceContentMetadataService instanceContentMetadataService)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
            this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        }

        public async Task<Result<IReadOnlyList<InstanceBrowserSummary>>> ExecuteAsync(
            ListInstancesRequest? request,
            CancellationToken cancellationToken = default)
        {
            request ??= new ListInstancesRequest();

            var instances = await instanceRepository.ListAsync(cancellationToken).ConfigureAwait(false);
            var visibleInstances = request.IncludeDeleted
                ? instances
                : instances.Where(static instance => instance.State != InstanceState.Deleted).ToArray();

            var summaries = new List<InstanceBrowserSummary>(visibleInstances.Count);
            foreach (var instance in visibleInstances)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var metadata = await instanceContentMetadataService
                    .GetAsync(instance, reindexIfMissing: true, cancellationToken)
                    .ConfigureAwait(false);

                summaries.Add(new InstanceBrowserSummary(
                    instance.InstanceId,
                    instance.Name,
                    instance.GameVersion.ToString(),
                    instance.LoaderType,
                    instance.LoaderVersion?.ToString(),
                    instance.State,
                    instance.CreatedAtUtc,
                    instance.LastPlayedAtUtc ?? metadata?.LastLaunchAtUtc,
                    metadata?.TotalPlaytimeSeconds ?? 0,
                    instance.InstallLocation,
                    ResolveIconPath(instance, metadata)));
            }

            return Result<IReadOnlyList<InstanceBrowserSummary>>.Success(summaries);
        }

        private static string? ResolveIconPath(LauncherInstance instance, InstanceContentMetadata? metadata)
        {
            if (!string.IsNullOrWhiteSpace(metadata?.IconPath) &&
                File.Exists(metadata.IconPath))
            {
                return metadata.IconPath;
            }

            return !string.IsNullOrWhiteSpace(instance.IconKey) && File.Exists(instance.IconKey)
                ? instance.IconKey
                : null;
        }
    }

    public sealed class ListInstanceContentUseCase
    {
        private readonly IInstanceRepository instanceRepository;
        private readonly IInstanceContentMetadataService instanceContentMetadataService;

        public ListInstanceContentUseCase(
            IInstanceRepository instanceRepository,
            IInstanceContentMetadataService instanceContentMetadataService)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
            this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        }

        public async Task<Result<InstanceContentMetadata>> ExecuteAsync(
            ListInstanceContentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null || request.InstanceId == default)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InvalidRequest);
            }

            var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
            if (instance is null)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InstanceNotFound);
            }

            var metadata = await instanceContentMetadataService
                .GetAsync(instance, reindexIfMissing: true, cancellationToken)
                .ConfigureAwait(false);

            return Result<InstanceContentMetadata>.Success(metadata ?? new InstanceContentMetadata());
        }
    }

    public sealed class RescanInstanceContentUseCase
    {
        private readonly IInstanceRepository instanceRepository;
        private readonly IInstanceContentMetadataService instanceContentMetadataService;

        public RescanInstanceContentUseCase(
            IInstanceRepository instanceRepository,
            IInstanceContentMetadataService instanceContentMetadataService)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
            this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        }

        public async Task<Result<InstanceContentMetadata>> ExecuteAsync(
            RescanInstanceContentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null || request.InstanceId == default)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InvalidRequest);
            }

            var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
            if (instance is null)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InstanceNotFound);
            }

            var metadata = await instanceContentMetadataService.ReindexAsync(instance, cancellationToken).ConfigureAwait(false);
            return Result<InstanceContentMetadata>.Success(metadata);
        }
    }

    public sealed class SetModEnabledUseCase
    {
        private readonly IInstanceRepository instanceRepository;
        private readonly IInstanceContentMetadataService instanceContentMetadataService;

        public SetModEnabledUseCase(
            IInstanceRepository instanceRepository,
            IInstanceContentMetadataService instanceContentMetadataService)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
            this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        }

        public async Task<Result<InstanceContentMetadata>> ExecuteAsync(
            SetModEnabledRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null ||
                request.InstanceId == default ||
                string.IsNullOrWhiteSpace(request.ModReference))
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InvalidRequest);
            }

            var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
            if (instance is null)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InstanceNotFound);
            }

            try
            {
                var metadata = await instanceContentMetadataService
                    .SetModEnabledAsync(instance, request.ModReference, request.Enabled, cancellationToken)
                    .ConfigureAwait(false);

                return Result<InstanceContentMetadata>.Success(metadata);
            }
            catch (FileNotFoundException)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.ContentNotFound);
            }
        }
    }

    public sealed class SetInstanceContentEnabledUseCase
    {
        private readonly IInstanceRepository instanceRepository;
        private readonly IInstanceContentMetadataService instanceContentMetadataService;

        public SetInstanceContentEnabledUseCase(
            IInstanceRepository instanceRepository,
            IInstanceContentMetadataService instanceContentMetadataService)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
            this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        }

        public async Task<Result<InstanceContentMetadata>> ExecuteAsync(
            SetInstanceContentEnabledRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null ||
                request.InstanceId == default ||
                string.IsNullOrWhiteSpace(request.ContentReference))
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InvalidRequest);
            }

            var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
            if (instance is null)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InstanceNotFound);
            }

            try
            {
                var metadata = await instanceContentMetadataService
                    .SetContentEnabledAsync(instance, request.Category, request.ContentReference, request.Enabled, cancellationToken)
                    .ConfigureAwait(false);

                return Result<InstanceContentMetadata>.Success(metadata);
            }
            catch (FileNotFoundException)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.ContentNotFound);
            }
        }
    }

    public sealed class DeleteInstanceContentUseCase
    {
        private readonly IInstanceRepository instanceRepository;
        private readonly IInstanceContentMetadataService instanceContentMetadataService;

        public DeleteInstanceContentUseCase(
            IInstanceRepository instanceRepository,
            IInstanceContentMetadataService instanceContentMetadataService)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
            this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        }

        public async Task<Result<InstanceContentMetadata>> ExecuteAsync(
            DeleteInstanceContentRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request is null ||
                request.InstanceId == default ||
                string.IsNullOrWhiteSpace(request.ContentReference))
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InvalidRequest);
            }

            var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
            if (instance is null)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.InstanceNotFound);
            }

            try
            {
                var metadata = await instanceContentMetadataService
                    .DeleteContentAsync(instance, request.Category, request.ContentReference, cancellationToken)
                    .ConfigureAwait(false);

                return Result<InstanceContentMetadata>.Success(metadata);
            }
            catch (FileNotFoundException)
            {
                return Result<InstanceContentMetadata>.Failure(InstanceContentErrors.ContentNotFound);
            }
        }
    }
}
