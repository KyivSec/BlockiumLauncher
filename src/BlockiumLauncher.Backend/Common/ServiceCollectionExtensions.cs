using BlockiumLauncher.Application.Abstractions.Auth;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Launch;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Application.UseCases.Skins;
using BlockiumLauncher.Backend.Catalog;
using BlockiumLauncher.Infrastructure.Auth;
using BlockiumLauncher.Infrastructure.Diagnostics;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Infrastructure.Java;
using BlockiumLauncher.Infrastructure.Launch;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Repositories;
using BlockiumLauncher.Infrastructure.Services;
using BlockiumLauncher.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace BlockiumLauncher.Backend.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBlockiumLauncherBackend(this IServiceCollection Services)
    {
        Services.AddSingleton(_ => new LauncherPaths(LauncherPaths.CreateDefault().RootDirectory));
        Services.AddSingleton<BlockiumLauncher.Application.Abstractions.Paths.ILauncherPaths>(Provider => Provider.GetRequiredService<LauncherPaths>());
        Services.AddSingleton<ILauncherPaths>(Provider => Provider.GetRequiredService<LauncherPaths>());

        Services.AddSingleton<BlockiumLauncher.Application.Abstractions.Security.ISecretStore, BlockiumLauncher.Infrastructure.Security.PlatformSecretStore>();
        Services.AddSingleton(Provider => CurseForgeOptions.FromConfiguration(
            Provider.GetRequiredService<ILauncherPaths>(),
            Provider.GetRequiredService<BlockiumLauncher.Application.Abstractions.Security.ISecretStore>()));
        Services.AddSingleton(new MetadataHttpOptions());
        Services.AddSingleton(new MetadataCachePolicy());
        Services.AddSingleton(new JavaDiscoveryOptions());

        Services.AddHttpClient<IMetadataHttpClient, MetadataHttpClient>();
        Services.AddHttpClient<CurseForgeContentCatalogService>();

        Services.AddSingleton<MojangVersionManifestClient>();
        Services.AddSingleton<ModrinthContentCatalogService>();
        Services.AddSingleton<CompositeContentCatalogService>();
        Services.AddSingleton<CompositeContentCatalogDetailsService>();
        Services.AddSingleton<CompositeContentCatalogMetadataService>();
        Services.AddSingleton<CompositeContentCatalogFileService>();
        Services.AddSingleton<FabricMetadataClient>();
        Services.AddSingleton<QuiltMetadataClient>();
        Services.AddSingleton<ForgeMetadataClient>();
        Services.AddSingleton<NeoForgeMetadataClient>();

        Services.AddSingleton<ILauncherDataMigrationService, LauncherDataMigrationService>();
        Services.AddSingleton<ISharedContentLayout, SharedContentLayout>();
        Services.AddSingleton<JsonFileStore>();

        Services.AddSingleton<IMetadataCacheRepository, JsonMetadataCacheRepository>();
        Services.AddSingleton<IAccountRepository, JsonAccountRepository>();
        Services.AddSingleton<IInstanceRepository, JsonInstanceRepository>();
        Services.AddSingleton<IInstanceContentMetadataRepository, JsonInstanceContentMetadataRepository>();
        Services.AddSingleton<IJavaInstallationRepository, JsonJavaInstallationRepository>();
        Services.AddSingleton<ISkinLibraryRepository, JsonSkinLibraryRepository>();
        Services.AddSingleton<IAccountAppearanceRepository, JsonAccountAppearanceRepository>();
        Services.AddSingleton<IManualDownloadStateStore, JsonManualDownloadStateStore>();

        Services.AddSingleton<IVersionManifestService, CachedVersionManifestService>();
        Services.AddSingleton<ILoaderMetadataService, CachedLoaderMetadataService>();
        Services.AddSingleton<IContentCatalogProvider>(Provider => Provider.GetRequiredService<ModrinthContentCatalogService>());
        Services.AddSingleton<IContentCatalogProvider>(Provider => Provider.GetRequiredService<CurseForgeContentCatalogService>());
        Services.AddSingleton<IContentCatalogDetailsProvider>(Provider => Provider.GetRequiredService<ModrinthContentCatalogService>());
        Services.AddSingleton<IContentCatalogDetailsProvider>(Provider => Provider.GetRequiredService<CurseForgeContentCatalogService>());
        Services.AddSingleton<IContentCatalogMetadataProvider>(Provider => Provider.GetRequiredService<ModrinthContentCatalogService>());
        Services.AddSingleton<IContentCatalogMetadataProvider>(Provider => Provider.GetRequiredService<CurseForgeContentCatalogService>());
        Services.AddSingleton<IContentCatalogFileProvider>(Provider => Provider.GetRequiredService<ModrinthContentCatalogService>());
        Services.AddSingleton<IContentCatalogFileProvider>(Provider => Provider.GetRequiredService<CurseForgeContentCatalogService>());
        Services.AddSingleton<IContentCatalogService, CompositeContentCatalogService>();
        Services.AddSingleton<IContentCatalogDetailsService, CompositeContentCatalogDetailsService>();
        Services.AddSingleton<IContentCatalogMetadataService, CompositeContentCatalogMetadataService>();
        Services.AddSingleton<IContentCatalogFileService, CompositeContentCatalogFileService>();

        Services.AddSingleton<ISecretRedactor, SensitiveDataRedactor>();
        Services.AddSingleton<IStructuredLogger, FileStructuredLogger>();
        Services.AddSingleton<IOperationContextFactory, BlockiumLauncher.Application.Diagnostics.DefaultOperationContextFactory>();
        Services.AddSingleton<IRuntimeMetadataStore, JsonRuntimeMetadataStore>();

        Services.AddSingleton<IDownloader, HttpDownloader>();

        Services.AddSingleton<IJavaVersionProbe, JavaVersionProbe>();
        Services.AddSingleton<IJavaValidationService, JavaValidationService>();
        Services.AddSingleton<IJavaDiscoveryService, JavaDiscoveryService>();
        Services.AddSingleton<IJavaRuntimeResolver, ManagedJavaRuntimeResolver>();
        Services.AddSingleton<IJavaRequirementResolver, JavaRequirementResolver>();
        Services.AddSingleton<IInstanceContentIndexer, FileSystemInstanceContentIndexer>();
        Services.AddSingleton<IInstanceContentMetadataService, InstanceContentMetadataService>();
        Services.AddSingleton<ILaunchSessionObserver, InstanceLaunchSessionObserver>();

        Services.AddTransient<DiscoverJavaUseCase>();
        Services.AddTransient<ValidateJavaUseCase>();

        Services.AddSingleton<BlockiumLauncher.Application.Abstractions.Storage.ITempWorkspaceFactory, BlockiumLauncher.Infrastructure.Storage.TempWorkspaceFactory>();
        Services.AddTransient<BlockiumLauncher.Application.Abstractions.Storage.IArchiveExtractor, BlockiumLauncher.Infrastructure.Storage.ZipArchiveExtractor>();
        Services.AddTransient<BlockiumLauncher.Application.Abstractions.Storage.IFileTransaction, BlockiumLauncher.Infrastructure.Storage.FileTransaction>();
        Services.AddTransient<LegacyLoaderRuntimePreparer>();
        Services.AddTransient<ILoaderRuntimePreparer, LegacyLoaderRuntimePreparer>();
        Services.AddTransient<IFabricInstallOrchestrator, FabricInstallOrchestrator>();
        Services.AddTransient<IQuiltInstallOrchestrator, QuiltInstallOrchestrator>();
        Services.AddTransient<ILoaderRuntimePreparer, FabricRuntimePreparer>();
        Services.AddTransient<ILoaderRuntimePreparer, QuiltRuntimePreparer>();
        Services.AddTransient<INeoForgeInstallOrchestrator, NeoForgeInstallOrchestrator>();
        Services.AddTransient<IForgeInstallOrchestrator, ForgeInstallOrchestrator>();
        Services.AddTransient<ILoaderRuntimePreparer, ForgeRuntimePreparer>();
        Services.AddTransient<ILoaderRuntimePreparer, NeoForgeRuntimePreparer>();
        Services.AddTransient<BlockiumLauncher.Application.Abstractions.Storage.IInstanceContentInstaller, BlockiumLauncher.Infrastructure.Storage.InstanceContentInstaller>();

        Services.AddSingleton<BlockiumLauncher.Application.Abstractions.Security.ITokenStore, BlockiumLauncher.Infrastructure.Security.WindowsProtectedTokenStore>();
        Services.AddSingleton<IMicrosoftAuthProvider, NotConfiguredMicrosoftAuthProvider>();
        Services.AddSingleton<ILaunchProcessRunner, LaunchProcessRunner>();

        Services.AddTransient<InstallPlanBuilder>();
        Services.AddTransient<InstallInstanceUseCase>();
        Services.AddTransient<ImportInstanceUseCase>();
        Services.AddTransient<VerifyInstanceFilesUseCase>();
        Services.AddTransient<RepairInstanceUseCase>();
        Services.AddTransient<BuildUpdatePlanUseCase>();
        Services.AddTransient<BuildLaunchPlanUseCase>();
        Services.AddTransient<LaunchInstanceUseCase>();
        Services.AddTransient<GetLaunchStatusUseCase>();
        Services.AddTransient<StopLaunchUseCase>();
        Services.AddTransient<ListInstanceBrowserSummariesUseCase>();
        Services.AddTransient<ListInstanceContentUseCase>();
        Services.AddTransient<RescanInstanceContentUseCase>();
        Services.AddTransient<SetModEnabledUseCase>();
        Services.AddTransient<SearchCatalogUseCase>();
        Services.AddTransient<GetCatalogProjectDetailsUseCase>();
        Services.AddTransient<GetCatalogProviderMetadataUseCase>();
        Services.AddTransient<ListCatalogFilesUseCase>();
        Services.AddTransient<InstallCatalogContentUseCase>();
        Services.AddTransient<ImportCatalogModpackUseCase>();
        Services.AddTransient<ImportArchiveInstanceUseCase>();
        Services.AddTransient<ResumeCatalogModpackImportUseCase>();
        Services.AddTransient<ConfigureCurseForgeApiKeyUseCase>();
        Services.AddTransient<ClearCurseForgeApiKeyUseCase>();
        Services.AddTransient<GetCurseForgeApiKeyStatusUseCase>();

        Services.AddTransient<AddAccountUseCase>();
        Services.AddTransient<ListAccountsUseCase>();
        Services.AddTransient<SetDefaultAccountUseCase>();
        Services.AddTransient<RemoveAccountUseCase>();
        Services.AddTransient<SignInMicrosoftUseCase>();
        Services.AddTransient<GetDefaultAccountUseCase>();
        Services.AddTransient<ResolveOfflineLaunchAccountUseCase>();
        Services.AddTransient<ListSkinAssetsUseCase>();
        Services.AddTransient<ImportSkinAssetUseCase>();
        Services.AddTransient<UpdateSkinModelUseCase>();
        Services.AddTransient<ListCapeAssetsUseCase>();
        Services.AddTransient<ImportCapeAssetUseCase>();
        Services.AddTransient<GetAccountAppearanceUseCase>();
        Services.AddTransient<SetAccountAppearanceUseCase>();

        return Services;
    }

    public static IServiceCollection AddBlockiumLauncherInfrastructure(this IServiceCollection Services)
    {
        return Services.AddBlockiumLauncherBackend();
    }
}

