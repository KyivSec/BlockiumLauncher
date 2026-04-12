using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class GetInstanceModpackMetadataRequest
{
    public InstanceId InstanceId { get; init; }
}

public sealed class SaveInstanceModpackMetadataRequest
{
    public InstanceId InstanceId { get; init; }
    public InstanceModpackMetadata Metadata { get; init; } = default!;
}

public sealed class DeleteInstanceModpackMetadataRequest
{
    public InstanceId InstanceId { get; init; }
}

public sealed class GetInstanceModpackMetadataUseCase
{
    private readonly IInstanceRepository instanceRepository;
    private readonly IInstanceModpackMetadataRepository modpackMetadataRepository;

    public GetInstanceModpackMetadataUseCase(
        IInstanceRepository instanceRepository,
        IInstanceModpackMetadataRepository modpackMetadataRepository)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.modpackMetadataRepository = modpackMetadataRepository ?? throw new ArgumentNullException(nameof(modpackMetadataRepository));
    }

    public async Task<Result<InstanceModpackMetadata?>> ExecuteAsync(
        GetInstanceModpackMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.InstanceId == default)
        {
            return Result<InstanceModpackMetadata?>.Failure(InstanceContentErrors.InvalidRequest);
        }

        var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Result<InstanceModpackMetadata?>.Failure(InstanceContentErrors.InstanceNotFound);
        }

        var metadata = await modpackMetadataRepository.LoadAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
        return Result<InstanceModpackMetadata?>.Success(metadata);
    }
}

public sealed class SaveInstanceModpackMetadataUseCase
{
    private readonly IInstanceRepository instanceRepository;
    private readonly IInstanceModpackMetadataRepository modpackMetadataRepository;

    public SaveInstanceModpackMetadataUseCase(
        IInstanceRepository instanceRepository,
        IInstanceModpackMetadataRepository modpackMetadataRepository)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.modpackMetadataRepository = modpackMetadataRepository ?? throw new ArgumentNullException(nameof(modpackMetadataRepository));
    }

    public async Task<Result> ExecuteAsync(
        SaveInstanceModpackMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.InstanceId == default || request.Metadata is null)
        {
            return Result.Failure(InstanceContentErrors.InvalidRequest);
        }

        var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Result.Failure(InstanceContentErrors.InstanceNotFound);
        }

        await modpackMetadataRepository.SaveAsync(instance.InstallLocation, request.Metadata, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}

public sealed class DeleteInstanceModpackMetadataUseCase
{
    private readonly IInstanceRepository instanceRepository;
    private readonly IInstanceModpackMetadataRepository modpackMetadataRepository;

    public DeleteInstanceModpackMetadataUseCase(
        IInstanceRepository instanceRepository,
        IInstanceModpackMetadataRepository modpackMetadataRepository)
    {
        this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        this.modpackMetadataRepository = modpackMetadataRepository ?? throw new ArgumentNullException(nameof(modpackMetadataRepository));
    }

    public async Task<Result> ExecuteAsync(
        DeleteInstanceModpackMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || request.InstanceId == default)
        {
            return Result.Failure(InstanceContentErrors.InvalidRequest);
        }

        var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
        if (instance is null)
        {
            return Result.Failure(InstanceContentErrors.InstanceNotFound);
        }

        await modpackMetadataRepository.DeleteAsync(instance.InstallLocation, cancellationToken).ConfigureAwait(false);
        return Result.Success();
    }
}
