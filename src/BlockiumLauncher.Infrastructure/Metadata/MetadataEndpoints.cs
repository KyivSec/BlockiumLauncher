namespace BlockiumLauncher.Infrastructure.Metadata;

internal static class MetadataEndpoints
{
    internal const string VanillaVersionManifest = "https://launchermeta.mojang.com/mc/game/version_manifest_v2.json";

    internal static string FabricLoaderVersions(string GameVersion)
    {
        return $"https://meta.fabricmc.net/v2/versions/loader/{GameVersion}";
    }

    internal static string QuiltLoaderVersions(string GameVersion)
    {
        return $"https://meta.quiltmc.org/v3/versions/loader/{GameVersion}";
    }

    internal const string ForgeMavenMetadata =
        "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml";

    internal const string NeoForgeMavenMetadata =
        "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml";
}
