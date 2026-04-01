using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BlockiumLauncher.Application.UseCases.Skins;

public static class SkinCustomizationErrors
{
    public static readonly Error InvalidRequest = new("Skin.InvalidRequest", "The skin customization request is invalid.");
    public static readonly Error AccountNotFound = new("Skin.AccountNotFound", "The requested account was not found.");
    public static readonly Error AccountNotSupported = new("Skin.AccountNotSupported", "Skin customization is currently available for offline accounts only.");
    public static readonly Error AssetNotFound = new("Skin.AssetNotFound", "The requested skin or cape asset was not found.");
    public static readonly Error InvalidImage = new("Skin.InvalidImage", "The selected image is not a supported PNG skin or cape.");
    public static readonly Error PersistenceFailed = new("Skin.PersistenceFailed", "The launcher could not persist skin customization data.");
}

public enum SkinModelType
{
    Classic = 0,
    Slim = 1
}

public sealed class SkinAssetSummary
{
    public string SkinId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public SkinModelType ModelType { get; init; }
    public DateTimeOffset ImportedAtUtc { get; init; }
}

public sealed class CapeAssetSummary
{
    public string CapeId { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public DateTimeOffset ImportedAtUtc { get; init; }
}

public sealed class AccountAppearanceSelection
{
    public AccountId AccountId { get; init; }
    public string? SelectedSkinId { get; init; }
    public string? SelectedCapeId { get; init; }
}

public sealed class ImportSkinAssetRequest
{
    public string SourceFilePath { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
}

public sealed class ImportCapeAssetRequest
{
    public string SourceFilePath { get; init; } = string.Empty;
    public string? DisplayName { get; init; }
}

public sealed class UpdateSkinModelRequest
{
    public string SkinId { get; init; } = string.Empty;
    public SkinModelType ModelType { get; init; }
}

public sealed class SetAccountAppearanceRequest
{
    public AccountId AccountId { get; init; }
    public string? SelectedSkinId { get; init; }
    public string? SelectedCapeId { get; init; }
}

public sealed class ListSkinAssetsUseCase
{
    private readonly ISkinLibraryRepository SkinLibraryRepository;

    public ListSkinAssetsUseCase(ISkinLibraryRepository skinLibraryRepository)
    {
        SkinLibraryRepository = skinLibraryRepository ?? throw new ArgumentNullException(nameof(skinLibraryRepository));
    }

    public async Task<Result<IReadOnlyList<SkinAssetSummary>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var skins = await SkinLibraryRepository.ListSkinsAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<SkinAssetSummary>>.Success(skins.OrderByDescending(skin => skin.ImportedAtUtc).ToList());
    }
}

public sealed class ListCapeAssetsUseCase
{
    private readonly ISkinLibraryRepository SkinLibraryRepository;

    public ListCapeAssetsUseCase(ISkinLibraryRepository skinLibraryRepository)
    {
        SkinLibraryRepository = skinLibraryRepository ?? throw new ArgumentNullException(nameof(skinLibraryRepository));
    }

    public async Task<Result<IReadOnlyList<CapeAssetSummary>>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var capes = await SkinLibraryRepository.ListCapesAsync(cancellationToken).ConfigureAwait(false);
        return Result<IReadOnlyList<CapeAssetSummary>>.Success(capes.OrderByDescending(cape => cape.ImportedAtUtc).ToList());
    }
}

public sealed class ImportSkinAssetUseCase
{
    private readonly ILauncherPaths LauncherPaths;
    private readonly ISkinLibraryRepository SkinLibraryRepository;

    public ImportSkinAssetUseCase(ILauncherPaths launcherPaths, ISkinLibraryRepository skinLibraryRepository)
    {
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        SkinLibraryRepository = skinLibraryRepository ?? throw new ArgumentNullException(nameof(skinLibraryRepository));
    }

