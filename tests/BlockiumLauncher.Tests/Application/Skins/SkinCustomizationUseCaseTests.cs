using BlockiumLauncher.Application.UseCases.Skins;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.Infrastructure.Persistence.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Repositories;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Skins;

public sealed class SkinCustomizationUseCaseTests
{
    [Fact]
    public async Task ImportSkin_64x64_CopiesFileIntoLauncherLibrary()
    {
        using var fixture = new SkinTestFixture();
        var sourcePath = fixture.CreatePng("slim-skin.png", 64, 64, image =>
        {
            image[54, 20] = new Rgba32(0, 0, 0, 0);
            image[55, 20] = new Rgba32(0, 0, 0, 0);
        });

        var result = await fixture.ImportSkinAssetUseCase.ExecuteAsync(new ImportSkinAssetRequest
        {
            SourceFilePath = sourcePath
        });

        Assert.True(result.IsSuccess);
        Assert.True(File.Exists(result.Value.StoragePath));
        Assert.Equal(64, Image.Load<Rgba32>(result.Value.StoragePath).Width);
        Assert.Equal(64, Image.Load<Rgba32>(result.Value.StoragePath).Height);
    }

    [Fact]
    public async Task ImportSkin_64x32_NormalizesLegacySkinTo64x64()
    {
        using var fixture = new SkinTestFixture();
        var sourcePath = fixture.CreatePng("legacy-skin.png", 64, 32);

        var result = await fixture.ImportSkinAssetUseCase.ExecuteAsync(new ImportSkinAssetRequest
        {
            SourceFilePath = sourcePath
        });

        Assert.True(result.IsSuccess);

        using var normalized = Image.Load<Rgba32>(result.Value.StoragePath);
        Assert.Equal(64, normalized.Width);
        Assert.Equal(64, normalized.Height);
        Assert.Equal(SkinModelType.Classic, result.Value.ModelType);
    }

    [Fact]
    public async Task ImportSkin_InvalidDimensions_FailsCleanly()
    {
        using var fixture = new SkinTestFixture();
        var sourcePath = fixture.CreatePng("bad-skin.png", 32, 32);

        var result = await fixture.ImportSkinAssetUseCase.ExecuteAsync(new ImportSkinAssetRequest
        {
            SourceFilePath = sourcePath
        });

        Assert.True(result.IsFailure);
        Assert.Equal(SkinCustomizationErrors.InvalidImage.Code, result.Error.Code);
    }

    [Fact]
    public async Task AccountAppearance_And_ModelOverride_RoundTrip()
    {
        using var fixture = new SkinTestFixture();
        var account = LauncherAccount.CreateOffline("Builder", IsDefault: true);
        await fixture.AccountRepository.SaveAsync(account);

        var skinSource = fixture.CreatePng("builder.png", 64, 64);
        var capeSource = fixture.CreatePng("builder-cape.png", 64, 32);

        var skinResult = await fixture.ImportSkinAssetUseCase.ExecuteAsync(new ImportSkinAssetRequest
        {
            SourceFilePath = skinSource
        });
        var capeResult = await fixture.ImportCapeAssetUseCase.ExecuteAsync(new ImportCapeAssetRequest
        {
            SourceFilePath = capeSource
        });

        Assert.True(skinResult.IsSuccess);
        Assert.True(capeResult.IsSuccess);

        var updateResult = await fixture.UpdateSkinModelUseCase.ExecuteAsync(new UpdateSkinModelRequest
        {
            SkinId = skinResult.Value.SkinId,
            ModelType = SkinModelType.Slim
        });

        Assert.True(updateResult.IsSuccess);
        Assert.Equal(SkinModelType.Slim, updateResult.Value.ModelType);

        var setAppearance = await fixture.SetAccountAppearanceUseCase.ExecuteAsync(new SetAccountAppearanceRequest
        {
            AccountId = account.AccountId,
            SelectedSkinId = skinResult.Value.SkinId,
            SelectedCapeId = capeResult.Value.CapeId
        });

        Assert.True(setAppearance.IsSuccess);

        var loadedAppearance = await fixture.GetAccountAppearanceUseCase.ExecuteAsync(account.AccountId);
        Assert.True(loadedAppearance.IsSuccess);
        Assert.Equal(skinResult.Value.SkinId, loadedAppearance.Value.SelectedSkinId);
        Assert.Equal(capeResult.Value.CapeId, loadedAppearance.Value.SelectedCapeId);
    }

