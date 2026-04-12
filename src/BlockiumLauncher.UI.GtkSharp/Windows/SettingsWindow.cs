using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Shared.Results;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class SettingsWindow : Gtk.Window
{
    private readonly LauncherUiPreferencesService uiPreferences;
    private readonly ILauncherPaths launcherPaths;
    private readonly GetManagedJavaRuntimeSlotsUseCase getManagedJavaRuntimeSlotsUseCase;
    private readonly InstallManagedJavaRuntimeUseCase installManagedJavaRuntimeUseCase;
    private readonly GetLauncherRuntimeSettingsUseCase getLauncherRuntimeSettingsUseCase;
    private readonly SaveLauncherRuntimeSettingsUseCase saveLauncherRuntimeSettingsUseCase;

    private readonly Button themeSelectorButton = new();
    private readonly Button themeReloadButton = new("Reload");
    private readonly Button saveButton = new("Save");
    private readonly Button cancelButton = new("Cancel");
    private readonly Stack contentStack = new() { TransitionType = StackTransitionType.None, Hexpand = true, Vexpand = true };
    private readonly Box javaSlotsBox = new(Orientation.Vertical, 8);
    private readonly Label javaStatusLabel = new() { Xalign = 0, Wrap = true };
    private readonly Button javaRefreshButton = new("Refresh");
    private readonly CheckButton skipCompatibilityChecksButton = new("Skip compatibility checks");
    private readonly SpinButton minMemorySpinButton = new(new Adjustment(2048, 512, 131072, 256, 1024, 0), 1, 0);
    private readonly SpinButton maxMemorySpinButton = new(new Adjustment(4096, 512, 131072, 256, 1024, 0), 1, 0);
    private readonly Dictionary<string, RadioButton> themeOptionButtons = new(StringComparer.OrdinalIgnoreCase);

    private string draftThemeId = "light";
    private string loadedThemeId = "light";
    private LauncherRuntimeSettings loadedRuntimeSettings = LauncherRuntimeSettings.CreateDefault();
    private LauncherRuntimeSettings draftRuntimeSettings = LauncherRuntimeSettings.CreateDefault();
    private bool suppressRuntimeEvents;
    private bool isSaving;
    private bool isInstallingJava;
    private Popover? themeSelectorPopover;

    public SettingsWindow(
        LauncherUiPreferencesService uiPreferences,
        ILauncherPaths launcherPaths,
        GetManagedJavaRuntimeSlotsUseCase getManagedJavaRuntimeSlotsUseCase,
        InstallManagedJavaRuntimeUseCase installManagedJavaRuntimeUseCase,
        GetLauncherRuntimeSettingsUseCase getLauncherRuntimeSettingsUseCase,
        SaveLauncherRuntimeSettingsUseCase saveLauncherRuntimeSettingsUseCase) : base("Settings")
    {
        this.uiPreferences = uiPreferences;
        this.launcherPaths = launcherPaths;
        this.getManagedJavaRuntimeSlotsUseCase = getManagedJavaRuntimeSlotsUseCase;
        this.installManagedJavaRuntimeUseCase = installManagedJavaRuntimeUseCase;
        this.getLauncherRuntimeSettingsUseCase = getLauncherRuntimeSettingsUseCase;
        this.saveLauncherRuntimeSettingsUseCase = saveLauncherRuntimeSettingsUseCase;

        SetDefaultSize(920, 640);
        Resizable = true;
        WindowPosition = WindowPosition.Center;
        DeleteEvent += (_, args) => { args.RetVal = true; CloseWindow(); };
        this.uiPreferences.Changed += HandlePreferencesChanged;
        Destroyed += (_, _) =>
        {
            this.uiPreferences.Changed -= HandlePreferencesChanged;
            LauncherWindowMemory.RequestAggressiveCleanup();
        };

        javaRefreshButton.StyleContext.AddClass("action-button");
        javaRefreshButton.Clicked += async (_, _) => await RefreshJavaSlotsAsync().ConfigureAwait(false);
        skipCompatibilityChecksButton.Toggled += (_, _) => UpdateRuntimeDraftFromControls();
        minMemorySpinButton.StyleContext.AddClass("app-field");
        maxMemorySpinButton.StyleContext.AddClass("app-field");
        minMemorySpinButton.ValueChanged += (_, _) => UpdateRuntimeDraftFromControls();
        maxMemorySpinButton.ValueChanged += (_, _) => UpdateRuntimeDraftFromControls();

        Titlebar = LauncherGtkChrome.CreateHeaderBar("Settings", string.Empty, allowWindowControls: true);
        Add(BuildRoot());
        LoadDraftFromPreferences();
        ApplyRuntimeSettingsToControls();
        UpdateFooterButtons();
    }

    public void PresentFrom(Gtk.Window owner)
    {
        TransientFor = owner;
        LoadDraftFromPreferences();
        ShowAll();
        Present();
        _ = RefreshRuntimePageAsync();
    }

    private Widget BuildRoot()
    {
        var root = new EventBox();
        root.StyleContext.AddClass("settings-shell");
        var layout = new Box(Orientation.Vertical, 0);
        layout.PackStart(BuildBody(), true, true, 0);
        layout.PackStart(BuildFooter(), false, false, 0);
        root.Add(layout);
        return root;
    }

    private Widget BuildBody()
    {
        var body = new Box(Orientation.Horizontal, 0);
        body.StyleContext.AddClass("settings-body");
        body.PackStart(BuildNavigation(), false, false, 0);
        body.PackStart(BuildContentShell(), true, true, 0);
        return body;
    }

    private Widget BuildNavigation()
    {
        var shell = new EventBox { WidthRequest = 190, Hexpand = false, Vexpand = true };
        shell.StyleContext.AddClass("settings-nav-shell");
        var navList = new ListBox { SelectionMode = SelectionMode.Single };
        navList.StyleContext.AddClass("settings-nav-list");
        navList.RowSelected += (_, args) =>
        {
            if (args.Row?.Name is { Length: > 0 } sectionName)
            {
                contentStack.VisibleChildName = sectionName;
            }
        };
        navList.Add(CreateNavigationRow("general", "General"));
        navList.Add(CreateNavigationRow("paths", "Paths"));
        navList.Add(CreateNavigationRow("java", "Java"));
        var container = new Box(Orientation.Vertical, 0) { MarginTop = 10, MarginBottom = 10, MarginStart = 10, MarginEnd = 10 };
        container.PackStart(navList, false, false, 0);
        container.PackStart(new Label { Vexpand = true }, true, true, 0);
        shell.Add(container);
        if (navList.GetRowAtIndex(0) is ListBoxRow firstRow)
        {
            navList.SelectRow(firstRow);
            contentStack.VisibleChildName = firstRow.Name;
        }
        return shell;
    }

    private Widget BuildContentShell()
    {
        var shell = new EventBox { Hexpand = true, Vexpand = true };
        shell.StyleContext.AddClass("settings-content-shell");
        contentStack.AddNamed(BuildGeneralPage(), "general");
        contentStack.AddNamed(BuildPathsPage(), "paths");
        contentStack.AddNamed(BuildJavaPage(), "java");
        shell.Add(contentStack);
        return shell;
    }

    private Widget BuildGeneralPage()
    {
        themeSelectorButton.StyleContext.AddClass("popover-menu-button");
        themeReloadButton.StyleContext.AddClass("action-button");
        themeSelectorButton.Clicked += (_, _) => { var popover = GetOrCreateThemeSelectorPopover(); popover.ShowAll(); popover.Popup(); };
        themeReloadButton.Clicked += async (_, _) => await ReloadThemesAsync().ConfigureAwait(false);
        UpdateThemeSelectorButton();
        var editorRow = new Box(Orientation.Horizontal, 8);
        editorRow.PackStart(themeSelectorButton, false, false, 0);
        editorRow.PackStart(themeReloadButton, false, false, 0);
        var card = CreateCard("Choose the launcher color scheme.");
        card.PackStart(CreateSettingEditorRow("Theme", editorRow), false, false, 0);
        return WrapPage("General", card);
    }

    private Widget BuildPathsPage()
    {
        var card = CreateCard(string.Empty);
        card.PackStart(CreatePathRow("Launcher root", launcherPaths.RootDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Data directory", launcherPaths.DataDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Instances directory", launcherPaths.InstancesDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Java directory", launcherPaths.ManagedJavaDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Logs directory", launcherPaths.LogsDirectory), false, false, 0);
        return WrapPage("Paths", card);
    }

    private Widget BuildJavaPage()
    {
        javaStatusLabel.StyleContext.AddClass("settings-caption");
        var runtimesHeader = new Box(Orientation.Horizontal, 8);
        runtimesHeader.PackStart(new Label("Launcher-managed Adoptium runtimes only") { Xalign = 0, Hexpand = true }, true, true, 0);
        runtimesHeader.PackStart(javaRefreshButton, false, false, 0);
        var runtimesCard = CreateCard(string.Empty);
        runtimesCard.PackStart(runtimesHeader, false, false, 0);
        runtimesCard.PackStart(javaSlotsBox, false, false, 0);
        runtimesCard.PackStart(javaStatusLabel, false, false, 0);
        var defaultsCard = CreateCard("These values are used for newly created and imported instances.");
        defaultsCard.PackStart(skipCompatibilityChecksButton, false, false, 0);
        defaultsCard.PackStart(CreateSettingEditorRow("Min memory (MB)", minMemorySpinButton), false, false, 0);
        defaultsCard.PackStart(CreateSettingEditorRow("Max memory (MB)", maxMemorySpinButton), false, false, 0);
        var page = new Box(Orientation.Vertical, 10) { MarginTop = 14, MarginBottom = 14, MarginStart = 14, MarginEnd = 14 };
        page.StyleContext.AddClass("settings-page");
        var titleLabel = new Label("Java") { Xalign = 0 };
        titleLabel.StyleContext.AddClass("settings-page-title");
        page.PackStart(titleLabel, false, false, 0);
        page.PackStart(runtimesCard, false, false, 0);
        page.PackStart(defaultsCard, false, false, 0);
        page.PackStart(new Label { Vexpand = true }, true, true, 0);
        var scroller = new ScrolledWindow { Hexpand = true, Vexpand = true };
        scroller.StyleContext.AddClass("settings-page-scroller");
        scroller.Add(page);
        return scroller;
    }

    private Widget BuildFooter()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-footer");
        saveButton.StyleContext.AddClass("action-button");
        saveButton.StyleContext.AddClass("primary-button");
        saveButton.Clicked += async (_, _) => await SaveAsync().ConfigureAwait(false);
        cancelButton.StyleContext.AddClass("action-button");
        cancelButton.Clicked += (_, _) => CloseWindow();
        var content = new Box(Orientation.Horizontal, 8) { MarginTop = 10, MarginBottom = 10, MarginStart = 14, MarginEnd = 14 };
        content.PackStart(new Label { Hexpand = true }, true, true, 0);
        content.PackStart(cancelButton, false, false, 0);
        content.PackStart(saveButton, false, false, 0);
        shell.Add(content);
        return shell;
    }

    private static ListBoxRow CreateNavigationRow(string name, string text)
    {
        var row = new ListBoxRow { Name = name, Selectable = true, Activatable = true };
        var outer = new Box(Orientation.Horizontal, 0) { HeightRequest = 38, Hexpand = true };
        var accent = new EventBox { WidthRequest = 4 };
        accent.StyleContext.AddClass("settings-nav-accent");
        var body = new Box(Orientation.Horizontal, 0) { MarginStart = 14, MarginEnd = 14, Halign = Align.Fill, Valign = Align.Center, Hexpand = true };
        body.StyleContext.AddClass("settings-nav-row-body");
        var label = new Label(text) { Xalign = 0 };
        label.StyleContext.AddClass("settings-nav-text");
        body.PackStart(label, true, true, 0);
        outer.PackStart(accent, false, false, 0);
        outer.PackStart(body, true, true, 0);
        row.Add(outer);
        return row;
    }

    private static Widget WrapPage(string title, Widget content)
    {
        var page = new Box(Orientation.Vertical, 10) { MarginTop = 14, MarginBottom = 14, MarginStart = 14, MarginEnd = 14 };
        page.StyleContext.AddClass("settings-page");
        var titleLabel = new Label(title) { Xalign = 0 };
        titleLabel.StyleContext.AddClass("settings-page-title");
        page.PackStart(titleLabel, false, false, 0);
        page.PackStart(content, false, false, 0);
        page.PackStart(new Label { Vexpand = true }, true, true, 0);
        var scroller = new ScrolledWindow { Hexpand = true, Vexpand = true };
        scroller.StyleContext.AddClass("settings-page-scroller");
        scroller.Add(page);
        return scroller;
    }

    private static Box CreateCard(string caption)
    {
        var card = new Box(Orientation.Vertical, 0);
        card.StyleContext.AddClass("settings-card");
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var description = new Label(caption) { Xalign = 0, LineWrap = true };
            description.StyleContext.AddClass("settings-caption");
            description.MarginBottom = 6;
            card.PackStart(description, false, false, 0);
        }
        return card;
    }

    private static Widget CreateSettingEditorRow(string labelText, Widget editor)
    {
        var row = new Box(Orientation.Horizontal, 16) { MarginTop = 8 };
        row.StyleContext.AddClass("settings-row");
        var label = new Label(labelText) { Xalign = 0 };
        label.StyleContext.AddClass("settings-row-label");
        row.PackStart(label, false, false, 0);
        row.PackStart(new Label { Hexpand = true }, true, true, 0);
        row.PackStart(editor, false, false, 0);
        return row;
    }

    private Widget CreatePathRow(string labelText, string path)
    {
        var row = new Box(Orientation.Horizontal, 12) { MarginTop = 8 };
        row.StyleContext.AddClass("settings-row");
        var labels = new Box(Orientation.Vertical, 4) { Hexpand = true };
        var label = new Label(labelText) { Xalign = 0 };
        label.StyleContext.AddClass("settings-row-label");
        var value = new Label(path) { Xalign = 0, LineWrap = true, Selectable = true };
        value.StyleContext.AddClass("settings-caption");
        labels.PackStart(label, false, false, 0);
        labels.PackStart(value, false, false, 0);
        var openButton = new Button("Open");
        openButton.StyleContext.AddClass("action-button");
        openButton.Clicked += (_, _) => { try { DesktopShell.OpenDirectory(path); } catch (Exception ex) { ShowError("Unable to open folder", ex.Message); } };
        row.PackStart(labels, true, true, 0);
        row.PackStart(openButton, false, false, 0);
        return row;
    }

    private void LoadDraftFromPreferences() { loadedThemeId = uiPreferences.CurrentThemeId; draftThemeId = loadedThemeId; UpdateThemeSelectorButton(); }

    private Popover GetOrCreateThemeSelectorPopover()
    {
        if (themeSelectorPopover is not null) return themeSelectorPopover;
        themeSelectorPopover = new Popover(themeSelectorButton) { BorderWidth = 0 };
        var content = new Box(Orientation.Vertical, 8) { MarginTop = 10, MarginBottom = 10, MarginStart = 10, MarginEnd = 10 };
        content.StyleContext.AddClass("popover-content");
        var title = new Label("Theme") { Xalign = 0 };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);
        themeOptionButtons.Clear();
        RadioButton? group = null;
        foreach (var option in uiPreferences.AvailableThemes)
        {
            var radio = group is null ? new RadioButton(option.DisplayName) : new RadioButton(group, option.DisplayName);
            group ??= radio;
            radio.Active = string.Equals(draftThemeId, option.Id, StringComparison.OrdinalIgnoreCase);
            radio.StyleContext.AddClass("popover-check");
            themeOptionButtons[option.Id] = radio;
            radio.Toggled += (_, _) =>
            {
                if (!radio.Active) return;
                draftThemeId = option.Id;
                UpdateThemeSelectorButton();
                UpdateFooterButtons();
                themeSelectorPopover?.Popdown();
            };
            content.PackStart(radio, false, false, 0);
        }
        themeSelectorPopover.Add(content);
        return themeSelectorPopover;
    }

    private void UpdateThemeSelectorButton()
    {
        var selectedTheme = uiPreferences.AvailableThemes.FirstOrDefault(theme => string.Equals(theme.Id, draftThemeId, StringComparison.OrdinalIgnoreCase))
            ?? uiPreferences.AvailableThemes.FirstOrDefault();
        themeSelectorButton.Label = selectedTheme?.DisplayName ?? "Select theme";
        foreach (var entry in themeOptionButtons)
        {
            entry.Value.Active = string.Equals(entry.Key, draftThemeId, StringComparison.OrdinalIgnoreCase);
        }
    }

    private async Task RefreshRuntimePageAsync()
    {
        try
        {
            var settings = await getLauncherRuntimeSettingsUseCase.ExecuteAsync().ConfigureAwait(false);
            Gtk.Application.Invoke((_, _) =>
            {
                loadedRuntimeSettings = settings.Normalize();
                draftRuntimeSettings = CloneRuntimeSettings(loadedRuntimeSettings.DefaultJavaMajor, loadedRuntimeSettings.SkipCompatibilityChecks, loadedRuntimeSettings.DefaultMinMemoryMb, loadedRuntimeSettings.DefaultMaxMemoryMb);
                ApplyRuntimeSettingsToControls();
                UpdateFooterButtons();
            });
        }
        catch (Exception ex)
        {
            Gtk.Application.Invoke((_, _) => javaStatusLabel.Text = ex.Message);
        }

        await RefreshJavaSlotsAsync().ConfigureAwait(false);
    }

    private async Task RefreshJavaSlotsAsync()
    {
        Gtk.Application.Invoke((_, _) => { javaRefreshButton.Sensitive = false; javaStatusLabel.Text = "Loading launcher-managed Java runtimes..."; });
        Result<IReadOnlyList<ManagedJavaRuntimeSlotSummary>> result;
        try
        {
            result = await getManagedJavaRuntimeSlotsUseCase.ExecuteAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Gtk.Application.Invoke((_, _) => { javaStatusLabel.Text = ex.Message; javaRefreshButton.Sensitive = true; });
            return;
        }

        Gtk.Application.Invoke((_, _) =>
        {
            foreach (var child in javaSlotsBox.Children.ToArray())
            {
                javaSlotsBox.Remove(child);
                child.Destroy();
            }

            if (result.IsFailure)
            {
                javaStatusLabel.Text = result.Error.Message;
                javaRefreshButton.Sensitive = true;
                return;
            }

            foreach (var slot in result.Value.OrderByDescending(static slot => slot.JavaMajor))
            {
                javaSlotsBox.PackStart(BuildJavaSlotRow(slot), false, false, 0);
            }

            javaSlotsBox.ShowAll();
            javaStatusLabel.Text = "Only launcher-managed Adoptium runtimes are shown here.";
            javaRefreshButton.Sensitive = true;
        });
    }

    private Widget BuildJavaSlotRow(ManagedJavaRuntimeSlotSummary slot)
    {
        var shell = new Box(Orientation.Vertical, 8) { MarginTop = 8 };
        shell.StyleContext.AddClass("settings-row");
        var header = new Box(Orientation.Horizontal, 10);
        var titleColumn = new Box(Orientation.Vertical, 4) { Hexpand = true };
        var title = new Label(slot.DisplayName) { Xalign = 0 };
        title.StyleContext.AddClass("settings-row-label");
        var details = new Label(BuildJavaSlotDetails(slot)) { Xalign = 0, Wrap = true, Selectable = true };
        details.StyleContext.AddClass("settings-caption");
        titleColumn.PackStart(title, false, false, 0);
        titleColumn.PackStart(details, false, false, 0);
        var buttons = new Box(Orientation.Horizontal, 8) { Halign = Align.End };
        var installButton = new Button(slot.IsInstalled ? "Reinstall" : "Install");
        installButton.StyleContext.AddClass("action-button");
        installButton.Clicked += async (_, _) => await InstallJavaSlotAsync(slot.JavaMajor, slot.IsInstalled).ConfigureAwait(false);
        var isDefault = slot.JavaMajor == draftRuntimeSettings.DefaultJavaMajor;
        var defaultButton = new Button(isDefault ? "Default" : "Set as default") { Sensitive = !isDefault };
        defaultButton.StyleContext.AddClass(isDefault ? "primary-button" : "action-button");
        defaultButton.Clicked += (_, _) =>
        {
            draftRuntimeSettings = CloneRuntimeSettings(slot.JavaMajor, draftRuntimeSettings.SkipCompatibilityChecks, draftRuntimeSettings.DefaultMinMemoryMb, draftRuntimeSettings.DefaultMaxMemoryMb);
            UpdateFooterButtons();
            _ = RefreshJavaSlotsAsync();
        };
        buttons.PackStart(installButton, false, false, 0);
        buttons.PackStart(defaultButton, false, false, 0);
        header.PackStart(titleColumn, true, true, 0);
        header.PackStart(buttons, false, false, 0);
        shell.PackStart(header, false, false, 0);
        return shell;
    }

    private async Task InstallJavaSlotAsync(int javaMajor, bool forceReinstall)
    {
        if (isInstallingJava) return;
        isInstallingJava = true;
        Gtk.Application.Invoke((_, _) =>
        {
            javaStatusLabel.Text = $"{(forceReinstall ? "Reinstalling" : "Installing")} Java {javaMajor} from Adoptium...";
            javaRefreshButton.Sensitive = false;
            UpdateFooterButtons();
        });
        try
        {
            var result = await installManagedJavaRuntimeUseCase.ExecuteAsync(new InstallManagedJavaRuntimeRequest { JavaMajor = javaMajor, ForceReinstall = forceReinstall }).ConfigureAwait(false);
            Gtk.Application.Invoke((_, _) =>
            {
                if (result.IsFailure) ShowError("Unable to install Java", result.Error.Message);
                else javaStatusLabel.Text = $"Java {javaMajor} is ready at {result.Value.ExecutablePath}.";
            });
        }
        catch (Exception ex)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to install Java", ex.Message));
        }
        finally
        {
            isInstallingJava = false;
            await RefreshJavaSlotsAsync().ConfigureAwait(false);
            Gtk.Application.Invoke((_, _) => UpdateFooterButtons());
        }
    }

    private async Task SaveAsync()
    {
        if (!HasThemeChanges() && !HasRuntimeChanges()) return;
        isSaving = true;
        UpdateFooterButtons();
        try
        {
            if (HasThemeChanges()) await uiPreferences.SetThemeAsync(draftThemeId).ConfigureAwait(false);
            if (HasRuntimeChanges()) await saveLauncherRuntimeSettingsUseCase.ExecuteAsync(draftRuntimeSettings.Normalize()).ConfigureAwait(false);
            Gtk.Application.Invoke((_, _) =>
            {
                loadedThemeId = draftThemeId;
                loadedRuntimeSettings = draftRuntimeSettings.Normalize();
                draftRuntimeSettings = CloneRuntimeSettings(loadedRuntimeSettings.DefaultJavaMajor, loadedRuntimeSettings.SkipCompatibilityChecks, loadedRuntimeSettings.DefaultMinMemoryMb, loadedRuntimeSettings.DefaultMaxMemoryMb);
                ApplyRuntimeSettingsToControls();
                UpdateFooterButtons();
            });
        }
        catch (Exception ex)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to save settings", ex.Message));
        }
        finally
        {
            isSaving = false;
            Gtk.Application.Invoke((_, _) => UpdateFooterButtons());
        }
    }

    private async Task ReloadThemesAsync()
    {
        try
        {
            await uiPreferences.ReloadThemesAsync().ConfigureAwait(false);
            Gtk.Application.Invoke((_, _) =>
            {
                themeSelectorPopover?.Destroy();
                themeSelectorPopover = null;
                if (!uiPreferences.AvailableThemes.Any(theme => string.Equals(theme.Id, draftThemeId, StringComparison.OrdinalIgnoreCase)))
                {
                    loadedThemeId = uiPreferences.CurrentThemeId;
                    draftThemeId = loadedThemeId;
                }
                UpdateThemeSelectorButton();
                UpdateFooterButtons();
            });
        }
        catch (Exception ex)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to reload themes", ex.Message));
        }
    }

    private void UpdateRuntimeDraftFromControls()
    {
        if (suppressRuntimeEvents) return;
        draftRuntimeSettings = CloneRuntimeSettings(draftRuntimeSettings.DefaultJavaMajor, skipCompatibilityChecksButton.Active, (int)minMemorySpinButton.Value, (int)maxMemorySpinButton.Value);
        UpdateFooterButtons();
    }

    private void ApplyRuntimeSettingsToControls()
    {
        suppressRuntimeEvents = true;
        try
        {
            skipCompatibilityChecksButton.Active = draftRuntimeSettings.SkipCompatibilityChecks;
            minMemorySpinButton.Value = draftRuntimeSettings.DefaultMinMemoryMb;
            maxMemorySpinButton.Value = draftRuntimeSettings.DefaultMaxMemoryMb;
        }
        finally
        {
            suppressRuntimeEvents = false;
        }
    }

    private void PrepareForClose()
    {
        themeSelectorPopover?.Popdown();
        LoadDraftFromPreferences();
        draftRuntimeSettings = CloneRuntimeSettings(loadedRuntimeSettings.DefaultJavaMajor, loadedRuntimeSettings.SkipCompatibilityChecks, loadedRuntimeSettings.DefaultMinMemoryMb, loadedRuntimeSettings.DefaultMaxMemoryMb);
        ApplyRuntimeSettingsToControls();
        UpdateFooterButtons();
    }

    private void CloseWindow()
    {
        PrepareForClose();
        Destroy();
    }

    private void HandlePreferencesChanged(object? sender, EventArgs e)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            if (!HasThemeChanges())
            {
                LoadDraftFromPreferences();
            }
        });
    }

    private void UpdateFooterButtons()
    {
        var hasUnsavedChanges = HasThemeChanges() || HasRuntimeChanges();
        saveButton.Sensitive = !isSaving && !isInstallingJava && hasUnsavedChanges;
        cancelButton.Sensitive = !isSaving && !isInstallingJava;
    }

    private bool HasThemeChanges() => !string.Equals(draftThemeId, loadedThemeId, StringComparison.OrdinalIgnoreCase);

    private bool HasRuntimeChanges()
    {
        return draftRuntimeSettings.DefaultJavaMajor != loadedRuntimeSettings.DefaultJavaMajor ||
               draftRuntimeSettings.SkipCompatibilityChecks != loadedRuntimeSettings.SkipCompatibilityChecks ||
               draftRuntimeSettings.DefaultMinMemoryMb != loadedRuntimeSettings.DefaultMinMemoryMb ||
               draftRuntimeSettings.DefaultMaxMemoryMb != loadedRuntimeSettings.DefaultMaxMemoryMb;
    }

    private static LauncherRuntimeSettings CloneRuntimeSettings(int defaultJavaMajor, bool skipCompatibilityChecks, int minMemoryMb, int maxMemoryMb)
    {
        return new LauncherRuntimeSettings
        {
            DefaultJavaMajor = defaultJavaMajor,
            SkipCompatibilityChecks = skipCompatibilityChecks,
            DefaultMinMemoryMb = minMemoryMb,
            DefaultMaxMemoryMb = maxMemoryMb
        }.Normalize();
    }

    private static string BuildJavaSlotDetails(ManagedJavaRuntimeSlotSummary slot)
    {
        if (!slot.IsInstalled) return "Not installed in the launcher yet.";
        var parts = new List<string>();
        if (!slot.IsValid) parts.Add("Installed but failed validation");
        if (!string.IsNullOrWhiteSpace(slot.Version)) parts.Add(slot.Version);
        if (!string.IsNullOrWhiteSpace(slot.Vendor)) parts.Add(slot.Vendor);
        if (!string.IsNullOrWhiteSpace(slot.ExecutablePath)) parts.Add(slot.ExecutablePath);
        return parts.Count == 0 ? "Installed in launcher storage." : string.Join("  •  ", parts);
    }

    private void ShowError(string title, string message)
    {
        LauncherGtkChrome.ShowMessage(this, title, message, MessageType.Error);
    }
}