    public async Task<Result<SkinAssetSummary>> ExecuteAsync(
        ImportSkinAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SourceFilePath))
        {
            return Result<SkinAssetSummary>.Failure(SkinCustomizationErrors.InvalidRequest);
        }

        if (!SkinImportSupport.IsPngFile(request.SourceFilePath) || !File.Exists(request.SourceFilePath))
        {
            return Result<SkinAssetSummary>.Failure(SkinCustomizationErrors.InvalidImage);
        }

        try
        {
            using var sourceImage = await Image.LoadAsync<Rgba32>(request.SourceFilePath, cancellationToken).ConfigureAwait(false);
            if (!SkinImportSupport.IsSupportedSkinSize(sourceImage.Width, sourceImage.Height))
            {
                return Result<SkinAssetSummary>.Failure(SkinCustomizationErrors.InvalidImage);
            }

            using var normalized = SkinImportSupport.NormalizeSkin(sourceImage);
            var skinId = Guid.NewGuid().ToString("N");
            var skinsDirectory = SkinImportSupport.GetSkinsDirectory(LauncherPaths);
            Directory.CreateDirectory(skinsDirectory);
            var fileName = SkinImportSupport.GetAvailableImportFileName(skinsDirectory, request.SourceFilePath);

            var destinationPath = Path.Combine(skinsDirectory, fileName);
            await normalized.SaveAsPngAsync(destinationPath, cancellationToken).ConfigureAwait(false);

            var modelType = SkinImportSupport.DetectModelType(normalized);
            var summary = new SkinAssetSummary
            {
                SkinId = skinId,
                DisplayName = SkinImportSupport.GetDisplayName(request.DisplayName, request.SourceFilePath),
                FileName = fileName,
                StoragePath = destinationPath,
                ModelType = modelType,
                ImportedAtUtc = DateTimeOffset.UtcNow
            };

            await SkinLibraryRepository.SaveSkinAsync(summary, cancellationToken).ConfigureAwait(false);
            return Result<SkinAssetSummary>.Success(summary);
        }
        catch (UnknownImageFormatException)
        {
            return Result<SkinAssetSummary>.Failure(SkinCustomizationErrors.InvalidImage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result<SkinAssetSummary>.Failure(new Error(
                SkinCustomizationErrors.PersistenceFailed.Code,
                SkinCustomizationErrors.PersistenceFailed.Message,
                ex.Message));
        }
    }
}

public sealed class ImportCapeAssetUseCase
{
    private readonly ILauncherPaths LauncherPaths;
    private readonly ISkinLibraryRepository SkinLibraryRepository;

    public ImportCapeAssetUseCase(ILauncherPaths launcherPaths, ISkinLibraryRepository skinLibraryRepository)
    {
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        SkinLibraryRepository = skinLibraryRepository ?? throw new ArgumentNullException(nameof(skinLibraryRepository));
    }

    public async Task<Result<CapeAssetSummary>> ExecuteAsync(
        ImportCapeAssetRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SourceFilePath))
        {
            return Result<CapeAssetSummary>.Failure(SkinCustomizationErrors.InvalidRequest);
        }

        if (!SkinImportSupport.IsPngFile(request.SourceFilePath) || !File.Exists(request.SourceFilePath))
        {
            return Result<CapeAssetSummary>.Failure(SkinCustomizationErrors.InvalidImage);
        }

        try
        {
            using var sourceImage = await Image.LoadAsync<Rgba32>(request.SourceFilePath, cancellationToken).ConfigureAwait(false);
            if (!SkinImportSupport.IsSupportedCapeSize(sourceImage.Width, sourceImage.Height))
            {
                return Result<CapeAssetSummary>.Failure(SkinCustomizationErrors.InvalidImage);
            }

            var capeId = Guid.NewGuid().ToString("N");
            var capesDirectory = SkinImportSupport.GetCapesDirectory(LauncherPaths);
            Directory.CreateDirectory(capesDirectory);
            var fileName = SkinImportSupport.GetAvailableImportFileName(capesDirectory, request.SourceFilePath);

            var destinationPath = Path.Combine(capesDirectory, fileName);
            await sourceImage.SaveAsPngAsync(destinationPath, cancellationToken).ConfigureAwait(false);

            var summary = new CapeAssetSummary
            {
                CapeId = capeId,
                DisplayName = SkinImportSupport.GetDisplayName(request.DisplayName, request.SourceFilePath),
                FileName = fileName,
                StoragePath = destinationPath,
                ImportedAtUtc = DateTimeOffset.UtcNow
            };

            await SkinLibraryRepository.SaveCapeAsync(summary, cancellationToken).ConfigureAwait(false);
            return Result<CapeAssetSummary>.Success(summary);
        }
        catch (UnknownImageFormatException)
        {
            return Result<CapeAssetSummary>.Failure(SkinCustomizationErrors.InvalidImage);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return Result<CapeAssetSummary>.Failure(new Error(
                SkinCustomizationErrors.PersistenceFailed.Code,
                SkinCustomizationErrors.PersistenceFailed.Message,
                ex.Message));
        }
    }
}

public sealed class UpdateSkinModelUseCase
{
    private readonly ISkinLibraryRepository SkinLibraryRepository;

    public UpdateSkinModelUseCase(ISkinLibraryRepository skinLibraryRepository)
    {
        SkinLibraryRepository = skinLibraryRepository ?? throw new ArgumentNullException(nameof(skinLibraryRepository));
    }

