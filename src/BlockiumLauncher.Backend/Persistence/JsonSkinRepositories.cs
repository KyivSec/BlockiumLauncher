using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.UseCases.Skins;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Models;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;

namespace BlockiumLauncher.Infrastructure.Persistence.Repositories;

public sealed class JsonSkinLibraryRepository : ISkinLibraryRepository
{
    private readonly JsonFileStore JsonFileStore;
    private readonly ILauncherPaths LauncherPaths;

    public JsonSkinLibraryRepository(JsonFileStore jsonFileStore, ILauncherPaths launcherPaths)
    {
        JsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
    }

    public async Task<IReadOnlyList<SkinAssetSummary>> ListSkinsAsync(CancellationToken cancellationToken = default)
    {
        var library = await ReadLibraryAsync(cancellationToken).ConfigureAwait(false);
        return library.Skins.Select(MapStoredSkinToSummary).ToList();
    }

    public async Task<SkinAssetSummary?> GetSkinByIdAsync(string skinId, CancellationToken cancellationToken = default)
    {
        var library = await ReadLibraryAsync(cancellationToken).ConfigureAwait(false);
        var skin = library.Skins.FirstOrDefault(item => string.Equals(item.SkinId, skinId, StringComparison.Ordinal));
        return skin is null ? null : MapStoredSkinToSummary(skin);
    }

    public async Task SaveSkinAsync(SkinAssetSummary skin, CancellationToken cancellationToken = default)
    {
        var library = await ReadLibraryAsync(cancellationToken).ConfigureAwait(false);
        var stored = MapSummarySkinToStored(skin);
        var index = library.Skins.FindIndex(item => string.Equals(item.SkinId, stored.SkinId, StringComparison.Ordinal));
        if (index >= 0)
        {
            library.Skins[index] = stored;
        }
        else
        {
            library.Skins.Add(stored);
        }

        await WriteLibraryAsync(library, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CapeAssetSummary>> ListCapesAsync(CancellationToken cancellationToken = default)
    {
        var library = await ReadLibraryAsync(cancellationToken).ConfigureAwait(false);
        return library.Capes.Select(MapStoredCapeToSummary).ToList();
    }

    public async Task<CapeAssetSummary?> GetCapeByIdAsync(string capeId, CancellationToken cancellationToken = default)
    {
        var library = await ReadLibraryAsync(cancellationToken).ConfigureAwait(false);
        var cape = library.Capes.FirstOrDefault(item => string.Equals(item.CapeId, capeId, StringComparison.Ordinal));
        return cape is null ? null : MapStoredCapeToSummary(cape);
    }

    public async Task SaveCapeAsync(CapeAssetSummary cape, CancellationToken cancellationToken = default)
    {
        var library = await ReadLibraryAsync(cancellationToken).ConfigureAwait(false);
        var stored = MapSummaryCapeToStored(cape);
        var index = library.Capes.FindIndex(item => string.Equals(item.CapeId, stored.CapeId, StringComparison.Ordinal));
        if (index >= 0)
        {
            library.Capes[index] = stored;
        }
        else
        {
            library.Capes.Add(stored);
        }

        await WriteLibraryAsync(library, cancellationToken).ConfigureAwait(false);
    }

    private async Task<StoredSkinLibrary> ReadLibraryAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SkinImportSupport.GetSkinsRootDirectory(LauncherPaths));
        Directory.CreateDirectory(SkinImportSupport.GetSkinsDirectory(LauncherPaths));
        Directory.CreateDirectory(SkinImportSupport.GetCapesDirectory(LauncherPaths));
        var library = await JsonFileStore.ReadOptionalAsync<StoredSkinLibrary>(
            SkinImportSupport.GetLibraryFilePath(LauncherPaths),
            cancellationToken).ConfigureAwait(false) ?? new StoredSkinLibrary();

        var changed = RemoveMissingAssets(library);
        changed |= await SyncAssetsFromDirectoryAsync(library, cancellationToken).ConfigureAwait(false);
        if (changed)
        {
            await WriteLibraryAsync(library, cancellationToken).ConfigureAwait(false);
        }

        return library;
    }

