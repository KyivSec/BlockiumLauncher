using System.Formats.Tar;
using System.IO.Compression;
using System.Runtime.InteropServices;
using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Java;

public sealed class ManagedJavaRuntimeService : IManagedJavaRuntimeService
{
    private readonly IStructuredLogger logger;
    private readonly IOperationContextFactory operationContextFactory;
    private readonly LauncherPaths launcherPaths;
    private readonly IJavaValidationService javaValidationService;

    public ManagedJavaRuntimeService(
        IStructuredLogger logger,
        IOperationContextFactory operationContextFactory,
        ILauncherPaths launcherPaths,
        IJavaValidationService javaValidationService)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.operationContextFactory = operationContextFactory ?? throw new ArgumentNullException(nameof(operationContextFactory));
        this.launcherPaths = launcherPaths as LauncherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        this.javaValidationService = javaValidationService ?? throw new ArgumentNullException(nameof(javaValidationService));
    }

    public async Task<Result<JavaInstallation?>> GetInstalledRuntimeAsync(int javaMajor, CancellationToken cancellationToken)
    {
        var normalizedMajor = NormalizeManagedJavaMajor(javaMajor);
        var runtimeDirectory = launcherPaths.GetManagedJavaDirectory(normalizedMajor.ToString());
        var executablePath = FindManagedJavaExecutable(runtimeDirectory);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return Result<JavaInstallation?>.Success(null);
        }

        var validationResult = await javaValidationService
            .ValidateExecutableAsync(executablePath, cancellationToken)
            .ConfigureAwait(false);

        if (validationResult.IsSuccess)
        {
            return Result<JavaInstallation?>.Success(validationResult.Value);
        }

        return Result<JavaInstallation?>.Success(JavaInstallation.Create(
            Domain.ValueObjects.JavaInstallationId.New(),
            executablePath,
            $"Java {normalizedMajor}",
            Domain.Enums.JavaArchitecture.X64,
            "Eclipse Adoptium",
            false));
    }

    public async Task<Result<JavaInstallation>> InstallRuntimeAsync(int javaMajor, bool forceReinstall, CancellationToken cancellationToken)
    {
        var normalizedMajor = NormalizeManagedJavaMajor(javaMajor);
        var context = operationContextFactory.Create("InstallManagedJavaRuntime");
        var runtimeDirectory = launcherPaths.GetManagedJavaDirectory(normalizedMajor.ToString());

        if (!forceReinstall)
        {
            var existingResult = await GetInstalledRuntimeAsync(normalizedMajor, cancellationToken).ConfigureAwait(false);
            if (existingResult.IsFailure)
            {
                return Result<JavaInstallation>.Failure(existingResult.Error);
            }

            if (existingResult.Value is not null && existingResult.Value.IsValid)
            {
                return Result<JavaInstallation>.Success(existingResult.Value);
            }
        }

        var parentDirectory = Path.GetDirectoryName(runtimeDirectory);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        var tempDirectory = runtimeDirectory + ".tmp";
        SafeDeleteDirectory(tempDirectory);
        Directory.CreateDirectory(tempDirectory);

        if (forceReinstall)
        {
            SafeDeleteDirectory(runtimeDirectory);
        }

        var archiveExtension = OperatingSystem.IsWindows() ? ".zip" : ".tar.gz";
        var archivePath = tempDirectory + archiveExtension;
        var downloadUrl = BuildAdoptiumBinaryUrl(normalizedMajor);

        logger.Info(context, nameof(ManagedJavaRuntimeService), "ManagedJavaDownloadStarted", "Downloading managed Java runtime.", new
        {
            JavaMajor = normalizedMajor,
            RuntimeDirectory = runtimeDirectory,
            DownloadUrl = downloadUrl
        });

        try
        {
            using var httpClient = new HttpClient();
            using var response = await httpClient
                .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var output = File.Create(archivePath))
            {
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            if (OperatingSystem.IsWindows())
            {
                ZipFile.ExtractToDirectory(archivePath, tempDirectory, true);
            }
            else
            {
                await using var fileStream = File.OpenRead(archivePath);
                await using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzipStream, tempDirectory, overwriteFiles: true);
            }

            SafeDeleteDirectory(runtimeDirectory);
            Directory.Move(tempDirectory, runtimeDirectory);

            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }

            var executablePath = FindManagedJavaExecutable(runtimeDirectory);
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return Result<JavaInstallation>.Failure(new Error(
                    "Java.ResolveFailed",
                    "Managed Java was downloaded, but no Java executable was found in the launcher runtime folder."));
            }

            var validationResult = await javaValidationService
                .ValidateExecutableAsync(executablePath, cancellationToken)
                .ConfigureAwait(false);

            if (validationResult.IsFailure)
            {
                return Result<JavaInstallation>.Failure(validationResult.Error);
            }

            logger.Info(context, nameof(ManagedJavaRuntimeService), "ManagedJavaDownloadCompleted", "Managed Java runtime downloaded successfully.", new
            {
                JavaMajor = normalizedMajor,
                ExecutablePath = executablePath
            });

            return Result<JavaInstallation>.Success(validationResult.Value);
        }
        catch (Exception exception)
        {
            logger.Error(context, nameof(ManagedJavaRuntimeService), "ManagedJavaDownloadFailed", "Managed Java download failed.", new
            {
                JavaMajor = normalizedMajor,
                RuntimeDirectory = runtimeDirectory
            }, exception);

            SafeDeleteDirectory(tempDirectory);
            return Result<JavaInstallation>.Failure(new Error(
                "Java.ResolveFailed",
                "Failed to download a managed Java runtime for the requested Java version."));
        }
    }

    public static int NormalizeManagedJavaMajor(int javaMajor)
    {
        return javaMajor switch
        {
            25 => 25,
            21 => 21,
            17 => 17,
            16 => 17,
            _ when javaMajor <= 8 => 8,
            _ when javaMajor < 17 => 17,
            _ when javaMajor < 21 => 17,
            _ when javaMajor < 25 => 21,
            _ => 25
        };
    }

    public static string? FindManagedJavaExecutable(string rootDirectory)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return null;
        }

        if (OperatingSystem.IsWindows())
        {
            var javaw = Directory.EnumerateFiles(rootDirectory, "javaw.exe", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(javaw))
            {
                return javaw;
            }

            return Directory.EnumerateFiles(rootDirectory, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
        }

        return Directory.EnumerateFiles(rootDirectory, "java", SearchOption.AllDirectories)
            .FirstOrDefault(pathValue => Path.GetFileName(pathValue).Equals("java", StringComparison.Ordinal));
    }

    private static string BuildAdoptiumBinaryUrl(int featureVersion)
    {
        var os = OperatingSystem.IsWindows()
            ? "windows"
            : OperatingSystem.IsMacOS()
                ? "mac"
                : "linux";

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "aarch64",
            Architecture.X86 => "x32",
            _ => "x64"
        };

        const string imageType = "jre";
        const string jvmImpl = "hotspot";
        const string heapSize = "normal";
        const string vendor = "eclipse";

        return $"https://api.adoptium.net/v3/binary/latest/{featureVersion}/ga/{os}/{arch}/{imageType}/{jvmImpl}/{heapSize}/{vendor}";
    }

    private static void SafeDeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Directory.Delete(path, true);
    }
}
