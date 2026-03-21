using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Infrastructure.Downloads;
using BlockiumLauncher.Infrastructure.Java;
using BlockiumLauncher.Infrastructure.Metadata;
using BlockiumLauncher.Infrastructure.Metadata.Clients;
using BlockiumLauncher.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BlockiumLauncher.Infrastructure.Composition;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBlockiumLauncherInfrastructure(this IServiceCollection Services)
    {
        Services.AddSingleton(new MetadataHttpOptions());
        Services.AddSingleton(new MetadataCachePolicy());
        Services.AddSingleton(new JavaDiscoveryOptions());

        Services.AddHttpClient<IMetadataHttpClient, MetadataHttpClient>();

        Services.AddSingleton<MojangVersionManifestClient>();
        Services.AddSingleton<FabricMetadataClient>();
        Services.AddSingleton<QuiltMetadataClient>();
        Services.AddSingleton<ForgeMetadataClient>();
        Services.AddSingleton<NeoForgeMetadataClient>();

        Services.AddSingleton<IVersionManifestService, CachedVersionManifestService>();
        Services.AddSingleton<ILoaderMetadataService, CachedLoaderMetadataService>();

        Services.AddSingleton<IDownloader, HttpDownloader>();

        Services.AddSingleton<IJavaVersionProbe, JavaVersionProbe>();
        Services.AddSingleton<IJavaValidationService, JavaValidationService>();
        Services.AddSingleton<IJavaDiscoveryService, JavaDiscoveryService>();

        Services.AddTransient<DiscoverJavaUseCase>();
        Services.AddTransient<ValidateJavaUseCase>();

        return Services;
    }
}
