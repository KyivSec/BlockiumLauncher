using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class VerifyInstanceFilesUseCase
{
    private readonly IInstanceRepository InstanceRepository;
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;

    public VerifyInstanceFilesUseCase(IInstanceRepository InstanceRepository)
        : this(
            InstanceRepository,
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance)
    {
    }

    public VerifyInstanceFilesUseCase(
        IInstanceRepository InstanceRepository,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
    }

    public async Task<Result<FileVerificationResult>> ExecuteAsync(
        VerifyInstanceFilesRequest Request,
        CancellationToken CancellationToken = default)
    {
        var Context = OperationContextFactory.Create("VerifyInstanceFiles");

        if (Request is null)
        {
            Logger.Warning(Context, nameof(VerifyInstanceFilesUseCase), "InvalidRequest", "Verify request was null.");
            return Result<FileVerificationResult>.Failure(InstallErrors.InvalidRequest);
        }

        Logger.Info(Context, nameof(VerifyInstanceFilesUseCase), "VerifyStarted", "Instance file verification started.", new
        {
            InstanceId = Request.InstanceId.ToString()
        });

        var Instance = await InstanceRepository.GetByIdAsync(Request.InstanceId, CancellationToken).ConfigureAwait(false);
        if (Instance is null)
        {
            Logger.Warning(Context, nameof(VerifyInstanceFilesUseCase), "InstanceNotFound", "Instance was not found.", new
            {
                InstanceId = Request.InstanceId.ToString()
            });

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

            Logger.Warning(Context, nameof(VerifyInstanceFilesUseCase), "RootDirectoryMissing", "Instance root directory is missing.", new
            {
                RootPath
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

        Logger.Info(Context, nameof(VerifyInstanceFilesUseCase), "VerifyCompleted", "Instance file verification completed.", new
        {
            InstanceId = Instance.InstanceId.ToString(),
            IsValid = Issues.Count == 0,
            IssueCount = Issues.Count
        });

        return Result<FileVerificationResult>.Success(new FileVerificationResult
        {
            Instance = Instance,
            IsValid = Issues.Count == 0,
            Issues = Issues
        });
    }
}