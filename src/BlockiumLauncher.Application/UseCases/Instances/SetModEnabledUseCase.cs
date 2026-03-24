using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances;

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
