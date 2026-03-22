using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class ManagedJavaRuntimeResolver : IJavaRuntimeResolver
{
    private readonly IJavaDiscoveryService JavaDiscoveryService;
    private readonly IJavaValidationService JavaValidationService;
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;

    public ManagedJavaRuntimeResolver(
        IJavaDiscoveryService JavaDiscoveryService,
        IJavaValidationService JavaValidationService,
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory)
    {
        this.JavaDiscoveryService = JavaDiscoveryService ?? throw new ArgumentNullException(nameof(JavaDiscoveryService));
        this.JavaValidationService = JavaValidationService ?? throw new ArgumentNullException(nameof(JavaValidationService));
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
    }

    public async Task<Result<string>> ResolveExecutablePathAsync(string MinecraftVersion, CancellationToken CancellationToken)
    {
        var Context = OperationContextFactory.Create("ResolveJavaRuntime");
        var RequiredMajor = GetRequiredJavaMajor(MinecraftVersion);

        Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaResolveStarted", "Resolving Java runtime.", new
        {
            MinecraftVersion,
            RequiredMajor
        });

        var DiscoverResult = await JavaDiscoveryService.DiscoverAsync(false, CancellationToken).ConfigureAwait(false);
        if (DiscoverResult.IsSuccess)
        {
            var Candidate = DiscoverResult.Value
                .Where(Item => Item.IsValid)
                .Where(Item => GetJavaMajor(Item.Version) >= RequiredMajor)
                .OrderBy(Item => GetJavaMajor(Item.Version))
                .ThenBy(Item => Item.ExecutablePath, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (Candidate is not null)
            {
                Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaResolvedLocal", "Resolved local Java installation.", new
                {
                    Candidate.ExecutablePath,
                    Candidate.Version,
                    RequiredMajor
                });

                return Result<string>.Success(Path.GetFullPath(Candidate.ExecutablePath));
            }
        }

        if (!OperatingSystem.IsWindows())
        {
            Logger.Warning(Context, nameof(ManagedJavaRuntimeResolver), "JavaManagedUnsupportedOs", "Managed Java download is currently implemented for Windows only.", new
            {
                RequiredMajor
            });

            return Result<string>.Failure(new Error(
                "Java.ResolveFailed",
                "Could not find a suitable local Java installation and managed Java download is not implemented for this operating system."));
        }

        var ManagedResult = await EnsureManagedJavaAsync(RequiredMajor, Context, CancellationToken).ConfigureAwait(false);
        if (ManagedResult.IsFailure)
        {
            return ManagedResult;
        }

        Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaResolvedManaged", "Resolved managed Java runtime.", new
        {
            ExecutablePath = ManagedResult.Value,
            RequiredMajor
        });

        return ManagedResult;
    }

    private async Task<Result<string>> EnsureManagedJavaAsync(
        int RequiredMajor,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var RuntimeRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BlockiumLauncher",
            "runtimes",
            "java",
            "temurin-" + RequiredMajor);

        var ExistingExecutable = FindJavaExecutable(RuntimeRoot);
        if (!string.IsNullOrWhiteSpace(ExistingExecutable))
        {
            var ExistingValidation = await JavaValidationService.ValidateExecutableAsync(ExistingExecutable, CancellationToken).ConfigureAwait(false);
            if (ExistingValidation.IsSuccess && GetJavaMajor(ExistingValidation.Value.Version) >= RequiredMajor)
            {
                return Result<string>.Success(Path.GetFullPath(ExistingExecutable));
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(RuntimeRoot)!);

        var TempRoot = RuntimeRoot + ".tmp";
        var ArchivePath = TempRoot + ".zip";

        if (Directory.Exists(TempRoot))
        {
            Directory.Delete(TempRoot, true);
        }

        if (File.Exists(ArchivePath))
        {
            File.Delete(ArchivePath);
        }

        Directory.CreateDirectory(TempRoot);

        var DownloadUrl = BuildAdoptiumBinaryUrl(RequiredMajor);

        Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaManagedDownloadStarted", "Downloading managed Java runtime.", new
        {
            DownloadUrl,
            RuntimeRoot,
            RequiredMajor
        });

        using var HttpClient = new HttpClient();
        using var Response = await HttpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, CancellationToken).ConfigureAwait(false);
        Response.EnsureSuccessStatusCode();

        await using (var Input = await Response.Content.ReadAsStreamAsync(CancellationToken).ConfigureAwait(false))
        await using (var Output = File.Create(ArchivePath))
        {
            await Input.CopyToAsync(Output, CancellationToken).ConfigureAwait(false);
        }

        ZipFile.ExtractToDirectory(ArchivePath, TempRoot, true);

        if (Directory.Exists(RuntimeRoot))
        {
            Directory.Delete(RuntimeRoot, true);
        }

        Directory.Move(TempRoot, RuntimeRoot);
        File.Delete(ArchivePath);

        var ExecutablePath = FindJavaExecutable(RuntimeRoot);
        if (string.IsNullOrWhiteSpace(ExecutablePath))
        {
            return Result<string>.Failure(new Error(
                "Java.ResolveFailed",
                "Managed Java download completed, but no Java executable was found."));
        }

        var ValidationResult = await JavaValidationService.ValidateExecutableAsync(ExecutablePath, CancellationToken).ConfigureAwait(false);
        if (ValidationResult.IsFailure || GetJavaMajor(ValidationResult.Value.Version) < RequiredMajor)
        {
            return Result<string>.Failure(new Error(
                "Java.ResolveFailed",
                "Managed Java download completed, but the runtime is not valid for the requested Minecraft version."));
        }

        return Result<string>.Success(Path.GetFullPath(ExecutablePath));
    }

    private static string BuildAdoptiumBinaryUrl(int FeatureVersion)
    {
        var Os = "windows";
        var Arch = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";
        return $"https://api.adoptium.net/v3/binary/latest/{FeatureVersion}/ga/{Os}/{Arch}/jre/hotspot/normal/eclipse?project=jdk";
    }

    private static string? FindJavaExecutable(string RootDirectory)
    {
        if (!Directory.Exists(RootDirectory))
        {
            return null;
        }

        var PreferredName = OperatingSystem.IsWindows() ? "javaw.exe" : "java";
        var FallbackName = OperatingSystem.IsWindows() ? "java.exe" : "java";

        var Preferred = Directory.EnumerateFiles(RootDirectory, PreferredName, SearchOption.AllDirectories).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(Preferred))
        {
            return Preferred;
        }

        return Directory.EnumerateFiles(RootDirectory, FallbackName, SearchOption.AllDirectories).FirstOrDefault();
    }

    private static int GetRequiredJavaMajor(string MinecraftVersion)
    {
        var Match = Regex.Match(MinecraftVersion ?? string.Empty, @"^(\d+)\.(\d+)(?:\.(\d+))?");
        if (!Match.Success)
        {
            return 21;
        }

        var Major = int.Parse(Match.Groups[1].Value);
        var Minor = int.Parse(Match.Groups[2].Value);
        var Patch = Match.Groups[3].Success ? int.Parse(Match.Groups[3].Value) : 0;

        if (Major == 1 && (Minor > 20 || (Minor == 20 && Patch >= 5)))
        {
            return 21;
        }

        if (Major == 1 && Minor >= 18)
        {
            return 17;
        }

        return 8;
    }

    private static int GetJavaMajor(string VersionText)
    {
        if (string.IsNullOrWhiteSpace(VersionText))
        {
            return 0;
        }

        var Match = Regex.Match(VersionText, @"(?<!\d)(\d+)(?:\.(\d+))?");
        if (!Match.Success)
        {
            return 0;
        }

        var First = int.Parse(Match.Groups[1].Value);
        if (First == 1 && Match.Groups[2].Success)
        {
            return int.Parse(Match.Groups[2].Value);
        }

        return First;
    }
}