    private Task WriteLibraryAsync(StoredSkinLibrary library, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SkinImportSupport.GetSkinsRootDirectory(LauncherPaths));
        return JsonFileStore.WriteAsync(
            SkinImportSupport.GetLibraryFilePath(LauncherPaths),
            library,
            cancellationToken);
    }

    private SkinAssetSummary MapStoredSkinToSummary(StoredSkinAsset stored)
    {
        return new SkinAssetSummary
        {
            SkinId = stored.SkinId,
            DisplayName = stored.DisplayName,
            FileName = stored.FileName,
            StoragePath = Path.Combine(SkinImportSupport.GetSkinsDirectory(LauncherPaths), stored.FileName),
            ModelType = Enum.TryParse<SkinModelType>(stored.ModelType, true, out var modelType) ? modelType : SkinModelType.Classic,
            ImportedAtUtc = stored.ImportedAtUtc
        };
    }

    private static StoredSkinAsset MapSummarySkinToStored(SkinAssetSummary summary)
    {
        return new StoredSkinAsset
        {
            SkinId = summary.SkinId,
            DisplayName = summary.DisplayName,
            FileName = summary.FileName,
            ModelType = summary.ModelType.ToString(),
            ImportedAtUtc = summary.ImportedAtUtc
        };
    }

    private CapeAssetSummary MapStoredCapeToSummary(StoredCapeAsset stored)
    {
        return new CapeAssetSummary
        {
            CapeId = stored.CapeId,
            DisplayName = stored.DisplayName,
            FileName = stored.FileName,
            StoragePath = Path.Combine(SkinImportSupport.GetCapesDirectory(LauncherPaths), stored.FileName),
            ImportedAtUtc = stored.ImportedAtUtc
        };
    }

    private static StoredCapeAsset MapSummaryCapeToStored(CapeAssetSummary summary)
    {
        return new StoredCapeAsset
        {
            CapeId = summary.CapeId,
            DisplayName = summary.DisplayName,
            FileName = summary.FileName,
            ImportedAtUtc = summary.ImportedAtUtc
        };
    }

    private bool RemoveMissingAssets(StoredSkinLibrary library)
    {
        var skinsDirectory = SkinImportSupport.GetSkinsDirectory(LauncherPaths);
        var capesDirectory = SkinImportSupport.GetCapesDirectory(LauncherPaths);

        var removedSkins = library.Skins.RemoveAll(item => !File.Exists(Path.Combine(skinsDirectory, item.FileName)));
        var removedCapes = library.Capes.RemoveAll(item => !File.Exists(Path.Combine(capesDirectory, item.FileName)));

        return removedSkins > 0 || removedCapes > 0;
    }

    private async Task<bool> SyncAssetsFromDirectoryAsync(StoredSkinLibrary library, CancellationToken cancellationToken)
    {
        var changed = false;
        changed |= await SyncSkinsFromDirectoryAsync(library, cancellationToken).ConfigureAwait(false);
        changed |= await SyncCapesFromDirectoryAsync(library, cancellationToken).ConfigureAwait(false);
        return changed;
    }

    private async Task<bool> SyncSkinsFromDirectoryAsync(StoredSkinLibrary library, CancellationToken cancellationToken)
    {
        var skinsDirectory = SkinImportSupport.GetSkinsDirectory(LauncherPaths);
        var knownFiles = library.Skins
            .Select(item => item.FileName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var filePath in Directory.EnumerateFiles(skinsDirectory, "*.png", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(filePath);
            if (knownFiles.Contains(fileName))
            {
                continue;
            }

            var discovered = await TryCreateSkinAssetAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (discovered is null)
            {
                continue;
            }

            library.Skins.Add(discovered);
            knownFiles.Add(fileName);
            changed = true;
        }

        return changed;
    }

    private async Task<bool> SyncCapesFromDirectoryAsync(StoredSkinLibrary library, CancellationToken cancellationToken)
    {
        var capesDirectory = SkinImportSupport.GetCapesDirectory(LauncherPaths);
        var knownFiles = library.Capes
            .Select(item => item.FileName)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var filePath in Directory.EnumerateFiles(capesDirectory, "*.png", SearchOption.TopDirectoryOnly)
                     .OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
        {
            var fileName = Path.GetFileName(filePath);
            if (knownFiles.Contains(fileName))
            {
                continue;
            }

            var discovered = await TryCreateCapeAssetAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (discovered is null)
            {
                continue;
            }

            library.Capes.Add(discovered);
            knownFiles.Add(fileName);
            changed = true;
        }

        return changed;
    }

    private static async Task<StoredSkinAsset?> TryCreateSkinAssetAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var sourceImage = await Image.LoadAsync<Rgba32>(filePath, cancellationToken).ConfigureAwait(false);
            if (!SkinImportSupport.IsSupportedSkinSize(sourceImage.Width, sourceImage.Height))
            {
                return null;
            }

            var importedAtUtc = GetImportedAtUtc(filePath);
            using var normalized = SkinImportSupport.NormalizeSkin(sourceImage);
            if (sourceImage.Height != normalized.Height)
            {
                await normalized.SaveAsPngAsync(filePath, cancellationToken).ConfigureAwait(false);
            }

            return new StoredSkinAsset
            {
                SkinId = Guid.NewGuid().ToString("N"),
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                FileName = Path.GetFileName(filePath),
                ModelType = SkinImportSupport.DetectModelType(normalized).ToString(),
                ImportedAtUtc = importedAtUtc
            };
        }
        catch (Exception ex) when (IsInvalidImageException(ex))
        {
            return null;
        }
    }

    private static async Task<StoredCapeAsset?> TryCreateCapeAssetAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var sourceImage = await Image.LoadAsync<Rgba32>(filePath, cancellationToken).ConfigureAwait(false);
            if (!SkinImportSupport.IsSupportedCapeSize(sourceImage.Width, sourceImage.Height))
            {
                return null;
            }

            return new StoredCapeAsset
            {
                CapeId = Guid.NewGuid().ToString("N"),
                DisplayName = Path.GetFileNameWithoutExtension(filePath),
                FileName = Path.GetFileName(filePath),
                ImportedAtUtc = GetImportedAtUtc(filePath)
            };
        }
        catch (Exception ex) when (IsInvalidImageException(ex))
        {
            return null;
        }
    }

    private static bool IsInvalidImageException(Exception exception)
    {
        return exception is UnknownImageFormatException or InvalidImageContentException or IOException or UnauthorizedAccessException;
    }

    private static DateTimeOffset GetImportedAtUtc(string filePath)
    {
        var timestamp = File.GetCreationTimeUtc(filePath);
        if (timestamp == DateTime.MinValue)
        {
            timestamp = File.GetLastWriteTimeUtc(filePath);
        }

        return new DateTimeOffset(timestamp, TimeSpan.Zero);
    }
}