    public async Task<Result<SkinAssetSummary>> ExecuteAsync(
        UpdateSkinModelRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.SkinId))
        {
            return Result<SkinAssetSummary>.Failure(SkinCustomizationErrors.InvalidRequest);
        }

        var skin = await SkinLibraryRepository.GetSkinByIdAsync(request.SkinId.Trim(), cancellationToken).ConfigureAwait(false);
        if (skin is null)
        {
            return Result<SkinAssetSummary>.Failure(SkinCustomizationErrors.AssetNotFound);
        }

        var updated = new SkinAssetSummary
        {
            SkinId = skin.SkinId,
            DisplayName = skin.DisplayName,
            FileName = skin.FileName,
            StoragePath = skin.StoragePath,
            ModelType = request.ModelType,
            ImportedAtUtc = skin.ImportedAtUtc
        };

        await SkinLibraryRepository.SaveSkinAsync(updated, cancellationToken).ConfigureAwait(false);
        return Result<SkinAssetSummary>.Success(updated);
    }
}

public sealed class GetAccountAppearanceUseCase
{
    private readonly IAccountRepository AccountRepository;
    private readonly IAccountAppearanceRepository AccountAppearanceRepository;

    public GetAccountAppearanceUseCase(
        IAccountRepository accountRepository,
        IAccountAppearanceRepository accountAppearanceRepository)
    {
        AccountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        AccountAppearanceRepository = accountAppearanceRepository ?? throw new ArgumentNullException(nameof(accountAppearanceRepository));
    }

    public async Task<Result<AccountAppearanceSelection>> ExecuteAsync(
        AccountId accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await AccountRepository.GetByIdAsync(accountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return Result<AccountAppearanceSelection>.Failure(SkinCustomizationErrors.AccountNotFound);
        }

        var selection = await AccountAppearanceRepository.GetAsync(accountId, cancellationToken).ConfigureAwait(false)
            ?? new AccountAppearanceSelection { AccountId = accountId };

        return Result<AccountAppearanceSelection>.Success(selection);
    }
}

public sealed class SetAccountAppearanceUseCase
{
    private readonly IAccountRepository AccountRepository;
    private readonly ISkinLibraryRepository SkinLibraryRepository;
    private readonly IAccountAppearanceRepository AccountAppearanceRepository;

    public SetAccountAppearanceUseCase(
        IAccountRepository accountRepository,
        ISkinLibraryRepository skinLibraryRepository,
        IAccountAppearanceRepository accountAppearanceRepository)
    {
        AccountRepository = accountRepository ?? throw new ArgumentNullException(nameof(accountRepository));
        SkinLibraryRepository = skinLibraryRepository ?? throw new ArgumentNullException(nameof(skinLibraryRepository));
        AccountAppearanceRepository = accountAppearanceRepository ?? throw new ArgumentNullException(nameof(accountAppearanceRepository));
    }

    public async Task<Result<AccountAppearanceSelection>> ExecuteAsync(
        SetAccountAppearanceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request is null)
        {
            return Result<AccountAppearanceSelection>.Failure(SkinCustomizationErrors.InvalidRequest);
        }

        var account = await AccountRepository.GetByIdAsync(request.AccountId, cancellationToken).ConfigureAwait(false);
        if (account is null)
        {
            return Result<AccountAppearanceSelection>.Failure(SkinCustomizationErrors.AccountNotFound);
        }

        if (account.Provider != AccountProvider.Offline)
        {
            return Result<AccountAppearanceSelection>.Failure(SkinCustomizationErrors.AccountNotSupported);
        }

        if (!string.IsNullOrWhiteSpace(request.SelectedSkinId))
        {
            var skin = await SkinLibraryRepository.GetSkinByIdAsync(request.SelectedSkinId.Trim(), cancellationToken).ConfigureAwait(false);
            if (skin is null)
            {
                return Result<AccountAppearanceSelection>.Failure(SkinCustomizationErrors.AssetNotFound);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.SelectedCapeId))
        {
            var cape = await SkinLibraryRepository.GetCapeByIdAsync(request.SelectedCapeId.Trim(), cancellationToken).ConfigureAwait(false);
            if (cape is null)
            {
                return Result<AccountAppearanceSelection>.Failure(SkinCustomizationErrors.AssetNotFound);
            }
        }

        var selection = new AccountAppearanceSelection
        {
            AccountId = request.AccountId,
            SelectedSkinId = string.IsNullOrWhiteSpace(request.SelectedSkinId) ? null : request.SelectedSkinId.Trim(),
            SelectedCapeId = string.IsNullOrWhiteSpace(request.SelectedCapeId) ? null : request.SelectedCapeId.Trim()
        };

