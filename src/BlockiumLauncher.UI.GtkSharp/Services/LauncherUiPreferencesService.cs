using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Json;
using BlockiumLauncher.UI.GtkSharp.Styling;

namespace BlockiumLauncher.UI.GtkSharp.Services;

public enum LauncherThemePreference
{
    Light = 0,
    Dark = 1
}

public sealed class LauncherUiSettings
{
    public string ThemeId { get; set; } = "light";
    public LauncherThemePreference? ThemePreference { get; set; }
}

public sealed class LauncherUiSettingsStore
{
    private readonly JsonFileStore JsonFileStore;
    private readonly ILauncherPaths LauncherPaths;

    public LauncherUiSettingsStore(JsonFileStore jsonFileStore, ILauncherPaths launcherPaths)
    {
        JsonFileStore = jsonFileStore ?? throw new ArgumentNullException(nameof(jsonFileStore));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
    }

    public async Task<LauncherUiSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        var settings = await JsonFileStore.ReadOptionalAsync<LauncherUiSettings>(GetSettingsPath(), cancellationToken).ConfigureAwait(false);
        return settings ?? new LauncherUiSettings();
    }

    public Task SaveAsync(LauncherUiSettings settings, CancellationToken cancellationToken = default)
    {
        return JsonFileStore.WriteAsync(GetSettingsPath(), settings, cancellationToken);
    }

    private string GetSettingsPath()
    {
        return Path.Combine(LauncherPaths.DataDirectory, "ui-settings.json");
    }
}

public sealed class LauncherUiPreferencesService
{
    private readonly SemaphoreSlim Gate = new(1, 1);
    private readonly LauncherUiSettingsStore SettingsStore;
    private readonly LauncherThemeCatalogService ThemeCatalog;

    public LauncherUiSettings CurrentSettings { get; private set; } = new();
    public string CurrentThemeId { get; private set; } = "light";
    public LauncherThemePalette CurrentPalette { get; private set; } = new(
        "#eef3f8",
        "#e6edf5",
        "#cbd6e1",
        "#dbe6f0",
        "#c8d4de",
        "#ffffff",
        "#e1e8ef",
        "#22303c",
        "#617386",
        "#2a7a53",
        "#f3f7fb",
        "#fbfdff",
        "#cad5df",
        "#f7f9fc",
        "#ffffff",
        "#c9d3dc",
        "#2e7be6",
        "#276fd1",
        "#f8f1f1",
        "#fcecec",
        "#9f4343",
        "#d8c6c6",
        "#ffffff",
        "#f5f9fd",
        "#edf4fd",
        "#f7f9fc",
        "#d2dce5",
        "#ffffff",
        "#d2dce5",
        "#ffffff",
        "#f7f9fc",
        "#2e7be6");

    public bool IsDarkTheme { get; private set; }
    public IReadOnlyList<LauncherThemeSummary> AvailableThemes => ThemeCatalog.AvailableThemes;

    public event EventHandler? Changed;

    public LauncherUiPreferencesService(LauncherUiSettingsStore settingsStore, LauncherThemeCatalogService themeCatalog)
    {
        SettingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        ThemeCatalog = themeCatalog ?? throw new ArgumentNullException(nameof(themeCatalog));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var changed = false;
        try
        {
            await ThemeCatalog.ReloadAsync(cancellationToken).ConfigureAwait(false);
            CurrentSettings = await SettingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
            changed = await NormalizeCurrentThemeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task ReloadThemesAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ThemeCatalog.ReloadAsync(cancellationToken).ConfigureAwait(false);
            await NormalizeCurrentThemeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetThemeAsync(string themeId, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var changed = false;
        try
        {
            if (!ThemeCatalog.TryGetTheme(themeId, out var theme))
            {
                return;
            }

            if (string.Equals(CurrentThemeId, theme.Id, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            CurrentSettings = new LauncherUiSettings
            {
                ThemeId = theme.Id
            };

            await SettingsStore.SaveAsync(CurrentSettings, cancellationToken).ConfigureAwait(false);
            ApplyTheme(theme);
            changed = true;
        }
        finally
        {
            Gate.Release();
        }

        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<bool> NormalizeCurrentThemeAsync(CancellationToken cancellationToken)
    {
        var resolvedThemeId = ResolveSavedThemeId(CurrentSettings);
        if (!ThemeCatalog.TryGetTheme(resolvedThemeId, out var theme))
        {
            return false;
        }

        var requiresSave = !string.Equals(CurrentSettings.ThemeId, theme.Id, StringComparison.OrdinalIgnoreCase)
            || CurrentSettings.ThemePreference is not null;

        if (requiresSave)
        {
            CurrentSettings = new LauncherUiSettings
            {
                ThemeId = theme.Id
            };

            await SettingsStore.SaveAsync(CurrentSettings, cancellationToken).ConfigureAwait(false);
        }

        ApplyTheme(theme);
        return requiresSave;
    }

    private void ApplyTheme(LauncherThemePaletteDefinition theme)
    {
        CurrentThemeId = theme.Id;
        CurrentPalette = theme.Palette;
        IsDarkTheme = theme.IsDarkTheme;
    }

    private static string ResolveSavedThemeId(LauncherUiSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ThemeId))
        {
            return settings.ThemeId.Trim();
        }

        return settings.ThemePreference == LauncherThemePreference.Dark ? "dark" : "light";
    }
}
