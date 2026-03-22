using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class ImportInstanceUseCase
{
    private readonly ITempWorkspaceFactory TempWorkspaceFactory;
    private readonly IFileTransaction FileTransaction;
    private readonly IInstanceRepository InstanceRepository;

    public ImportInstanceUseCase(
        ITempWorkspaceFactory TempWorkspaceFactory,
        IFileTransaction FileTransaction,
        IInstanceRepository InstanceRepository)
    {
        this.TempWorkspaceFactory = TempWorkspaceFactory ?? throw new ArgumentNullException(nameof(TempWorkspaceFactory));
        this.FileTransaction = FileTransaction ?? throw new ArgumentNullException(nameof(FileTransaction));
        this.InstanceRepository = InstanceRepository ?? throw new ArgumentNullException(nameof(InstanceRepository));
    }

    public async Task<Result<ImportInstanceResult>> ExecuteAsync(
        ImportInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        ITempWorkspace? Workspace = null;
        var TransactionStarted = false;

        try
        {
            if (string.IsNullOrWhiteSpace(Request.InstanceName) || string.IsNullOrWhiteSpace(Request.SourceDirectory))
            {
                return Result<ImportInstanceResult>.Failure(InstallErrors.InvalidRequest);
            }

            var ExistingInstance = await InstanceRepository.GetByNameAsync(Request.InstanceName.Trim(), CancellationToken).ConfigureAwait(false);
            if (ExistingInstance is not null)
            {
                return Result<ImportInstanceResult>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            var SourceDirectory = Path.GetFullPath(Request.SourceDirectory);
            if (!Directory.Exists(SourceDirectory))
            {
                return Result<ImportInstanceResult>.Failure(InstallErrors.ImportSourceMissing);
            }

            if (!LooksLikeInstanceDirectory(SourceDirectory))
            {
                return Result<ImportInstanceResult>.Failure(InstallErrors.ImportInvalidStructure);
            }

            var TargetDirectory = ResolveTargetDirectory(Request);
            if (Directory.Exists(TargetDirectory))
            {
                return Result<ImportInstanceResult>.Failure(InstallErrors.InstanceAlreadyExists);
            }

            Workspace = await TempWorkspaceFactory.CreateAsync("import", CancellationToken).ConfigureAwait(false);
            var StagedSourcePath = Workspace.GetPath("import-root");
            CopyDirectory(SourceDirectory, StagedSourcePath, true);

            var BeginResult = await FileTransaction.BeginAsync(TargetDirectory, CancellationToken).ConfigureAwait(false);
            if (BeginResult.IsFailure)
            {
                return Result<ImportInstanceResult>.Failure(BeginResult.Error);
            }

            TransactionStarted = true;

            var StageResult = await FileTransaction.StageDirectoryAsync(StagedSourcePath, CancellationToken).ConfigureAwait(false);
            if (StageResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
                return Result<ImportInstanceResult>.Failure(StageResult.Error);
            }

            var CommitResult = await FileTransaction.CommitAsync(CancellationToken).ConfigureAwait(false);
            if (CommitResult.IsFailure)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
                return Result<ImportInstanceResult>.Failure(CommitResult.Error);
            }

            if (!Request.CopyInsteadOfMove)
            {
                try
                {
                    Directory.Delete(SourceDirectory, true);
                }
                catch
                {
                }
            }

            var Instance = LauncherInstance.Create(
                InstanceId.New(),
                Request.InstanceName.Trim(),
                CreateVersionId("imported"),
                LoaderType.Vanilla,
                null,
                TargetDirectory,
                DateTimeOffset.UtcNow,
                LaunchProfile.CreateDefault(),
                null);

            await InstanceRepository.SaveAsync(Instance, CancellationToken).ConfigureAwait(false);

            return Result<ImportInstanceResult>.Success(new ImportInstanceResult
            {
                Instance = Instance,
                InstalledPath = TargetDirectory
            });
        }
        catch
        {
            if (TransactionStarted)
            {
                await FileTransaction.RollbackAsync(CancellationToken).ConfigureAwait(false);
            }

            return Result<ImportInstanceResult>.Failure(InstallErrors.Unexpected);
        }
        finally
        {
            if (Workspace is not null)
            {
                await Workspace.DisposeAsync().ConfigureAwait(false);
            }

            await FileTransaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static VersionId CreateVersionId(string Value)
    {
        var Type = typeof(VersionId);

        var ParseMethod = Type.GetMethod("Parse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (ParseMethod is not null)
        {
            return (VersionId)ParseMethod.Invoke(null, new object[] { Value })!;
        }

        var CreateMethod = Type.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (CreateMethod is not null)
        {
            return (VersionId)CreateMethod.Invoke(null, new object[] { Value })!;
        }

        var Constructor = Type.GetConstructor(new[] { typeof(string) });
        if (Constructor is not null)
        {
            return (VersionId)Constructor.Invoke(new object[] { Value });
        }

        throw new InvalidOperationException("Could not create VersionId from string.");
    }

    private static string ResolveTargetDirectory(ImportInstanceRequest Request)
    {
        if (!string.IsNullOrWhiteSpace(Request.TargetDirectory))
        {
            return Path.GetFullPath(Request.TargetDirectory.Trim());
        }

        var SafeName = string.Join(
            "_",
            Request.InstanceName
                .Trim()
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "instances", SafeName));
    }

    private static bool LooksLikeInstanceDirectory(string SourceDirectory)
    {
        return Directory.Exists(Path.Combine(SourceDirectory, ".minecraft")) ||
               File.Exists(Path.Combine(SourceDirectory, "instance.json")) ||
               Directory.Exists(Path.Combine(SourceDirectory, "mods")) ||
               Directory.Exists(Path.Combine(SourceDirectory, "config"));
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
            var DestinationParent = Path.GetDirectoryName(DestinationFilePath);
            if (!string.IsNullOrWhiteSpace(DestinationParent))
            {
                Directory.CreateDirectory(DestinationParent);
            }

            File.Copy(FilePath, DestinationFilePath, Overwrite);
        }
    }
}