        await AccountAppearanceRepository.SaveAsync(selection, cancellationToken).ConfigureAwait(false);
        return Result<AccountAppearanceSelection>.Success(selection);
    }
}

internal static class SkinImportSupport
{
    public static string GetSkinsRootDirectory(ILauncherPaths launcherPaths)
    {
        return Path.Combine(launcherPaths.DataDirectory, "skins");
    }

    public static string GetSkinsDirectory(ILauncherPaths launcherPaths)
    {
        return Path.Combine(GetSkinsRootDirectory(launcherPaths), "skins");
    }

    public static string GetCapesDirectory(ILauncherPaths launcherPaths)
    {
        return Path.Combine(GetSkinsRootDirectory(launcherPaths), "capes");
    }

    public static string GetLibraryFilePath(ILauncherPaths launcherPaths)
    {
        return Path.Combine(GetSkinsRootDirectory(launcherPaths), "library.json");
    }

    public static string GetAccountAppearancesFilePath(ILauncherPaths launcherPaths)
    {
        return Path.Combine(GetSkinsRootDirectory(launcherPaths), "account-appearances.json");
    }

    public static bool IsPngFile(string path)
    {
        return string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSupportedSkinSize(int width, int height)
    {
        return width == 64 && (height == 64 || height == 32);
    }

    public static bool IsSupportedCapeSize(int width, int height)
    {
        return width == 64 && height == 32;
    }

    public static string GetDisplayName(string? preferredName, string sourcePath)
    {
        if (!string.IsNullOrWhiteSpace(preferredName))
        {
            return preferredName.Trim();
        }

        return Path.GetFileNameWithoutExtension(sourcePath).Trim();
    }

    public static string GetAvailableImportFileName(string directoryPath, string sourcePath)
    {
        var baseName = SanitizeImportedFileName(Path.GetFileNameWithoutExtension(sourcePath));
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "imported-skin";
        }

        var candidate = baseName + ".png";
        if (!File.Exists(Path.Combine(directoryPath, candidate)))
        {
            return candidate;
        }

        for (var suffix = 2; suffix <= 9999; suffix++)
        {
            candidate = $"{baseName}-{suffix}.png";
            if (!File.Exists(Path.Combine(directoryPath, candidate)))
            {
                return candidate;
            }
        }

        return $"{baseName}-{Guid.NewGuid():N}.png";
    }

    private static string SanitizeImportedFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var buffer = new char[value.Length];
        var count = 0;

        foreach (var character in value.Trim())
        {
            if (invalidChars.Contains(character))
            {
                continue;
            }

            buffer[count++] = character switch
            {
                ' ' => '-',
                _ => character
            };
        }

        var sanitized = new string(buffer, 0, count).Trim('-', '.');
        while (sanitized.Contains("--", StringComparison.Ordinal))
        {
            sanitized = sanitized.Replace("--", "-", StringComparison.Ordinal);
        }

        return sanitized;
    }

    public static Image<Rgba32> NormalizeSkin(Image<Rgba32> source)
    {
        if (source.Width == 64 && source.Height == 64)
        {
            return source.Clone();
        }

        var normalized = new Image<Rgba32>(64, 64);
        Clear(normalized);
        CopyRect(source, normalized, 0, 0, 64, 32, 0, 0);

        CopyRect(source, normalized, 0, 16, 16, 16, 16, 48);
        CopyRect(source, normalized, 40, 16, 16, 16, 32, 48);

        return normalized;
    }

    public static SkinModelType DetectModelType(Image<Rgba32> image)
    {
        if (image.Height != 64)
        {
            return SkinModelType.Classic;
        }

        return AreAllTransparent(image, 54, 20, 2, 12) && AreAllTransparent(image, 46, 52, 2, 12)
            ? SkinModelType.Slim
            : SkinModelType.Classic;
    }

    private static bool AreAllTransparent(Image<Rgba32> image, int x, int y, int width, int height)
    {
        for (var offsetY = 0; offsetY < height; offsetY++)
        {
            for (var offsetX = 0; offsetX < width; offsetX++)
            {
                if (image[x + offsetX, y + offsetY].A != 0)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void CopyRect(Image<Rgba32> source, Image<Rgba32> destination, int srcX, int srcY, int width, int height, int dstX, int dstY)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                destination[dstX + x, dstY + y] = source[srcX + x, srcY + y];
            }
        }
    }

    private static void Clear(Image<Rgba32> image)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                image[x, y] = new Rgba32(0, 0, 0, 0);
            }
        }
    }
}
