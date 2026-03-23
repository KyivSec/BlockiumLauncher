using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class FabricInstallOrchestrator : IFabricInstallOrchestrator
{
    private readonly ISharedContentLayout SharedContentLayout;
    private readonly LegacyLoaderRuntimePreparer FallbackPreparer;

    public FabricInstallOrchestrator(
        ISharedContentLayout sharedContentLayout,
        LegacyLoaderRuntimePreparer fallbackPreparer)
    {
        SharedContentLayout = sharedContentLayout ?? throw new ArgumentNullException(nameof(sharedContentLayout));
        FallbackPreparer = fallbackPreparer ?? throw new ArgumentNullException(nameof(fallbackPreparer));
    }

    public async Task<Result<string>> PrepareAsync(
        InstallPlan plan,
        ITempWorkspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(workspace);

        if (plan.LoaderType != LoaderType.Fabric)
        {
            return Result<string>.Failure(
                new Error(
                    "Install.InvalidFabricRequest",
                    "Fabric orchestrator received a non-Fabric install plan."));
        }

        if (string.IsNullOrWhiteSpace(plan.GameVersion))
        {
            return Result<string>.Failure(
                new Error(
                    "Install.FabricMissingGameVersion",
                    "Fabric preparation requires a Minecraft version."));
        }

        if (string.IsNullOrWhiteSpace(plan.LoaderVersion))
        {
            return Result<string>.Failure(
                new Error(
                    "Install.FabricMissingLoaderVersion",
                    "Fabric preparation requires a loader version."));
        }

        var sharedLoaderDirectory = SharedContentLayout.GetSharedLoaderDirectory(
            LoaderType.Fabric,
            plan.GameVersion,
            plan.LoaderVersion);

        Directory.CreateDirectory(sharedLoaderDirectory);

        var runtimeRoot = Path.Combine(sharedLoaderDirectory, "runtime");
        Directory.CreateDirectory(runtimeRoot);

        var launcherProfilesPath = Path.Combine(runtimeRoot, "launcher_profiles.json");
        if (!File.Exists(launcherProfilesPath))
        {
            var launcherProfilesJson = """
{
  "profiles": {},
  "settings": {},
  "version": 3
}
""";

            await File.WriteAllTextAsync(launcherProfilesPath, launcherProfilesJson, cancellationToken).ConfigureAwait(false);
        }

        var snapshotPath = Path.Combine(sharedLoaderDirectory, "plan.snapshot.json");
        var snapshot = new FabricPlanSnapshot(
            plan.InstanceName,
            plan.GameVersion,
            plan.LoaderVersion,
            sharedLoaderDirectory,
            runtimeRoot,
            workspace.RootPath,
            DateTimeOffset.UtcNow);

        await using (var stream = File.Create(snapshotPath))
        {
            await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken);
        }

        return await FallbackPreparer.PrepareAsync(plan, workspace, cancellationToken).ConfigureAwait(false);
    }

    private sealed record FabricPlanSnapshot(
        string InstanceName,
        string GameVersion,
        string LoaderVersion,
        string SharedLoaderDirectory,
        string RuntimeRoot,
        string WorkspaceRootPath,
        DateTimeOffset CreatedAtUtc);
}