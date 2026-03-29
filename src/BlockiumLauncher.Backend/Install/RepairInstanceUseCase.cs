using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Diagnostics;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class RepairInstanceUseCase
{
    private readonly VerifyInstanceFilesUseCase VerifyInstanceFilesUseCase;
    private readonly IInstanceContentMetadataService InstanceContentMetadataService;
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;

    public RepairInstanceUseCase(VerifyInstanceFilesUseCase VerifyInstanceFilesUseCase)
        : this(
            VerifyInstanceFilesUseCase,
            NoOpInstanceContentMetadataService.Instance,
            NullStructuredLogger.Instance,
            DefaultOperationContextFactory.Instance)
    {
    }

    public RepairInstanceUseCase(
        VerifyInstanceFilesUseCase VerifyInstanceFilesUseCase,
        IInstanceContentMetadataService InstanceContentMetadataService,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.VerifyInstanceFilesUseCase = VerifyInstanceFilesUseCase ?? throw new ArgumentNullException(nameof(VerifyInstanceFilesUseCase));
        this.InstanceContentMetadataService = InstanceContentMetadataService ?? throw new ArgumentNullException(nameof(InstanceContentMetadataService));
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
    }

    public async Task<Result<RepairInstanceResult>> ExecuteAsync(
        RepairInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        var Context = OperationContextFactory.Create("RepairInstance");

        if (Request is null)
        {
            Logger.Warning(Context, nameof(RepairInstanceUseCase), "InvalidRequest", "Repair request was null.");
            return Result<RepairInstanceResult>.Failure(InstallErrors.InvalidRequest);
        }

        Logger.Info(Context, nameof(RepairInstanceUseCase), "RepairStarted", "Instance repair started.", new
        {
            InstanceId = Request.InstanceId.ToString()
        });

        var VerificationResult = await VerifyInstanceFilesUseCase.ExecuteAsync(
            new VerifyInstanceFilesRequest
            {
                InstanceId = Request.InstanceId
            },
            CancellationToken).ConfigureAwait(false);

        if (VerificationResult.IsFailure)
        {
            Logger.Warning(Context, nameof(RepairInstanceUseCase), "InitialVerificationFailed", "Initial verification failed.", new
            {
                VerificationResult.Error.Code,
                VerificationResult.Error.Message
            });

            return Result<RepairInstanceResult>.Failure(VerificationResult.Error);
        }

        var Verification = VerificationResult.Value;
        var RepairedPaths = new List<string>();
        var RootPath = Path.GetFullPath(Verification.Instance.InstallLocation);
        var MinecraftPath = Path.Combine(RootPath, ".minecraft");
        var BlockiumPath = Path.Combine(RootPath, ".blockium");

        if (!Directory.Exists(RootPath))
        {
            Directory.CreateDirectory(RootPath);
            RepairedPaths.Add(RootPath);
        }

        if (!Directory.Exists(MinecraftPath))
        {
            Directory.CreateDirectory(MinecraftPath);
            RepairedPaths.Add(MinecraftPath);
        }

        if (!Directory.Exists(BlockiumPath))
        {
            Directory.CreateDirectory(BlockiumPath);
            RepairedPaths.Add(BlockiumPath);
        }

        var FinalVerificationResult = await VerifyInstanceFilesUseCase.ExecuteAsync(
            new VerifyInstanceFilesRequest
            {
                InstanceId = Request.InstanceId
            },
            CancellationToken).ConfigureAwait(false);

        if (FinalVerificationResult.IsFailure)
        {
            Logger.Warning(Context, nameof(RepairInstanceUseCase), "FinalVerificationFailed", "Final verification failed.", new
            {
                FinalVerificationResult.Error.Code,
                FinalVerificationResult.Error.Message
            });

            return Result<RepairInstanceResult>.Failure(FinalVerificationResult.Error);
        }

        Logger.Info(Context, nameof(RepairInstanceUseCase), "RepairCompleted", "Instance repair completed.", new
        {
            InstanceId = FinalVerificationResult.Value.Instance.InstanceId.ToString(),
            Changed = RepairedPaths.Count > 0,
            RepairedPaths
        });

        await InstanceContentMetadataService
            .ReindexAsync(FinalVerificationResult.Value.Instance, CancellationToken)
            .ConfigureAwait(false);

        return Result<RepairInstanceResult>.Success(new RepairInstanceResult
        {
            Instance = FinalVerificationResult.Value.Instance,
            Changed = RepairedPaths.Count > 0,
            RepairedPaths = RepairedPaths,
            Verification = FinalVerificationResult.Value
        });
    }
}
