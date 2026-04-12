using System;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Instances
{
    public sealed class CloneInstanceRequest
    {
        public InstanceId SourceInstanceId { get; }
        public string NewInstanceName { get; }

        public CloneInstanceRequest(InstanceId SourceInstanceId, string NewInstanceName)
        {
            this.SourceInstanceId = SourceInstanceId;
            this.NewInstanceName = string.IsNullOrWhiteSpace(NewInstanceName) ? throw new ArgumentException("Name cannot be empty", nameof(NewInstanceName)) : NewInstanceName.Trim();
        }
    }

    public sealed class CloneInstanceUseCase
    {
        private readonly IInstanceRepository instanceRepository;
        private readonly ILauncherPaths launcherPaths;
        private readonly IInstanceContentMetadataService instanceContentMetadataService;

        public CloneInstanceUseCase(
            IInstanceRepository instanceRepository,
            ILauncherPaths launcherPaths,
            IInstanceContentMetadataService instanceContentMetadataService)
        {
            this.instanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
            this.launcherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
            this.instanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        }

        public async Task<Result<LauncherInstance>> ExecuteAsync(CloneInstanceRequest request, CancellationToken cancellationToken = default)
        {
            if (request is null || request.SourceInstanceId == default)
            {
                return Result<LauncherInstance>.Failure(InstanceContentErrors.InvalidRequest);
            }

            var existingNameCheck = await instanceRepository.GetByNameAsync(request.NewInstanceName, cancellationToken).ConfigureAwait(false);
            if (existingNameCheck is not null)
            {
                return Result<LauncherInstance>.Failure(new Error("Instances.CloneNameConflict", "An instance with the active name already exists."));
            }

            var sourceInstance = await instanceRepository.GetByIdAsync(request.SourceInstanceId, cancellationToken).ConfigureAwait(false);
            if (sourceInstance is null)
            {
                return Result<LauncherInstance>.Failure(InstanceContentErrors.InstanceNotFound);
            }

            var newLocation = System.IO.Path.GetFullPath(launcherPaths.GetDefaultInstanceDirectory(request.NewInstanceName));
            if (System.IO.Directory.Exists(newLocation))
            {
                return Result<LauncherInstance>.Failure(new Error("Instances.ClonePathConflict", "The target installation path already exists on disk."));
            }

            try
            {
                CopyDirectory(sourceInstance.InstallLocation, newLocation, overwrite: true);

                var metadataPath = System.IO.Path.Combine(newLocation, ".blockium");
                if (System.IO.Directory.Exists(metadataPath))
                {
                    System.IO.Directory.Delete(metadataPath, recursive: true);
                }
            }
            catch (Exception ex)
            {
                return Result<LauncherInstance>.Failure(new Error("Instances.CloneCopyFailed", $"Failed to copy instance directory: {ex.Message}"));
            }

            var newInstance = LauncherInstance.Create(
                InstanceId.New(),
                request.NewInstanceName,
                sourceInstance.GameVersion,
                sourceInstance.LoaderType,
                sourceInstance.LoaderVersion,
                newLocation,
                DateTimeOffset.UtcNow,
                sourceInstance.LaunchProfile,
                sourceInstance.IconKey);

            await instanceRepository.SaveAsync(newInstance, cancellationToken).ConfigureAwait(false);
            await instanceContentMetadataService.ReindexAsync(newInstance, cancellationToken).ConfigureAwait(false);

            return Result<LauncherInstance>.Success(newInstance);
        }

        private static void CopyDirectory(string sourceDir, string destinationDir, bool overwrite)
        {
            System.IO.Directory.CreateDirectory(destinationDir);

            foreach (var dirPath in System.IO.Directory.GetDirectories(sourceDir, "*", System.IO.SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(sourceDir, dirPath);
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(destinationDir, relative));
            }

            foreach (var filePath in System.IO.Directory.GetFiles(sourceDir, "*", System.IO.SearchOption.AllDirectories))
            {
                var relative = System.IO.Path.GetRelativePath(sourceDir, filePath);
                var destFile = System.IO.Path.Combine(destinationDir, relative);
                var destParent = System.IO.Path.GetDirectoryName(destFile);
                if (!string.IsNullOrWhiteSpace(destParent))
                {
                    System.IO.Directory.CreateDirectory(destParent);
                }
                System.IO.File.Copy(filePath, destFile, overwrite);
            }
        }
    }
}
