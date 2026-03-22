using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class RepairInstanceUseCase
{
    private readonly VerifyInstanceFilesUseCase VerifyInstanceFilesUseCase;

    public RepairInstanceUseCase(VerifyInstanceFilesUseCase VerifyInstanceFilesUseCase)
    {
        this.VerifyInstanceFilesUseCase = VerifyInstanceFilesUseCase ?? throw new ArgumentNullException(nameof(VerifyInstanceFilesUseCase));
    }

    public async Task<Result<RepairInstanceResult>> ExecuteAsync(
        RepairInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null)
        {
            return Result<RepairInstanceResult>.Failure(InstallErrors.InvalidRequest);
        }

        var VerificationResult = await VerifyInstanceFilesUseCase.ExecuteAsync(
            new VerifyInstanceFilesRequest
            {
                InstanceId = Request.InstanceId
            },
            CancellationToken).ConfigureAwait(false);

        if (VerificationResult.IsFailure)
        {
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
            return Result<RepairInstanceResult>.Failure(FinalVerificationResult.Error);
        }

        return Result<RepairInstanceResult>.Success(new RepairInstanceResult
        {
            Instance = FinalVerificationResult.Value.Instance,
            Changed = RepairedPaths.Count > 0,
            RepairedPaths = RepairedPaths,
            Verification = FinalVerificationResult.Value
        });
    }
}