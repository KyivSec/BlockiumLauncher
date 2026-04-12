using System;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances
{
    public sealed class DeleteInstanceUseCase
    {
        private readonly IInstanceRepository instanceRepository;

        public DeleteInstanceUseCase(IInstanceRepository instanceRepository)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        }

        public async Task<Result<bool>> ExecuteAsync(DeleteInstanceRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null || request.InstanceId == default)
            {
                return Result<bool>.Failure(InstanceContentErrors.InvalidRequest);
            }

            var instance = await instanceRepository.GetByIdAsync(request.InstanceId, cancellationToken).ConfigureAwait(false);
            if (instance is null)
            {
                // Can't fail if it's already gone
                return Result<bool>.Success(true);
            }

            try
            {
                if (System.IO.Directory.Exists(instance.InstallLocation))
                {
                    System.IO.Directory.Delete(instance.InstallLocation, recursive: true);
                }
            }
            catch (Exception ex)
            {
                return Result<bool>.Failure(new Error("Instances.DeleteFailed", $"Failed to delete directory: {ex.Message}"));
            }

            await instanceRepository.DeleteAsync(instance.InstanceId, cancellationToken).ConfigureAwait(false);
            return Result<bool>.Success(true);
        }
    }
}
