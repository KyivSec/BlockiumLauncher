using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Infrastructure.Persistence.Json;

namespace BlockiumLauncher.UI.GtkSharp.Services;

public enum LauncherThemePreference
{
    Light = 0,
    Dark = 1
}

public sealed class LauncherUiSettings
{
    public LauncherThemePreference ThemePreference { get; set; } = LauncherThemePreference.Light;
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
        return System.IO.Path.Combine(LauncherPaths.DataDirectory, "ui-settings.json");
    }
}

public sealed class LauncherUiPreferencesService
{
    private readonly SemaphoreSlim Gate = new(1, 1);
    private readonly LauncherUiSettingsStore SettingsStore;

    public LauncherUiSettings CurrentSettings { get; private set; } = new();

    public LauncherThemePreference CurrentThemePreference => CurrentSettings.ThemePreference;
    public bool IsDarkTheme => CurrentThemePreference == LauncherThemePreference.Dark;

    public event EventHandler? Changed;

    public LauncherUiPreferencesService(LauncherUiSettingsStore settingsStore)
    {
        SettingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            CurrentSettings = await SettingsStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task ToggleThemeAsync(CancellationToken cancellationToken = default)
    {
        var nextTheme = IsDarkTheme ? LauncherThemePreference.Light : LauncherThemePreference.Dark;
        return SetThemeAsync(nextTheme, cancellationToken);
    }

    public async Task SetThemeAsync(LauncherThemePreference themePreference, CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (CurrentSettings.ThemePreference == themePreference)
            {
                return;
            }

            CurrentSettings = new LauncherUiSettings
            {
                ThemePreference = themePreference
            };

            await SettingsStore.SaveAsync(CurrentSettings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
