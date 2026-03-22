using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class InstanceContentInstaller : IInstanceContentInstaller
{
    public async Task<Result<string>> PrepareAsync(
        InstallPlan Plan,
        ITempWorkspace Workspace,
        CancellationToken CancellationToken = default)
    {
        try
        {
            CancellationToken.ThrowIfCancellationRequested();

            var RootPath = Workspace.GetPath("instance-root");
            Directory.CreateDirectory(RootPath);

            foreach (var Step in Plan.Steps)
            {
                CancellationToken.ThrowIfCancellationRequested();

                if (Step.Kind == InstallPlanStepKind.CreateDirectory)
                {
                    Directory.CreateDirectory(Path.Combine(RootPath, Step.Destination));
                    continue;
                }

                if (Step.Kind == InstallPlanStepKind.WriteMetadata)
                {
                    var FilePath = Path.Combine(RootPath, Step.Destination);
                    var ParentDirectory = Path.GetDirectoryName(FilePath);
                    if (!string.IsNullOrWhiteSpace(ParentDirectory))
                    {
                        Directory.CreateDirectory(ParentDirectory);
                    }

                    var Payload = new
                    {
                        Plan.InstanceName,
                        Plan.GameVersion,
                        LoaderType = Plan.LoaderType.ToString(),
                        Plan.LoaderVersion,
                        Plan.TargetDirectory,
                        CreatedAtUtc = DateTime.UtcNow
                    };

                    await File.WriteAllTextAsync(
                        FilePath,
                        JsonSerializer.Serialize(Payload, new JsonSerializerOptions { WriteIndented = true }),
                        CancellationToken).ConfigureAwait(false);
                }
            }

            var MarkerPath = Path.Combine(RootPath, "instance.json");
            var MarkerPayload = new
            {
                Name = Plan.InstanceName,
                Version = Plan.GameVersion,
                LoaderType = Plan.LoaderType.ToString(),
                Plan.LoaderVersion,
                InstalledBy = "BlockiumLauncher.Stage8"
            };

            await File.WriteAllTextAsync(
                MarkerPath,
                JsonSerializer.Serialize(MarkerPayload, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken).ConfigureAwait(false);

            return Result<string>.Success(RootPath);
        }
        catch
        {
            return Result<string>.Failure(InstallErrors.DownloadFailed);
        }
    }
}