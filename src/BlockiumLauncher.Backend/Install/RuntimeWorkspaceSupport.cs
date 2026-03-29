using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;

namespace BlockiumLauncher.Infrastructure.Storage;

internal static class RuntimeWorkspaceSupport
{
    internal static async Task<string> PrepareRootAsync(
        InstallPlan Plan,
        ITempWorkspace Workspace,
        CancellationToken CancellationToken)
    {
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

            if (Step.Kind != InstallPlanStepKind.WriteMetadata)
            {
                continue;
            }

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
                Plan.DownloadRuntime,
                CreatedAtUtc = DateTime.UtcNow
            };

            await File.WriteAllTextAsync(
                FilePath,
                JsonSerializer.Serialize(Payload, new JsonSerializerOptions { WriteIndented = true }),
                CancellationToken).ConfigureAwait(false);
        }

        return RootPath;
    }

    internal static Task WriteInstanceMarkerAsync(
        InstallPlan Plan,
        string RootPath,
        CancellationToken CancellationToken)
    {
        var MarkerPath = Path.Combine(RootPath, "instance.json");
        var MarkerPayload = new
        {
            Name = Plan.InstanceName,
            Version = Plan.GameVersion,
            LoaderType = Plan.LoaderType.ToString(),
            Plan.LoaderVersion,
            Plan.DownloadRuntime,
            InstalledBy = "BlockiumLauncher.SharedStorage"
        };

        return File.WriteAllTextAsync(
            MarkerPath,
            JsonSerializer.Serialize(MarkerPayload, new JsonSerializerOptions { WriteIndented = true }),
            CancellationToken);
    }
}