    [Fact]
    public async Task ListAssets_PrunesMissingSkinAndCapeFiles()
    {
        using var fixture = new SkinTestFixture();
        var skinSource = fixture.CreatePng("missing-soon-skin.png", 64, 64);
        var capeSource = fixture.CreatePng("missing-soon-cape.png", 64, 32);

        var skinResult = await fixture.ImportSkinAssetUseCase.ExecuteAsync(new ImportSkinAssetRequest
        {
            SourceFilePath = skinSource
        });
        var capeResult = await fixture.ImportCapeAssetUseCase.ExecuteAsync(new ImportCapeAssetRequest
        {
            SourceFilePath = capeSource
        });

        Assert.True(skinResult.IsSuccess);
        Assert.True(capeResult.IsSuccess);

        File.Delete(skinResult.Value.StoragePath);
        File.Delete(capeResult.Value.StoragePath);

        var listedSkins = await fixture.SkinLibraryRepository.ListSkinsAsync();
        var listedCapes = await fixture.SkinLibraryRepository.ListCapesAsync();

        Assert.DoesNotContain(listedSkins, item => item.SkinId == skinResult.Value.SkinId);
        Assert.DoesNotContain(listedCapes, item => item.CapeId == capeResult.Value.CapeId);
    }

    [Fact]
    public async Task ListAssets_DiscoversNewSkinAndCapeFilesFromLibraryFolders()
    {
        using var fixture = new SkinTestFixture();
        var skinsDirectory = Path.Combine(fixture.LauncherPaths.DataDirectory, "skins", "skins");
        var capesDirectory = Path.Combine(fixture.LauncherPaths.DataDirectory, "skins", "capes");
        Directory.CreateDirectory(skinsDirectory);
        Directory.CreateDirectory(capesDirectory);

        var discoveredSkinPath = Path.Combine(skinsDirectory, "manual-skin.png");
        var discoveredCapePath = Path.Combine(capesDirectory, "manual-cape.png");

        using (var legacySkin = new Image<Rgba32>(64, 32))
        {
            legacySkin[54, 20] = new Rgba32(0, 0, 0, 0);
            legacySkin[55, 20] = new Rgba32(0, 0, 0, 0);
            legacySkin.SaveAsPng(discoveredSkinPath);
        }

        using (var cape = new Image<Rgba32>(64, 32))
        {
            cape.SaveAsPng(discoveredCapePath);
        }

        var listedSkins = await fixture.SkinLibraryRepository.ListSkinsAsync();
        var listedCapes = await fixture.SkinLibraryRepository.ListCapesAsync();

        var discoveredSkin = Assert.Single(listedSkins);
        var discoveredCape = Assert.Single(listedCapes);

        Assert.Equal("manual-skin.png", discoveredSkin.FileName);
        Assert.Equal("manual-cape.png", discoveredCape.FileName);

        using var normalizedSkin = Image.Load<Rgba32>(discoveredSkin.StoragePath);
        Assert.Equal(64, normalizedSkin.Width);
        Assert.Equal(64, normalizedSkin.Height);
    }

    private sealed class SkinTestFixture : IDisposable
    {
        public SkinTestFixture()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "BlockiumLauncherTests", "Skins", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(RootDirectory);

            LauncherPaths = new LauncherPaths(RootDirectory);
            JsonFileStore = new JsonFileStore();
            AccountRepository = new JsonAccountRepository(JsonFileStore, LauncherPaths);
            SkinLibraryRepository = new JsonSkinLibraryRepository(JsonFileStore, LauncherPaths);
            AccountAppearanceRepository = new JsonAccountAppearanceRepository(JsonFileStore, LauncherPaths);

            ImportSkinAssetUseCase = new ImportSkinAssetUseCase(LauncherPaths, SkinLibraryRepository);
            ImportCapeAssetUseCase = new ImportCapeAssetUseCase(LauncherPaths, SkinLibraryRepository);
            UpdateSkinModelUseCase = new UpdateSkinModelUseCase(SkinLibraryRepository);
            GetAccountAppearanceUseCase = new GetAccountAppearanceUseCase(AccountRepository, AccountAppearanceRepository);
            SetAccountAppearanceUseCase = new SetAccountAppearanceUseCase(AccountRepository, SkinLibraryRepository, AccountAppearanceRepository);
        }

        public string RootDirectory { get; }
        public LauncherPaths LauncherPaths { get; }
        public JsonFileStore JsonFileStore { get; }
        public JsonAccountRepository AccountRepository { get; }
        public JsonSkinLibraryRepository SkinLibraryRepository { get; }
        public JsonAccountAppearanceRepository AccountAppearanceRepository { get; }
        public ImportSkinAssetUseCase ImportSkinAssetUseCase { get; }
        public ImportCapeAssetUseCase ImportCapeAssetUseCase { get; }
        public UpdateSkinModelUseCase UpdateSkinModelUseCase { get; }
        public GetAccountAppearanceUseCase GetAccountAppearanceUseCase { get; }
        public SetAccountAppearanceUseCase SetAccountAppearanceUseCase { get; }

        public string CreatePng(string fileName, int width, int height, Action<Image<Rgba32>>? configure = null)
        {
            var path = Path.Combine(RootDirectory, fileName);
            using var image = new Image<Rgba32>(width, height);
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    image[x, y] = new Rgba32((byte)(x % 255), (byte)(y % 255), 80, 255);
                }
            }

            configure?.Invoke(image);
            image.SaveAsPng(path);
            return path;
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootDirectory))
                {
                    Directory.Delete(RootDirectory, true);
                }
            }
            catch
            {
            }
        }
    }
}
