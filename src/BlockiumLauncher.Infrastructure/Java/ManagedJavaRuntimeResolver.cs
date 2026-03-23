using System.IO.Compression;
using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class ManagedJavaRuntimeResolver : IJavaRuntimeResolver
{
    private readonly IStructuredLogger Logger;
    private readonly IOperationContextFactory OperationContextFactory;
    private readonly LauncherPaths LauncherPaths;

    public ManagedJavaRuntimeResolver(
        IStructuredLogger Logger,
        IOperationContextFactory OperationContextFactory,
        ILauncherPaths LauncherPaths)
    {
        this.Logger = Logger ?? throw new ArgumentNullException(nameof(Logger));
        this.OperationContextFactory = OperationContextFactory ?? throw new ArgumentNullException(nameof(OperationContextFactory));
        this.LauncherPaths = LauncherPaths as LauncherPaths ?? throw new ArgumentNullException(nameof(LauncherPaths));
    }

    public async Task<Result<string>> ResolveExecutablePathAsync(string MinecraftVersion, CancellationToken CancellationToken)
    {
        var Context = OperationContextFactory.Create("ResolveJavaRuntime");
        var RequiredMajor = GetRequiredJavaMajor(MinecraftVersion);
        var RuntimeKey = RequiredMajor.ToString();
        var RuntimeDirectory = GetManagedRuntimeDirectory(RuntimeKey);

        Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaResolveStarted", "Resolving managed Java runtime.", new
        {
            MinecraftVersion,
            RequiredMajor,
            RuntimeDirectory
        });

        var ExistingExecutable = FindManagedJavaExecutable(RuntimeDirectory);
        if (!string.IsNullOrWhiteSpace(ExistingExecutable))
        {
            Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaResolvedManagedExisting", "Using existing launcher-managed Java.", new
            {
                ExistingExecutable,
                RequiredMajor
            });

            return Result<string>.Success(Path.GetFullPath(ExistingExecutable));
        }

        var DownloadResult = await DownloadManagedJavaAsync(RuntimeDirectory, RequiredMajor, Context, CancellationToken).ConfigureAwait(false);
        if (DownloadResult.IsFailure)
        {
            return DownloadResult;
        }

        return DownloadResult;
    }

    private async Task<Result<string>> DownloadManagedJavaAsync(
        string RuntimeDirectory,
        int RequiredMajor,
        OperationContext Context,
        CancellationToken CancellationToken)
    {
        var ParentDirectory = Path.GetDirectoryName(RuntimeDirectory);
        if (!string.IsNullOrWhiteSpace(ParentDirectory))
        {
            Directory.CreateDirectory(ParentDirectory);
        }

        var TempDirectory = RuntimeDirectory + ".tmp";
        if (Directory.Exists(TempDirectory))
        {
            Directory.Delete(TempDirectory, true);
        }

        Directory.CreateDirectory(TempDirectory);

        var DownloadUrl = BuildAdoptiumBinaryUrl(RequiredMajor);
        var ArchiveExtension = OperatingSystem.IsWindows() ? ".zip" : ".tar.gz";
        var ArchivePath = TempDirectory + ArchiveExtension;

        Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaDownloadStarted", "Downloading managed Java runtime.", new
        {
            DownloadUrl,
            ArchivePath,
            RequiredMajor
        });

        try
        {
            using var HttpClient = new HttpClient();
            using var Response = await HttpClient.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, CancellationToken).ConfigureAwait(false);
            Response.EnsureSuccessStatusCode();

            await using (var Input = await Response.Content.ReadAsStreamAsync(CancellationToken).ConfigureAwait(false))
            await using (var Output = File.Create(ArchivePath))
            {
                await Input.CopyToAsync(Output, CancellationToken).ConfigureAwait(false);
            }

            if (OperatingSystem.IsWindows())
            {
                ZipFile.ExtractToDirectory(ArchivePath, TempDirectory, true);
            }
            else
            {
                await using var FileStream = File.OpenRead(ArchivePath);
                await using var GzipStream = new GZipStream(FileStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(GzipStream, TempDirectory, overwriteFiles: true);
            }

            if (Directory.Exists(RuntimeDirectory))
            {
                Directory.Delete(RuntimeDirectory, true);
            }

            Directory.Move(TempDirectory, RuntimeDirectory);

            if (File.Exists(ArchivePath))
            {
                File.Delete(ArchivePath);
            }

            var ExecutablePath = FindManagedJavaExecutable(RuntimeDirectory);
            if (string.IsNullOrWhiteSpace(ExecutablePath))
            {
                return Result<string>.Failure(new Error(
                    "Java.ResolveFailed",
                    "Managed Java was downloaded, but no Java executable was found in the launcher runtime folder."));
            }

            Logger.Info(Context, nameof(ManagedJavaRuntimeResolver), "JavaDownloadCompleted", "Managed Java runtime downloaded successfully.", new
            {
                ExecutablePath,
                RequiredMajor
            });

            return Result<string>.Success(Path.GetFullPath(ExecutablePath));
        }
        catch (Exception Exception)
        {
            Logger.Error(Context, nameof(ManagedJavaRuntimeResolver), "JavaDownloadFailed", "Managed Java download failed.", new
            {
                RuntimeDirectory,
                RequiredMajor
            }, Exception);

            return Result<string>.Failure(new Error(
                "Java.ResolveFailed",
                "Failed to download a managed Java runtime for the requested Minecraft version."));
        }
    }

    private string GetManagedRuntimeDirectory(string RuntimeKey)
    {
        var CandidateMethod = LauncherPaths.GetType().GetMethod("GetManagedJavaDirectory");
        if (CandidateMethod is not null)
        {
            var Value = CandidateMethod.Invoke(LauncherPaths, new object[] { RuntimeKey }) as string;
            if (!string.IsNullOrWhiteSpace(Value))
            {
                return Value!;
            }
        }

        return Path.Combine(LauncherPaths.RootDirectory, "runtimes", "java", RuntimeKey);
    }

    private static string BuildAdoptiumBinaryUrl(int FeatureVersion)
    {
        var Os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "mac"
                : "linux";

        var Arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "x32",
            _ => "x64"
        };

        var ImageType = "jre";
        var JvmImpl = "hotspot";
        var HeapSize = "normal";
        var Vendor = "eclipse";

        return $"https://api.adoptium.net/v3/binary/latest/{FeatureVersion}/ga/{Os}/{Arch}/{ImageType}/{JvmImpl}/{HeapSize}/{Vendor}";
    }

    private static string? FindManagedJavaExecutable(string RootDirectory)
    {
        if (!Directory.Exists(RootDirectory))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            var Javaw = Directory.EnumerateFiles(RootDirectory, "javaw.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(Javaw))
            {
                return Javaw;
            }

            return Directory.EnumerateFiles(RootDirectory, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
        }

        return Directory.EnumerateFiles(RootDirectory, "java", SearchOption.AllDirectories)
            .FirstOrDefault(PathValue => Path.GetFileName(PathValue).Equals("java", StringComparison.Ordinal));
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
}