using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.UI.GtkSharp.Styling;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BlockiumLauncher.UI.GtkSharp.Services;

public sealed record LauncherThemeSummary(
    string Id,
    string DisplayName,
    bool IsDarkTheme,
    string FilePath);

public sealed class LauncherThemePaletteDefinition
{
    public string Id { get; set; } = "light";
    public string DisplayName { get; set; } = "Light";
    public bool IsDarkTheme { get; set; }

    public string WindowBackground { get; set; } = "#ffffff";
    public string TopbarBackground { get; set; } = "#ffffff";
    public string TopbarBorder { get; set; } = "#d0d0d0";
    public string SidebarBackground { get; set; } = "#ffffff";
    public string SidebarBorder { get; set; } = "#d0d0d0";
    public string ContentBackground { get; set; } = "#ffffff";
    public string ContentBorder { get; set; } = "#d0d0d0";
    public string PrimaryText { get; set; } = "#222222";
    public string SecondaryText { get; set; } = "#666666";
    public string StatusText { get; set; } = "#2a7a53";
    public string ToolbarButtonBackground { get; set; } = "#f3f7fb";
    public string ToolbarButtonHover { get; set; } = "#fbfdff";
    public string ToolbarBorder { get; set; } = "#cad5df";
    public string ActionButtonBackground { get; set; } = "#f7f9fc";
    public string ActionButtonHover { get; set; } = "#ffffff";
    public string ActionBorder { get; set; } = "#c9d3dc";
    public string PrimaryButton { get; set; } = "#2e7be6";
    public string PrimaryButtonHover { get; set; } = "#276fd1";
    public string DangerBackground { get; set; } = "#f8f1f1";
    public string DangerHover { get; set; } = "#fcecec";
    public string DangerText { get; set; } = "#9f4343";
    public string DangerBorder { get; set; } = "#d8c6c6";
    public string RowBackground { get; set; } = "#ffffff";
    public string RowHover { get; set; } = "#f5f9fd";
    public string RowSelected { get; set; } = "#edf4fd";
    public string SearchBackground { get; set; } = "#f7f9fc";
    public string SearchBorder { get; set; } = "#d2dce5";
    public string PopoverBackground { get; set; } = "#ffffff";
    public string PopoverBorder { get; set; } = "#d2dce5";
    public string SectionBackground { get; set; } = "#ffffff";
    public string SectionMutedBackground { get; set; } = "#f7f9fc";
    public string Accent { get; set; } = "#2e7be6";

    [JsonIgnore]
    public LauncherThemePalette Palette => new(
        WindowBackground,
        TopbarBackground,
        TopbarBorder,
        SidebarBackground,
        SidebarBorder,
        ContentBackground,
        ContentBorder,
        PrimaryText,
        SecondaryText,
        StatusText,
        ToolbarButtonBackground,
        ToolbarButtonHover,
        ToolbarBorder,
        ActionButtonBackground,
        ActionButtonHover,
        ActionBorder,
        PrimaryButton,
        PrimaryButtonHover,
        DangerBackground,
        DangerHover,
        DangerText,
        DangerBorder,
        RowBackground,
        RowHover,
        RowSelected,
        SearchBackground,
        SearchBorder,
        PopoverBackground,
        PopoverBorder,
        SectionBackground,
        SectionMutedBackground,
        Accent);
}

