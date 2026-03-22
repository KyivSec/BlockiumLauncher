using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class VerifyInstanceFilesUseCase
{
    private readonly IInstanceRepository InstanceRepository;

    public VerifyInstanceFilesUseCase(IInstanceRepository InstanceRepository)
    {
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
    }

    public async Task<Result<FileVerificationResult>> ExecuteAsync(
        VerifyInstanceFilesRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null)
        {
            return Result<FileVerificationResult>.Failure(InstallErrors.InvalidRequest);
        }

        var Instance = await InstanceRepository.GetByIdAsync(Request.InstanceId, CancellationToken).ConfigureAwait(false);
        if (Instance is null)
        {
            return Result<FileVerificationResult>.Failure(InstallErrors.InstanceNotFound);
        }

        var Issues = new List<FileVerificationIssue>();
        var RootPath = Path.GetFullPath(Instance.InstallLocation);

        if (!Directory.Exists(RootPath))
        {
            Issues.Add(new FileVerificationIssue
            {
                Kind = FileVerificationIssueKind.RootDirectoryMissing,
                Path = RootPath,
                Message = "The instance root directory does not exist."
            });

            return Result<FileVerificationResult>.Success(new FileVerificationResult
            {
                Instance = Instance,
                IsValid = false,
                Issues = Issues
            });
        }

        var MinecraftPath = Path.Combine(RootPath, ".minecraft");
        if (!Directory.Exists(MinecraftPath))
        {
            Issues.Add(new FileVerificationIssue
            {
                Kind = FileVerificationIssueKind.MinecraftDirectoryMissing,
                Path = MinecraftPath,
                Message = "The instance is missing the .minecraft directory."
            });
        }

        var BlockiumPath = Path.Combine(RootPath, ".blockium");
        if (!Directory.Exists(BlockiumPath))
        {
            Issues.Add(new FileVerificationIssue
            {
                Kind = FileVerificationIssueKind.BlockiumDirectoryMissing,
                Path = BlockiumPath,
                Message = "The instance is missing the .blockium directory."
            });
        }

        return Result<FileVerificationResult>.Success(new FileVerificationResult
        {
            Instance = Instance,
            IsValid = Issues.Count == 0,
            Issues = Issues
        });
    }
}