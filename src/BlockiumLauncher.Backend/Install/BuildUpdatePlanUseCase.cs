using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class BuildUpdatePlanUseCase
{
    private readonly IInstanceRepository InstanceRepository;
    private readonly VerifyInstanceFilesUseCase VerifyInstanceFilesUseCase;

    public BuildUpdatePlanUseCase(
        IInstanceRepository InstanceRepository,
        VerifyInstanceFilesUseCase VerifyInstanceFilesUseCase)
    {
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
        this.VerifyInstanceFilesUseCase = VerifyInstanceFilesUseCase ?? throw new ArgumentNullException(nameof(VerifyInstanceFilesUseCase));
    }

    public async Task<Result<UpdatePlan>> ExecuteAsync(
        UpdateInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        if (Request is null)
        {
            return Result<UpdatePlan>.Failure(InstallErrors.InvalidRequest);
        }

        if (Request.TargetLoaderType == LoaderType.Vanilla && Request.TargetLoaderVersion is not null)
        {
            return Result<UpdatePlan>.Failure(InstallErrors.InvalidRequest);
        }

        if (Request.TargetLoaderType != LoaderType.Vanilla && Request.TargetLoaderVersion is null)
        {
            return Result<UpdatePlan>.Failure(InstallErrors.InvalidRequest);
        }

        var Instance = await InstanceRepository.GetByIdAsync(Request.InstanceId, CancellationToken).ConfigureAwait(false);
        if (Instance is null)
        {
            return Result<UpdatePlan>.Failure(InstallErrors.InstanceNotFound);
        }

        var VerificationResult = await VerifyInstanceFilesUseCase.ExecuteAsync(
            new VerifyInstanceFilesRequest
            {
                InstanceId = Request.InstanceId
            },
            CancellationToken).ConfigureAwait(false);

        if (VerificationResult.IsFailure)
        {
            return Result<UpdatePlan>.Failure(VerificationResult.Error);
        }

        var Steps = new List<UpdatePlanStep>
        {
            new UpdatePlanStep
            {
                Kind = UpdatePlanStepKind.VerifyInstance,
                Message = "Verify the current instance structure before planning an update."
            }
        };

        var RequiresRepair = !VerificationResult.Value.IsValid;
        if (RequiresRepair)
        {
            Steps.Add(new UpdatePlanStep
            {
                Kind = UpdatePlanStepKind.RepairStructure,
                Message = "Repair missing managed instance directories before updating."
            });
        }

        var SameGameVersion =
            string.Equals(
                Instance.GameVersion.ToString(),
                Request.TargetGameVersion.ToString(),
                StringComparison.OrdinalIgnoreCase);

        var SameLoaderType = Instance.LoaderType == Request.TargetLoaderType;

        var CurrentLoaderVersion = Instance.LoaderVersion?.ToString() ?? string.Empty;
        var RequestedLoaderVersion = Request.TargetLoaderVersion?.ToString() ?? string.Empty;
        var SameLoaderVersion =
            string.Equals(CurrentLoaderVersion, RequestedLoaderVersion, StringComparison.OrdinalIgnoreCase);

        var IsNoOp = SameGameVersion && SameLoaderType && SameLoaderVersion;

        if (IsNoOp)
        {
            Steps.Add(new UpdatePlanStep
            {
                Kind = UpdatePlanStepKind.NoOp,
                Message = "The requested version and loader configuration already match the current instance."
            });
        }
        else
        {
            Steps.Add(new UpdatePlanStep
            {
                Kind = UpdatePlanStepKind.UpdateManagedContent,
                Message = "Rebuild managed instance content for the requested version and loader."
            });

            Steps.Add(new UpdatePlanStep
            {
                Kind = UpdatePlanStepKind.PersistMetadata,
                Message = "Persist updated instance metadata after managed content is replaced."
            });
        }

        return Result<UpdatePlan>.Success(new UpdatePlan
        {
            Instance = Instance,
            IsNoOp = IsNoOp,
            RequiresRepair = RequiresRepair,
            Steps = Steps
        });
    }
}