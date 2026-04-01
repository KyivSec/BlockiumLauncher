using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class SettingsWindow : Gtk.Window
{
    private readonly LauncherUiPreferencesService UiPreferences;
    private readonly ILauncherPaths LauncherPaths;
    private readonly DiscoverJavaUseCase DiscoverJavaUseCase;
    private readonly GetCurseForgeApiKeyStatusUseCase GetCurseForgeApiKeyStatusUseCase;
    private readonly ConfigureCurseForgeApiKeyUseCase ConfigureCurseForgeApiKeyUseCase;
    private readonly ClearCurseForgeApiKeyUseCase ClearCurseForgeApiKeyUseCase;

    private readonly Button ThemeSelectorButton = new();
    private readonly Button SaveButton = new("Save");
    private readonly Button CancelButton = new("Cancel");
    private readonly Stack ContentStack = new()
    {
        TransitionType = StackTransitionType.None,
        Hexpand = true,
        Vexpand = true
    };
    private readonly ListBox JavaList = new()
    {
        SelectionMode = SelectionMode.None
    };
    private readonly Label JavaStatusLabel = new()
    {
        Xalign = 0
    };
    private readonly Button JavaRescanButton = new("Rescan");
    private readonly Label CatalogBackendValue = new() { Xalign = 1 };
    private readonly Label CatalogPersistValue = new() { Xalign = 1 };
    private readonly Label CatalogEnvironmentValue = new() { Xalign = 1 };
    private readonly Label CatalogSecureStoreValue = new() { Xalign = 1 };
    private readonly Label CatalogEffectiveValue = new() { Xalign = 1 };

    private LauncherThemePreference DraftThemePreference;
    private bool HasUnsavedThemeChanges;
    private Popover? ThemeSelectorPopover;
    private RadioButton? LightThemeRadioButton;
    private RadioButton? DarkThemeRadioButton;

    public SettingsWindow(
        LauncherUiPreferencesService uiPreferences,
        ILauncherPaths launcherPaths,
        DiscoverJavaUseCase discoverJavaUseCase,
        GetCurseForgeApiKeyStatusUseCase getCurseForgeApiKeyStatusUseCase,
        ConfigureCurseForgeApiKeyUseCase configureCurseForgeApiKeyUseCase,
        ClearCurseForgeApiKeyUseCase clearCurseForgeApiKeyUseCase) : base("Settings")
    {
        UiPreferences = uiPreferences ?? throw new ArgumentNullException(nameof(uiPreferences));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        DiscoverJavaUseCase = discoverJavaUseCase ?? throw new ArgumentNullException(nameof(discoverJavaUseCase));
        GetCurseForgeApiKeyStatusUseCase = getCurseForgeApiKeyStatusUseCase ?? throw new ArgumentNullException(nameof(getCurseForgeApiKeyStatusUseCase));
        ConfigureCurseForgeApiKeyUseCase = configureCurseForgeApiKeyUseCase ?? throw new ArgumentNullException(nameof(configureCurseForgeApiKeyUseCase));
        ClearCurseForgeApiKeyUseCase = clearCurseForgeApiKeyUseCase ?? throw new ArgumentNullException(nameof(clearCurseForgeApiKeyUseCase));

        SetDefaultSize(920, 640);
        Resizable = true;
        WindowPosition = WindowPosition.Center;

        DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            PrepareForHide();
            Hide();
        };

        UiPreferences.Changed += HandlePreferencesChanged;
        Destroyed += (_, _) => UiPreferences.Changed -= HandlePreferencesChanged;

        Titlebar = BuildHeaderBar();
        Add(BuildRoot());
        LoadDraftFromPreferences();
        RefreshCatalogStatus();
    }

    public void PresentFrom(Gtk.Window owner)
    {
        if (!Visible)
        {
            LoadDraftFromPreferences();
        }

        ShowAll();
        Present();
        RefreshCatalogStatus();
        _ = RefreshJavaAsync();
    }

    private void PrepareForHide()
    {
        ThemeSelectorPopover?.Popdown();
        LoadDraftFromPreferences();
    }

    private void HandlePreferencesChanged(object? sender, EventArgs e)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            if (!HasUnsavedThemeChanges)
            {
                LoadDraftFromPreferences();
            }
        });
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

    private Widget BuildHeaderBar()
    {
        var bar = new HeaderBar
        {
            ShowCloseButton = true,
            HasSubtitle = false,
            DecorationLayout = ":minimize,maximize,close"
        };
        bar.StyleContext.AddClass("topbar-shell");

        var content = new Box(Orientation.Vertical, 2)
        {
            Halign = Align.Start
        };
        content.StyleContext.AddClass("topbar-content");

        var title = new Label("Settings")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-title");

        var subtitle = new Label("Launcher appearance and system configuration.")
        {
            Xalign = 0
        };
        subtitle.StyleContext.AddClass("settings-subtitle");

        content.PackStart(title, false, false, 0);
        content.PackStart(subtitle, false, false, 0);
        bar.PackStart(content);
        return bar;
    }

    private Widget BuildBody()
    {
        var body = new Box(Orientation.Horizontal, 0);
        body.StyleContext.AddClass("settings-body");
        var contentShell = BuildContentShell();
        var navigation = BuildNavigation();
        body.PackStart(navigation, false, false, 0);
        body.PackStart(contentShell, true, true, 0);
        return body;
    }

    private Widget BuildNavigation()
    {
        var shell = new EventBox
        {
            WidthRequest = 190,
            Hexpand = false,
            Vexpand = true
        };
        shell.StyleContext.AddClass("settings-nav-shell");

        var navList = new ListBox
        {
            SelectionMode = SelectionMode.Single
        };
        navList.StyleContext.AddClass("settings-nav-list");
        navList.RowSelected += (_, args) =>
        {
            if (args.Row is not null && args.Row.Name is { Length: > 0 } sectionName)
            {
                ContentStack.VisibleChildName = sectionName;
            }
        };

        navList.Add(CreateNavigationRow("general", "General"));
        navList.Add(CreateNavigationRow("paths", "Paths"));
        navList.Add(CreateNavigationRow("java", "Java"));
        navList.Add(CreateNavigationRow("catalog", "Catalog"));

        var container = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };
        container.PackStart(navList, false, false, 0);
        container.PackStart(new Label { Vexpand = true }, true, true, 0);

        shell.Add(container);

        if (navList.GetRowAtIndex(0) is ListBoxRow firstRow)
        {
            navList.SelectRow(firstRow);
            ContentStack.VisibleChildName = firstRow.Name;
        }

        return shell;
    }

    private static ListBoxRow CreateNavigationRow(string name, string text)
    {
        var row = new ListBoxRow
        {
            Name = name,
            Selectable = true,
            Activatable = true
        };

        var outer = new Box(Orientation.Horizontal, 0)
        {
            HeightRequest = 44,
            Hexpand = true
        };

        var accent = new EventBox
        {
            WidthRequest = 4
        };
        accent.StyleContext.AddClass("settings-nav-accent");

        var body = new Box(Orientation.Horizontal, 0)
        {
            MarginStart = 14,
            MarginEnd = 14,
            Halign = Align.Fill,
            Valign = Align.Center,
            Hexpand = true
        };
        body.StyleContext.AddClass("settings-nav-row-body");

        var label = new Label(text)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("settings-nav-text");

        body.PackStart(label, true, true, 0);
        outer.PackStart(accent, false, false, 0);
        outer.PackStart(body, true, true, 0);
        row.Add(outer);
        return row;
    }

    private Widget BuildContentShell()
    {
        var shell = new EventBox
        {
            Hexpand = true,
            Vexpand = true
        };
        shell.StyleContext.AddClass("settings-content-shell");

        ContentStack.AddNamed(BuildGeneralPage(), "general");
        ContentStack.AddNamed(BuildPathsPage(), "paths");
        ContentStack.AddNamed(BuildJavaPage(), "java");
        ContentStack.AddNamed(BuildCatalogPage(), "catalog");

        shell.Add(ContentStack);
        return shell;
    }

    private Widget BuildGeneralPage()
    {
        ThemeSelectorButton.StyleContext.AddClass("popover-menu-button");
        UpdateThemeSelectorButton();
        ThemeSelectorButton.Clicked += (_, _) =>
        {
            var popover = GetOrCreateThemeSelectorPopover();
            popover.ShowAll();
            popover.Popup();
        };

        var card = CreateCard("Choose the launcher color scheme.");
        card.PackStart(CreateSettingEditorRow("Theme", ThemeSelectorButton), false, false, 0);

        return WrapPage(
            "General",
            "Appearance preferences for the launcher shell.",
            card);
    }

    private Widget BuildPathsPage()
    {
        var card = CreateCard("Current launcher directories. These values are read from the active launcher pathing service.");
        card.PackStart(CreatePathRow("Launcher root", LauncherPaths.RootDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Data directory", LauncherPaths.DataDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Instances directory", LauncherPaths.InstancesDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Java directory", LauncherPaths.ManagedJavaDirectory), false, false, 0);
        card.PackStart(CreatePathRow("Logs directory", LauncherPaths.LogsDirectory), false, false, 0);

        return WrapPage(
            "Paths",
            "Quick access to launcher-managed folders.",
            card);
    }

    private Widget BuildJavaPage()
    {
        JavaStatusLabel.StyleContext.AddClass("settings-caption");

        JavaRescanButton.StyleContext.AddClass("action-button");
        JavaRescanButton.Clicked += async (_, _) => await RefreshJavaAsync().ConfigureAwait(false);

        var headerRow = new Box(Orientation.Horizontal, 8);
        headerRow.PackStart(JavaStatusLabel, true, true, 0);
        headerRow.PackStart(JavaRescanButton, false, false, 0);

        JavaList.StyleContext.AddClass("settings-java-list");

        var scroller = new ScrolledWindow
        {
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("settings-java-scroller");
        scroller.Add(JavaList);

        var card = CreateCard("Detected Java installations available to the launcher.");
        card.PackStart(headerRow, false, false, 0);
        card.PackStart(scroller, true, true, 12);

        return WrapPage(
            "Java",
            "Discover and validate runtimes without selecting a preferred default yet.",
            card,
            fillCard: true);
    }

    private Widget BuildCatalogPage()
    {
        var card = CreateCard("CurseForge API key status and secure-store details.");
        card.PackStart(CreateStatusRow("Backend", CatalogBackendValue), false, false, 0);
        card.PackStart(CreateStatusRow("Can persist secrets", CatalogPersistValue), false, false, 0);
        card.PackStart(CreateStatusRow("Environment variable", CatalogEnvironmentValue), false, false, 0);
        card.PackStart(CreateStatusRow("Secure store value", CatalogSecureStoreValue), false, false, 0);
        card.PackStart(CreateStatusRow("Effective source", CatalogEffectiveValue), false, false, 0);

        var note = new Label("Environment variables override the secure store when both are present.")
        {
            Xalign = 0
        };
        note.StyleContext.AddClass("settings-caption");
        note.MarginTop = 10;
        card.PackStart(note, false, false, 0);

        var buttons = new Box(Orientation.Horizontal, 8)
        {
            MarginTop = 14
        };

        var setButton = new Button("Set API key");
        setButton.StyleContext.AddClass("action-button");
        setButton.Clicked += (_, _) => SetApiKey();

        var clearButton = new Button("Clear stored key");
        clearButton.StyleContext.AddClass("action-button");
        clearButton.Clicked += (_, _) => ClearApiKey();

        buttons.PackStart(setButton, false, false, 0);
        buttons.PackStart(clearButton, false, false, 0);
        card.PackStart(buttons, false, false, 0);

        return WrapPage(
            "Catalog",
            "Manage the CurseForge key used by the launcher catalog integration.",
            card);
    }

    private Widget WrapPage(string title, string subtitle, Widget content, bool fillCard = false)
    {
        var page = new Box(Orientation.Vertical, 14)
        {
            MarginTop = 18,
            MarginBottom = 18,
            MarginStart = 18,
            MarginEnd = 18
        };
        page.StyleContext.AddClass("settings-page");

        var titleLabel = new Label(title)
        {
            Xalign = 0
        };
        titleLabel.StyleContext.AddClass("settings-page-title");

        var subtitleLabel = new Label(subtitle)
        {
            Xalign = 0,
            LineWrap = true
        };
        subtitleLabel.StyleContext.AddClass("settings-subtitle");

        page.PackStart(titleLabel, false, false, 0);
        page.PackStart(subtitleLabel, false, false, 0);
        page.PackStart(content, fillCard, fillCard, 0);

        var scroller = new ScrolledWindow
        {
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("settings-page-scroller");
        scroller.Add(page);
        return scroller;
    }

    private static Box CreateCard(string caption)
    {
        var card = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 4,
            MarginBottom = 4,
            MarginStart = 0,
            MarginEnd = 0
        };
        card.StyleContext.AddClass("settings-card");

        var description = new Label(caption)
        {
            Xalign = 0,
            LineWrap = true
        };
        description.StyleContext.AddClass("settings-caption");
        description.MarginBottom = 10;
        card.PackStart(description, false, false, 0);

        return card;
    }

    private static Widget CreateSettingEditorRow(string labelText, Widget editor)
    {
        var row = new Box(Orientation.Horizontal, 16)
        {
            MarginTop = 10
        };
        row.StyleContext.AddClass("settings-row");

        var label = new Label(labelText)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("settings-row-label");

        row.PackStart(label, false, false, 0);
        row.PackStart(editor, false, false, 0);
        return row;
    }

    private Widget CreatePathRow(string labelText, string path)
    {
        var row = new Box(Orientation.Horizontal, 12)
        {
            MarginTop = 10
        };
        row.StyleContext.AddClass("settings-row");

        var labels = new Box(Orientation.Vertical, 4)
        {
            Hexpand = true
        };

        var label = new Label(labelText)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("settings-row-label");

        var value = new Label(path)
        {
            Xalign = 0,
            LineWrap = true,
            Selectable = true
        };
        value.StyleContext.AddClass("settings-caption");

        labels.PackStart(label, false, false, 0);
        labels.PackStart(value, false, false, 0);

        var openButton = new Button("Open");
        openButton.StyleContext.AddClass("action-button");
        openButton.Clicked += (_, _) =>
        {
            try
            {
                DesktopShell.OpenDirectory(path);
            }
            catch (Exception ex)
            {
                ShowError("Unable to open folder", ex.Message);
            }
        };

        row.PackStart(labels, true, true, 0);
        row.PackStart(openButton, false, false, 0);
        return row;
    }

    private static Widget CreateStatusRow(string labelText, Label valueLabel)
    {
        var row = new Box(Orientation.Horizontal, 12)
        {
            MarginTop = 10
        };
        row.StyleContext.AddClass("settings-row");

        var label = new Label(labelText)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("settings-row-label");

        valueLabel.StyleContext.AddClass("settings-row-value");

        row.PackStart(label, false, false, 0);
        row.PackStart(new Label { Hexpand = true }, true, true, 0);
        row.PackStart(valueLabel, false, false, 0);
        return row;
    }

    private Widget BuildFooter()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-footer");

        SaveButton.StyleContext.AddClass("action-button");
        SaveButton.StyleContext.AddClass("primary-button");
        SaveButton.Clicked += async (_, _) => await SaveAsync().ConfigureAwait(false);

        CancelButton.StyleContext.AddClass("action-button");
        CancelButton.Clicked += (_, _) => PrepareForHide();

        var content = new Box(Orientation.Horizontal, 8)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 18,
            MarginEnd = 18
        };
        content.PackStart(new Label { Hexpand = true }, true, true, 0);
        content.PackStart(CancelButton, false, false, 0);
        content.PackStart(SaveButton, false, false, 0);

        shell.Add(content);
        UpdateFooterButtons();
        return shell;
    }

    private void LoadDraftFromPreferences()
    {
        DraftThemePreference = UiPreferences.CurrentThemePreference;
        UpdateThemeSelectorButton();
        HasUnsavedThemeChanges = false;
        UpdateFooterButtons();
    }

    private Popover GetOrCreateThemeSelectorPopover()
    {
        if (ThemeSelectorPopover is not null)
        {
            return ThemeSelectorPopover;
        }

        ThemeSelectorPopover = new Popover(ThemeSelectorButton)
        {
            BorderWidth = 0
        };

        var content = new Box(Orientation.Vertical, 8)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };
        content.StyleContext.AddClass("popover-content");

        var title = new Label("Theme")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);

        RadioButton? group = null;
        foreach (var option in new[]
                 {
                     (LauncherThemePreference.Light, "Light"),
                     (LauncherThemePreference.Dark, "Dark")
                 })
        {
            var radio = group is null ? new RadioButton(option.Item2) : new RadioButton(group, option.Item2);
            group ??= radio;
            radio.Active = DraftThemePreference == option.Item1;
            radio.StyleContext.AddClass("popover-check");
            switch (option.Item1)
            {
                case LauncherThemePreference.Light:
                    LightThemeRadioButton = radio;
                    break;
                case LauncherThemePreference.Dark:
                    DarkThemeRadioButton = radio;
                    break;
            }
            radio.Toggled += (_, _) =>
            {
                if (!radio.Active)
                {
                    return;
                }

                DraftThemePreference = option.Item1;
                HasUnsavedThemeChanges = DraftThemePreference != UiPreferences.CurrentThemePreference;
                UpdateThemeSelectorButton();
                UpdateFooterButtons();
                ThemeSelectorPopover?.Popdown();
            };
            content.PackStart(radio, false, false, 0);
        }

        ThemeSelectorPopover.Add(content);
        return ThemeSelectorPopover;
    }

    private void UpdateThemeSelectorButton()
    {
        ThemeSelectorButton.Label = DraftThemePreference == LauncherThemePreference.Dark ? "Dark" : "Light";
        if (LightThemeRadioButton is not null)
        {
            LightThemeRadioButton.Active = DraftThemePreference == LauncherThemePreference.Light;
        }

        if (DarkThemeRadioButton is not null)
        {
            DarkThemeRadioButton.Active = DraftThemePreference == LauncherThemePreference.Dark;
        }
    }

    private void UpdateFooterButtons()
    {
        SaveButton.Sensitive = HasUnsavedThemeChanges;
        CancelButton.Sensitive = HasUnsavedThemeChanges;
    }

    private async Task SaveAsync()
    {
        if (!HasUnsavedThemeChanges)
        {
            return;
        }

        try
        {
            await UiPreferences.SetThemeAsync(DraftThemePreference).ConfigureAwait(false);
            Gtk.Application.Invoke((_, _) =>
            {
                HasUnsavedThemeChanges = false;
                UpdateFooterButtons();
            });
        }
        catch (Exception ex)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to save settings", ex.Message));
        }
    }

    private async Task RefreshJavaAsync()
    {
        Gtk.Application.Invoke((_, _) =>
        {
            JavaRescanButton.Sensitive = false;
            JavaStatusLabel.Text = "Scanning Java installations...";
        });

        IReadOnlyList<JavaInstallationSummary> installations;
        try
        {
            var result = await DiscoverJavaUseCase.ExecuteAsync(new DiscoverJavaRequest(true), CancellationToken.None).ConfigureAwait(false);
            if (result.IsFailure)
            {
                Gtk.Application.Invoke((_, _) =>
                {
                    JavaStatusLabel.Text = result.Error.Message;
                    JavaRescanButton.Sensitive = true;
                });
                return;
            }

            installations = result.Value;
        }
        catch (Exception ex)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                JavaStatusLabel.Text = ex.Message;
                JavaRescanButton.Sensitive = true;
            });
            return;
        }

        Gtk.Application.Invoke((_, _) =>
        {
            foreach (var child in JavaList.Children.Cast<Widget>().ToArray())
            {
                JavaList.Remove(child);
            }

            if (installations.Count == 0)
            {
                JavaList.Add(BuildJavaEmptyRow("No Java installations were discovered yet."));
            }
            else
            {
                foreach (var installation in installations)
                {
                    JavaList.Add(BuildJavaRow(installation));
                }
            }

            JavaList.ShowAll();
            JavaStatusLabel.Text = $"{installations.Count} installation(s) discovered.";
            JavaRescanButton.Sensitive = true;
        });
    }

    private static Widget BuildJavaEmptyRow(string message)
    {
        var row = new ListBoxRow
        {
            Selectable = false,
            Activatable = false
        };

        var label = new Label(message)
        {
            Xalign = 0,
            LineWrap = true
        };
        label.StyleContext.AddClass("settings-caption");

        var box = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };
        box.PackStart(label, false, false, 0);
        row.Add(box);
        return row;
    }

    private static Widget BuildJavaRow(JavaInstallationSummary installation)
    {
        var row = new ListBoxRow
        {
            Selectable = false,
            Activatable = false
        };

        var body = new Box(Orientation.Vertical, 6)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };
        body.StyleContext.AddClass("settings-java-row-body");

        var header = new Box(Orientation.Horizontal, 8);
        var title = new Label($"{installation.Version} · {installation.Vendor}")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-row-label");

        var state = new Label(installation.IsValid ? "Valid" : "Invalid")
        {
            Xalign = 1
        };
        state.StyleContext.AddClass(installation.IsValid ? "settings-valid" : "settings-invalid");

        header.PackStart(title, true, true, 0);
        header.PackStart(state, false, false, 0);

        var path = new Label(installation.ExecutablePath)
        {
            Xalign = 0,
            LineWrap = true,
            Selectable = true
        };
        path.StyleContext.AddClass("settings-caption");

        body.PackStart(header, false, false, 0);
        body.PackStart(path, false, false, 0);

        row.Add(body);
        return row;
    }

    private void RefreshCatalogStatus()
    {
        var status = GetCurseForgeApiKeyStatusUseCase.Execute();
        CatalogBackendValue.Text = status.BackendName;
        CatalogPersistValue.Text = status.CanPersistSecrets ? "Yes" : "No";
        CatalogEnvironmentValue.Text = status.EnvironmentVariablePresent ? "Present" : "Missing";
        CatalogSecureStoreValue.Text = status.SecureStoreValuePresent ? "Present" : "Missing";
        CatalogEffectiveValue.Text = status.EffectiveSource;
    }

    private void SetApiKey()
    {
        using var dialog = new Dialog("Set CurseForge API key", this, DialogFlags.Modal);
        dialog.AddButton("Cancel", ResponseType.Cancel);
        dialog.AddButton("Save", ResponseType.Ok);
        dialog.DefaultResponse = ResponseType.Ok;

        var entry = new Entry
        {
            Visibility = false,
            ActivatesDefault = true,
            PlaceholderText = "Enter CurseForge API key"
        };
        entry.StyleContext.AddClass("app-field");

        var content = dialog.ContentArea;
        content.MarginTop = 12;
        content.MarginBottom = 12;
        content.MarginStart = 12;
        content.MarginEnd = 12;
        content.Spacing = 8;

        var label = new Label("Store the CurseForge API key in the launcher secure store.")
        {
            Xalign = 0,
            LineWrap = true
        };
        label.StyleContext.AddClass("settings-caption");

        content.PackStart(label, false, false, 0);
        var fieldLabel = new Label("API key")
        {
            Xalign = 0
        };
        fieldLabel.StyleContext.AddClass("app-field-label");
        content.PackStart(fieldLabel, false, false, 0);
        content.PackStart(entry, false, false, 0);

        dialog.ShowAll();

        var response = (ResponseType)dialog.Run();

        if (response != ResponseType.Ok || string.IsNullOrWhiteSpace(entry.Text))
        {
            return;
        }

        var result = ConfigureCurseForgeApiKeyUseCase.Execute(new ConfigureCurseForgeApiKeyRequest
        {
            ApiKey = entry.Text.Trim()
        });

        if (result.IsFailure)
        {
            ShowError("Unable to store API key", result.Error.Message);
            return;
        }

        RefreshCatalogStatus();
    }

    private void ClearApiKey()
    {
        var result = ClearCurseForgeApiKeyUseCase.Execute();
        if (result.IsFailure)
        {
            ShowError("Unable to clear API key", result.Error.Message);
            return;
        }

        RefreshCatalogStatus();
    }

    private void ShowError(string title, string message)
    {
        using var dialog = new MessageDialog(
            this,
            DialogFlags.Modal,
            MessageType.Error,
            ButtonsType.Ok,
            message)
        {
            Title = title
        };

        dialog.Run();
    }
}
