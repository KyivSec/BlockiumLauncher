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
    private string? StagingRootPath;
    private string? StagedContentPath;
    private bool CommitCompleted;

    public string? TargetRootPath { get; private set; }

    public Task<Result<Unit>> BeginAsync(string TargetRootPath, CancellationToken CancellationToken = default)
    {
        try
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(TargetRootPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.TargetPathInvalid));
            }

            this.TargetRootPath = Path.GetFullPath(TargetRootPath);
            StagingRootPath = Path.Combine(
                Path.GetTempPath(),
                "BlockiumLauncher",
                "transactions",
                $"{DateTime.UtcNow:yyyyMMddHHmmss}-{Guid.NewGuid():N}");

            StagedContentPath = Path.Combine(StagingRootPath, "content");
            Directory.CreateDirectory(StagedContentPath);

            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
        catch
        {
            return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
        }
    }

    public Task<Result<Unit>> StageDirectoryAsync(string SourceDirectoryPath, CancellationToken CancellationToken = default)
    {
        try
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(StagedContentPath) || !Directory.Exists(StagedContentPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
            }

            if (string.IsNullOrWhiteSpace(SourceDirectoryPath) || !Directory.Exists(SourceDirectoryPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
            }

            CopyDirectory(SourceDirectoryPath, StagedContentPath, true);
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
            CancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(TargetRootPath) || string.IsNullOrWhiteSpace(StagedContentPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.CommitFailed));
            }

            if (Directory.Exists(TargetRootPath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.InstanceAlreadyExists));
            }

            var ParentDirectory = Path.GetDirectoryName(TargetRootPath);
            if (!string.IsNullOrWhiteSpace(ParentDirectory))
            {
                Directory.CreateDirectory(ParentDirectory);
            }

            Directory.Move(StagedContentPath, TargetRootPath);
            CommitCompleted = true;

            if (!string.IsNullOrWhiteSpace(StagingRootPath) && Directory.Exists(StagingRootPath))
            {
                Directory.Delete(StagingRootPath, true);
            }

            return Task.FromResult(Result<Unit>.Success(Unit.Value));
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
            CancellationToken.ThrowIfCancellationRequested();

            if (!CommitCompleted && !string.IsNullOrWhiteSpace(StagingRootPath) && Directory.Exists(StagingRootPath))
            {
                Directory.Delete(StagingRootPath, true);
            }

            if (!string.IsNullOrWhiteSpace(TargetRootPath) && Directory.Exists(TargetRootPath))
            {
                Directory.Delete(TargetRootPath, true);
            }

            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
        catch
        {
            return Task.FromResult(Result<Unit>.Failure(InstallErrors.RollbackFailed));
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(StagingRootPath) && Directory.Exists(StagingRootPath))
            {
                Directory.Delete(StagingRootPath, true);
            }
        }
        catch
        {
        }

        return ValueTask.CompletedTask;
    }

    private static void CopyDirectory(string SourceDirectory, string DestinationDirectory, bool Overwrite)
    {
        Directory.CreateDirectory(DestinationDirectory);

        foreach (var DirectoryPath in Directory.GetDirectories(SourceDirectory, "*", SearchOption.AllDirectories))
        {
            var RelativePath = Path.GetRelativePath(SourceDirectory, DirectoryPath);
            Directory.CreateDirectory(Path.Combine(DestinationDirectory, RelativePath));
        }

        foreach (var FilePath in Directory.GetFiles(SourceDirectory, "*", SearchOption.AllDirectories))
        {
            var RelativePath = Path.GetRelativePath(SourceDirectory, FilePath);
            var DestinationFilePath = Path.Combine(DestinationDirectory, RelativePath);
            var ParentDirectory = Path.GetDirectoryName(DestinationFilePath);
            if (!string.IsNullOrWhiteSpace(ParentDirectory))
            {
                Directory.CreateDirectory(ParentDirectory);
            }

            File.Copy(FilePath, DestinationFilePath, Overwrite);
        }
    }
}