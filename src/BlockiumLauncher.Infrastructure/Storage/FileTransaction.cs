using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Shared.Primitives;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class FileTransaction : IFileTransaction
{
    private string? StagedDirectoryPath;
    private string? BackupDirectoryPath;
    private bool CommitCompleted;

    public string? TargetRootPath { get; private set; }

    public Task<Result<Unit>> BeginAsync(string TargetRootPath, CancellationToken CancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(TargetRootPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.TargetPathInvalid));
            }

            if (this.TargetRootPath is not null)
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.InvalidRequest));
            }

            this.TargetRootPath = Path.GetFullPath(TargetRootPath);
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
        catch
        {
            return Task.FromResult(Result<Unit>.Failure(InstallErrors.TargetPathInvalid));
        }
    }

    public Task<Result<Unit>> StageDirectoryAsync(string SourceDirectoryPath, CancellationToken CancellationToken = default)
    {
        try
        {
            if (TargetRootPath is null)
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.InvalidRequest));
            }

            if (string.IsNullOrWhiteSpace(SourceDirectoryPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.InvalidRequest));
            }

            var FullSourceDirectoryPath = Path.GetFullPath(SourceDirectoryPath);
            if (!Directory.Exists(FullSourceDirectoryPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
            }

            if (StagedDirectoryPath is not null)
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.InvalidRequest));
            }

            StagedDirectoryPath = FullSourceDirectoryPath;
            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
        catch
        {
            return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
        }
    }

    public Task<Result<Unit>> CommitAsync(CancellationToken CancellationToken = default)
    {
        try
        {
            if (TargetRootPath is null || StagedDirectoryPath is null)
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.InvalidRequest));
            }

            if (CommitCompleted)
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.InvalidRequest));
            }

            var TargetParentDirectoryPath = Path.GetDirectoryName(TargetRootPath);
            if (string.IsNullOrWhiteSpace(TargetParentDirectoryPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.TargetPathInvalid));
            }

            Directory.CreateDirectory(TargetParentDirectoryPath);

            BackupDirectoryPath = TargetRootPath + ".__blockium_backup__";
            DeleteDirectoryIfExists(BackupDirectoryPath);

            var TargetPreviouslyExisted = Directory.Exists(TargetRootPath);

            try
            {
                if (TargetPreviouslyExisted)
                {
                    Directory.Move(TargetRootPath, BackupDirectoryPath);
                }

                Directory.Move(StagedDirectoryPath, TargetRootPath);

                CommitCompleted = true;
                StagedDirectoryPath = null;

                DeleteDirectoryIfExists(BackupDirectoryPath);
                BackupDirectoryPath = null;

                return Task.FromResult(Result<Unit>.Success(Unit.Value));
            }
            catch
            {
                try
                {
                    if (Directory.Exists(TargetRootPath))
                    {
                        Directory.Delete(TargetRootPath, true);
                    }
                }
                catch
                {
                }

                try
                {
                    if (BackupDirectoryPath is not null && Directory.Exists(BackupDirectoryPath) && !Directory.Exists(TargetRootPath))
                    {
                        Directory.Move(BackupDirectoryPath, TargetRootPath);
                    }
                }
                catch
                {
                    return Task.FromResult(Result<Unit>.Failure(InstallErrors.RollbackFailed));
                }

                return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
            }
        }
        catch
        {
            return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
        }
    }

    public Task<Result<Unit>> RollbackAsync(CancellationToken CancellationToken = default)
    {
        try
        {
            if (CommitCompleted)
            {
                return Task.FromResult(Result<Unit>.Success(Unit.Value));
            }

            if (StagedDirectoryPath is not null)
            {
                DeleteDirectoryIfExists(StagedDirectoryPath);
                StagedDirectoryPath = null;
            }

            if (TargetRootPath is not null && BackupDirectoryPath is not null)
            {
                if (!Directory.Exists(TargetRootPath) && Directory.Exists(BackupDirectoryPath))
                {
                    Directory.Move(BackupDirectoryPath, TargetRootPath);
                }
            }

            if (BackupDirectoryPath is not null)
            {
                DeleteDirectoryIfExists(BackupDirectoryPath);
                BackupDirectoryPath = null;
            }

            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
        catch
        {
            return Task.FromResult(Result<Unit>.Failure(InstallErrors.RollbackFailed));
        }
    }

    public async ValueTask DisposeAsync()
    {
        await RollbackAsync().ConfigureAwait(false);
    }

    private static void DeleteDirectoryIfExists(string PathValue)
    {
        if (Directory.Exists(PathValue))
        {
            Directory.Delete(PathValue, true);
        }
    }
}