public sealed class LauncherThemeCatalogService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly ILauncherPaths LauncherPaths;
    private readonly SemaphoreSlim Gate = new(1, 1);
    private readonly Dictionary<string, LauncherThemePaletteDefinition> ThemesById = new(StringComparer.OrdinalIgnoreCase);

    public LauncherThemeCatalogService(ILauncherPaths launcherPaths)
    {
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
    }

    public IReadOnlyList<LauncherThemeSummary> AvailableThemes { get; private set; } = [];

    public string PaletteDirectoryPath => Path.Combine(LauncherPaths.DataDirectory, "palettes");

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(PaletteDirectoryPath);
            await EnsureSeedThemesAsync(cancellationToken).ConfigureAwait(false);

            ThemesById.Clear();
            var themes = new List<LauncherThemeSummary>();

            foreach (var filePath in Directory.EnumerateFiles(PaletteDirectoryPath, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
                    var palette = JsonSerializer.Deserialize<LauncherThemePaletteDefinition>(json, SerializerOptions);
                    if (palette is null)
                    {
                        continue;
                    }

                    var themeId = NormalizeId(string.IsNullOrWhiteSpace(palette.Id)
                        ? Path.GetFileNameWithoutExtension(filePath)
                        : palette.Id);

                    if (string.IsNullOrWhiteSpace(themeId))
                    {
                        continue;
                    }

                    palette.Id = themeId;
                    palette.DisplayName = string.IsNullOrWhiteSpace(palette.DisplayName)
                        ? BuildDisplayName(themeId)
                        : palette.DisplayName.Trim();

                    ThemesById[themeId] = palette;
                    themes.Add(new LauncherThemeSummary(themeId, palette.DisplayName, palette.IsDarkTheme, filePath));
                }
                catch
                {
                    // Invalid custom palettes are ignored so one broken file does not break the launcher.
                }
            }

            if (themes.Count == 0)
            {
                var fallbackThemes = new[]
                {
                    CreateDefaultLightTheme(),
                    CreateDefaultDarkTheme()
                };

                foreach (var palette in fallbackThemes)
                {
                    ThemesById[palette.Id] = palette;
                }

                AvailableThemes = fallbackThemes
                    .Select(static palette => new LauncherThemeSummary(
                        palette.Id,
                        palette.DisplayName,
                        palette.IsDarkTheme,
                        Path.Combine(".", $"{palette.Id}.json")))
                    .ToArray();
                return;
            }

            AvailableThemes = themes.OrderBy(static theme => theme.DisplayName, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            Gate.Release();
        }
    }

    public bool TryGetTheme(string? themeId, out LauncherThemePaletteDefinition theme)
    {
        var resolvedId = NormalizeId(themeId);
        if (!string.IsNullOrWhiteSpace(resolvedId) && ThemesById.TryGetValue(resolvedId, out theme!))
        {
            return true;
        }

        if (ThemesById.TryGetValue("light", out theme!))
        {
            return true;
        }

        theme = ThemesById.Values.FirstOrDefault() ?? CreateDefaultLightTheme();
        return theme is not null;
    }

    private async Task EnsureSeedThemesAsync(CancellationToken cancellationToken)
    {
        var builtIns = new[]
        {
            CreateDefaultLightTheme(),
            CreateDefaultDarkTheme()
        };

        foreach (var palette in builtIns)
        {
            var filePath = Path.Combine(PaletteDirectoryPath, $"{palette.Id}.json");
            if (File.Exists(filePath))
            {
                continue;
            }

            var json = JsonSerializer.Serialize(palette, SerializerOptions);
            await File.WriteAllTextAsync(filePath, json, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string NormalizeId(string? rawId)
    {
        return string.IsNullOrWhiteSpace(rawId)
            ? string.Empty
            : rawId.Trim().ToLowerInvariant();
    }

    private static string BuildDisplayName(string themeId)
    {
        return string.Join(" ", themeId
            .Split(['-', '_', ' '], StringSplitOptions.RemoveEmptyEntries)
            .Select(static part => string.Concat(char.ToUpperInvariant(part[0]), part[1..])));
    }

    private static LauncherThemePaletteDefinition CreateDefaultLightTheme()
    {
        return new LauncherThemePaletteDefinition
        {
            Id = "light",
            DisplayName = "Light",
            IsDarkTheme = false,
            WindowBackground = "#eef3f8",
            TopbarBackground = "#e6edf5",
            TopbarBorder = "#cbd6e1",
            SidebarBackground = "#dbe6f0",
            SidebarBorder = "#c8d4de",
            ContentBackground = "#ffffff",
            ContentBorder = "#e1e8ef",
            PrimaryText = "#22303c",
            SecondaryText = "#617386",
            StatusText = "#2a7a53",
            ToolbarButtonBackground = "#f3f7fb",
            ToolbarButtonHover = "#fbfdff",
            ToolbarBorder = "#cad5df",
            ActionButtonBackground = "#f7f9fc",
            ActionButtonHover = "#ffffff",
            ActionBorder = "#c9d3dc",
            PrimaryButton = "#2e7be6",
            PrimaryButtonHover = "#276fd1",
            DangerBackground = "#f8f1f1",
            DangerHover = "#fcecec",
            DangerText = "#9f4343",
            DangerBorder = "#d8c6c6",
            RowBackground = "#ffffff",
            RowHover = "#f5f9fd",
            RowSelected = "#edf4fd",
            SearchBackground = "#f7f9fc",
            SearchBorder = "#d2dce5",
            PopoverBackground = "#ffffff",
            PopoverBorder = "#d2dce5",
            SectionBackground = "#ffffff",
            SectionMutedBackground = "#f7f9fc",
            Accent = "#2e7be6"
        };
    }

    private static LauncherThemePaletteDefinition CreateDefaultDarkTheme()
    {
        return new LauncherThemePaletteDefinition
        {
            Id = "dark",
            DisplayName = "Dark",
            IsDarkTheme = true,
            WindowBackground = "#0f141a",
            TopbarBackground = "#18212b",
            TopbarBorder = "#2a3744",
            SidebarBackground = "#14202a",
            SidebarBorder = "#263442",
            ContentBackground = "#111820",
            ContentBorder = "#1e2a35",
            PrimaryText = "#edf3f8",
            SecondaryText = "#9cb0c2",
            StatusText = "#73d49c",
            ToolbarButtonBackground = "#202b36",
            ToolbarButtonHover = "#273544",
            ToolbarBorder = "#334251",
            ActionButtonBackground = "#1d2833",
            ActionButtonHover = "#24313d",
            ActionBorder = "#31404f",
            PrimaryButton = "#2e7be6",
            PrimaryButtonHover = "#4f95ef",
            DangerBackground = "#352326",
            DangerHover = "#422b2f",
            DangerText = "#f2a9a9",
            DangerBorder = "#5a373d",
            RowBackground = "#111820",
            RowHover = "#18222d",
            RowSelected = "#1a2734",
            SearchBackground = "#17212b",
            SearchBorder = "#2e3b48",
            PopoverBackground = "#1a232d",
            PopoverBorder = "#334251",
            SectionBackground = "#16202a",
            SectionMutedBackground = "#1a2530",
            Accent = "#2e7be6"
        };
    }
}
