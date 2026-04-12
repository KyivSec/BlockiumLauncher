using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Styling;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using BlockiumLauncher.UI.GtkSharp.Windows;
using Cairo;
using Gdk;
using Gtk;
using Pango;
using Microsoft.Extensions.DependencyInjection;
using System.Text;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class MainWindow : Gtk.Window
{
    private readonly IServiceProvider ServiceProvider;
    private AddInstanceWindow? AddInstanceWindow;
    private AccountsWindow? AccountsWindow;
    private EditInstanceWindow? EditInstanceWindow;
    private SettingsWindow? SettingsWindow;
    private const string DefaultInstanceIconFileName = "instance_default.png";

    private readonly InstanceBrowserRefreshService InstanceBrowserRefreshService;
    private readonly ListInstanceBrowserSummariesUseCase ListInstanceBrowserSummariesUseCase;
    private readonly CloneInstanceUseCase CloneInstanceUseCase;
    private readonly DeleteInstanceUseCase DeleteInstanceUseCase;
    private readonly LaunchController LaunchController;
    private readonly ILauncherPaths LauncherPaths;
    private readonly LauncherUiPreferencesService UiPreferences;
    private readonly List<System.Action> ThemeIconRefreshers = [];
    private Button? _filterButton;
    private Popover? _filterPopover;

    private EventBox? _selectedInstanceIconShell;
    private Label? _selectedInstanceNameLabel;
    private Label? _selectedInstanceMetaLabel;
    private ListBox? _instanceList;
    private System.Action? _refreshSortIcon;
    private string _searchQuery = string.Empty;
    private SortMode _sortMode = SortMode.Name;
    private SortDirection _sortDirection = SortDirection.Ascending;
    private readonly HashSet<string> _selectedModLoaders = [];
    private readonly HashSet<string> _selectedVanillaVersions = [];
    private readonly HashSet<string> _selectedTags = [];
    private IReadOnlyList<InstanceBrowserSummary> Instances = [];
    private InstanceId? PreferredSelectionInstanceId;

    private Button? _btnPlay;
    private Button? _btnStop;
    private Button? _btnModify;
    private Button? _btnFolder;
    private Button? _btnCopy;
    private Button? _btnDelete;

    public MainWindow(
        IServiceProvider serviceProvider,
        InstanceBrowserRefreshService instanceBrowserRefreshService,
        ListInstanceBrowserSummariesUseCase listInstanceBrowserSummariesUseCase,
        CloneInstanceUseCase cloneInstanceUseCase,
        DeleteInstanceUseCase deleteInstanceUseCase,
        LaunchController launchController,
        ILauncherPaths launcherPaths,
        LauncherUiPreferencesService uiPreferences) : base("BlockiumLauncher")
    {
        ServiceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        InstanceBrowserRefreshService = instanceBrowserRefreshService ?? throw new ArgumentNullException(nameof(instanceBrowserRefreshService));
        ListInstanceBrowserSummariesUseCase = listInstanceBrowserSummariesUseCase ?? throw new ArgumentNullException(nameof(listInstanceBrowserSummariesUseCase));
        CloneInstanceUseCase = cloneInstanceUseCase ?? throw new ArgumentNullException(nameof(cloneInstanceUseCase));
        DeleteInstanceUseCase = deleteInstanceUseCase ?? throw new ArgumentNullException(nameof(deleteInstanceUseCase));
        LaunchController = launchController ?? throw new ArgumentNullException(nameof(launchController));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        UiPreferences = uiPreferences ?? throw new ArgumentNullException(nameof(uiPreferences));

        SetDefaultSize(1180, 720);
        Resizable = true;
        WindowPosition = WindowPosition.Center;

        DeleteEvent += (_, args) =>
        {
            args.RetVal = false;
            AddInstanceWindow?.ShutdownForApplicationExit();
            DestroyOwnedWindow(ref AccountsWindow);
            DestroyOwnedWindow(ref EditInstanceWindow);
            DestroyOwnedWindow(ref SettingsWindow);
            LaunchController.Dispose();
            Gtk.Application.Quit();
        };

        UiPreferences.Changed += HandlePreferencesChanged;
        InstanceBrowserRefreshService.RefreshRequested += HandleInstanceBrowserRefreshRequested;
        LaunchController.StatusChanged += HandleLaunchStatusChanged;

        Titlebar = BuildTopbar();
        Add(BuildRoot());
        _ = ReloadInstancesAsync();
    }

    private void HandlePreferencesChanged(object? sender, EventArgs e)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            RefreshThemeIcons();
        });
    }

    private void HandleInstanceBrowserRefreshRequested(object? sender, InstanceBrowserRefreshEventArgs e)
    {
        PreferredSelectionInstanceId = e.PreferredInstanceId;
        _ = ReloadInstancesAsync();
    }

    private void HandleLaunchStatusChanged(object? sender, InstanceId instanceId)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            if (PreferredSelectionInstanceId == instanceId)
            {
                var summary = Instances.FirstOrDefault(x => x.InstanceId == instanceId);
                if (summary != null)
                {
                    UpdateActionButtonsState(summary);
                }
            }
        });
    }

    private Widget BuildRoot()
    {
        return BuildBody();
    }

    private Widget BuildTopbar()
    {
        var bar = new HeaderBar
        {
            ShowCloseButton = true,
            HasSubtitle = false,
            DecorationLayout = ":minimize,maximize,close"
        };
        bar.StyleContext.AddClass("topbar-shell");

        var group = new Box(Orientation.Horizontal, 6)
        {
            Halign = Align.Center
        };
        group.StyleContext.AddClass("toolbar-group");
        group.PackStart(CreateAddInstanceToolbarButton(), false, false, 0);
        group.PackStart(CreateFoldersToolbarButton(), false, false, 0);
        group.PackStart(CreateSettingsToolbarButton(), false, false, 0);

        var titleHost = new Box(Orientation.Horizontal, 0)
        {
            Halign = Align.Center
        };
        titleHost.StyleContext.AddClass("topbar-content");
        titleHost.PackStart(group, false, false, 0);

        bar.CustomTitle = titleHost;
        bar.PackStart(new Box(Orientation.Horizontal, 0) { WidthRequest = 36, HeightRequest = 1 });
        bar.PackEnd(CreateAccountsToolbarButton());
        return bar;
    }

    private Widget BuildBody()
    {
        var layout = new Box(Orientation.Horizontal, 0);
        layout.PackStart(BuildSidebar(), false, false, 0);
        layout.PackStart(BuildInstancesBrowser(), true, true, 0);
        return layout;
    }

    private Widget BuildSidebar()
    {
        var shell = new EventBox
        {
            WidthRequest = 228,
            Hexpand = false,
            Vexpand = true
        };
        shell.StyleContext.AddClass("sidebar-shell");

        var sidebar = new Box(Orientation.Vertical, 14)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };

        sidebar.PackStart(BuildInstancePanel(), false, false, 0);
        sidebar.PackStart(BuildActionsPanel(), false, false, 0);
        sidebar.PackStart(new Label { Vexpand = true }, true, true, 0);

        shell.Add(sidebar);
        return shell;
    }

    private Widget BuildInstancePanel()
    {
        var content = new Box(Orientation.Horizontal, 14)
        {
            MarginTop = 2,
            MarginBottom = 12
        };
        content.StyleContext.AddClass("sidebar-summary");

        _selectedInstanceIconShell = CreateInstanceIconShell(56, 56, null);
        _selectedInstanceIconShell.Vexpand = false;
        _selectedInstanceIconShell.Halign = Align.Start;
        _selectedInstanceIconShell.Valign = Align.Start;
        var text = new Box(Orientation.Vertical, 4);

        _selectedInstanceNameLabel = new Label("No instances installed")
        {
            Xalign = 0,
            LineWrap = true,
            LineWrapMode = Pango.WrapMode.WordChar,
            MaxWidthChars = 24
        };
        _selectedInstanceNameLabel.StyleContext.AddClass("instance-name");

        _selectedInstanceMetaLabel = new Label("Create or import an instance to get started")
        {
            Xalign = 0,
            LineWrap = true,
            LineWrapMode = Pango.WrapMode.WordChar,
            MaxWidthChars = 24
        };
        _selectedInstanceMetaLabel.StyleContext.AddClass("secondary-text");

        text.PackStart(_selectedInstanceNameLabel, false, false, 0);
        text.PackStart(_selectedInstanceMetaLabel, false, false, 0);

        content.PackStart(_selectedInstanceIconShell, false, false, 0);
        content.PackStart(text, true, true, 0);

        return content;
    }

    private Widget BuildActionsPanel()
    {
        var content = new Box(Orientation.Vertical, 8)
        {
            MarginTop = 2
        };
        content.StyleContext.AddClass("sidebar-actions");

        _btnPlay = CreateActionButton("Play", primary: true);
        _btnStop = CreateActionButton("Stop");
        _btnModify = CreateActionButton("Modify");
        _btnFolder = CreateActionButton("Folder");
        _btnCopy = CreateActionButton("Copy");
        _btnDelete = CreateActionButton("Delete", danger: true);

        _btnPlay.Clicked += async (_, _) => await HandlePlayClickedAsync();
        _btnStop.Clicked += async (_, _) => await HandleStopClickedAsync();
        _btnModify.Clicked += async (_, _) => await HandleModifyClickedAsync();
        _btnCopy.Clicked += async (_, _) => await HandleCopyClickedAsync();
        _btnDelete.Clicked += async (_, _) => await HandleDeleteClickedAsync();

        content.PackStart(_btnPlay, false, false, 0);
        content.PackStart(_btnStop, false, false, 0);
        content.PackStart(_btnModify, false, false, 0);
        content.PackStart(_btnFolder, false, false, 0);
        content.PackStart(_btnCopy, false, false, 0);
        content.PackStart(_btnDelete, false, false, 0);

        return content;
    }

    private async Task HandlePlayClickedAsync()
    {
        if (PreferredSelectionInstanceId == null) return;
        
        var result = await LaunchController.StartLaunchAsync(PreferredSelectionInstanceId.Value);
        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Launch Failed", result.Error.Message));
        }
    }

    private async Task HandleStopClickedAsync()
    {
        if (PreferredSelectionInstanceId == null) return;
        
        var result = await LaunchController.StopLaunchAsync(PreferredSelectionInstanceId.Value);
        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Stop Failed", result.Error.Message));
        }
    }

    private async Task HandleModifyClickedAsync()
    {
        if (PreferredSelectionInstanceId == null) return;

        var window = GetOrCreateEditInstanceWindow();
        window.TransientFor = this;
        if (!await window.LoadInstanceAsync(PreferredSelectionInstanceId.Value))
        {
            return;
        }

        window.ShowAll();
        window.Present();
    }

    private async Task HandleCopyClickedAsync()
    {
        if (PreferredSelectionInstanceId is null)
        {
            return;
        }

        var sourceId = PreferredSelectionInstanceId.Value;
        using var dialog = LauncherGtkChrome.CreateFormDialog(
            this,
            "Copy instance",
            "Create a duplicated instance with a new name.",
            resizable: false,
            width: 420);
        var cancelButton = new Button("Cancel");
        cancelButton.StyleContext.AddClass("action-button");
        dialog.AddActionWidget(cancelButton, ResponseType.Cancel);

        var copyButton = new Button("Copy");
        copyButton.StyleContext.AddClass("primary-button");
        dialog.AddActionWidget(copyButton, ResponseType.Accept);
        dialog.DefaultResponse = ResponseType.Accept;

        var entry = new Entry
        {
            Text = $"{_selectedInstanceNameLabel?.Text} (Copy)",
            Hexpand = true,
        };
        entry.StyleContext.AddClass("app-field");

        var fieldLabel = new Label("Instance name")
        {
            Xalign = 0
        };
        fieldLabel.StyleContext.AddClass("app-field-label");

        var content = dialog.ContentArea;
        content.MarginTop = 16;
        content.MarginBottom = 16;
        content.MarginStart = 16;
        content.MarginEnd = 16;
        content.Spacing = 8;

        content.PackStart(fieldLabel, false, false, 0);
        content.PackStart(entry, false, false, 0);
        content.ShowAll();

        var result = (ResponseType)dialog.Run();
        var newName = entry.Text;

        if (result != ResponseType.Accept || string.IsNullOrWhiteSpace(newName))
        {
            return;
        }

        using var dialogLoader = LauncherGtkChrome.CreateBusyDialog(this, "Copying instance", "Duplicating instance files and metadata.");

        try
        {
            var request = new CloneInstanceRequest(sourceId, newName);
            var cloneResult = await CloneInstanceUseCase.ExecuteAsync(request);

            if (cloneResult.IsFailure)
            {
                ShowError("Failed to copy instance", cloneResult.Error.Message);
                return;
            }

            InstanceBrowserRefreshService.RequestRefresh(cloneResult.Value.InstanceId);
        }
        catch (Exception ex)
        {
            ShowError("Failed to copy instance", ex.Message);
        }
    }

    private async Task HandleDeleteClickedAsync()
    {
        if (PreferredSelectionInstanceId is null)
        {
            return;
        }

        var sourceId = PreferredSelectionInstanceId.Value;
        var instanceName = _selectedInstanceNameLabel?.Text ?? "this instance";

        if (!LauncherGtkChrome.Confirm(this, "Delete instance", $"Are you sure you want to permanently delete entirely '{instanceName}'?\n\nThis will remove all saves, resource packs, configuration, and mods.", confirmText: "Delete", danger: true))
        {
            return;
        }

        using var dialogLoader = LauncherGtkChrome.CreateBusyDialog(this, "Deleting instance", "Removing files, saves, mods, and metadata.");

        var request = new DeleteInstanceRequest(sourceId);
        var result = await DeleteInstanceUseCase.ExecuteAsync(request);

        if (result.IsFailure)
        {
            ShowError("Failed to delete instance", result.Error.Message);
            return;
        }

        InstanceBrowserRefreshService.RequestRefresh(null);
    }

    private Widget BuildInstancesBrowser()
    {
        var shell = new EventBox
        {
            Hexpand = true,
            Vexpand = true
        };
        shell.StyleContext.AddClass("content-shell");

        var content = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 10,
            MarginEnd = 10
        };

        content.PackStart(BuildInstanceBrowserControls(), false, false, 0);

        _instanceList = new ListBox
        {
            SelectionMode = SelectionMode.Single,
            Hexpand = true,
            Vexpand = true,
            Halign = Align.Fill,
            Valign = Align.Start
        };
        _instanceList.StyleContext.AddClass("instance-list");
        _instanceList.RowSelected += (_, args) =>
        {
            if (args.Row is InstanceRow row)
            {
                PreferredSelectionInstanceId = row.Instance.InstanceId;
                BindSelectedInstance(row.Instance);
                return;
            }

            BindSelectedInstance(null);
        };

        var scroller = new ScrolledWindow
        {
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("instance-scroller");
        scroller.HscrollbarPolicy = PolicyType.Never;
        scroller.VscrollbarPolicy = PolicyType.Automatic;
        scroller.Add(_instanceList);

        content.PackStart(scroller, true, true, 0);

        shell.Add(content);
        RefreshInstanceList();
        return shell;
    }

    private Widget BuildInstanceBrowserControls()
    {
        var controls = new Box(Orientation.Horizontal, 10)
        {
            MarginBottom = 8
        };
        controls.StyleContext.AddClass("browser-controls");

        var searchShell = new EventBox
        {
            Hexpand = true
        };
        searchShell.StyleContext.AddClass("search-shell");

        var searchContent = new Box(Orientation.Horizontal, 10)
        {
            MarginTop = 7,
            MarginBottom = 7,
            MarginStart = 10,
            MarginEnd = 10
        };

        searchContent.PackStart(CreateRegisteredThemeIcon("search.svg", 16), false, false, 0);

        var searchEntry = new Entry
        {
            Hexpand = true,
            HasFrame = false,
            PlaceholderText = "Search instances"
        };
        searchEntry.StyleContext.AddClass("search-entry");
        searchEntry.Changed += (_, _) =>
        {
            _searchQuery = searchEntry.Text?.Trim() ?? string.Empty;
            RefreshInstanceList();
        };

        searchContent.PackStart(searchEntry, true, true, 0);
        searchShell.Add(searchContent);

        var actions = new Box(Orientation.Horizontal, 8)
        {
            Halign = Align.End
        };

        actions.PackStart(CreateSortButton(), false, false, 0);
        actions.PackStart(CreateFilterButton(), false, false, 0);

        controls.PackStart(searchShell, true, true, 0);
        controls.PackStart(actions, false, false, 0);

        return controls;
    }

    private Button CreateSortButton()
    {
        var button = new Button
        {
            WidthRequest = 36,
            HeightRequest = 36,
            Relief = ReliefStyle.None
        };
        button.StyleContext.AddClass("square-icon-button");
        button.TooltipText = "Sort instances";

        button.Add(CreateSortDirectionIcon(16));

        var popover = BuildSortPopover(button);
        button.Clicked += (_, _) =>
        {
            popover.ShowAll();
            popover.Popup();
        };

        return button;
    }

    private Button CreateFilterButton()
    {
        _filterButton = CreateSquareIconButton("funnel-fill.svg", "view-filter-symbolic", "Filter instances");
        _filterPopover = BuildFilterPopover(_filterButton);
        _filterButton.Clicked += (_, _) =>
        {
            RefreshFilterPopover();
            _filterPopover?.ShowAll();
            _filterPopover?.Popup();
        };

        return _filterButton;
    }

    private Popover BuildSortPopover(Widget relativeTo)
    {
        var popover = new Popover(relativeTo)
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

        var title = new Label("Sort by")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);

        RadioButton? group = null;
        foreach (var option in new[]
                 {
                     (SortMode.Name, "Name"),
                     (SortMode.Version, "Version"),
                     (SortMode.LastPlayed, "Last played"),
                     (SortMode.Playtime, "Playtime"),
                     (SortMode.TimeAdded, "Time added")
                 })
        {
            var radio = group is null ? new RadioButton(option.Item2) : new RadioButton(group, option.Item2);
            group ??= radio;
            radio.Active = _sortMode == option.Item1;
            radio.StyleContext.AddClass("popover-check");
            radio.Toggled += (_, _) =>
            {
                if (!radio.Active)
                {
                    return;
                }

                _sortMode = option.Item1;
                RefreshInstanceList();
            };
            content.PackStart(radio, false, false, 0);
        }

        var directionTitle = new Label("Direction")
        {
            Xalign = 0,
            MarginTop = 8
        };
        directionTitle.StyleContext.AddClass("filter-section-title");
        content.PackStart(directionTitle, false, false, 0);

        RadioButton? directionGroup = null;
        foreach (var option in new[]
                 {
                     (SortDirection.Ascending, "Ascending"),
                     (SortDirection.Descending, "Descending")
                 })
        {
            var radio = directionGroup is null ? new RadioButton(option.Item2) : new RadioButton(directionGroup, option.Item2);
            directionGroup ??= radio;
            radio.Active = _sortDirection == option.Item1;
            radio.StyleContext.AddClass("popover-check");
            radio.Toggled += (_, _) =>
            {
                if (!radio.Active)
                {
                    return;
                }

                _sortDirection = option.Item1;
                _refreshSortIcon?.Invoke();
                RefreshInstanceList();
            };
            content.PackStart(radio, false, false, 0);
        }

        popover.Add(content);
        return popover;
    }

    private Popover BuildFilterPopover(Widget relativeTo)
    {
        var popover = new Popover(relativeTo)
        {
            BorderWidth = 0
        };

        var content = new Box(Orientation.Vertical, 10)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };
        content.StyleContext.AddClass("popover-content");

        var title = new Label("Filters")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);
        content.PackStart(BuildFilterSection("Modloaders", Instances.Select(GetLoaderDisplayName).Distinct().OrderBy(name => name), _selectedModLoaders), false, false, 0);
        content.PackStart(BuildFilterSection("Vanilla versions", Instances.Select(instance => instance.GameVersion).Distinct().OrderByDescending(version => version), _selectedVanillaVersions), false, false, 0);
        content.PackStart(BuildFilterSection("Tags", Instances.Select(GetStateTag).Distinct().OrderBy(tag => tag), _selectedTags), false, false, 0);

        var clearButton = new Button("Clear filters");
        clearButton.StyleContext.AddClass("flat-inline-button");
        clearButton.Clicked += (_, _) =>
        {
            _selectedModLoaders.Clear();
            _selectedVanillaVersions.Clear();
            _selectedTags.Clear();
            RefreshInstanceList();
            popover.Popdown();
        };

        content.PackStart(clearButton, false, false, 4);
        popover.Add(content);
        return popover;
    }

    private void RefreshFilterPopover()
    {
        if (_filterPopover is null || _filterButton is null)
        {
            return;
        }

        PruneUnavailableFilters();
        _filterPopover.Hide();
        _filterPopover.Destroy();
        _filterPopover = BuildFilterPopover(_filterButton);
    }

    private void PruneUnavailableFilters()
    {
        var availableLoaders = Instances.Select(GetLoaderDisplayName).ToHashSet(StringComparer.Ordinal);
        var availableVersions = Instances.Select(instance => instance.GameVersion).ToHashSet(StringComparer.Ordinal);
        var availableTags = Instances.Select(GetStateTag).ToHashSet(StringComparer.Ordinal);

        _selectedModLoaders.RemoveWhere(value => !availableLoaders.Contains(value));
        _selectedVanillaVersions.RemoveWhere(value => !availableVersions.Contains(value));
        _selectedTags.RemoveWhere(value => !availableTags.Contains(value));
    }

    private Widget BuildFilterSection(string title, IEnumerable<string> values, HashSet<string> selectedValues)
    {
        var section = new Box(Orientation.Vertical, 6)
        {
            MarginTop = 4
        };

        var label = new Label(title)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("filter-section-title");
        section.PackStart(label, false, false, 0);

        foreach (var value in values)
        {
            var check = new CheckButton(value)
            {
                Active = selectedValues.Contains(value)
            };
            check.StyleContext.AddClass("popover-check");
            check.Toggled += (_, _) =>
            {
                if (check.Active)
                {
                    selectedValues.Add(value);
                }
                else
                {
                    selectedValues.Remove(value);
                }

                RefreshInstanceList();
            };
            section.PackStart(check, false, false, 0);
        }

        return section;
    }

    private Widget BuildInstanceRow(InstanceBrowserSummary instance)
    {
        var row = new InstanceRow(instance);
        row.StyleContext.AddClass("instance-row");

        var outer = new Box(Orientation.Horizontal, 0)
        {
            HeightRequest = 68,
            Hexpand = true
        };

        var accent = new EventBox
        {
            WidthRequest = 4,
            Halign = Align.Start,
            Valign = Align.Fill
        };
        accent.StyleContext.AddClass("instance-row-accent");

        var content = new Box(Orientation.Horizontal, 10)
        {
            MarginTop = 8,
            MarginBottom = 8,
            MarginStart = 12,
            MarginEnd = 12,
            Halign = Align.Fill,
            Valign = Align.Center,
            Hexpand = true
        };
        content.StyleContext.AddClass("instance-row-body");

        var iconContainer = new Box(Orientation.Horizontal, 0)
        {
            Halign = Align.Center,
            Valign = Align.Center,
            WidthRequest = 44,
            HeightRequest = 44
        };
        iconContainer.PackStart(CreateInstanceIconWidget(40, 40, instance.IconPath), false, false, 0);

        var label = new Label(instance.Name)
        {
            Xalign = 0,
            Justify = Justification.Left,
            Ellipsize = EllipsizeMode.End,
            SingleLineMode = true
        };
        label.StyleContext.AddClass("instance-row-text");

        content.PackStart(iconContainer, false, false, 0);
        content.PackStart(label, true, true, 0);

        outer.PackStart(accent, false, false, 0);
        outer.PackStart(content, true, true, 0);

        row.Add(outer);
        return row;
    }

    private Widget BuildEmptyStateRow()
    {
        var row = new ListBoxRow
        {
            Selectable = false,
            Activatable = false
        };
        row.StyleContext.AddClass("empty-row");

        var label = new Label("No instances match the current search or filters.")
        {
            Xalign = 0.5f,
            Yalign = 0.5f,
            Justify = Justification.Center,
            LineWrap = true
        };
        label.StyleContext.AddClass("empty-row-text");

        var box = new Box(Orientation.Vertical, 0)
        {
            HeightRequest = 80,
            MarginStart = 16,
            MarginEnd = 16
        };
        box.PackStart(label, true, true, 0);
        row.Add(box);
        return row;
    }

    private void RefreshInstanceList()
    {
        if (_instanceList is null)
        {
            return;
        }

        foreach (var child in _instanceList.Children.Cast<Widget>().ToArray())
        {
            _instanceList.Remove(child);
        }

        var instances = GetVisibleInstances().ToArray();
        if (instances.Length == 0)
        {
            _instanceList.Add(BuildEmptyStateRow());
            BindSelectedInstance(null);
            _instanceList.ShowAll();
            return;
        }

        foreach (var instance in instances)
        {
            _instanceList.Add(BuildInstanceRow(instance));
        }

        var preferredRow = PreferredSelectionInstanceId is not null
            ? _instanceList.Children
                .OfType<InstanceRow>()
                .FirstOrDefault(row => row.Instance.InstanceId.Equals(PreferredSelectionInstanceId))
            : null;

        if (preferredRow is not null)
        {
            _instanceList.SelectRow(preferredRow);
            BindSelectedInstance(preferredRow.Instance);
        }
        else if (_instanceList.GetRowAtIndex(0) is InstanceRow firstRow)
        {
            _instanceList.SelectRow(firstRow);
            PreferredSelectionInstanceId = firstRow.Instance.InstanceId;
            BindSelectedInstance(firstRow.Instance);
        }

        _instanceList.ShowAll();
    }

    private IEnumerable<InstanceBrowserSummary> GetVisibleInstances()
    {
        IEnumerable<InstanceBrowserSummary> query = Instances;

        if (!string.IsNullOrWhiteSpace(_searchQuery))
        {
            query = query.Where(instance =>
                instance.Name.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                instance.GameVersion.Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                GetLoaderDisplayName(instance).Contains(_searchQuery, StringComparison.OrdinalIgnoreCase) ||
                GetStateTag(instance).Contains(_searchQuery, StringComparison.OrdinalIgnoreCase));
        }

        if (_selectedModLoaders.Count > 0)
        {
            query = query.Where(instance => _selectedModLoaders.Contains(GetLoaderDisplayName(instance)));
        }

        if (_selectedVanillaVersions.Count > 0)
        {
            query = query.Where(instance => _selectedVanillaVersions.Contains(instance.GameVersion));
        }

        if (_selectedTags.Count > 0)
        {
            query = query.Where(instance => _selectedTags.Contains(GetStateTag(instance)));
        }

        return _sortMode switch
        {
            SortMode.Name => _sortDirection == SortDirection.Ascending
                ? query.OrderBy(instance => instance.Name, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(instance => instance.Name, StringComparer.OrdinalIgnoreCase),
            SortMode.Version => _sortDirection == SortDirection.Ascending
                ? query.OrderBy(instance => instance.GameVersion, StringComparer.OrdinalIgnoreCase)
                : query.OrderByDescending(instance => instance.GameVersion, StringComparer.OrdinalIgnoreCase),
            SortMode.LastPlayed => _sortDirection == SortDirection.Ascending
                ? query.OrderBy(instance => instance.LastPlayedAtUtc)
                : query.OrderByDescending(instance => instance.LastPlayedAtUtc),
            SortMode.Playtime => _sortDirection == SortDirection.Ascending
                ? query.OrderBy(instance => instance.TotalPlaytimeSeconds)
                : query.OrderByDescending(instance => instance.TotalPlaytimeSeconds),
            SortMode.TimeAdded => _sortDirection == SortDirection.Ascending
                ? query.OrderBy(instance => instance.CreatedAtUtc)
                : query.OrderByDescending(instance => instance.CreatedAtUtc),
            _ => query
        };
    }

    private async Task ReloadInstancesAsync()
    {
        var result = await ListInstanceBrowserSummariesUseCase
            .ExecuteAsync(new ListInstancesRequest())
            .ConfigureAwait(false);

        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsSuccess)
            {
                Instances = result.Value;
                RefreshFilterPopover();
                RefreshInstanceList();
                return;
            }

            Instances = [];
            RefreshFilterPopover();
            RefreshInstanceList();
            BindSelectedInstance(null);
        });
    }

    private void BindSelectedInstance(InstanceBrowserSummary? instance)
    {
        if (_selectedInstanceNameLabel is null ||
            _selectedInstanceMetaLabel is null ||
            _selectedInstanceIconShell is null)
        {
            return;
        }

        if (instance is null)
        {
            _selectedInstanceNameLabel.Text = Instances.Count == 0 ? "No instances installed" : "No instance selected";
            _selectedInstanceMetaLabel.Text = Instances.Count == 0
                ? "Create or import an instance to get started"
                : "Select an instance to see its summary";
            SetEventBoxChild(_selectedInstanceIconShell, CreateInstanceIconWidget(56, 56, null));
            UpdateActionButtonsState(null);
            return;
        }

        _selectedInstanceNameLabel.Text = instance.Name;
        _selectedInstanceMetaLabel.Text = BuildInstanceMeta(instance);
        SetEventBoxChild(_selectedInstanceIconShell, CreateInstanceIconWidget(56, 56, instance.IconPath));
        UpdateActionButtonsState(instance);
    }

    private void UpdateActionButtonsState(InstanceBrowserSummary? instance)
    {
        var hasSelection = instance is not null;
        var isRunning = hasSelection && LaunchController.IsRunning(instance!.InstanceId);

        if (_btnPlay is not null)
        {
            _btnPlay.Sensitive = hasSelection && !isRunning;
            SetActionButtonText(_btnPlay, isRunning ? "Running" : "Play");
            SetPrimaryButtonState(_btnPlay, hasSelection && !isRunning);
        }

        if (_btnStop is not null)
        {
            _btnStop.Sensitive = hasSelection && isRunning;
            SetPrimaryButtonState(_btnStop, hasSelection && isRunning);
        }

        if (_btnModify is not null) _btnModify.Sensitive = hasSelection && !isRunning;
        if (_btnFolder is not null) _btnFolder.Sensitive = hasSelection;
        if (_btnCopy is not null) _btnCopy.Sensitive = hasSelection && !isRunning;
        if (_btnDelete is not null) _btnDelete.Sensitive = hasSelection && !isRunning;
    }

    private static string BuildInstanceMeta(InstanceBrowserSummary instance)
    {
        var loaderText = GetLoaderDisplayName(instance);
        return loaderText == "Vanilla"
            ? $"Minecraft {instance.GameVersion}"
            : $"Minecraft {instance.GameVersion} · {loaderText}";
    }

    private static string GetLoaderDisplayName(InstanceBrowserSummary instance)
    {
        return instance.LoaderType switch
        {
            LoaderType.Vanilla => "Vanilla",
            LoaderType.NeoForge => "NeoForge",
            LoaderType.Forge => "Forge",
            LoaderType.Fabric => "Fabric",
            LoaderType.Quilt => "Quilt",
            _ => instance.LoaderType.ToString()
        };
    }

    private static string GetStateTag(InstanceBrowserSummary instance)
    {
        return instance.State switch
        {
            InstanceState.Installed => "installed",
            InstanceState.NeedsRepair => "needs repair",
            InstanceState.Broken => "broken",
            InstanceState.Updating => "updating",
            InstanceState.Created => "new",
            _ => instance.State.ToString().ToLowerInvariant()
        };
    }

    private EventBox CreateInstanceIconShell(int width, int height, string? iconPath)
    {
        var shell = new EventBox
        {
            WidthRequest = width,
            HeightRequest = height,
            VisibleWindow = true
        };
        shell.StyleContext.AddClass("add-instance-pack-icon-shell");
        SetEventBoxChild(shell, CreateInstanceIconWidget(width, height, iconPath));
        return shell;
    }

    private Widget CreateInstanceIconWidget(int width, int height, string? iconPath)
    {
        var resolvedIconPath = ResolveInstanceIconPath(iconPath);
        if (!string.IsNullOrWhiteSpace(resolvedIconPath) && System.IO.File.Exists(resolvedIconPath))
        {
            try
            {
                using var original = new Pixbuf(resolvedIconPath);
                var size = Math.Min(width, height);
                
                Pixbuf scaled;
                if (original.Width == original.Height)
                {
                    scaled = original.ScaleSimple(size, size, InterpType.Bilinear);
                }
                else
                {
                    // Center and scale to fill the square while maintaining aspect ratio
                    double ratio = (double)original.Width / original.Height;
                    int targetW, targetH;
                    if (ratio > 1) { targetW = (int)(size * ratio); targetH = size; }
                    else { targetW = size; targetH = (int)(size / ratio); }
                    
                    using var intermediate = original.ScaleSimple(targetW, targetH, InterpType.Bilinear);
                    scaled = new Pixbuf(Colorspace.Rgb, true, 8, size, size);
                    scaled.Fill(0x00000000);
                    intermediate.CopyArea((targetW - size) / 2, (targetH - size) / 2, size, size, scaled, 0, 0);
                }

                return new Image(scaled)
                {
                    Halign = Align.Center,
                    Valign = Align.Center
                };
            }
            catch
            {
            }
        }

        var fallback = new DrawingArea
        {
            WidthRequest = width,
            HeightRequest = height,
            Halign = Align.Center,
            Valign = Align.Center
        };
        fallback.Drawn += (_, args) =>
        {
            args.Cr.SetSourceRGB(0.37, 0.45, 0.54);
            args.Cr.Rectangle(0, 0, width, height);
            args.Cr.Fill();
        };

        return fallback;
    }

    private string? ResolveInstanceIconPath(string? iconPath)
    {
        if (!string.IsNullOrWhiteSpace(iconPath) && System.IO.File.Exists(iconPath))
        {
            return iconPath;
        }

        return ResolveBundledDefaultInstanceIconPath();
    }

    private static string? ResolveBundledDefaultInstanceIconPath()
    {
        var outputPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Images", DefaultInstanceIconFileName);
        if (System.IO.File.Exists(outputPath))
        {
            return outputPath;
        }

        var sourcePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Images", DefaultInstanceIconFileName));
        return System.IO.File.Exists(sourcePath) ? sourcePath : null;
    }

    private static void SetEventBoxChild(EventBox shell, Widget child)
    {
        if (shell.Child is Widget existingChild)
        {
            shell.Remove(existingChild);
            existingChild.Destroy();
        }

        shell.Add(child);
        child.ShowAll();
        shell.ShowAll();
    }

    private Button CreateToolbarButton(string text, string assetFileName, string fallbackIconName)
    {
        var button = new Button();
        button.StyleContext.AddClass("toolbar-button");

        var content = new Box(Orientation.Horizontal, 8)
        {
            Halign = Align.Center,
            Valign = Align.Center
        };

        content.PackStart(CreateRegisteredThemeIcon(assetFileName, 16), false, false, 0);

        var label = new Label(text)
        {
            Xalign = 0,
            Yalign = 0.5f
        };
        label.StyleContext.AddClass("toolbar-button-label");

        content.PackStart(label, false, false, 0);
        button.Add(content);
        return button;
    }

    private Button CreateFoldersToolbarButton()
    {
        var button = CreateToolbarButton("Folders", "folder-fill.svg", "folder-symbolic");
        var popover = BuildFoldersPopover(button);
        button.Clicked += (_, _) =>
        {
            popover.ShowAll();
            popover.Popup();
        };
        return button;
    }

    private Button CreateAddInstanceToolbarButton()
    {
        var button = CreateToolbarButton("Add instance", "plus-square-fill.svg", "list-add-symbolic");
        button.Clicked += (_, _) => GetOrCreateAddInstanceWindow().PresentFrom(this);
        return button;
    }

    private AddInstanceWindow GetOrCreateAddInstanceWindow()
    {
        if (AddInstanceWindow is not null)
        {
            return AddInstanceWindow;
        }

        var window = ServiceProvider.GetRequiredService<AddInstanceWindow>();
        window.Destroyed += HandleAddInstanceWindowDestroyed;
        AddInstanceWindow = window;
        return window;
    }

    private void HandleAddInstanceWindowDestroyed(object? sender, EventArgs e)
    {
        if (sender is AddInstanceWindow window)
        {
            window.Destroyed -= HandleAddInstanceWindowDestroyed;
            if (ReferenceEquals(AddInstanceWindow, window))
            {
                AddInstanceWindow = null;
            }
        }
    }

    private AccountsWindow GetOrCreateAccountsWindow()
    {
        if (AccountsWindow is not null)
        {
            return AccountsWindow;
        }

        var window = ServiceProvider.GetRequiredService<AccountsWindow>();
        window.Destroyed += HandleAccountsWindowDestroyed;
        AccountsWindow = window;
        return window;
    }

    private EditInstanceWindow GetOrCreateEditInstanceWindow()
    {
        if (EditInstanceWindow is not null)
        {
            return EditInstanceWindow;
        }

        var window = ServiceProvider.GetRequiredService<EditInstanceWindow>();
        window.Destroyed += HandleEditInstanceWindowDestroyed;
        EditInstanceWindow = window;
        return window;
    }

    private SettingsWindow GetOrCreateSettingsWindow()
    {
        if (SettingsWindow is not null)
        {
            return SettingsWindow;
        }

        var window = ServiceProvider.GetRequiredService<SettingsWindow>();
        window.Destroyed += HandleSettingsWindowDestroyed;
        SettingsWindow = window;
        return window;
    }

    private void HandleAccountsWindowDestroyed(object? sender, EventArgs e)
    {
        if (sender is AccountsWindow window)
        {
            window.Destroyed -= HandleAccountsWindowDestroyed;
            if (ReferenceEquals(AccountsWindow, window))
            {
                AccountsWindow = null;
            }
        }
    }

    private void HandleEditInstanceWindowDestroyed(object? sender, EventArgs e)
    {
        if (sender is EditInstanceWindow window)
        {
            window.Destroyed -= HandleEditInstanceWindowDestroyed;
            if (ReferenceEquals(EditInstanceWindow, window))
            {
                EditInstanceWindow = null;
            }
        }
    }

    private void HandleSettingsWindowDestroyed(object? sender, EventArgs e)
    {
        if (sender is SettingsWindow window)
        {
            window.Destroyed -= HandleSettingsWindowDestroyed;
            if (ReferenceEquals(SettingsWindow, window))
            {
                SettingsWindow = null;
            }
        }
    }

    private static void DestroyOwnedWindow<TWindow>(ref TWindow? window) where TWindow : Gtk.Window
    {
        if (window is null)
        {
            return;
        }

        try
        {
            window.Destroy();
        }
        catch
        {
        }
        finally
        {
            window = null;
        }
    }

    private Button CreateSettingsToolbarButton()
    {
        var button = CreateToolbarButton("Settings", "gear-fill.svg", "emblem-system-symbolic");
        button.Clicked += (_, _) => GetOrCreateSettingsWindow().PresentFrom(this);
        return button;
    }

    private Button CreateAccountsToolbarButton()
    {
        var button = CreateToolbarButton("Accounts", "person-circle.svg", "avatar-default-symbolic");
        button.Clicked += (_, _) => GetOrCreateAccountsWindow().PresentFrom(this);
        return button;
    }

    private Popover BuildFoldersPopover(Widget relativeTo)
    {
        var popover = new Popover(relativeTo)
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

        var title = new Label("Folders")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);

        foreach (var option in new[]
                 {
                     ("Launcher root", LauncherPaths.RootDirectory),
                     ("Instances folder", LauncherPaths.InstancesDirectory),
                     ("Skins folder", System.IO.Path.Combine(LauncherPaths.DataDirectory, "skins")),
                     ("Java folder", LauncherPaths.ManagedJavaDirectory)
                 })
        {
            var optionButton = new Button(option.Item1)
            {
                Halign = Align.Fill,
                Hexpand = true
            };
            optionButton.StyleContext.AddClass("popover-menu-button");
            optionButton.Clicked += (_, _) =>
            {
                try
                {
                    DesktopShell.OpenDirectory(option.Item2);
                }
                catch (Exception ex)
                {
                    ShowError("Unable to open folder", ex.Message);
                }

                popover.Popdown();
            };
            content.PackStart(optionButton, false, false, 0);
        }

        popover.Add(content);
        return popover;
    }

    private Button CreateActionButton(string text, bool primary = false, bool danger = false)
    {
        var button = new Button
        {
            Hexpand = true,
            HeightRequest = 38
        };
        button.StyleContext.AddClass("action-button");

        var label = new Label(text)
        {
            Xalign = 0.5f,
            Yalign = 0.5f,
            LineWrap = true,
            LineWrapMode = Pango.WrapMode.WordChar,
            Justify = Justification.Center,
            MaxWidthChars = 16
        };
        label.StyleContext.AddClass("action-button-label");
        button.Add(label);

        if (primary)
        {
            button.StyleContext.AddClass("primary-button");
        }

        if (danger)
        {
            button.StyleContext.AddClass("danger-button");
        }

        return button;
    }

    private static void SetActionButtonText(Button button, string text)
    {
        if (button.Child is Label label)
        {
            label.Text = text;
            return;
        }

        if (button.Child is Container container)
        {
            foreach (var child in container.Children)
            {
                if (child is Label nestedLabel)
                {
                    nestedLabel.Text = text;
                    return;
                }
            }
        }
    }

    private static void SetPrimaryButtonState(Button button, bool isPrimary)
    {
        if (isPrimary)
        {
            button.StyleContext.AddClass("primary-button");
        }
        else
        {
            button.StyleContext.RemoveClass("primary-button");
        }
    }

    private Button CreateSquareIconButton(string assetFileName, string fallbackIconName, string tooltip)
    {
        _ = fallbackIconName;
        var button = new Button
        {
            WidthRequest = 36,
            HeightRequest = 36,
            Relief = ReliefStyle.None
        };
        button.StyleContext.AddClass("square-icon-button");
        button.TooltipText = tooltip;
        button.Add(CreateRegisteredThemeIcon(assetFileName, 16));
        return button;
    }

    private Widget CreateSortDirectionIcon(int size)
    {
        return CreateThemeAwareIcon(
            () => _sortDirection == SortDirection.Ascending ? "sort-down-alt.svg" : "sort-down.svg",
            size,
            refresh => _refreshSortIcon = refresh);
    }

    private Widget CreateRegisteredThemeIcon(string assetFileName, int size)
    {
        return CreateThemeAwareIcon(() => assetFileName, size);
    }

    private Widget CreateThemeAwareIcon(Func<string> assetFileNameProvider, int size, System.Action<System.Action>? captureRefresh = null)
    {
        var image = new Image
        {
            Halign = Align.Center,
            Valign = Align.Center
        };

        System.Action refreshImage = () =>
        {
            if (TryLoadToolbarPixbuf(assetFileNameProvider(), size, out var pixbuf))
            {
                image.Pixbuf = pixbuf;
                image.Visible = true;
            }
        };

        refreshImage();
        if (image.Pixbuf is not null)
        {
            ThemeIconRefreshers.Add(refreshImage);
            captureRefresh?.Invoke(refreshImage);
            image.Show();
            return image;
        }

        var fallback = new DrawingArea
        {
            WidthRequest = size,
            HeightRequest = size,
            Halign = Align.Center,
            Valign = Align.Center
        };
        fallback.Drawn += (_, args) => DrawToolbarIcon(args.Cr, assetFileNameProvider(), size);

        System.Action refreshFallback = fallback.QueueDraw;
        ThemeIconRefreshers.Add(refreshFallback);
        captureRefresh?.Invoke(refreshFallback);
        fallback.Show();
        return fallback;
    }

    private bool TryLoadToolbarPixbuf(string assetFileName, int size, out Pixbuf? pixbuf)
    {
        pixbuf = null;

        var assetPath = ResolveToolbarAssetPath(assetFileName);
        if (assetPath is null)
        {
            return false;
        }

        try
        {
            var svg = System.IO.File.ReadAllText(assetPath);
            var color = UiPreferences.CurrentPalette.PrimaryText;
            var themedSvg = svg.Replace("currentColor", color, StringComparison.OrdinalIgnoreCase);

            using var loader = new PixbufLoader("image/svg+xml");
            loader.Write(Encoding.UTF8.GetBytes(themedSvg));
            loader.Close();

            var loaded = loader.Pixbuf;
            pixbuf = loaded is null
                ? null
                : (loaded.Width == size && loaded.Height == size
                    ? loaded
                    : loaded.ScaleSimple(size, size, InterpType.Bilinear));

            return pixbuf is not null;
        }
        catch
        {
            pixbuf = null;
            return false;
        }
    }

    private static string? ResolveToolbarAssetPath(string assetFileName)
    {
        var candidates = new[]
        {
            System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Toolbar", assetFileName)),
            System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Toolbar", assetFileName)
        };

        foreach (var candidate in candidates)
        {
            if (System.IO.File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private void RefreshThemeIcons()
    {
        foreach (var refresh in ThemeIconRefreshers)
        {
            refresh();
        }
    }

    private void DrawToolbarIcon(Cairo.Context cr, string assetFileName, int size)
    {
        var iconName = System.IO.Path.GetFileNameWithoutExtension(assetFileName).ToLowerInvariant();
        var color = ParseThemeColor(UiPreferences.CurrentPalette.PrimaryText, fallback: UiPreferences.IsDarkTheme
            ? (r: 0.93, g: 0.95, b: 0.97)
            : (r: 0.14, g: 0.19, b: 0.24));

        cr.SetSourceRGB(color.r, color.g, color.b);
        cr.LineWidth = Math.Max(1.4, size / 8.5);
        cr.LineCap = LineCap.Round;
        cr.LineJoin = LineJoin.Round;

        switch (iconName)
        {
            case "plus-square":
            case "add-instance":
                DrawPlusSquareIcon(cr, size);
                break;
            case "folder-fill":
            case "folders":
                DrawFolderIcon(cr, size);
                break;
            case "gear-fill":
            case "settings":
                DrawSettingsIcon(cr, size);
                break;
            case "person-circle":
            case "accounts":
                DrawAccountsIcon(cr, size);
                break;
            case "search":
                DrawSearchIcon(cr, size);
                break;
            case "sort-down-alt":
                DrawSortAltIcon(cr, size);
                break;
            case "sort-down":
            case "sort":
                DrawSortIcon(cr, size);
                break;
            case "funnel-fill":
            case "filter":
                DrawFilterIcon(cr, size);
                break;
            default:
                DrawDotIcon(cr, size);
                break;
        }
    }

    private void ShowError(string title, string message)
    {
        LauncherGtkChrome.ShowMessage(this, title, message, MessageType.Error);
    }

    private static (double r, double g, double b) ParseThemeColor(string hexColor, (double r, double g, double b) fallback)
    {
        if (string.IsNullOrWhiteSpace(hexColor))
        {
            return fallback;
        }

        var value = hexColor.Trim();
        if (!value.StartsWith('#') || (value.Length != 7 && value.Length != 4))
        {
            return fallback;
        }

        try
        {
            if (value.Length == 7)
            {
                return
                (
                    Convert.ToInt32(value.Substring(1, 2), 16) / 255d,
                    Convert.ToInt32(value.Substring(3, 2), 16) / 255d,
                    Convert.ToInt32(value.Substring(5, 2), 16) / 255d
                );
            }

            return
            (
                Convert.ToInt32(new string(value[1], 2), 16) / 255d,
                Convert.ToInt32(new string(value[2], 2), 16) / 255d,
                Convert.ToInt32(new string(value[3], 2), 16) / 255d
            );
        }
        catch
        {
            return fallback;
        }
    }

    private static void DrawPlusSquareIcon(Cairo.Context cr, int size)
    {
        var mid = size / 2.0;
        var offset = size * 0.28;

        cr.Rectangle(size * 0.14, size * 0.14, size * 0.72, size * 0.72);
        cr.Stroke();

        cr.MoveTo(mid, mid - offset);
        cr.LineTo(mid, mid + offset);
        cr.MoveTo(mid - offset, mid);
        cr.LineTo(mid + offset, mid);
        cr.Stroke();
    }

    private static void DrawFolderIcon(Cairo.Context cr, int size)
    {
        var left = size * 0.12;
        var right = size * 0.88;
        var top = size * 0.28;
        var midTop = size * 0.18;
        var tabRight = size * 0.44;
        var bottom = size * 0.78;

        cr.MoveTo(left, top);
        cr.LineTo(size * 0.28, top);
        cr.LineTo(size * 0.34, midTop);
        cr.LineTo(tabRight, midTop);
        cr.LineTo(size * 0.5, top);
        cr.LineTo(right, top);
        cr.LineTo(right, bottom);
        cr.LineTo(left, bottom);
        cr.ClosePath();
        cr.Stroke();
    }

    private static void DrawSettingsIcon(Cairo.Context cr, int size)
    {
        var center = size / 2.0;
        var outer = size * 0.3;
        var inner = size * 0.12;

        for (var index = 0; index < 8; index++)
        {
            var angle = Math.PI / 4 * index;
            var start = outer * 0.78;
            var end = outer;
            cr.MoveTo(center + Math.Cos(angle) * start, center + Math.Sin(angle) * start);
            cr.LineTo(center + Math.Cos(angle) * end, center + Math.Sin(angle) * end);
        }
        cr.Stroke();

        cr.Arc(center, center, outer * 0.62, 0, Math.PI * 2);
        cr.Stroke();
        cr.Arc(center, center, inner, 0, Math.PI * 2);
        cr.Stroke();
    }

    private static void DrawAccountsIcon(Cairo.Context cr, int size)
    {
        var center = size / 2.0;
        cr.Arc(center, center, size * 0.4, 0, Math.PI * 2);
        cr.Stroke();

        cr.Arc(center, size * 0.38, size * 0.12, 0, Math.PI * 2);
        cr.Stroke();

        cr.Arc(center, size * 0.72, size * 0.2, Math.PI * 1.12, Math.PI * 1.88);
        cr.Stroke();
    }

    private static void DrawSearchIcon(Cairo.Context cr, int size)
    {
        var radius = size * 0.22;
        var center = size * 0.42;
        cr.Arc(center, center, radius, 0, Math.PI * 2);
        cr.Stroke();

        cr.MoveTo(size * 0.58, size * 0.58);
        cr.LineTo(size * 0.82, size * 0.82);
        cr.Stroke();
    }

    private static void DrawSortIcon(Cairo.Context cr, int size)
    {
        cr.MoveTo(size * 0.35, size * 0.22);
        cr.LineTo(size * 0.35, size * 0.78);
        cr.Stroke();

        cr.MoveTo(size * 0.28, size * 0.3);
        cr.LineTo(size * 0.35, size * 0.22);
        cr.LineTo(size * 0.42, size * 0.3);
        cr.Stroke();

        cr.MoveTo(size * 0.65, size * 0.22);
        cr.LineTo(size * 0.65, size * 0.78);
        cr.Stroke();

        cr.MoveTo(size * 0.58, size * 0.7);
        cr.LineTo(size * 0.65, size * 0.78);
        cr.LineTo(size * 0.72, size * 0.7);
        cr.Stroke();
    }

    private static void DrawSortAltIcon(Cairo.Context cr, int size)
    {
        cr.MoveTo(size * 0.35, size * 0.22);
        cr.LineTo(size * 0.35, size * 0.78);
        cr.Stroke();

        cr.MoveTo(size * 0.28, size * 0.3);
        cr.LineTo(size * 0.35, size * 0.22);
        cr.LineTo(size * 0.42, size * 0.3);
        cr.Stroke();

        cr.MoveTo(size * 0.65, size * 0.22);
        cr.LineTo(size * 0.65, size * 0.78);
        cr.Stroke();

        cr.MoveTo(size * 0.58, size * 0.3);
        cr.LineTo(size * 0.65, size * 0.22);
        cr.LineTo(size * 0.72, size * 0.3);
        cr.Stroke();
    }

    private static void DrawFilterIcon(Cairo.Context cr, int size)
    {
        cr.MoveTo(size * 0.18, size * 0.24);
        cr.LineTo(size * 0.82, size * 0.24);
        cr.LineTo(size * 0.58, size * 0.5);
        cr.LineTo(size * 0.58, size * 0.78);
        cr.LineTo(size * 0.42, size * 0.7);
        cr.LineTo(size * 0.42, size * 0.5);
        cr.ClosePath();
        cr.Stroke();
    }

    private static void DrawDotIcon(Cairo.Context cr, int size)
    {
        cr.Arc(size / 2.0, size / 2.0, size * 0.12, 0, Math.PI * 2);
        cr.Fill();
    }

    private enum SortMode
    {
        Name,
        Version,
        LastPlayed,
        Playtime,
        TimeAdded
    }

    private enum SortDirection
    {
        Ascending,
        Descending
    }

    private sealed class InstanceRow : ListBoxRow
    {
        public InstanceRow(InstanceBrowserSummary instance)
        {
            Instance = instance;
            Selectable = true;
            Activatable = true;
        }

        public InstanceBrowserSummary Instance { get; }
    }
}
