using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Skins;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Abstractions.Repositories;

public interface IAccountRepository
{
    Task<IReadOnlyList<LauncherAccount>> ListAsync(CancellationToken CancellationToken = default);
    Task<LauncherAccount?> GetByIdAsync(AccountId AccountId, CancellationToken CancellationToken = default);
    Task<LauncherAccount?> GetDefaultAsync(CancellationToken CancellationToken = default);
    Task SaveAsync(LauncherAccount Account, CancellationToken CancellationToken = default);
    Task DeleteAsync(AccountId AccountId, CancellationToken CancellationToken = default);
}

public interface ISkinLibraryRepository
{
    Task<IReadOnlyList<SkinAssetSummary>> ListSkinsAsync(CancellationToken cancellationToken = default);
    Task<SkinAssetSummary?> GetSkinByIdAsync(string skinId, CancellationToken cancellationToken = default);
    Task SaveSkinAsync(SkinAssetSummary skin, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CapeAssetSummary>> ListCapesAsync(CancellationToken cancellationToken = default);
    Task<CapeAssetSummary?> GetCapeByIdAsync(string capeId, CancellationToken cancellationToken = default);
    Task SaveCapeAsync(CapeAssetSummary cape, CancellationToken cancellationToken = default);
}

public interface IAccountAppearanceRepository
{
    Task<AccountAppearanceSelection?> GetAsync(AccountId accountId, CancellationToken cancellationToken = default);
    Task SaveAsync(AccountAppearanceSelection selection, CancellationToken cancellationToken = default);
}

public interface IInstanceContentMetadataRepository
{
    Task<InstanceContentMetadata?> LoadAsync(string installLocation, CancellationToken cancellationToken = default);
    Task SaveAsync(string installLocation, InstanceContentMetadata metadata, CancellationToken cancellationToken = default);
}

public interface IInstanceRepository
{
    Task<IReadOnlyList<LauncherInstance>> ListAsync(CancellationToken CancellationToken);
    Task<LauncherInstance?> GetByIdAsync(InstanceId InstanceId, CancellationToken CancellationToken);
    Task<LauncherInstance?> GetByNameAsync(string Name, CancellationToken CancellationToken);
    Task SaveAsync(LauncherInstance Instance, CancellationToken CancellationToken);
    Task DeleteAsync(InstanceId InstanceId, CancellationToken CancellationToken);
}

public interface IJavaInstallationRepository
{
    Task<IReadOnlyList<JavaInstallation>> ListAsync(CancellationToken CancellationToken);
    Task<JavaInstallation?> GetByIdAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken);
    Task SaveAsync(JavaInstallation JavaInstallation, CancellationToken CancellationToken);
    Task DeleteAsync(JavaInstallationId JavaInstallationId, CancellationToken CancellationToken);
}

public interface IMetadataCacheRepository
{
    Task<IReadOnlyList<VersionSummary>?> GetCachedVersionsAsync(CancellationToken CancellationToken);
    Task SaveCachedVersionsAsync(IReadOnlyList<VersionSummary> Versions, CancellationToken CancellationToken);

    Task<IReadOnlyList<LoaderVersionSummary>?> GetCachedLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        CancellationToken CancellationToken);

    Task SaveCachedLoaderVersionsAsync(
        LoaderType LoaderType,
        VersionId GameVersion,
        IReadOnlyList<LoaderVersionSummary> LoaderVersions,
        CancellationToken CancellationToken);
}
