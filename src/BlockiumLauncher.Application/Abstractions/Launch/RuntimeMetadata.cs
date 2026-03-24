namespace BlockiumLauncher.Application.Abstractions.Launch;

public sealed class RuntimeMetadata
{
    public string Version { get; init; } = string.Empty;
    public string MainClass { get; init; } = string.Empty;
    public string ClientJarPath { get; init; } = string.Empty;
    public IReadOnlyList<string> ClasspathEntries { get; init; } = [];
    public string AssetsDirectory { get; init; } = string.Empty;
    public string AssetIndexId { get; init; } = string.Empty;
    public string NativesDirectory { get; init; } = string.Empty;
    public string LibraryDirectory { get; init; } = string.Empty;
    public IReadOnlyList<string> ExtraJvmArguments { get; init; } = [];
    public IReadOnlyList<string> ExtraGameArguments { get; init; } = [];
}
