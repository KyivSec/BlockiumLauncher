using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances;

public sealed class GetInstanceDetailsUseCase
{
    private readonly IInstanceRepository _instanceRepository;

    public GetInstanceDetailsUseCase(IInstanceRepository instanceRepository)
    {
        _instanceRepository = instanceRepository;
    }

    public async Task<Result<LauncherInstance>> ExecuteAsync(GetInstanceDetailsRequest request)
    {
        var instance = await _instanceRepository.GetByIdAsync(request.InstanceId, CancellationToken.None);
        if (instance is null)
        {
            return Result<LauncherInstance>.Failure(new Error("InstanceNotFound", "The requested instance could not be found."));
        }

        return Result<LauncherInstance>.Success(instance);
    }
}