public sealed class JsonAccountAppearanceRepository : IAccountAppearanceRepository
{
    private readonly JsonFileStore JsonFileStore;
    private readonly ILauncherPaths LauncherPaths;

    public JsonAccountAppearanceRepository(JsonFileStore jsonFileStore, ILauncherPaths launcherPaths)
    {
        JsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
    }

    public async Task<AccountAppearanceSelection?> GetAsync(AccountId accountId, CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var stored = items.FirstOrDefault(item => string.Equals(item.AccountId, accountId.ToString(), StringComparison.Ordinal));
        return stored is null ? null : Map(stored);
    }

    public async Task SaveAsync(AccountAppearanceSelection selection, CancellationToken cancellationToken = default)
    {
        var items = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        var stored = Map(selection);
        var index = items.FindIndex(item => string.Equals(item.AccountId, stored.AccountId, StringComparison.Ordinal));
        if (index >= 0)
        {
            items[index] = stored;
        }
        else
        {
            items.Add(stored);
        }

        Directory.CreateDirectory(SkinImportSupport.GetSkinsRootDirectory(LauncherPaths));
        await JsonFileStore.WriteAsync(
            SkinImportSupport.GetAccountAppearancesFilePath(LauncherPaths),
            items,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<StoredAccountAppearance>> ReadAllAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(SkinImportSupport.GetSkinsRootDirectory(LauncherPaths));
        return await JsonFileStore.ReadOptionalAsync<List<StoredAccountAppearance>>(
            SkinImportSupport.GetAccountAppearancesFilePath(LauncherPaths),
            cancellationToken).ConfigureAwait(false) ?? [];
    }

    private static AccountAppearanceSelection Map(StoredAccountAppearance stored)
    {
        return new AccountAppearanceSelection
        {
            AccountId = new AccountId(stored.AccountId),
            SelectedSkinId = stored.SelectedSkinId,
            SelectedCapeId = stored.SelectedCapeId
        };
    }

    private static StoredAccountAppearance Map(AccountAppearanceSelection selection)
    {
        return new StoredAccountAppearance
        {
            AccountId = selection.AccountId.ToString(),
            SelectedSkinId = selection.SelectedSkinId,
            SelectedCapeId = selection.SelectedCapeId
        };
    }
}
