using System.Diagnostics;
using System.Text;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Application.UseCases.Java;
using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using Gdk;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Window = Gtk.Window;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class EditInstanceWindow : Window
{
    private const uint LogRefreshIntervalMilliseconds = 1000;
    private const int ProviderFilesPageSize = 50;
    private const int MaxProviderFilePages = 5;

    private readonly IServiceProvider serviceProvider;
    private readonly GetInstanceDetailsUseCase getInstanceDetailsUseCase;
    private readonly ListInstanceContentUseCase listInstanceContentUseCase;
    private readonly RescanInstanceContentUseCase rescanInstanceContentUseCase;
    private readonly SetInstanceContentEnabledUseCase setInstanceContentEnabledUseCase;
    private readonly DeleteInstanceContentUseCase deleteInstanceContentUseCase;
    private readonly GetInstanceModpackMetadataUseCase getInstanceModpackMetadataUseCase;
    private readonly GetLatestLaunchOutputUseCase getLatestLaunchOutputUseCase;
    private readonly ClearLatestLaunchOutputUseCase clearLatestLaunchOutputUseCase;
    private readonly UpdateInstanceConfigurationUseCase updateInstanceConfigurationUseCase;
    private readonly GetManagedJavaRuntimeSlotsUseCase getManagedJavaRuntimeSlotsUseCase;
    private readonly ListCatalogFilesUseCase listCatalogFilesUseCase;
    private readonly GetCatalogFileDetailsUseCase getCatalogFileDetailsUseCase;
    private readonly ProviderMediaCacheService providerMediaCacheService;
    private readonly ContentArchiveIconCacheService contentArchiveIconCacheService;

    private readonly ListBox categoryList = new() { SelectionMode = SelectionMode.Single };
    private readonly Stack contentStack = new() { Hexpand = true, Vexpand = true, TransitionType = StackTransitionType.Crossfade, TransitionDuration = 150 };
    private readonly Button saveButton = new("Save");
    private readonly Button closeButton = new("Close");
    private readonly Image instanceIconImage = new();
    private readonly Label instanceIconText = new() { Halign = Align.Center, Valign = Align.Center };
    private readonly Label headerTitleLabel = new() { Xalign = 0 };
    private readonly Label headerSubtitleLabel = new() { Xalign = 0, Wrap = true };
    private readonly TextView launchLogTextView = new() { Editable = false, CursorVisible = false, Monospace = true, WrapMode = WrapMode.None };
    private readonly Button copyLogButton = new("Copy");
    private readonly Button clearLogButton = new("Clear");
    private readonly Label providerSummaryLabel = new() { Xalign = 0, Wrap = true, Selectable = true };
    private readonly Button providerWebsiteButton = new() { Halign = Align.Start, Relief = ReliefStyle.None };
    private readonly Button providerVersionButton = new("Select version") { Hexpand = true, Sensitive = false };
    private readonly Button providerUpdateButton = new("Update modpack") { Sensitive = false };
    private readonly Label providerVersionStatusLabel = new() { Xalign = 0, Wrap = true };
    private readonly TextView providerChangelogTextView = new() { Editable = false, CursorVisible = false, Monospace = true, WrapMode = WrapMode.WordChar };
    private readonly Button javaSelectorButton = new() { Hexpand = true };
    private readonly CheckButton skipCompatibilityChecksButton = new("Skip compatibility checks");
    private readonly SpinButton minMemorySpinButton = new(new Adjustment(2048, 512, 131072, 256, 1024, 0), 1, 0);
    private readonly SpinButton maxMemorySpinButton = new(new Adjustment(4096, 512, 131072, 256, 1024, 0), 1, 0);
    private readonly Label settingsStatusLabel = new() { Xalign = 0, Wrap = true };
    private readonly Dictionary<InstanceContentCategory, ContentTabState> contentTabs = new();

    private InstanceId? targetInstanceId;
    private LauncherInstance? instance;
    private InstanceContentMetadata contentMetadata = new();
    private InstanceModpackMetadata? modpackMetadata;
    private IReadOnlyList<ManagedJavaRuntimeSlotSummary> managedJavaSlots = [];
    private IReadOnlyList<CatalogFileSummary> providerFiles = [];
    private CatalogFileSummary? selectedProviderFile;
    private bool providerFilesLoaded;
    private bool providerFilesLoading;
    private int? loadedPreferredJavaMajor;
    private bool loadedSkipCompatibilityChecks;
    private int loadedMinMemoryMb;
    private int loadedMaxMemoryMb;
    private int? draftPreferredJavaMajor;
    private bool draftSkipCompatibilityChecks;
    private int draftMinMemoryMb;
    private int draftMaxMemoryMb;
    private Popover? providerVersionPopover;
    private Popover? javaSelectorPopover;
    private uint? logRefreshSourceId;
    private string lastLaunchLogText = string.Empty;
    private bool suppressSettingsEvents;
    private bool isSaving;
    private bool isClosing;
    private readonly Dictionary<CatalogContentType, CatalogContentBrowserWindow> contentBrowserWindows = new();

    public EditInstanceWindow(
        IServiceProvider serviceProvider,
        GetInstanceDetailsUseCase getInstanceDetailsUseCase,
        ListInstanceContentUseCase listInstanceContentUseCase,
        RescanInstanceContentUseCase rescanInstanceContentUseCase,
        SetInstanceContentEnabledUseCase setInstanceContentEnabledUseCase,
        DeleteInstanceContentUseCase deleteInstanceContentUseCase,
        GetInstanceModpackMetadataUseCase getInstanceModpackMetadataUseCase,
        GetLatestLaunchOutputUseCase getLatestLaunchOutputUseCase,
        ClearLatestLaunchOutputUseCase clearLatestLaunchOutputUseCase,
        UpdateInstanceConfigurationUseCase updateInstanceConfigurationUseCase,
        GetManagedJavaRuntimeSlotsUseCase getManagedJavaRuntimeSlotsUseCase,
        ListCatalogFilesUseCase listCatalogFilesUseCase,
        GetCatalogFileDetailsUseCase getCatalogFileDetailsUseCase,
        ProviderMediaCacheService providerMediaCacheService,
        ContentArchiveIconCacheService contentArchiveIconCacheService) : base("Edit Instance")
    {
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.getInstanceDetailsUseCase = getInstanceDetailsUseCase;
        this.listInstanceContentUseCase = listInstanceContentUseCase;
        this.rescanInstanceContentUseCase = rescanInstanceContentUseCase;
        this.setInstanceContentEnabledUseCase = setInstanceContentEnabledUseCase;
        this.deleteInstanceContentUseCase = deleteInstanceContentUseCase;
        this.getInstanceModpackMetadataUseCase = getInstanceModpackMetadataUseCase;
        this.getLatestLaunchOutputUseCase = getLatestLaunchOutputUseCase;
        this.clearLatestLaunchOutputUseCase = clearLatestLaunchOutputUseCase;
        this.updateInstanceConfigurationUseCase = updateInstanceConfigurationUseCase;
        this.getManagedJavaRuntimeSlotsUseCase = getManagedJavaRuntimeSlotsUseCase;
        this.listCatalogFilesUseCase = listCatalogFilesUseCase;
        this.getCatalogFileDetailsUseCase = getCatalogFileDetailsUseCase;
        this.providerMediaCacheService = providerMediaCacheService ?? throw new ArgumentNullException(nameof(providerMediaCacheService));
        this.contentArchiveIconCacheService = contentArchiveIconCacheService ?? throw new ArgumentNullException(nameof(contentArchiveIconCacheService));

        SetDefaultSize(1100, 720);
        Resizable = true;
        WindowPosition = WindowPosition.CenterOnParent;
        Titlebar = LauncherGtkChrome.CreateHeaderBar("Edit Instance", string.Empty, allowWindowControls: true);

        categoryList.StyleContext.AddClass("settings-nav-list");
        categoryList.RowSelected += (_, args) =>
        {
            if (args.Row?.Name is { Length: > 0 } pageId)
            {
                contentStack.VisibleChildName = pageId;
                if (string.Equals(pageId, "provider", StringComparison.Ordinal))
                {
                    _ = EnsureProviderFilesLoadedAsync();
                }
            }
        };

        headerTitleLabel.StyleContext.AddClass("settings-page-title");
        headerSubtitleLabel.StyleContext.AddClass("settings-help");
        launchLogTextView.StyleContext.AddClass("edit-instance-log-view");
        providerChangelogTextView.StyleContext.AddClass("edit-instance-log-view");
        providerSummaryLabel.StyleContext.AddClass("settings-help");
        providerVersionStatusLabel.StyleContext.AddClass("settings-caption");
        settingsStatusLabel.StyleContext.AddClass("settings-caption");

        foreach (var button in new[] { copyLogButton, clearLogButton, providerUpdateButton, closeButton })
        {
            button.StyleContext.AddClass("action-button");
        }

        providerWebsiteButton.StyleContext.AddClass("flat-inline-button");
        providerWebsiteButton.StyleContext.AddClass("provider-inline-link");
        providerVersionButton.StyleContext.AddClass("popover-menu-button");
        javaSelectorButton.StyleContext.AddClass("popover-menu-button");
        saveButton.StyleContext.AddClass("action-button");
        saveButton.StyleContext.AddClass("primary-button");
        minMemorySpinButton.StyleContext.AddClass("app-field");
        maxMemorySpinButton.StyleContext.AddClass("app-field");

        copyLogButton.Clicked += (_, _) => CopyLaunchLog();
        clearLogButton.Clicked += async (_, _) => await ClearLaunchLogAsync().ConfigureAwait(false);
        providerWebsiteButton.Clicked += (_, _) => OpenProviderWebsite();
        providerVersionButton.Clicked += (_, _) => TogglePopover(GetOrCreateProviderVersionPopover(), providerVersionButton);
        providerUpdateButton.Clicked += async (_, _) => await HandleProviderUpdateAsync().ConfigureAwait(false);
        javaSelectorButton.Clicked += (_, _) => TogglePopover(GetOrCreateJavaSelectorPopover(), javaSelectorButton);
        skipCompatibilityChecksButton.Toggled += (_, _) => HandleSettingsChanged();
        minMemorySpinButton.ValueChanged += (_, _) => HandleSettingsChanged();
        maxMemorySpinButton.ValueChanged += (_, _) => HandleSettingsChanged();
        saveButton.Clicked += async (_, _) => await SaveChangesAsync().ConfigureAwait(false);
        closeButton.Clicked += (_, _) => CloseWindow();

        Add(BuildRoot());

        DeleteEvent += (_, args) => { args.RetVal = true; CloseWindow(); };
        Destroyed += (_, _) =>
        {
            StopLogRefreshTimer();
            foreach (var tab in contentTabs.Values)
            {
                tab.IconLoadCancellationSource?.Cancel();
                tab.IconLoadCancellationSource?.Dispose();
            }
            foreach (var window in contentBrowserWindows.Values.ToArray())
            {
                window.Destroy();
            }
            contentBrowserWindows.Clear();
            providerVersionPopover?.Destroy();
            javaSelectorPopover?.Destroy();
            LauncherWindowMemory.RequestAggressiveCleanup();
        };
    }

    public async Task<bool> LoadInstanceAsync(InstanceId instanceId)
    {
        targetInstanceId = instanceId;
        var instanceResult = await getInstanceDetailsUseCase.ExecuteAsync(new GetInstanceDetailsRequest(instanceId)).ConfigureAwait(false);
        if (instanceResult.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => LauncherGtkChrome.ShowMessage(this, "Unable to load instance", instanceResult.Error.Message, MessageType.Error));
            return false;
        }

        instance = instanceResult.Value;
        ApplyHeader(instance);
        var contentTask = listInstanceContentUseCase.ExecuteAsync(new ListInstanceContentRequest { InstanceId = instanceId });
        var modpackTask = getInstanceModpackMetadataUseCase.ExecuteAsync(new GetInstanceModpackMetadataRequest { InstanceId = instanceId });
        var javaSlotsTask = getManagedJavaRuntimeSlotsUseCase.ExecuteAsync();
        var logTask = getLatestLaunchOutputUseCase.ExecuteAsync(new GetLatestLaunchOutputRequest { InstanceId = instanceId });
        await Task.WhenAll(contentTask, modpackTask, javaSlotsTask, logTask).ConfigureAwait(false);

        contentMetadata = contentTask.Result.IsSuccess ? contentTask.Result.Value : new InstanceContentMetadata();
        modpackMetadata = modpackTask.Result.IsSuccess ? modpackTask.Result.Value : null;
        providerFiles = [];
        selectedProviderFile = null;
        providerFilesLoaded = false;
        providerFilesLoading = false;
        managedJavaSlots = javaSlotsTask.Result.IsSuccess ? javaSlotsTask.Result.Value.Where(static slot => slot.IsInstalled).OrderByDescending(static slot => slot.JavaMajor).ToArray() : [];
        loadedPreferredJavaMajor = instance.LaunchProfile.PreferredJavaMajor;
        loadedSkipCompatibilityChecks = instance.LaunchProfile.SkipCompatibilityChecks;
        loadedMinMemoryMb = instance.LaunchProfile.MinMemoryMb;
        loadedMaxMemoryMb = instance.LaunchProfile.MaxMemoryMb;
        draftPreferredJavaMajor = loadedPreferredJavaMajor;
        draftSkipCompatibilityChecks = loadedSkipCompatibilityChecks;
        draftMinMemoryMb = loadedMinMemoryMb;
        draftMaxMemoryMb = loadedMaxMemoryMb;

        Gtk.Application.Invoke((_, _) =>
        {
            ApplySettingsToControls();
            ApplyLaunchLog(logTask.Result.IsSuccess ? logTask.Result.Value : []);
        });

        Gtk.Application.Invoke((_, _) =>
        {
            RebuildTabs();
            UpdateSaveState();
            StartLogRefreshTimer();
        });

        return true;
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
        body.PackStart(BuildNavigationShell(), false, false, 0);
        body.PackStart(BuildContentShell(), true, true, 0);
        return body;
    }

    private Widget BuildNavigationShell()
    {
        var shell = new EventBox { WidthRequest = 210, Hexpand = false, Vexpand = true };
        shell.StyleContext.AddClass("settings-nav-shell");
        shell.StyleContext.AddClass("edit-instance-nav-shell");
        var content = new Box(Orientation.Vertical, 10) { MarginTop = 12, MarginBottom = 12, MarginStart = 12, MarginEnd = 12 };
        content.PackStart(BuildHeader(), false, false, 0);
        content.PackStart(categoryList, false, false, 0);
        content.PackStart(new Label { Vexpand = true }, true, true, 0);
        shell.Add(content);
        return shell;
    }

    private Widget BuildHeader()
    {
        var shell = new Box(Orientation.Horizontal, 12);
        var iconShell = new EventBox { WidthRequest = 56, HeightRequest = 56, VisibleWindow = true };
        iconShell.StyleContext.AddClass("add-instance-icon-placeholder");
        var overlay = new Overlay();
        instanceIconText.StyleContext.AddClass("add-instance-icon-text");
        overlay.Add(instanceIconImage);
        overlay.AddOverlay(instanceIconText);
        iconShell.Add(overlay);
        var text = new Box(Orientation.Vertical, 4) { Hexpand = true, Valign = Align.Center };
        text.PackStart(headerTitleLabel, false, false, 0);
        text.PackStart(headerSubtitleLabel, false, false, 0);
        shell.PackStart(iconShell, false, false, 0);
        shell.PackStart(text, true, true, 0);
        return shell;
    }

    private Widget BuildContentShell()
    {
        var shell = new EventBox { Hexpand = true, Vexpand = true };
        shell.StyleContext.AddClass("settings-content-shell");
        shell.Add(contentStack);
        return shell;
    }

    private Widget BuildFooter()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-footer");
        var content = new Box(Orientation.Horizontal, 8) { MarginTop = 10, MarginBottom = 10, MarginStart = 14, MarginEnd = 14 };
        content.PackStart(new Label { Hexpand = true }, true, true, 0);
        content.PackStart(closeButton, false, false, 0);
        content.PackStart(saveButton, false, false, 0);
        shell.Add(content);
        return shell;
    }

    private void RebuildTabs()
    {
        foreach (var child in categoryList.Children.ToArray()) { categoryList.Remove(child); child.Destroy(); }
        foreach (var child in contentStack.Children.ToArray()) { contentStack.Remove(child); child.Destroy(); }
        contentTabs.Clear();

        AddTab("log", "Minecraft log", BuildLogPage());
        if (modpackMetadata is not null)
        {
            AddTab("provider", modpackMetadata.Provider == CatalogProvider.Modrinth ? "Modrinth" : "CurseForge", BuildProviderPage());
        }

        AddContentTab(InstanceContentCategory.Mods, "Mods");
        AddContentTab(InstanceContentCategory.ResourcePacks, "Resource packs");
        AddContentTab(InstanceContentCategory.Shaders, "Shader packs");
        AddContentTab(InstanceContentCategory.Worlds, "Worlds");
        AddContentTab(InstanceContentCategory.Screenshots, "Screenshots");
        AddTab("settings", "Settings", BuildSettingsPage());

        if (categoryList.GetRowAtIndex(0) is ListBoxRow firstRow)
        {
            categoryList.SelectRow(firstRow);
            contentStack.VisibleChildName = firstRow.Name;
        }
    }

    private void AddTab(string id, string title, Widget page)
    {
        contentStack.AddNamed(page, id);
        categoryList.Add(new CategoryRow(id, title));
    }

    private void AddContentTab(InstanceContentCategory category, string title)
    {
        var state = BuildContentPage(category, title);
        contentTabs[category] = state;
        AddTab(state.PageId, title, state.Root);
        RenderContentTab(category);
    }

    private Widget BuildLogPage()
    {
        var page = CreatePageShell("Minecraft log", "Launcher-captured output stays here until you clear it or close the launcher.");
        var toolbar = new Box(Orientation.Horizontal, 8) { Halign = Align.End };
        toolbar.PackStart(new Label { Hexpand = true }, true, true, 0);
        toolbar.PackStart(copyLogButton, false, false, 0);
        toolbar.PackStart(clearLogButton, false, false, 0);
        var scroller = new ScrolledWindow { Hexpand = true, Vexpand = true };
        scroller.StyleContext.AddClass("edit-instance-log-shell");
        scroller.Add(launchLogTextView);
        page.PackStart(toolbar, false, false, 0);
        page.PackStart(scroller, true, true, 0);
        return WrapPage(page);
    }

    private Widget BuildProviderPage()
    {
        var title = modpackMetadata?.Provider == CatalogProvider.Modrinth ? "Modrinth" : "CurseForge";
        var page = CreatePageShell(title, "This tab tracks the provider-managed modpack identity for this instance.");
        providerWebsiteButton.Visible = !string.IsNullOrWhiteSpace(modpackMetadata?.ProjectUrl);
        providerWebsiteButton.Label = BuildProviderWebsiteLabel();
        providerVersionButton.Sensitive = providerFilesLoaded && providerFiles.Count > 0;
        providerUpdateButton.Sensitive = providerFilesLoaded && providerFiles.Count > 0;
        providerSummaryLabel.Text = BuildProviderSummary();
        providerVersionStatusLabel.Text = BuildProviderVersionStatus();
        var summaryCard = CreateCard(string.Empty);
        summaryCard.PackStart(providerSummaryLabel, false, false, 0);
        summaryCard.PackStart(providerWebsiteButton, false, false, 4);
        var controls = new Box(Orientation.Horizontal, 8);
        controls.PackStart(providerVersionButton, true, true, 0);
        controls.PackStart(providerUpdateButton, false, false, 0);
        var changelogScroller = new ScrolledWindow { Hexpand = true, Vexpand = true, MinContentHeight = 260 };
        changelogScroller.StyleContext.AddClass("edit-instance-log-shell");
        changelogScroller.Add(providerChangelogTextView);
        page.PackStart(summaryCard, false, false, 0);
        page.PackStart(controls, false, false, 0);
        page.PackStart(providerVersionStatusLabel, false, false, 0);
        page.PackStart(changelogScroller, true, true, 0);
        return WrapPage(page);
    }

    private ContentTabState BuildContentPage(InstanceContentCategory category, string title)
    {
        var searchEntry = new Entry { PlaceholderText = $"Search {title.ToLowerInvariant()}" };
        searchEntry.StyleContext.AddClass("app-field");
        searchEntry.StyleContext.AddClass("app-search-field");
        var refreshButton = new Button("Refresh");
        var openFolderButton = new Button("Open folder");
        Button? browseButton = null;
        refreshButton.StyleContext.AddClass("action-button");
        openFolderButton.StyleContext.AddClass("action-button");
        if (TryMapCatalogContentType(category, out _))
        {
            browseButton = new Button("Browse online");
            browseButton.StyleContext.AddClass("action-button");
        }

        var list = new ListBox { SelectionMode = SelectionMode.Multiple };
        list.StyleContext.AddClass("add-instance-version-list");
        list.StyleContext.AddClass("edit-instance-content-list");
        var statusLabel = new Label { Xalign = 0, Wrap = true };
        statusLabel.StyleContext.AddClass("settings-caption");
        var selectionStatusLabel = new Label("No items selected.") { Xalign = 0, Wrap = true };
        selectionStatusLabel.StyleContext.AddClass("settings-caption");
        var toggleButton = new Button("Enable/Disable selected");
        var openSelectedButton = new Button("Open selected");
        var showInFolderButton = new Button("Show in folder");
        var deleteSelectedButton = new Button("Delete selected");
        foreach (var actionButton in new[] { toggleButton, openSelectedButton, showInFolderButton, deleteSelectedButton })
        {
            actionButton.StyleContext.AddClass("action-button");
        }
        deleteSelectedButton.StyleContext.AddClass("danger-button");
        var state = new ContentTabState(category, title, $"{category.ToString().ToLowerInvariant()}-page")
        {
            SearchEntry = searchEntry,
            RefreshButton = refreshButton,
            OpenFolderButton = openFolderButton,
            BrowseButton = browseButton,
            List = list,
            StatusLabel = statusLabel,
            SelectionStatusLabel = selectionStatusLabel,
            ToggleButton = toggleButton,
            OpenSelectedButton = openSelectedButton,
            ShowInFolderButton = showInFolderButton,
            DeleteSelectedButton = deleteSelectedButton
        };
        var toolbar = new Box(Orientation.Horizontal, 8);
        toolbar.PackStart(searchEntry, true, true, 0);
        toolbar.PackStart(refreshButton, false, false, 0);
        toolbar.PackStart(openFolderButton, false, false, 0);
        if (browseButton is not null)
        {
            toolbar.PackStart(browseButton, false, false, 0);
        }
        var scroller = new ScrolledWindow { Hexpand = true, Vexpand = true };
        scroller.StyleContext.AddClass("add-instance-list-scroller");
        scroller.Add(list);
        var actionCard = CreateCard("Actions");
        actionCard.StyleContext.AddClass("edit-instance-actions-sidebar");
        actionCard.PackStart(selectionStatusLabel, false, false, 0);
        actionCard.PackStart(toggleButton, false, false, 0);
        actionCard.PackStart(openSelectedButton, false, false, 0);
        actionCard.PackStart(showInFolderButton, false, false, 0);
        actionCard.PackStart(deleteSelectedButton, false, false, 0);
        actionCard.PackStart(new Label { Vexpand = true }, true, true, 0);
        var page = CreatePageShell(title, string.Empty);
        page.PackStart(toolbar, false, false, 0);
        page.PackStart(statusLabel, false, false, 0);
        var listPane = new Box(Orientation.Vertical, 0) { Hexpand = true, Vexpand = true };
        listPane.PackStart(BuildContentHeaderRow(state), false, false, 0);
        listPane.PackStart(scroller, true, true, 0);
        var split = new Box(Orientation.Horizontal, 12) { Hexpand = true, Vexpand = true };
        split.PackStart(listPane, true, true, 0);
        split.PackStart(actionCard, false, false, 0);
        page.PackStart(split, true, true, 0);
        state.Root = WrapPage(page);
        searchEntry.Changed += (_, _) => RenderContentTab(category);
        refreshButton.Clicked += async (_, _) => await RefreshContentMetadataAsync(true).ConfigureAwait(false);
        openFolderButton.Clicked += (_, _) => OpenCategoryFolder(category);
        list.SelectedRowsChanged += (_, _) => UpdateContentSelectionState(category);
        toggleButton.Clicked += async (_, _) => await ToggleSelectedContentAsync(category).ConfigureAwait(false);
        openSelectedButton.Clicked += (_, _) => OpenSelectedContent(category);
        showInFolderButton.Clicked += (_, _) => OpenSelectedContentFolders(category);
        deleteSelectedButton.Clicked += async (_, _) => await DeleteSelectedContentAsync(category).ConfigureAwait(false);
        if (browseButton is not null)
        {
            browseButton.Clicked += (_, _) => OpenContentBrowser(category);
        }
        UpdateContentHeaderState(state);
        UpdateContentSelectionState(category);
        return state;
    }

    private Widget BuildSettingsPage()
    {
        var page = CreatePageShell("Settings", "These values override the launcher default only for this instance.");
        var javaCard = CreateCard(string.Empty);
        javaCard.PackStart(CreateSettingRow("Java runtime", javaSelectorButton), false, false, 0);
        var compatibilityCard = CreateCard(string.Empty);
        compatibilityCard.PackStart(skipCompatibilityChecksButton, false, false, 0);
        var memoryCard = CreateCard(string.Empty);
        memoryCard.PackStart(CreateSettingRow("Min memory (MB)", minMemorySpinButton), false, false, 0);
        memoryCard.PackStart(CreateSettingRow("Max memory (MB)", maxMemorySpinButton), false, false, 0);
        page.PackStart(javaCard, false, false, 0);
        page.PackStart(compatibilityCard, false, false, 0);
        page.PackStart(memoryCard, false, false, 0);
        page.PackStart(settingsStatusLabel, false, false, 0);
        return WrapPage(page);
    }

    private Box CreatePageShell(string title, string helperText)
    {
        var page = new Box(Orientation.Vertical, 12) { MarginTop = 14, MarginBottom = 14, MarginStart = 14, MarginEnd = 14 };
        page.StyleContext.AddClass("settings-page");
        var titleLabel = new Label(title) { Xalign = 0 };
        titleLabel.StyleContext.AddClass("settings-page-title");
        page.PackStart(titleLabel, false, false, 0);
        if (!string.IsNullOrWhiteSpace(helperText))
        {
            var helperLabel = new Label(helperText) { Xalign = 0, Wrap = true };
            helperLabel.StyleContext.AddClass("settings-help");
            page.PackStart(helperLabel, false, false, 0);
        }
        return page;
    }

    private static Widget WrapPage(Box page)
    {
        var scroller = new ScrolledWindow { Hexpand = true, Vexpand = true };
        scroller.StyleContext.AddClass("settings-page-scroller");
        scroller.Add(page);
        return scroller;
    }

    private static Box CreateCard(string caption)
    {
        var card = new Box(Orientation.Vertical, 8);
        card.StyleContext.AddClass("settings-card");
        if (!string.IsNullOrWhiteSpace(caption))
        {
            var label = new Label(caption) { Xalign = 0, Wrap = true };
            label.StyleContext.AddClass("settings-caption");
            card.PackStart(label, false, false, 0);
        }
        return card;
    }

    private static Widget CreateSettingRow(string labelText, Widget editor)
    {
        var row = new Box(Orientation.Horizontal, 16);
        row.StyleContext.AddClass("settings-row");
        var label = new Label(labelText) { Xalign = 0 };
        label.StyleContext.AddClass("settings-row-label");
        row.PackStart(label, false, false, 0);
        row.PackStart(new Label { Hexpand = true }, true, true, 0);
        row.PackStart(editor, false, false, 0);
        return row;
    }

    private void ApplyHeader(LauncherInstance loadedInstance)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            headerTitleLabel.Text = loadedInstance.Name;
            headerSubtitleLabel.Text = $"{loadedInstance.GameVersion} • {FormatLoaderSummary(loadedInstance)}";
            if (!string.IsNullOrWhiteSpace(loadedInstance.IconKey) && File.Exists(loadedInstance.IconKey))
            {
                using var pixbuf = new Pixbuf(loadedInstance.IconKey);
                instanceIconImage.Pixbuf = ScaleToSquare(pixbuf, 56);
                instanceIconImage.Show();
                instanceIconText.Hide();
            }
            else
            {
                instanceIconImage.Hide();
                instanceIconText.Text = string.IsNullOrWhiteSpace(loadedInstance.Name) ? "I" : loadedInstance.Name[..1].ToUpperInvariant();
                instanceIconText.Show();
            }
        });
    }

    private void StartLogRefreshTimer()
    {
        StopLogRefreshTimer();
        logRefreshSourceId = GLib.Timeout.Add(LogRefreshIntervalMilliseconds, () =>
        {
            if (isClosing || targetInstanceId is null)
            {
                logRefreshSourceId = null;
                return false;
            }
            _ = RefreshLaunchLogAsync();
            return true;
        });
    }

    private void StopLogRefreshTimer()
    {
        if (logRefreshSourceId is uint sourceId)
        {
            GLib.Source.Remove(sourceId);
            logRefreshSourceId = null;
        }
    }

    private async Task RefreshLaunchLogAsync()
    {
        if (targetInstanceId is null) return;
        var result = await getLatestLaunchOutputUseCase.ExecuteAsync(new GetLatestLaunchOutputRequest { InstanceId = targetInstanceId.Value }).ConfigureAwait(false);
        if (result.IsSuccess) Gtk.Application.Invoke((_, _) => ApplyLaunchLog(result.Value));
    }

    private void ApplyLaunchLog(IReadOnlyList<LaunchOutputLine> lines)
    {
        var text = BuildLaunchLogText(lines);
        if (string.Equals(lastLaunchLogText, text, StringComparison.Ordinal)) return;
        lastLaunchLogText = text;
        launchLogTextView.Buffer.Text = text;
    }

    private static string BuildLaunchLogText(IReadOnlyList<LaunchOutputLine> lines)
    {
        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            if (builder.Length > 0) builder.AppendLine();
            var streamPrefix = string.Equals(line.Stream, "stderr", StringComparison.OrdinalIgnoreCase) ? "[stderr] " : string.Empty;
            builder.Append('[').Append(line.TimestampUtc.ToLocalTime().ToString("HH:mm:ss")).Append("] ").Append(streamPrefix).Append(line.Message);
        }
        return builder.ToString();
    }

    private void CopyLaunchLog()
    {
        try { Clipboard.Get(Atom.Intern("CLIPBOARD", false)).Text = launchLogTextView.Buffer.Text ?? string.Empty; }
        catch (Exception ex) { ShowError("Unable to copy log", ex.Message); }
    }

    private async Task ClearLaunchLogAsync()
    {
        if (targetInstanceId is null) return;
        var result = await clearLatestLaunchOutputUseCase.ExecuteAsync(new ClearLatestLaunchOutputRequest { InstanceId = targetInstanceId.Value }).ConfigureAwait(false);
        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsFailure) ShowError("Unable to clear log", result.Error.Message);
            else { lastLaunchLogText = string.Empty; launchLogTextView.Buffer.Text = string.Empty; }
        });
    }

    private async Task RefreshContentMetadataAsync(bool forceRescan)
    {
        if (targetInstanceId is null) return;
        var result = forceRescan
            ? await rescanInstanceContentUseCase.ExecuteAsync(new RescanInstanceContentRequest { InstanceId = targetInstanceId.Value }).ConfigureAwait(false)
            : await listInstanceContentUseCase.ExecuteAsync(new ListInstanceContentRequest { InstanceId = targetInstanceId.Value }).ConfigureAwait(false);
        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsFailure) ShowError("Unable to refresh content", result.Error.Message);
            else
            {
                contentMetadata = result.Value;
                foreach (var category in contentTabs.Keys.ToArray()) RenderContentTab(category);
            }
        });
    }

    private void RenderContentTab(InstanceContentCategory category)
    {
        if (!contentTabs.TryGetValue(category, out var state)) return;
        state.IconLoadCancellationSource?.Cancel();
        state.IconLoadCancellationSource?.Dispose();
        state.IconLoadCancellationSource = new CancellationTokenSource();
        state.RenderVersion++;
        foreach (var child in state.List.Children.ToArray()) { state.List.Remove(child); child.Destroy(); }
        var items = GetItems(category);
        var query = state.SearchEntry.Text?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(query))
        {
            items = items.Where(item => item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        items = ApplyContentSort(state, items);
        state.StatusLabel.Text = items.Count == 0 ? (string.IsNullOrWhiteSpace(query) ? $"No {state.Title.ToLowerInvariant()} found." : $"No {state.Title.ToLowerInvariant()} matched “{query}”.") : $"{items.Count} item{(items.Count == 1 ? string.Empty : "s")}";
        for (var index = 0; index < items.Count; index++)
        {
            state.List.Add(CreateContentRow(category, items[index], index));
        }
        state.List.ShowAll();
        UpdateContentSelectionState(category);
        QueueContentIconLoads(state);
    }

    private List<InstanceFileMetadata> GetItems(InstanceContentCategory category) => category switch
    {
        InstanceContentCategory.Mods => contentMetadata.Mods.ToList(),
        InstanceContentCategory.ResourcePacks => contentMetadata.ResourcePacks.ToList(),
        InstanceContentCategory.Shaders => contentMetadata.Shaders.ToList(),
        InstanceContentCategory.Worlds => contentMetadata.Worlds.ToList(),
        InstanceContentCategory.Screenshots => contentMetadata.Screenshots.ToList(),
        _ => []
    };

    private static List<InstanceFileMetadata> ApplyContentSort(ContentTabState state, List<InstanceFileMetadata> items)
    {
        IOrderedEnumerable<InstanceFileMetadata> ordered = state.SortColumn switch
        {
            ContentSortColumn.Created => state.SortAscending
                ? items.OrderBy(static item => item.LastModifiedAtUtc ?? DateTimeOffset.MinValue).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(static item => item.LastModifiedAtUtc ?? DateTimeOffset.MinValue).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase),
            ContentSortColumn.Source => state.SortAscending
                ? items.OrderBy(static item => GetSourceSortValue(item), StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(static item => GetSourceSortValue(item), StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.Name, StringComparer.OrdinalIgnoreCase),
            _ => state.SortAscending
                ? items.OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                : items.OrderByDescending(static item => item.Name, StringComparer.OrdinalIgnoreCase).ThenBy(static item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
        };

        return ordered.ToList();
    }

    private Button CreateSortHeaderButton(ContentTabState state, ContentSortColumn column)
    {
        var button = new Button { Relief = ReliefStyle.None, Halign = Align.Fill, CanFocus = false };
        button.StyleContext.AddClass("edit-instance-sort-header");
        button.Clicked += (_, _) => HandleSortColumnClicked(state, column);
        switch (column)
        {
            case ContentSortColumn.Name:
                state.NameSortButton = button;
                break;
            case ContentSortColumn.Created:
                state.CreatedSortButton = button;
                break;
            case ContentSortColumn.Source:
                state.SourceSortButton = button;
                break;
        }

        return button;
    }

    private void HandleSortColumnClicked(ContentTabState state, ContentSortColumn column)
    {
        if (state.SortColumn == column)
        {
            state.SortAscending = !state.SortAscending;
        }
        else
        {
            state.SortColumn = column;
            state.SortAscending = true;
        }

        UpdateContentHeaderState(state);
        RenderContentTab(state.Category);
    }

    private static void UpdateContentHeaderState(ContentTabState state)
    {
        if (state.NameSortButton is not null)
        {
            state.NameSortButton.Label = BuildSortHeaderLabel("Name", state, ContentSortColumn.Name);
        }

        if (state.CreatedSortButton is not null)
        {
            state.CreatedSortButton.Label = BuildSortHeaderLabel("Created", state, ContentSortColumn.Created);
        }

        if (state.SourceSortButton is not null)
        {
            state.SourceSortButton.Label = BuildSortHeaderLabel("Source", state, ContentSortColumn.Source);
        }
    }

    private static string BuildSortHeaderLabel(string title, ContentTabState state, ContentSortColumn column)
    {
        if (state.SortColumn != column)
        {
            return title;
        }

        return state.SortAscending ? $"{title} ^" : $"{title} v";
    }

    private Widget BuildContentHeaderRow(ContentTabState state)
    {
        return LauncherStructuredList.CreateHeaderRow(
            new LauncherStructuredList.CellDefinition(new Label(string.Empty), WidthRequest: 52, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(CreateSortHeaderButton(state, ContentSortColumn.Name), Expand: true, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(CreateSortHeaderButton(state, ContentSortColumn.Created), WidthRequest: 150, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(CreateSortHeaderButton(state, ContentSortColumn.Source), WidthRequest: 130));
    }

    private Widget CreateContentRow(InstanceContentCategory category, InstanceFileMetadata item, int index)
    {
        var row = new ContentItemRow(item, category) { Selectable = true, Activatable = false };
        row.StyleContext.AddClass(index % 2 == 0 ? "add-instance-version-row-even" : "add-instance-version-row-odd");
        row.StyleContext.AddClass("edit-instance-structured-row");
        row.AddEvents((int)EventMask.ButtonPressMask);
        row.ButtonPressEvent += (_, args) => HandleContentRowButtonPress(category, row, args);

        var title = LauncherStructuredList.CreateCellLabel(item.Name, selectable: false);
        title.StyleContext.AddClass("settings-row-label");
        var subtitle = LauncherStructuredList.CreateCellLabel(BuildCompactContentSubtitle(item), selectable: false, wrap: false);
        subtitle.StyleContext.AddClass("settings-caption");
        var textContent = new Box(Orientation.Vertical, 3)
        {
            MarginTop = 5,
            MarginBottom = 5,
            MarginStart = 8,
            MarginEnd = 8,
            Valign = Align.Center,
            Hexpand = true
        };
        textContent.PackStart(title, false, false, 0);
        textContent.PackStart(subtitle, false, false, 0);
        var createdLabel = LauncherStructuredList.CreateCellLabel(FormatCreatedValue(item), selectable: false);
        createdLabel.StyleContext.AddClass("settings-caption");

        Widget sourceWidget;
        if (item.Source is not null)
        {
            var badge = new Label(FormatSourceBadge(item.Source)) { Xalign = 0 };
            badge.StyleContext.AddClass("edit-instance-source-badge");
            sourceWidget = badge;
        }
        else
        {
            sourceWidget = LauncherStructuredList.CreateCellLabel("Local");
        }

        row.Add(LauncherStructuredList.CreateRowContent(
            new LauncherStructuredList.CellDefinition(CreateContentIcon(row, category, item), WidthRequest: 52, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(textContent, Expand: true, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(createdLabel, WidthRequest: 150, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(sourceWidget, WidthRequest: 130)));
        return row;
    }

    private void HandleContentRowButtonPress(InstanceContentCategory category, ContentItemRow row, ButtonPressEventArgs args)
    {
        if (!contentTabs.TryGetValue(category, out var state) || args.Event.Button != 1)
        {
            return;
        }

        var ctrlPressed = (args.Event.State & ModifierType.ControlMask) != 0;
        if (!ctrlPressed)
        {
            foreach (var selectedRow in state.List.SelectedRows.ToArray())
            {
                if (!ReferenceEquals(selectedRow, row))
                {
                    state.List.UnselectRow(selectedRow);
                }
            }

            if (!row.IsSelected)
            {
                state.List.SelectRow(row);
            }
        }
        else if (row.IsSelected)
        {
            state.List.UnselectRow(row);
        }
        else
        {
            state.List.SelectRow(row);
        }

        UpdateContentSelectionState(category);
        args.RetVal = true;
    }

    private async Task ToggleContentAsync(InstanceContentCategory category, InstanceFileMetadata item, bool enabled)
    {
        if (targetInstanceId is null) return;
        var result = await setInstanceContentEnabledUseCase.ExecuteAsync(new SetInstanceContentEnabledRequest { InstanceId = targetInstanceId.Value, Category = category, ContentReference = item.RelativePath, Enabled = enabled }).ConfigureAwait(false);
        Gtk.Application.Invoke((_, _) => { if (result.IsFailure) ShowError("Unable to update content", result.Error.Message); else { contentMetadata = result.Value; RenderContentTab(category); } });
    }

    private IReadOnlyList<InstanceFileMetadata> GetSelectedContentItems(InstanceContentCategory category)
        => contentTabs.TryGetValue(category, out var state)
            ? state.List.SelectedRows.OfType<ContentItemRow>().Select(static row => row.Item).ToArray()
            : [];

    private void UpdateContentSelectionState(InstanceContentCategory category)
    {
        if (!contentTabs.TryGetValue(category, out var state))
        {
            return;
        }

        var selectedItems = GetSelectedContentItems(category);
        var selectionCount = selectedItems.Count;
        var supportsToggle = SupportsToggle(category);
        state.SelectionStatusLabel.Text = selectionCount == 0
            ? "No items selected."
            : selectionCount == 1
                ? selectedItems[0].Name
                : $"{selectionCount} items selected.";
        state.OpenSelectedButton.Sensitive = selectionCount > 0;
        state.ShowInFolderButton.Sensitive = selectionCount > 0;
        state.DeleteSelectedButton.Sensitive = selectionCount > 0;
        state.ToggleButton.Visible = supportsToggle;
        state.ToggleButton.Sensitive = supportsToggle && selectionCount > 0;
        if (supportsToggle)
        {
            var allDisabled = selectionCount > 0 && selectedItems.All(static item => item.IsDisabled);
            state.ToggleButton.Label = allDisabled ? "Enable selected" : "Disable selected";
        }
    }

    private void OpenSelectedContent(InstanceContentCategory category)
    {
        foreach (var item in GetSelectedContentItems(category))
        {
            OpenPath(item.AbsolutePath);
        }
    }

    private void OpenSelectedContentFolders(InstanceContentCategory category)
    {
        foreach (var directory in GetSelectedContentItems(category)
                     .Select(static item => Directory.Exists(item.AbsolutePath) ? item.AbsolutePath : global::System.IO.Path.GetDirectoryName(item.AbsolutePath))
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DesktopShell.OpenDirectory(directory!);
        }
    }

    private async Task ToggleSelectedContentAsync(InstanceContentCategory category)
    {
        if (targetInstanceId is null || !SupportsToggle(category))
        {
            return;
        }

        var selectedItems = GetSelectedContentItems(category);
        if (selectedItems.Count == 0)
        {
            return;
        }

        var enable = selectedItems.All(static item => item.IsDisabled);
        InstanceContentMetadata? latestMetadata = null;
        foreach (var item in selectedItems)
        {
            var result = await setInstanceContentEnabledUseCase.ExecuteAsync(new SetInstanceContentEnabledRequest
            {
                InstanceId = targetInstanceId.Value,
                Category = category,
                ContentReference = item.RelativePath,
                Enabled = enable
            }).ConfigureAwait(false);
            if (result.IsFailure)
            {
                Gtk.Application.Invoke((_, _) => ShowError("Unable to update content", result.Error.Message));
                return;
            }

            latestMetadata = result.Value;
        }

        if (latestMetadata is not null)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                contentMetadata = latestMetadata;
                RenderContentTab(category);
            });
        }
    }

    private async Task DeleteContentAsync(InstanceContentCategory category, InstanceFileMetadata item)
    {
        if (targetInstanceId is null) return;
        if (!LauncherGtkChrome.Confirm(this, "Delete content", $"Delete {item.Name} from this instance?", confirmText: "Delete", danger: true)) return;
        var result = await deleteInstanceContentUseCase.ExecuteAsync(new DeleteInstanceContentRequest { InstanceId = targetInstanceId.Value, Category = category, ContentReference = item.RelativePath }).ConfigureAwait(false);
        Gtk.Application.Invoke((_, _) => { if (result.IsFailure) ShowError("Unable to delete content", result.Error.Message); else { contentMetadata = result.Value; RenderContentTab(category); } });
    }

    private async Task DeleteSelectedContentAsync(InstanceContentCategory category)
    {
        if (targetInstanceId is null)
        {
            return;
        }

        var selectedItems = GetSelectedContentItems(category);
        if (selectedItems.Count == 0)
        {
            return;
        }

        var description = selectedItems.Count == 1
            ? $"Delete {selectedItems[0].Name} from this instance?"
            : $"Delete {selectedItems.Count} selected items from this instance?";
        if (!LauncherGtkChrome.Confirm(this, "Delete content", description, confirmText: "Delete", danger: true))
        {
            return;
        }

        InstanceContentMetadata? latestMetadata = null;
        foreach (var item in selectedItems)
        {
            var result = await deleteInstanceContentUseCase.ExecuteAsync(new DeleteInstanceContentRequest
            {
                InstanceId = targetInstanceId.Value,
                Category = category,
                ContentReference = item.RelativePath
            }).ConfigureAwait(false);
            if (result.IsFailure)
            {
                Gtk.Application.Invoke((_, _) => ShowError("Unable to delete content", result.Error.Message));
                return;
            }

            latestMetadata = result.Value;
        }

        if (latestMetadata is not null)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                contentMetadata = latestMetadata;
                RenderContentTab(category);
            });
        }
    }

    private async Task LoadProviderFilesAsync()
    {
        if (instance is null || modpackMetadata is null)
        {
            providerFiles = [];
            selectedProviderFile = null;
            providerFilesLoaded = false;
            providerFilesLoading = false;
            return;
        }

        providerFilesLoading = true;
        Gtk.Application.Invoke((_, _) =>
        {
            providerVersionStatusLabel.Text = "Loading compatible versions...";
            providerVersionButton.Sensitive = false;
            providerUpdateButton.Sensitive = false;
        });
        var loaded = new List<CatalogFileSummary>();
        for (var page = 0; page < MaxProviderFilePages; page++)
        {
            var result = await listCatalogFilesUseCase.ExecuteAsync(new ListCatalogFilesRequest
            {
                Provider = modpackMetadata.Provider,
                ContentType = CatalogContentType.Modpack,
                ProjectId = modpackMetadata.ProjectId,
                GameVersion = instance.GameVersion.ToString(),
                Loader = GetLoaderText(instance.LoaderType),
                Limit = ProviderFilesPageSize,
                Offset = page * ProviderFilesPageSize
            }).ConfigureAwait(false);

            if (!result.IsSuccess) break;
            loaded.AddRange(result.Value.Where(static file => !file.IsServerPack));
            if (result.Value.Count < ProviderFilesPageSize) break;
        }

        providerFiles = loaded.OrderByDescending(static file => file.PublishedAtUtc ?? DateTimeOffset.MinValue).ToArray();
        selectedProviderFile = providerFiles.FirstOrDefault(file => string.Equals(file.FileId, modpackMetadata.FileId, StringComparison.OrdinalIgnoreCase)) ?? providerFiles.FirstOrDefault();
        providerFilesLoaded = true;
        providerFilesLoading = false;
        await LoadSelectedProviderChangelogAsync().ConfigureAwait(false);
    }

    private async Task EnsureProviderFilesLoadedAsync()
    {
        if (providerFilesLoaded || providerFilesLoading || modpackMetadata is null)
        {
            return;
        }

        await LoadProviderFilesAsync().ConfigureAwait(false);
        Gtk.Application.Invoke((_, _) =>
        {
            providerVersionButton.Sensitive = providerFiles.Count > 0;
            providerUpdateButton.Sensitive = providerFiles.Count > 0;
            providerVersionStatusLabel.Text = BuildProviderVersionStatus();
        });
    }

    private async Task LoadSelectedProviderChangelogAsync()
    {
        if (modpackMetadata is null || selectedProviderFile is null)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                providerVersionButton.Label = "Select version";
                providerVersionStatusLabel.Text = BuildProviderVersionStatus();
                providerChangelogTextView.Buffer.Text = string.Empty;
            });
            return;
        }

        var detailsResult = await getCatalogFileDetailsUseCase.ExecuteAsync(new GetCatalogFileDetailsRequest
        {
            Provider = modpackMetadata.Provider,
            ContentType = CatalogContentType.Modpack,
            ProjectId = modpackMetadata.ProjectId,
            FileId = selectedProviderFile.FileId
        }).ConfigureAwait(false);

        Gtk.Application.Invoke((_, _) =>
        {
            providerVersionButton.Label = BuildProviderVersionLabel(selectedProviderFile);
            providerVersionStatusLabel.Text = BuildProviderVersionStatus();
            providerChangelogTextView.Buffer.Text = detailsResult.IsSuccess ? detailsResult.Value.ChangelogText ?? string.Empty : string.Empty;
            RebuildProviderVersionPopover();
        });
    }

    private string BuildProviderSummary()
        => modpackMetadata is null
            ? "No provider metadata is available."
            : $"{modpackMetadata.PackName} • {modpackMetadata.PackVersionLabel}\nProvider: {(modpackMetadata.Provider == CatalogProvider.Modrinth ? "Modrinth" : "CurseForge")} • Website: {modpackMetadata.ProjectUrl ?? "n/a"} • Pack ID: {modpackMetadata.ProjectId} • Version ID: {modpackMetadata.FileId}";

    private string BuildProviderWebsiteLabel()
        => modpackMetadata is null
            ? string.Empty
            : $"Open on {(modpackMetadata.Provider == CatalogProvider.Modrinth ? "Modrinth" : "CurseForge")}";

    private string BuildProviderVersionStatus()
    {
        if (modpackMetadata is null) return string.Empty;
        if (providerFilesLoading) return "Loading compatible versions for this instance...";
        if (!providerFilesLoaded) return "Select this tab to load compatible provider versions and changelogs.";
        if (providerFiles.Count == 0) return "No compatible versions were returned for this instance's Minecraft version and loader.";
        var newest = providerFiles[0];
        return string.Equals(newest.FileId, modpackMetadata.FileId, StringComparison.OrdinalIgnoreCase)
            ? "This modpack is already on the newest compatible provider version."
            : $"Installed: {modpackMetadata.PackVersionLabel} • Newest compatible: {newest.DisplayName}";
    }

    private string BuildProviderVersionLabel(CatalogFileSummary file)
    {
        return LauncherStructuredList.BuildSimpleCatalogFileLabel(file);
#if false
        var published = file.PublishedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd") ?? "unknown";
        var compatibility = BuildCompatibilityText(file);
        return string.IsNullOrWhiteSpace(compatibility)
            ? $"{file.DisplayName} ({published})"
            : $"{file.DisplayName} ({published}) • {compatibility}";
    }

    private static string BuildCompatibilityText(CatalogFileSummary file)
    {
        var parts = new List<string>();
        if (file.GameVersions.Count > 0) parts.Add($"MC {file.GameVersions[0]}");
        if (file.Loaders.Count > 0) parts.Add(string.Join(", ", file.Loaders.Select(static item => item.ToLowerInvariant())));
        return string.Join(" • ", parts);
    }

