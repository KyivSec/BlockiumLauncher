using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances;

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