#endif
    }
    private Popover GetOrCreateProviderVersionPopover()
    {
        providerVersionPopover ??= CreateMenuPopover(providerVersionButton);
        RebuildProviderVersionPopover();
        return providerVersionPopover;
    }

    private void RebuildProviderVersionPopover()
    {
        if (providerVersionPopover is null) return;
        foreach (var child in providerVersionPopover.Children.ToArray()) { providerVersionPopover.Remove(child); child.Destroy(); }
        providerVersionPopover.Add(LauncherStructuredList.BuildCatalogFileSelectionPopover(
            "Modpack version",
            providerFiles,
            selectedProviderFile?.FileId,
            BuildProviderVersionLabel,
            file =>
            {
                selectedProviderFile = file;
                _ = LoadSelectedProviderChangelogAsync();
            }));
        providerVersionPopover.ShowAll();
    }

    private async Task HandleProviderUpdateAsync()
    {
        if (modpackMetadata is null || selectedProviderFile is null) return;

        var newest = providerFiles.FirstOrDefault();
        if (newest is null || string.Equals(newest.FileId, modpackMetadata.FileId, StringComparison.OrdinalIgnoreCase))
        {
            LauncherGtkChrome.ShowMessage(this, "Modpack is up to date", "No newer compatible modpack version is available for this instance.", MessageType.Info);
            return;
        }

        var details = await getCatalogFileDetailsUseCase.ExecuteAsync(new GetCatalogFileDetailsRequest
        {
            Provider = modpackMetadata.Provider,
            ContentType = CatalogContentType.Modpack,
            ProjectId = modpackMetadata.ProjectId,
            FileId = selectedProviderFile.FileId
        }).ConfigureAwait(false);

        var confirmed = LauncherGtkChrome.Confirm(
            this,
            "Update modpack",
            $"Current version: {modpackMetadata.PackVersionLabel}\nTarget version: {selectedProviderFile.DisplayName}\n\n{(details.IsSuccess ? details.Value.ChangelogText : string.Empty)}".Trim(),
            confirmText: "Continue");

        if (!confirmed) return;

        LauncherGtkChrome.ShowMessage(this, "Update not wired yet", "Version discovery and changelog loading are ready here, but the provider-managed in-place update workflow still needs the backend apply step.", MessageType.Info);
    }

    private Popover GetOrCreateJavaSelectorPopover()
    {
        javaSelectorPopover ??= CreateMenuPopover(javaSelectorButton);
        RebuildJavaSelectorPopover();
        return javaSelectorPopover;
    }

    private void RebuildJavaSelectorPopover()
    {
        if (javaSelectorPopover is null) return;
        foreach (var child in javaSelectorPopover.Children.ToArray()) { javaSelectorPopover.Remove(child); child.Destroy(); }

        var content = new Box(Orientation.Vertical, 8) { MarginTop = 10, MarginBottom = 10, MarginStart = 10, MarginEnd = 10 };
        content.StyleContext.AddClass("popover-content");
        var title = new Label("Java runtime") { Xalign = 0 };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);

        RadioButton? group = new("Use launcher default");
        group.Active = draftPreferredJavaMajor is null;
        group.StyleContext.AddClass("popover-check");
        group.Toggled += (_, _) =>
        {
            if (!group.Active) return;
            draftPreferredJavaMajor = null;
            ApplySettingsToControls();
            javaSelectorPopover?.Popdown();
        };
        content.PackStart(group, false, false, 0);

        foreach (var slot in managedJavaSlots)
        {
            var radio = new RadioButton(group, $"{slot.DisplayName} • {slot.Version ?? "Installed"}");
            radio.Active = draftPreferredJavaMajor == slot.JavaMajor;
            radio.StyleContext.AddClass("popover-check");
            radio.Toggled += (_, _) =>
            {
                if (!radio.Active) return;
                draftPreferredJavaMajor = slot.JavaMajor;
                ApplySettingsToControls();
                javaSelectorPopover?.Popdown();
            };
            content.PackStart(radio, false, false, 0);
        }

        javaSelectorPopover.Add(content);
        javaSelectorPopover.ShowAll();
    }

    private void ApplySettingsToControls()
    {
        suppressSettingsEvents = true;
        try
        {
            javaSelectorButton.Label = draftPreferredJavaMajor is null
                ? "Use launcher default"
                : managedJavaSlots.FirstOrDefault(slot => slot.JavaMajor == draftPreferredJavaMajor) is { } slot
                    ? $"{slot.DisplayName} • {slot.Version ?? "Installed"}"
                    : $"Java {draftPreferredJavaMajor}";
            skipCompatibilityChecksButton.Active = draftSkipCompatibilityChecks;
            minMemorySpinButton.Value = draftMinMemoryMb;
            maxMemorySpinButton.Value = draftMaxMemoryMb;
            settingsStatusLabel.Text = BuildSettingsStatusText();
        }
        finally
        {
            suppressSettingsEvents = false;
        }
    }

    private void HandleSettingsChanged()
    {
        if (suppressSettingsEvents) return;
        draftSkipCompatibilityChecks = skipCompatibilityChecksButton.Active;
        draftMinMemoryMb = (int)minMemorySpinButton.Value;
        draftMaxMemoryMb = (int)maxMemorySpinButton.Value;
        settingsStatusLabel.Text = BuildSettingsStatusText();
        UpdateSaveState();
    }

    private string BuildSettingsStatusText()
        => instance is null ? string.Empty : $"{instance.GameVersion} • {FormatLoaderSummary(instance)} • {(draftPreferredJavaMajor is null ? "Launcher default Java" : $"Java {draftPreferredJavaMajor}")}";

    private void UpdateSaveState() => saveButton.Sensitive = !isSaving && instance is not null && HasPendingSettingsChanges();

    private bool HasPendingSettingsChanges()
        => loadedPreferredJavaMajor != draftPreferredJavaMajor ||
           loadedSkipCompatibilityChecks != draftSkipCompatibilityChecks ||
           loadedMinMemoryMb != draftMinMemoryMb ||
           loadedMaxMemoryMb != draftMaxMemoryMb;

    private async Task SaveChangesAsync()
    {
        if (instance is null || !HasPendingSettingsChanges()) return;
        isSaving = true;
        UpdateSaveState();

        var result = await updateInstanceConfigurationUseCase.ExecuteAsync(new UpdateInstanceConfigurationRequest
        {
            InstanceId = instance.InstanceId,
            Name = instance.Name,
            IconKey = instance.IconKey,
            PreferredJavaMajor = draftPreferredJavaMajor,
            SkipCompatibilityChecks = draftSkipCompatibilityChecks,
            MinimumMemoryMb = draftMinMemoryMb,
            MaximumMemoryMb = draftMaxMemoryMb
        }).ConfigureAwait(false);

        Gtk.Application.Invoke((_, _) =>
        {
            isSaving = false;
            if (result.IsFailure)
            {
                ShowError("Unable to save instance settings", result.Error.Message);
            }
            else
            {
                instance = result.Value;
                loadedPreferredJavaMajor = instance.LaunchProfile.PreferredJavaMajor;
                loadedSkipCompatibilityChecks = instance.LaunchProfile.SkipCompatibilityChecks;
                loadedMinMemoryMb = instance.LaunchProfile.MinMemoryMb;
                loadedMaxMemoryMb = instance.LaunchProfile.MaxMemoryMb;
                draftPreferredJavaMajor = loadedPreferredJavaMajor;
                draftSkipCompatibilityChecks = loadedSkipCompatibilityChecks;
                draftMinMemoryMb = loadedMinMemoryMb;
                draftMaxMemoryMb = loadedMaxMemoryMb;
                ApplyHeader(instance);
                ApplySettingsToControls();
            }

            UpdateSaveState();
        });
    }

    private void OpenCategoryFolder(InstanceContentCategory category)
    {
        if (instance is not null) DesktopShell.OpenDirectory(ResolveCategoryDirectory(instance.InstallLocation, category));
    }

    private void OpenProviderWebsite()
    {
        if (!string.IsNullOrWhiteSpace(modpackMetadata?.ProjectUrl)) DesktopShell.OpenUrl(modpackMetadata.ProjectUrl);
    }

    private void OpenContentBrowser(InstanceContentCategory category)
    {
        if (instance is null || targetInstanceId is null || !TryMapCatalogContentType(category, out var contentType))
        {
            return;
        }

        if (contentBrowserWindows.TryGetValue(contentType, out var existingWindow))
        {
            existingWindow.PresentForInstance(this, instance, targetInstanceId.Value);
            return;
        }

        var window = serviceProvider.GetRequiredService<CatalogContentBrowserWindow>();
        window.ContentInstalled += HandleCatalogContentInstalled;
        window.Destroyed += HandleContentBrowserWindowDestroyed;
        contentBrowserWindows[contentType] = window;
        window.Configure(contentType);
        window.PresentForInstance(this, instance, targetInstanceId.Value);
    }

    private void HandleCatalogContentInstalled(object? sender, EventArgs e)
    {
        _ = RefreshContentMetadataAsync(false);
    }

    private void HandleContentBrowserWindowDestroyed(object? sender, EventArgs e)
    {
        if (sender is not CatalogContentBrowserWindow window)
        {
            return;
        }

        window.ContentInstalled -= HandleCatalogContentInstalled;
        window.Destroyed -= HandleContentBrowserWindowDestroyed;
        foreach (var pair in contentBrowserWindows.Where(pair => ReferenceEquals(pair.Value, window)).ToArray())
        {
            contentBrowserWindows.Remove(pair.Key);
        }
    }

    private static bool SupportsToggle(InstanceContentCategory category)
        => category is InstanceContentCategory.Mods or InstanceContentCategory.ResourcePacks or InstanceContentCategory.Shaders;

    private static bool TryMapCatalogContentType(InstanceContentCategory category, out CatalogContentType contentType)
    {
        switch (category)
        {
            case InstanceContentCategory.Mods:
                contentType = CatalogContentType.Mod;
                return true;
            case InstanceContentCategory.ResourcePacks:
                contentType = CatalogContentType.ResourcePack;
                return true;
            case InstanceContentCategory.Shaders:
                contentType = CatalogContentType.Shader;
                return true;
            default:
                contentType = default;
                return false;
        }
    }

    private static string BuildContentSubtitle(InstanceFileMetadata item)
        => string.Join(" • ", new[]
        {
            item.RelativePath,
            item.LastModifiedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            item.SizeBytes > 0 ? FormatSize(item.SizeBytes) : null,
            item.IsDisabled ? "Disabled" : null
        }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string BuildCompactContentSubtitle(InstanceFileMetadata item)
        => string.Join(" | ", new[]
        {
            item.RelativePath,
            item.IsDisabled ? "Disabled" : null
        }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    private static string FormatCreatedValue(InstanceFileMetadata item)
        => item.LastModifiedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "-";

    private static string GetSourceSortValue(InstanceFileMetadata item)
        => item.Source is null ? "Local" : FormatSourceBadge(item.Source);

    private static Widget CreateContentIcon(ContentItemRow row, InstanceContentCategory category, InstanceFileMetadata item)
    {
        var iconCell = new EventBox
        {
            WidthRequest = 40,
            HeightRequest = 40,
            Vexpand = false,
            Hexpand = false
        };
        iconCell.StyleContext.AddClass("add-instance-pack-icon-cell");
        row.InitializeIcon(iconCell, GetContentIconText(category), item.IconUrl);
        return iconCell;
    }

    private static string GetContentIconText(InstanceContentCategory category) => category switch
    {
        InstanceContentCategory.Mods => "MOD",
        InstanceContentCategory.ResourcePacks => "RP",
        InstanceContentCategory.Shaders => "SP",
        InstanceContentCategory.Worlds => "W",
        InstanceContentCategory.Screenshots => "SS",
        _ => "?"
    };

    private void QueueContentIconLoads(ContentTabState state)
    {
        if (state.IconLoadQueued)
        {
            return;
        }

        state.IconLoadQueued = true;
        GLib.Idle.Add(() =>
        {
            state.IconLoadQueued = false;
            LoadContentIcons(state);
            return false;
        });
    }

    private void LoadContentIcons(ContentTabState state)
    {
        var requestVersion = state.RenderVersion;
        var cancellationToken = state.IconLoadCancellationSource?.Token ?? CancellationToken.None;
        foreach (var child in state.List.Children)
        {
            if (child is ContentItemRow row &&
                !row.IsDisposed &&
                row.CanAttemptDeferredIconLoad)
            {
                row.MarkIconLoadQueued();
                _ = LoadContentRowIconAsync(state, row, requestVersion, cancellationToken);
            }
        }
    }

    private async Task LoadContentRowIconAsync(ContentTabState state, ContentItemRow row, int requestVersion, CancellationToken cancellationToken)
    {
        Pixbuf? pixbuf = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(row.IconUrl))
            {
                pixbuf = await providerMediaCacheService
                    .LoadIconPixbufAsync(row.IconUrl, row.Item.Source?.OriginalUrl, squareSize: 36, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (pixbuf is null && row.SupportsArchiveFallback)
            {
                pixbuf = await contentArchiveIconCacheService
                    .LoadIconPixbufAsync(row.Item.AbsolutePath, squareSize: 36, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }

        Gtk.Application.Invoke((_, _) =>
        {
            if (!row.IsDisposed &&
                requestVersion == state.RenderVersion &&
                row.Parent is not null)
            {
                row.SetIcon(pixbuf);
            }
            else
            {
                pixbuf?.Dispose();
            }
        });
    }

    private static string FormatSourceBadge(ContentSourceMetadata source) => source.Provider switch
    {
        ContentOriginProvider.Modrinth => "Modrinth",
        ContentOriginProvider.CurseForge => "CurseForge",
        ContentOriginProvider.FileImport => "Imported",
        ContentOriginProvider.Prism => "Prism",
        ContentOriginProvider.MultiMc => "MultiMC",
        ContentOriginProvider.Local => "Local",
        _ => "Unknown"
    };

    private static string FormatLoaderSummary(LauncherInstance instance)
        => instance.LoaderType == LoaderType.Vanilla ? "Vanilla" : instance.LoaderVersion is null ? instance.LoaderType.ToString() : $"{instance.LoaderType} {instance.LoaderVersion}";

    private static string? GetLoaderText(LoaderType loaderType) => loaderType switch
    {
        LoaderType.Forge => "forge",
        LoaderType.Fabric => "fabric",
        LoaderType.Quilt => "quilt",
        LoaderType.NeoForge => "neoforge",
        _ => null
    };

    private static Popover CreateMenuPopover(Widget relativeTo) => new(relativeTo) { BorderWidth = 0 };

    private static void TogglePopover(Popover popover, Widget relativeTo)
    {
        popover.RelativeTo = relativeTo;
        if (popover.Visible) popover.Popdown();
        else { popover.ShowAll(); popover.Popup(); }
    }

    private void CloseWindow()
    {
        if (isClosing) return;
        isClosing = true;
        StopLogRefreshTimer();
        targetInstanceId = null;
        instance = null;
        Destroy();
    }

    private void ShowError(string title, string message) => LauncherGtkChrome.ShowMessage(this, title, message, MessageType.Error);

    private static Pixbuf ScaleToSquare(Pixbuf original, int size)
    {
        if (original.Width == original.Height) return original.ScaleSimple(size, size, InterpType.Bilinear);
        var ratio = (double)original.Width / original.Height;
        var scaledWidth = ratio > 1d ? (int)Math.Ceiling(size * ratio) : size;
        var scaledHeight = ratio > 1d ? size : (int)Math.Ceiling(size / ratio);
        using var intermediate = original.ScaleSimple(scaledWidth, scaledHeight, InterpType.Bilinear);
        var scaled = new Pixbuf(Colorspace.Rgb, true, 8, size, size);
        scaled.Fill(0x00000000);
        intermediate.CopyArea(Math.Max(0, (scaledWidth - size) / 2), Math.Max(0, (scaledHeight - size) / 2), size, size, scaled, 0, 0);
        return scaled;
    }

    private static string ResolveCategoryDirectory(string installLocation, InstanceContentCategory category)
    {
        var candidates = category switch
        {
            InstanceContentCategory.Mods => new[] { global::System.IO.Path.Combine(installLocation, "mods"), global::System.IO.Path.Combine(installLocation, ".minecraft", "mods") },
            InstanceContentCategory.ResourcePacks => new[] { global::System.IO.Path.Combine(installLocation, "resourcepacks"), global::System.IO.Path.Combine(installLocation, ".minecraft", "resourcepacks") },
            InstanceContentCategory.Shaders => new[] { global::System.IO.Path.Combine(installLocation, "shaderpacks"), global::System.IO.Path.Combine(installLocation, ".minecraft", "shaderpacks") },
            InstanceContentCategory.Worlds => new[] { global::System.IO.Path.Combine(installLocation, "saves"), global::System.IO.Path.Combine(installLocation, ".minecraft", "saves") },
            InstanceContentCategory.Screenshots => new[] { global::System.IO.Path.Combine(installLocation, "screenshots"), global::System.IO.Path.Combine(installLocation, ".minecraft", "screenshots") },
            _ => new[] { installLocation }
        };
        return candidates.FirstOrDefault(Directory.Exists) ?? candidates[0];
    }

    private static void OpenPath(string path)
    {
        if (!string.IsNullOrWhiteSpace(path)) Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    }

    private static void OpenContainingFolder(string path)
    {
        var directory = Directory.Exists(path) ? path : global::System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) DesktopShell.OpenDirectory(directory);
    }

    private static string FormatSize(long sizeBytes)
    {
        var size = (double)Math.Max(sizeBytes, 0);
        var suffixes = new[] { "B", "KB", "MB", "GB" };
        var index = 0;
        while (size >= 1024d && index < suffixes.Length - 1) { size /= 1024d; index++; }
        return $"{size:0.#} {suffixes[index]}";
    }

    private sealed class CategoryRow : ListBoxRow
    {
        public CategoryRow(string pageId, string title)
        {
            Name = pageId;
            Selectable = true;
            Activatable = true;
            var outer = new Box(Orientation.Horizontal, 0) { HeightRequest = 38, Hexpand = true };
            var accent = new EventBox { WidthRequest = 4 };
            accent.StyleContext.AddClass("settings-nav-accent");
            var body = new Box(Orientation.Horizontal, 0) { MarginStart = 14, MarginEnd = 14, Halign = Align.Fill, Valign = Align.Center, Hexpand = true };
            body.StyleContext.AddClass("settings-nav-row-body");
            var label = new Label(title) { Xalign = 0 };
            label.StyleContext.AddClass("settings-nav-text");
            body.PackStart(label, true, true, 0);
            outer.PackStart(accent, false, false, 0);
            outer.PackStart(body, true, true, 0);
            Add(outer);
        }
    }

    private sealed class ContentTabState
    {
        public ContentTabState(InstanceContentCategory category, string title, string pageId) { Category = category; Title = title; PageId = pageId; }
        public InstanceContentCategory Category { get; }
        public string Title { get; }
        public string PageId { get; }
        public Widget Root { get; set; } = null!;
        public Entry SearchEntry { get; init; } = null!;
        public Button RefreshButton { get; init; } = null!;
        public Button OpenFolderButton { get; init; } = null!;
        public Button? BrowseButton { get; init; }
        public ListBox List { get; init; } = null!;
        public Label StatusLabel { get; init; } = null!;
        public Label SelectionStatusLabel { get; init; } = null!;
        public Button ToggleButton { get; init; } = null!;
        public Button OpenSelectedButton { get; init; } = null!;
        public Button ShowInFolderButton { get; init; } = null!;
        public Button DeleteSelectedButton { get; init; } = null!;
        public CancellationTokenSource? IconLoadCancellationSource { get; set; }
        public int RenderVersion { get; set; }
        public bool IconLoadQueued { get; set; }
        public ContentSortColumn SortColumn { get; set; } = ContentSortColumn.Name;
        public bool SortAscending { get; set; } = true;
        public Button? NameSortButton { get; set; }
        public Button? CreatedSortButton { get; set; }
        public Button? SourceSortButton { get; set; }
    }

    private sealed class ContentItemRow : ListBoxRow
    {
        private Image? iconImage;
        private Label? iconPlaceholder;

        public ContentItemRow(InstanceFileMetadata item, InstanceContentCategory category)
        {
            Item = item;
            Category = category;
            Destroyed += (_, _) =>
            {
                IsDisposed = true;
                if (iconImage?.Pixbuf is { } pixbuf)
                {
                    iconImage.Pixbuf = null;
                    pixbuf.Dispose();
                }
            };
        }

        public InstanceFileMetadata Item { get; }
        public InstanceContentCategory Category { get; }
        public string? IconUrl { get; private set; }
        public bool HasLoadedIcon => iconImage?.Pixbuf is not null;
        public bool IsDisposed { get; private set; }
        public bool IconLoadQueued { get; private set; }
        public bool SupportsArchiveFallback => Category is InstanceContentCategory.Mods or InstanceContentCategory.ResourcePacks or InstanceContentCategory.Shaders;
        public bool CanAttemptDeferredIconLoad => !IconLoadQueued && !HasLoadedIcon && (!string.IsNullOrWhiteSpace(IconUrl) || SupportsArchiveFallback);

        public void InitializeIcon(EventBox iconCell, string fallbackText, string? iconUrl)
        {
            IconUrl = iconUrl;
            iconImage = new Image
            {
                Halign = Align.Center,
                Valign = Align.Center,
                NoShowAll = true
            };
            iconPlaceholder = new Label(fallbackText)
            {
                Halign = Align.Center,
                Valign = Align.Center
            };
            iconPlaceholder.StyleContext.AddClass("add-instance-pack-icon-placeholder");
            var overlay = new Overlay();
            overlay.Add(iconImage);
            overlay.AddOverlay(iconPlaceholder);
            iconCell.Add(overlay);
        }

        public void MarkIconLoadQueued()
        {
            IconLoadQueued = true;
        }

        public void SetIcon(Pixbuf? pixbuf)
        {
            if (IsDisposed || iconImage is null || iconPlaceholder is null)
            {
                pixbuf?.Dispose();
                return;
            }

            IconLoadQueued = true;
            if (pixbuf is null)
            {
                iconImage.Hide();
                iconPlaceholder.Show();
                return;
            }

            if (iconImage.Pixbuf is { } previous)
            {
                iconImage.Pixbuf = null;
                previous.Dispose();
            }

            iconImage.Pixbuf = pixbuf;
            iconImage.Show();
            iconPlaceholder.Hide();
        }
    }

    private enum ContentSortColumn
    {
        Name = 0,
        Created = 1,
        Source = 2
    }
}
