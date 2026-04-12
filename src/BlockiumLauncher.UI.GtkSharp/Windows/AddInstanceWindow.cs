using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Application.Abstractions.Repositories;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using BlockiumLauncher.UI.GtkSharp.Widgets;
using Gdk;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class AddInstanceWindow : Gtk.Window
{
    private const string DefaultInstanceIconFileName = "instance_default.png";
    private const int InstanceNameMaxLength = 128;
    private const int CatalogPageSize = 50;
    private const double CatalogScrollLoadThreshold = 180d;

    private readonly SearchCatalogUseCase SearchCatalogUseCase;
    private readonly GetCatalogProjectDetailsUseCase GetCatalogProjectDetailsUseCase;
    private readonly GetCatalogProviderMetadataUseCase GetCatalogProviderMetadataUseCase;
    private readonly ListCatalogFilesUseCase ListCatalogFilesUseCase;
    private readonly ImportCatalogModpackUseCase ImportCatalogModpackUseCase;
    private readonly ImportArchiveInstanceUseCase ImportArchiveInstanceUseCase;
    private readonly ImportInstanceUseCase ImportInstanceUseCase;
    private readonly InstallInstanceUseCase InstallInstanceUseCase;
    private readonly IVersionManifestService VersionManifestService;
    private readonly ILoaderMetadataService LoaderMetadataService;
    private readonly IInstanceRepository InstanceRepository;
    private readonly IInstanceContentMetadataService InstanceContentMetadataService;
    private readonly ILauncherPaths LauncherPaths;
    private readonly ProviderMediaCacheService ProviderMediaCacheService;
    private readonly InstanceBrowserRefreshService InstanceBrowserRefreshService;
    private readonly ManualDownloadWindow ManualDownloadWindow;

    private readonly Entry NameEntry = new()
    {
        PlaceholderText = "New instance",
        Hexpand = true,
        MaxLength = InstanceNameMaxLength
    };

    private readonly EventBox InstanceIconButton = new()
    {
        WidthRequest = 72,
        HeightRequest = 72,
        VisibleWindow = true
    };

    private readonly Overlay InstanceIconOverlay = new();
    private readonly Image InstanceIconImage = new();
    private readonly Label InstanceIconText = new("Add");

    private readonly ListBox SourceList = new()
    {
        SelectionMode = SelectionMode.Single
    };

    private readonly Stack ContentStack = new()
    {
        TransitionType = StackTransitionType.None,
        Hexpand = true,
        Vexpand = true
    };

    private readonly Button PrimaryActionButton = new("Create");
    private readonly Button CancelButton = new("Cancel");
    private Dialog? OperationDialog;
    private ProgressBar? OperationProgressBar;
    private Label? OperationTitleLabel;
    private Label? OperationBodyLabel;
    private Button? OperationCancelButton;
    private CancellationTokenSource? ActiveOperationCancellationSource;
    private uint? OperationProgressPollSourceId;
    private string? LastObservedOperationLine;

    private readonly ListBox QuickVersionList = new() { SelectionMode = SelectionMode.Single };
    private readonly Entry QuickVersionSearchEntry = new() { PlaceholderText = "Search versions" };
    private readonly RadioButton QuickNoneLoaderRadio = new("None");
    private readonly RadioButton QuickNeoForgeLoaderRadio;
    private readonly RadioButton QuickForgeLoaderRadio;
    private readonly RadioButton QuickFabricLoaderRadio;
    private readonly RadioButton QuickQuiltLoaderRadio;

    private readonly ListBox AdvancedVersionList = new() { SelectionMode = SelectionMode.Single };
    private readonly Entry AdvancedVersionSearchEntry = new() { PlaceholderText = "Search versions" };
    private readonly CheckButton AdvancedReleasesFilter = new("Releases") { Active = true };
    private readonly CheckButton AdvancedSnapshotsFilter = new("Snapshots");
    private readonly CheckButton AdvancedBetasFilter = new("Betas");
    private readonly CheckButton AdvancedAlphasFilter = new("Alphas");
    private readonly CheckButton AdvancedExperimentsFilter = new("Experiments");
    
    private readonly RadioButton AdvancedNoneLoaderRadio = new("None");
    private readonly RadioButton AdvancedNeoForgeLoaderRadio;
    private readonly RadioButton AdvancedForgeLoaderRadio;
    private readonly RadioButton AdvancedFabricLoaderRadio;
    private readonly RadioButton AdvancedQuiltLoaderRadio;

    private readonly ListBox AdvancedLoaderVersionList = new() { SelectionMode = SelectionMode.Single };
    private readonly Label AdvancedLoaderListStatus = new("Select a mod loader to see available versions.")
    {
        Xalign = 0,
        Wrap = true
    };

    private readonly Entry ImportArchiveEntry = new()
    {
        IsEditable = false,
        Hexpand = true,
        PlaceholderText = "Choose a ZIP or .mrpack archive"
    };

    private readonly Entry ImportFolderEntry = new()
    {
        IsEditable = false,
        Hexpand = true,
        PlaceholderText = "Choose an instance folder"
    };

    private readonly CheckButton ImportCopyFilesOption = new("Copy files into launcher storage") { Active = true };
    private readonly CheckButton ImportKeepMetadataOption = new("Keep launcher metadata when present") { Active = true };

    private readonly CatalogPageState CurseForgePage;
    private readonly CatalogPageState ModrinthPage;

    private AddInstancePageKind CurrentPage = AddInstancePageKind.QuickInstall;
    private string SelectedLoader = "None";
    private string? SelectedVersion;
    private string? SelectedLoaderVersion;
    private string? SelectedIconPath;
    private bool IsResettingState;
    private bool IsSubmitting;
    private bool IsShuttingDown;
    private bool VersionsLoaded;
    private bool VersionsLoading;
    private int LoaderVersionRequestGeneration;
    private IReadOnlyList<LoaderVersionSummary> AvailableLoaderVersions = [];
    private IReadOnlyList<MinecraftVersionOption> MinecraftVersions = DefaultMinecraftVersions;

    private static readonly LoaderChoice[] LoaderChoices =
    [
        new("none", "None"),
        new("neoforge", "NeoForge"),
        new("forge", "Forge"),
        new("fabric", "Fabric"),
        new("quilt", "Quilt")
    ];

    private static readonly CatalogSearchSort[] DefaultSorts =
    [
        CatalogSearchSort.Relevance,
        CatalogSearchSort.Downloads,
        CatalogSearchSort.Follows,
        CatalogSearchSort.Newest,
        CatalogSearchSort.Updated
    ];

    private static readonly MinecraftVersionOption[] DefaultMinecraftVersions =
    [
        new("1.21.5", "2026-03-24", ReleaseKind.Release),
        new("1.21.4", "2025-12-03", ReleaseKind.Release),
        new("1.21.3", "2025-10-23", ReleaseKind.Release),
        new("1.21.2", "2025-10-22", ReleaseKind.Release),
        new("1.21.1", "2025-08-09", ReleaseKind.Release),
        new("1.21", "2025-06-13", ReleaseKind.Release),
        new("25w14craftmine", "2025-04-01", ReleaseKind.Experiment),
        new("25w10a", "2025-03-05", ReleaseKind.Snapshot),
        new("1.20.6", "2024-04-29", ReleaseKind.Release),
        new("1.20.4", "2023-12-07", ReleaseKind.Release),
        new("1.20.2", "2023-09-21", ReleaseKind.Release),
        new("1.12.2", "2017-09-18", ReleaseKind.Release),
        new("b1.7.3", "2011-07-08", ReleaseKind.Beta),
        new("a1.2.6", "2010-12-03", ReleaseKind.Alpha)
    ];

    public AddInstanceWindow(
        SearchCatalogUseCase searchCatalogUseCase,
        GetCatalogProjectDetailsUseCase getCatalogProjectDetailsUseCase,
        GetCatalogProviderMetadataUseCase getCatalogProviderMetadataUseCase,
        ListCatalogFilesUseCase listCatalogFilesUseCase,
        ImportCatalogModpackUseCase importCatalogModpackUseCase,
        ImportArchiveInstanceUseCase importArchiveInstanceUseCase,
        ImportInstanceUseCase importInstanceUseCase,
        InstallInstanceUseCase installInstanceUseCase,
        IVersionManifestService versionManifestService,
        ILoaderMetadataService loaderMetadataService,
        IInstanceRepository instanceRepository,
        IInstanceContentMetadataService instanceContentMetadataService,
        ILauncherPaths launcherPaths,
        ProviderMediaCacheService providerMediaCacheService,
        InstanceBrowserRefreshService instanceBrowserRefreshService,
        ManualDownloadWindow manualDownloadWindow) : base("Add Instance")
    {
        SearchCatalogUseCase = searchCatalogUseCase ?? throw new ArgumentNullException(nameof(searchCatalogUseCase));
        GetCatalogProjectDetailsUseCase = getCatalogProjectDetailsUseCase ?? throw new ArgumentNullException(nameof(getCatalogProjectDetailsUseCase));
        GetCatalogProviderMetadataUseCase = getCatalogProviderMetadataUseCase ?? throw new ArgumentNullException(nameof(getCatalogProviderMetadataUseCase));
        ListCatalogFilesUseCase = listCatalogFilesUseCase ?? throw new ArgumentNullException(nameof(listCatalogFilesUseCase));
        ImportCatalogModpackUseCase = importCatalogModpackUseCase ?? throw new ArgumentNullException(nameof(importCatalogModpackUseCase));
        ImportArchiveInstanceUseCase = importArchiveInstanceUseCase ?? throw new ArgumentNullException(nameof(importArchiveInstanceUseCase));
        ImportInstanceUseCase = importInstanceUseCase ?? throw new ArgumentNullException(nameof(importInstanceUseCase));
        InstallInstanceUseCase = installInstanceUseCase ?? throw new ArgumentNullException(nameof(installInstanceUseCase));
        VersionManifestService = versionManifestService ?? throw new ArgumentNullException(nameof(versionManifestService));
        LoaderMetadataService = loaderMetadataService ?? throw new ArgumentNullException(nameof(loaderMetadataService));
        InstanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        InstanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        ProviderMediaCacheService = providerMediaCacheService ?? throw new ArgumentNullException(nameof(providerMediaCacheService));
        InstanceBrowserRefreshService = instanceBrowserRefreshService ?? throw new ArgumentNullException(nameof(instanceBrowserRefreshService));
        ManualDownloadWindow = manualDownloadWindow ?? throw new ArgumentNullException(nameof(manualDownloadWindow));

        QuickNeoForgeLoaderRadio = new RadioButton(QuickNoneLoaderRadio, "NeoForge");
        QuickForgeLoaderRadio = new RadioButton(QuickNoneLoaderRadio, "Forge");
        QuickFabricLoaderRadio = new RadioButton(QuickNoneLoaderRadio, "Fabric");
        QuickQuiltLoaderRadio = new RadioButton(QuickNoneLoaderRadio, "Quilt");

        AdvancedNeoForgeLoaderRadio = new RadioButton(AdvancedNoneLoaderRadio, "NeoForge");
        AdvancedForgeLoaderRadio = new RadioButton(AdvancedNoneLoaderRadio, "Forge");
        AdvancedFabricLoaderRadio = new RadioButton(AdvancedNoneLoaderRadio, "Fabric");
        AdvancedQuiltLoaderRadio = new RadioButton(AdvancedNoneLoaderRadio, "Quilt");

        CurseForgePage = CreateCatalogPageState(CatalogProvider.CurseForge, "Search CurseForge modpacks");
        ModrinthPage = CreateCatalogPageState(CatalogProvider.Modrinth, "Search Modrinth modpacks");

        SetDefaultSize(1180, 760);
        Resizable = true;
        WindowPosition = WindowPosition.Center;

        DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            if (!IsSubmitting && !IsShuttingDown)
            {
                ResetAndHide();
            }
        };

        Destroyed += (_, _) => ShutdownForApplicationExit();

        ApplyFieldStyles(NameEntry);
        ApplyFieldStyles(QuickVersionSearchEntry, isSearchField: true);
        ApplyFieldStyles(AdvancedVersionSearchEntry, isSearchField: true);
        ApplyFieldStyles(ImportArchiveEntry, isReadOnly: true);
        ApplyFieldStyles(ImportFolderEntry, isReadOnly: true);
        ApplyFieldStyles(CurseForgePage.SearchEntry, isSearchField: true);
        ApplyFieldStyles(ModrinthPage.SearchEntry, isSearchField: true);
        CurseForgePage.ProjectVersionButton.StyleContext.AddClass("popover-menu-button");
        ModrinthPage.ProjectVersionButton.StyleContext.AddClass("popover-menu-button");

        PrimaryActionButton.StyleContext.AddClass("primary-button");
        PrimaryActionButton.StyleContext.AddClass("add-instance-footer-primary");
        CancelButton.StyleContext.AddClass("action-button");
        CancelButton.StyleContext.AddClass("add-instance-footer-secondary");

        InstanceIconButton.WidthRequest = 72;
        InstanceIconButton.Vexpand = false;
        InstanceIconButton.Hexpand = false;
        InstanceIconButton.Halign = Align.Center;
        InstanceIconButton.Valign = Align.Center;
        InstanceIconButton.StyleContext.AddClass("add-instance-icon-placeholder");
        InstanceIconText.StyleContext.AddClass("add-instance-icon-text");
        InstanceIconImage.Halign = Align.Fill;
        InstanceIconImage.Valign = Align.Fill;
        InstanceIconText.Halign = Align.Center;
        InstanceIconText.Valign = Align.Center;
        InstanceIconOverlay.Add(InstanceIconImage);
        InstanceIconOverlay.AddOverlay(InstanceIconText);
        InstanceIconButton.Add(InstanceIconOverlay);
        InstanceIconButton.ButtonPressEvent += (_, _) => ChooseInstanceIcon();

        Titlebar = BuildHeaderBar();
        Add(BuildRoot());

        HookEvents();
        PopulateNavigation();
        PopulateVersionLists();
        ConfigureCatalogControls(CurseForgePage);
        ConfigureCatalogControls(ModrinthPage);
        UpdateInstanceIconPreview();
        UpdatePrimaryActionState();
    }

    public void PresentFrom(Gtk.Window owner)
    {
        if (IsShuttingDown)
        {
            return;
        }

        if (Visible)
        {
            Present();
            return;
        }

        ResetWindowState();
        ShowAll();
        Present();

        if (!VersionsLoaded && !VersionsLoading)
        {
            _ = LoadAvailableVersionsAsync();
        }

        if (CurrentPage == AddInstancePageKind.CurseForge)
        {
            _ = EnsureCatalogPageReadyAsync(CurseForgePage);
        }
        else if (CurrentPage == AddInstancePageKind.Modrinth)
        {
            _ = EnsureCatalogPageReadyAsync(ModrinthPage);
        }
    }

    public void ShutdownForApplicationExit()
    {
        if (IsShuttingDown)
        {
            return;
        }

        IsShuttingDown = true;
        ActiveOperationCancellationSource?.Cancel();
        ManualDownloadWindow.ShutdownForApplicationExit();
        CloseOperationDialog();
        CancelCatalogPageTimers(CurseForgePage);
        CancelCatalogPageTimers(ModrinthPage);
        CancelCatalogIconLoads(CurseForgePage);
        CancelCatalogIconLoads(ModrinthPage);
        CurseForgePage.RequestVersion++;
        CurseForgePage.DetailsRequestVersion++;
        ModrinthPage.RequestVersion++;
        ModrinthPage.DetailsRequestVersion++;
        CurseForgePage.DescriptionView.Unload();
        ModrinthPage.DescriptionView.Unload();
        if (Visible)
        {
            Destroy();
        }
    }

    private void HookEvents()
    {
        NameEntry.Changed += (_, _) => UpdatePrimaryActionState();
        QuickVersionSearchEntry.Changed += (_, _) => PopulateVersionLists();
        AdvancedVersionSearchEntry.Changed += (_, _) => PopulateVersionLists();

        QuickVersionList.RowSelected += (_, args) =>
        {
            SelectedVersion = (args.Row as VersionRow)?.Version.Version;
            if (CurrentPage == AddInstancePageKind.QuickInstall)
            {
                SyncVersionSelection(AdvancedVersionList);
            }
            UpdatePrimaryActionState();
        };

        AdvancedVersionList.RowSelected += (_, args) =>
        {
            SelectedVersion = (args.Row as VersionRow)?.Version.Version;
            if (CurrentPage == AddInstancePageKind.AdvancedInstall)
            {
                SyncVersionSelection(QuickVersionList);
                _ = RefreshAdvancedLoaderVersionsAsync();
            }
            UpdatePrimaryActionState();
        };

        AdvancedLoaderVersionList.RowSelected += (_, args) =>
        {
            SelectedLoaderVersion = (args.Row as LoaderVersionRow)?.Version.LoaderVersion;
            UpdatePrimaryActionState();
        };

        foreach (var filter in new[] { AdvancedReleasesFilter, AdvancedSnapshotsFilter, AdvancedBetasFilter, AdvancedAlphasFilter, AdvancedExperimentsFilter })
        {
            filter.StyleContext.AddClass("add-instance-filter-check");
            filter.Toggled += (_, _) => PopulateVersionLists();
        }

        foreach (var button in new[] { QuickNoneLoaderRadio, QuickNeoForgeLoaderRadio, QuickForgeLoaderRadio, QuickFabricLoaderRadio, QuickQuiltLoaderRadio })
        {
            button.Toggled += (_, _) => HandleLoaderChanged(true);
        }

        foreach (var button in new[] { AdvancedNoneLoaderRadio, AdvancedNeoForgeLoaderRadio, AdvancedForgeLoaderRadio, AdvancedFabricLoaderRadio, AdvancedQuiltLoaderRadio })
        {
            button.Toggled += (_, _) => HandleLoaderChanged(false);
        }

        CancelButton.Clicked += (_, _) =>
        {
            if (!IsSubmitting)
            {
                ResetAndHide();
            }
        };
        PrimaryActionButton.Clicked += (_, _) => BeginPrimaryAction();
    }

    private void SyncVersionSelection(ListBox targetList)
    {
        if (SelectedVersion is null)
        {
            targetList.UnselectAll();
            return;
        }

        foreach (Widget child in targetList.Children)
        {
            if (child is VersionRow row && row.Version.Version == SelectedVersion)
            {
                targetList.SelectRow(row);
                return;
            }
        }
    }

    private CatalogPageState CreateCatalogPageState(CatalogProvider provider, string searchPlaceholder)
    {
        var state = new CatalogPageState(provider)
        {
            SearchEntry =
            {
                PlaceholderText = searchPlaceholder
            }
        };

        state.SortButton.StyleContext.AddClass("popover-menu-button");
        state.CategoryButton.StyleContext.AddClass("popover-menu-button");
        state.VersionButton.StyleContext.AddClass("popover-menu-button");
        state.LoaderButton.StyleContext.AddClass("popover-menu-button");
        state.ProjectVersionButton.StyleContext.AddClass("popover-menu-button");

        state.SortButton.Clicked += (_, _) => TogglePopover(state.SortPopover);
        state.CategoryButton.Clicked += (_, _) => TogglePopover(state.CategoryPopover);
        state.VersionButton.Clicked += (_, _) => TogglePopover(state.VersionPopover);
        state.LoaderButton.Clicked += (_, _) => TogglePopover(state.LoaderPopover);

        state.SortPopover = CreatePopover(state.SortButton);
        state.CategoryPopover = CreatePopover(state.CategoryButton);
        state.VersionPopover = CreatePopover(state.VersionButton);
        state.LoaderPopover = CreatePopover(state.LoaderButton);
        state.ProjectVersionPopover = CreatePopover(state.ProjectVersionButton);

        state.SelectedSort = CatalogSearchSort.Relevance;
        RebuildSortPopover(state);
        RebuildFilterPopovers(state);
        RebuildProjectVersionPopover(state);
        return state;
    }

    private Widget BuildHeaderBar()
    {
        return LauncherGtkChrome.CreateHeaderBar("Add Instance", "Create, import, or browse launcher-ready instances.", allowWindowControls: true);
    }

    private Widget BuildRoot()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-shell");

        var layout = new Box(Orientation.Vertical, 0);
        layout.PackStart(BuildIdentitySection(), false, false, 0);
        layout.PackStart(BuildContentArea(), true, true, 0);
        layout.PackStart(BuildFooter(), false, false, 0);

        shell.Add(layout);
        return shell;
    }

    private Widget BuildIdentitySection()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("add-instance-identity-shell");

        var layout = new Box(Orientation.Horizontal, 14)
        {
            MarginTop = 14,
            MarginBottom = 14,
            MarginStart = 14,
            MarginEnd = 14
        };

        var fields = new Box(Orientation.Vertical, 0)
        {
            Hexpand = true
        };
        fields.PackStart(BuildLabeledField("Name", NameEntry, "Used in the instance list and launch actions."), false, false, 0);

        layout.PackStart(InstanceIconButton, false, false, 0);
        layout.PackStart(fields, true, true, 0);

        shell.Add(layout);
        return shell;
    }

    private Widget BuildContentArea()
    {
        var layout = new Box(Orientation.Horizontal, 0);
        layout.PackStart(BuildSourceRail(), false, false, 0);
        layout.PackStart(BuildContentShell(), true, true, 0);
        return layout;
    }

    private Widget BuildSourceRail()
    {
        var shell = new EventBox
        {
            WidthRequest = 186
        };
        shell.StyleContext.AddClass("settings-nav-shell");
        shell.StyleContext.AddClass("add-instance-nav-shell");

        var layout = new Box(Orientation.Vertical, 10)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 10,
            MarginEnd = 10
        };

        var title = new Label("Source")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-section-title");

        SourceList.StyleContext.AddClass("settings-nav-list");
        SourceList.StyleContext.AddClass("add-instance-source-list");
        SourceList.RowSelected += (_, args) =>
        {
            if (args.Row is SourceRow row)
            {
                SwitchToPage(row.PageKind);
            }
        };

        layout.PackStart(title, false, false, 0);
        layout.PackStart(SourceList, true, true, 0);

        shell.Add(layout);
        return shell;
    }

    private Widget BuildContentShell()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-content-shell");
        shell.StyleContext.AddClass("add-instance-content-shell");

        ContentStack.AddNamed(BuildQuickInstallPage(), AddInstancePageKind.QuickInstall.ToString());
        ContentStack.AddNamed(BuildAdvancedInstallPage(), AddInstancePageKind.AdvancedInstall.ToString());
        ContentStack.AddNamed(BuildPlatformPage(
            ModrinthPage,
            "Modrinth"), AddInstancePageKind.Modrinth.ToString());
        ContentStack.AddNamed(BuildPlatformPage(
            CurseForgePage,
            "CurseForge"), AddInstancePageKind.CurseForge.ToString());
        ContentStack.AddNamed(BuildImportPage(), AddInstancePageKind.Import.ToString());

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            Hexpand = true,
            Vexpand = true
        };
        scroller.Add(ContentStack);

        shell.Add(scroller);
        return shell;
    }

    private Widget BuildQuickInstallPage()
    {
        var page = CreatePageBox();
        var title = CreatePageTitle("Quick Install");

        var versionCard = BuildVersionBrowser(QuickVersionList, QuickVersionSearchEntry);
        
        var loadersCard = CreateCardShell();
        var loadersLayout = CreateCardContentBox();
        loadersLayout.PackStart(BuildSectionHeader("Mod Loader", string.Empty), false, false, 0);
        
        var loadersRow = new Box(Orientation.Horizontal, 14);
        foreach (var button in new[] { QuickNoneLoaderRadio, QuickNeoForgeLoaderRadio, QuickForgeLoaderRadio, QuickFabricLoaderRadio, QuickQuiltLoaderRadio })
        {
            loadersRow.PackStart(button, false, false, 0);
        }
        loadersLayout.PackStart(loadersRow, false, false, 0);
        loadersCard.Add(loadersLayout);

        page.PackStart(title, false, false, 0);
        page.PackStart(versionCard, true, true, 0);
        page.PackStart(loadersCard, false, false, 0);

        return page;
    }

    private Widget BuildAdvancedInstallPage()
    {
        var page = CreatePageBox();
        var title = CreatePageTitle("Advanced Install");

        var top = new Box(Orientation.Horizontal, 10)
        {
            Hexpand = true,
            Vexpand = true
        };
        top.StyleContext.AddClass("add-instance-pane");
        var versionBrowser = BuildVersionBrowser(AdvancedVersionList, AdvancedVersionSearchEntry);
        versionBrowser.Hexpand = true;
        top.PackStart(versionBrowser, true, true, 0);
        
        var filtersCard = CreateCardShell();
        filtersCard.WidthRequest = 188;
        var filtersLayout = CreateCardContentBox();
        filtersLayout.PackStart(BuildSectionHeader("Filter", string.Empty), false, false, 0);
        foreach (var filter in new[] { AdvancedReleasesFilter, AdvancedSnapshotsFilter, AdvancedBetasFilter, AdvancedAlphasFilter, AdvancedExperimentsFilter })
        {
            filtersLayout.PackStart(filter, false, false, 0);
        }
        filtersLayout.PackStart(new Box(Orientation.Vertical, 0), true, true, 0);
        filtersCard.Add(filtersLayout);
        top.PackStart(filtersCard, false, false, 0);

        var bottom = new Box(Orientation.Horizontal, 10)
        {
            Hexpand = true,
            Vexpand = true
        };
        bottom.StyleContext.AddClass("add-instance-pane");
        
        var loaderVersionCard = CreateCardShell();
        loaderVersionCard.Hexpand = true;
        var loaderVersionLayout = CreateCardContentBox();
        loaderVersionLayout.PackStart(BuildSectionHeader("Loader Version", string.Empty), false, false, 0);
        
        var loaderVersionHeader = new EventBox();
        loaderVersionHeader.StyleContext.AddClass("add-instance-list-header");
        loaderVersionHeader.Add(CreateVersionRowContent(
            CreateHeaderLabel("Version"),
            CreateHeaderLabel("Type"),
            CreateHeaderLabel(string.Empty)));
        loaderVersionLayout.PackStart(loaderVersionHeader, false, false, 0);

        AdvancedLoaderVersionList.StyleContext.AddClass("add-instance-version-list");
        var loaderScroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            Hexpand = true,
            Vexpand = true
        };
        loaderScroller.StyleContext.AddClass("add-instance-list-scroller");
        loaderScroller.Add(AdvancedLoaderVersionList);
        loaderVersionLayout.PackStart(loaderScroller, true, true, 0);
        AdvancedLoaderListStatus.StyleContext.AddClass("add-instance-loader-status");
        loaderVersionLayout.PackStart(AdvancedLoaderListStatus, false, false, 0);
        loaderVersionCard.Add(loaderVersionLayout);
        
        bottom.PackStart(loaderVersionCard, true, true, 0);

        var loaderChoiceCard = CreateCardShell();
        loaderChoiceCard.WidthRequest = 188;
        var loaderChoiceLayout = CreateCardContentBox();
        loaderChoiceLayout.StyleContext.AddClass("add-instance-loader-choice-shell");
        loaderChoiceLayout.PackStart(BuildSectionHeader("Mod Loader", string.Empty), false, false, 0);
        foreach (var button in new[] { AdvancedNoneLoaderRadio, AdvancedNeoForgeLoaderRadio, AdvancedForgeLoaderRadio, AdvancedFabricLoaderRadio, AdvancedQuiltLoaderRadio })
        {
            loaderChoiceLayout.PackStart(button, false, false, 0);
        }
        loaderChoiceLayout.PackStart(new Box(Orientation.Vertical, 0), true, true, 0);
        loaderChoiceCard.Add(loaderChoiceLayout);
        
        bottom.PackStart(loaderChoiceCard, false, false, 0);

        page.PackStart(title, false, false, 0);
        page.PackStart(top, true, true, 0);
        page.PackStart(bottom, true, true, 0);

        return page;
    }

    private Widget BuildVersionBrowser(ListBox versionList, Entry searchEntry)
    {
        var card = CreateCardShell();
        var layout = CreateCardContentBox();
        layout.PackStart(BuildSectionHeader("Version", string.Empty), false, false, 0);
        layout.PackStart(BuildVersionHeaderRow(), false, false, 0);

        versionList.StyleContext.AddClass("add-instance-version-list");

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("add-instance-list-scroller");
        scroller.Add(versionList);

        layout.PackStart(scroller, true, true, 0);
        layout.PackStart(BuildLabeledField("Search", searchEntry, null, additionalLabelClass: "add-instance-search-label"), false, false, 0);

        card.Add(layout);
        return card;
    }

    private Widget BuildVersionHeaderRow()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("add-instance-list-header");
        shell.Add(CreateVersionRowContent(
            CreateHeaderLabel("Version"),
            CreateHeaderLabel("Released"),
            CreateHeaderLabel("Type")));
        return shell;
    }



    private Widget BuildImportPage()
    {
        var page = CreatePageBox();

        page.PackStart(CreatePageTitle("Import"), false, false, 0);

        var pathsCard = CreateCardShell();
        var pathLayout = CreateCardContentBox(12);
        pathLayout.PackStart(BuildSectionHeader("Source", string.Empty), false, false, 0);
        pathLayout.PackStart(BuildPickerRow("Archive", ImportArchiveEntry, "Browse", BrowseArchive), false, false, 0);
        pathLayout.PackStart(BuildPickerRow("Folder", ImportFolderEntry, "Browse", BrowseFolder), false, false, 0);
        pathsCard.Add(pathLayout);

        var optionsCard = CreateCardShell();
        var optionsLayout = CreateCardContentBox(10);
        optionsLayout.PackStart(BuildSectionHeader("Options", string.Empty), false, false, 0);
        optionsLayout.PackStart(ImportCopyFilesOption, false, false, 0);
        optionsLayout.PackStart(ImportKeepMetadataOption, false, false, 0);
        optionsCard.Add(optionsLayout);

        page.PackStart(pathsCard, false, false, 0);
        page.PackStart(optionsCard, false, false, 0);
        page.PackStart(new Box(Orientation.Vertical, 0), true, true, 0);
        return page;
    }

    private Widget BuildPlatformPage(CatalogPageState state, string titleText)
    {
        state.DetailsTitleButton = new Button
        {
            Halign = Align.Start,
            Hexpand = false,
            CanFocus = false,
            FocusOnClick = false,
            Relief = ReliefStyle.None,
            Sensitive = false
        };
        state.DetailsTitleButton.StyleContext.AddClass("settings-page-title-link");

        state.DetailsTitle = new Label("Nothing selected")
        {
            Xalign = 0,
            Wrap = true
        };
        state.DetailsTitle.StyleContext.AddClass("settings-page-title");
        state.DetailsTitleButton.Add(state.DetailsTitle);
        state.DetailsTitleButton.Clicked += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(state.DetailsProjectUrl))
            {
                DesktopShell.OpenUrl(state.DetailsProjectUrl);
            }
        };

        state.DetailsMeta = new Label("Choose a project to inspect its details.")
        {
            Xalign = 0,
            Wrap = true
        };
        state.DetailsMeta.StyleContext.AddClass("settings-help");

        state.DescriptionView = new CatalogDescriptionView(ProviderMediaCacheService);
        state.DescriptionView.StyleContext.AddClass("catalog-description-view");
        state.ListStatus = new Label("Loading projects...")
        {
            Xalign = 0,
            Wrap = true
        };
        state.ListStatus.StyleContext.AddClass("settings-help");

        var page = CreatePageBox();
        page.PackStart(CreatePageTitle(titleText), false, false, 0);
        var searchCard = CreateCardShell();
        var searchLayout = CreateCardContentBox(10);
        searchLayout.PackStart(BuildSectionHeader("Search", string.Empty), false, false, 0);
        searchLayout.PackStart(state.SearchEntry, false, false, 0);

        var controlsRow = new Box(Orientation.Horizontal, 8);
        controlsRow.PackStart(state.SortButton, false, false, 0);
        controlsRow.PackStart(state.CategoryButton, false, false, 0);
        controlsRow.PackStart(state.VersionButton, false, false, 0);
        controlsRow.PackStart(state.LoaderButton, false, false, 0);
        controlsRow.PackStart(new Box(Orientation.Horizontal, 0), true, true, 0);
        searchLayout.PackStart(controlsRow, false, false, 0);
        searchCard.Add(searchLayout);

        var browserSplit = new Box(Orientation.Horizontal, 10)
        {
            Homogeneous = true,
            Hexpand = true,
            Vexpand = true
        };
        var listCard = BuildPackListCard(state);
        listCard.Hexpand = true;
        var detailsCard = BuildPackDetailsCard(state);
        detailsCard.Hexpand = true;
        browserSplit.PackStart(listCard, true, true, 0);
        browserSplit.PackStart(detailsCard, true, true, 0);

        page.PackStart(searchCard, false, false, 0);
        page.PackStart(browserSplit, true, true, 0);
        return page;
    }

    private Widget BuildPackListCard(CatalogPageState state)
    {
        var card = CreateCardShell();
        var layout = CreateCardContentBox(10);
        layout.PackStart(BuildSectionHeader("Packs", string.Empty), false, false, 0);

        state.PackList.StyleContext.AddClass("add-instance-pack-list");
        state.PackList.RowSelected += (_, args) =>
        {
            state.SelectedProject = (args.Row as PackRow)?.Project;
            UpdatePrimaryActionState();

            if (state.SelectedProject is not null)
            {
                _ = LoadProjectDetailsAsync(state, state.SelectedProject);
                _ = LoadProjectVersionsAsync(state, state.SelectedProject);
            }
            else
            {
                ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
                ResetVersionSelection(state, "Choose a project to load its available versions.");
            }
        };

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("add-instance-list-scroller");
        scroller.Add(state.PackList);
        state.PackScroller = scroller;
        scroller.Vadjustment.ValueChanged += (_, _) =>
        {
            MaybeLoadMoreCatalogResults(state);
            QueueVisiblePackIconLoads(state);
        };
        scroller.SizeAllocated += (_, _) => QueueVisiblePackIconLoads(state);

        layout.PackStart(scroller, true, true, 0);
        layout.PackStart(state.ListStatus, false, false, 0);
        card.Add(layout);
        return card;
    }

    private Widget BuildPackDetailsCard(CatalogPageState state)
    {
        var card = CreateCardShell();
        card.Hexpand = true;

        var layout = CreateCardContentBox(12);
        layout.PackStart(BuildSectionHeader("Details", string.Empty), false, false, 0);
        layout.PackStart(state.DetailsTitleButton, false, false, 0);
        layout.PackStart(state.DetailsMeta, false, false, 0);
        layout.PackStart(state.DescriptionView, true, true, 0);
        layout.PackStart(BuildLabeledField("Version", state.ProjectVersionButton, null, additionalLabelClass: "add-instance-search-label"), false, false, 0);
        layout.PackStart(state.VersionStatus, false, false, 0);
        card.Add(layout);
        return card;
    }

    private Widget BuildFooter()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("add-instance-footer");

        var layout = new Box(Orientation.Horizontal, 10)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 12,
            MarginEnd = 12
        };

        layout.PackStart(new Box(Orientation.Horizontal, 0), true, true, 0);
        layout.PackEnd(CancelButton, false, false, 0);
        layout.PackEnd(PrimaryActionButton, false, false, 0);

        shell.Add(layout);
        return shell;
    }

    private Widget BuildLabeledField(string labelText, Widget field, string? helperText = null, string? additionalLabelClass = null)
    {
        var box = new Box(Orientation.Vertical, 6)
        {
            Hexpand = true
        };
        box.StyleContext.AddClass("app-field-block");

        var label = new Label(labelText)
        {
            Xalign = 0,
            Yalign = 0.5f
        };
        label.StyleContext.AddClass("app-field-label");
        label.StyleContext.AddClass("add-instance-field-label");
        if (!string.IsNullOrWhiteSpace(additionalLabelClass))
        {
            label.StyleContext.AddClass(additionalLabelClass);
        }

        box.PackStart(label, false, false, 0);
        box.PackStart(field, false, false, 0);

        if (!string.IsNullOrWhiteSpace(helperText))
        {
            var helper = new Label(helperText)
            {
                Xalign = 0,
                Wrap = true
            };
            helper.StyleContext.AddClass("app-field-help");
            box.PackStart(helper, false, false, 0);
        }

        return box;
    }

    private EventBox CreateCardShell()
    {
        var card = new EventBox();
        card.StyleContext.AddClass("settings-card");
        card.StyleContext.AddClass("add-instance-card");
        return card;
    }

    private static Box CreateCardContentBox(int spacing = 10)
    {
        return new Box(Orientation.Vertical, spacing)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 10,
            MarginEnd = 10
        };
    }

    private static Box CreatePageBox()
    {
        var page = new Box(Orientation.Vertical, 10)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 10,
            MarginEnd = 10
        };
        page.StyleContext.AddClass("add-instance-page");
        return page;
    }

    private static Label CreatePageTitle(string text)
    {
        var label = new Label(text)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("settings-page-title");
        return label;
    }

    private Widget BuildSectionHeader(string titleText, string subtitleText)
    {
        var box = new Box(Orientation.Vertical, 4)
        {
            MarginBottom = 0
        };

        var title = new Label(titleText)
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-section-title");

        box.PackStart(title, false, false, 0);
        if (!string.IsNullOrWhiteSpace(subtitleText))
        {
            var subtitle = new Label(subtitleText)
            {
                Xalign = 0,
                Wrap = true
            };
            subtitle.StyleContext.AddClass("settings-help");
            box.PackStart(subtitle, false, false, 0);
        }

        return box;
    }

    private Widget BuildPickerRow(string labelText, Entry entry, string buttonText, System.Action onBrowse)
    {
        var fieldRow = new Grid
        {
            ColumnSpacing = 12,
            RowSpacing = 0
        };

        var button = new Button(buttonText);
        button.StyleContext.AddClass("toolbar-button");
        button.Clicked += (_, _) => onBrowse();

        fieldRow.Attach(entry, 0, 0, 1, 1);
        fieldRow.Attach(button, 1, 0, 1, 1);

        return BuildLabeledField(labelText, fieldRow);
    }

    private static Label CreateHeaderLabel(string text)
    {
        var label = new Label(text)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("add-instance-header-label");
        return label;
    }

    private static void ApplyFieldStyles(Widget widget, bool isSearchField = false, bool isReadOnly = false)
    {
        if (widget is Entry entry)
        {
            entry.StyleContext.AddClass("app-field");

            if (isSearchField)
            {
                entry.StyleContext.AddClass("app-search-field");
            }

            if (isReadOnly)
            {
                entry.StyleContext.AddClass("app-field-readonly");
            }

            return;
        }

        widget.StyleContext.AddClass("app-combo-field");
    }

    private void PopulateNavigation()
    {
        foreach (var item in new[]
                 {
                     (AddInstancePageKind.QuickInstall, "Quick Install"),
                     (AddInstancePageKind.AdvancedInstall, "Advanced Install"),
                     (AddInstancePageKind.Modrinth, "Modrinth"),
                     (AddInstancePageKind.CurseForge, "CurseForge"),
                     (AddInstancePageKind.Import, "Import")
                 })
        {
            SourceList.Add(new SourceRow(item.Item1, item.Item2));
        }

        SourceList.ShowAll();
        SourceList.SelectRow(SourceList.GetRowAtIndex(0));
        SwitchToPage(AddInstancePageKind.QuickInstall);
    }

    private async Task LoadAvailableVersionsAsync()
    {
        VersionsLoading = true;
        var result = await VersionManifestService.GetAvailableVersionsAsync(CancellationToken.None).ConfigureAwait(false);
        VersionsLoading = false;

        if (result.IsFailure)
        {
            return;
        }

        var versions = result.Value
            .Select(MapVersionOption)
            .ToArray();
        if (versions.Length == 0)
        {
            return;
        }

        Gtk.Application.Invoke((_, _) =>
        {
            MinecraftVersions = versions;
            VersionsLoaded = true;
            PopulateVersionLists();
            PopulateGameVersionOptions(CurseForgePage, versions.Where(static version => version.Kind == ReleaseKind.Release).Select(static version => version.Version).ToArray());
            PopulateGameVersionOptions(ModrinthPage, versions.Where(static version => version.Kind == ReleaseKind.Release).Select(static version => version.Version).ToArray());
        });
    }

    private void PopulateVersionLists()
    {
        PopulateSpecificVersionList(QuickVersionList, QuickVersionSearchEntry.Text, isQuickInstall: true);
        PopulateSpecificVersionList(AdvancedVersionList, AdvancedVersionSearchEntry.Text, isQuickInstall: false);
        UpdatePrimaryActionState();
    }

    private void PopulateSpecificVersionList(ListBox targetList, string? searchText, bool isQuickInstall)
    {
        foreach (var row in targetList.Children.ToArray())
        {
            targetList.Remove(row);
            row.Destroy();
        }

        var normalizedSearch = searchText?.Trim() ?? string.Empty;

        var visibleVersions = MinecraftVersions
            .Where(version => IsReleaseKindEnabled(version.Kind, isQuickInstall))
            .Where(version => string.IsNullOrWhiteSpace(normalizedSearch)
                || version.Version.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        var hasVisibleSelectedVersion = visibleVersions.Any(version =>
            string.Equals(version.Version, SelectedVersion, StringComparison.OrdinalIgnoreCase));

        for (var index = 0; index < visibleVersions.Length; index++)
        {
            var version = visibleVersions[index];
            var row = new VersionRow(version, index % 2 == 0);
            targetList.Add(row);

            if (SelectedVersion == version.Version)
            {
                targetList.SelectRow(row);
            }
        }

        if (!hasVisibleSelectedVersion)
        {
            targetList.UnselectAll();
            if ((isQuickInstall && CurrentPage == AddInstancePageKind.QuickInstall) ||
                (!isQuickInstall && CurrentPage == AddInstancePageKind.AdvancedInstall))
            {
                SelectedVersion = null;
            }
        }

        targetList.ShowAll();
    }

    private bool IsReleaseKindEnabled(ReleaseKind kind, bool isQuickInstall)
    {
        if (isQuickInstall) return kind == ReleaseKind.Release;

        return kind switch
        {
            ReleaseKind.Release => AdvancedReleasesFilter.Active,
            ReleaseKind.Snapshot => AdvancedSnapshotsFilter.Active,
            ReleaseKind.Beta => AdvancedBetasFilter.Active,
            ReleaseKind.Alpha => AdvancedAlphasFilter.Active,
            ReleaseKind.Experiment => AdvancedExperimentsFilter.Active,
            _ => true
        };
    }

    private void HandleLoaderChanged(bool isQuickInstall)
    {
        var activeRadioButton = isQuickInstall
            ? new[] { QuickNoneLoaderRadio, QuickNeoForgeLoaderRadio, QuickForgeLoaderRadio, QuickFabricLoaderRadio, QuickQuiltLoaderRadio }.FirstOrDefault(button => button.Active)
            : new[] { AdvancedNoneLoaderRadio, AdvancedNeoForgeLoaderRadio, AdvancedForgeLoaderRadio, AdvancedFabricLoaderRadio, AdvancedQuiltLoaderRadio }.FirstOrDefault(button => button.Active);

        var nextLoader = activeRadioButton?.Label ?? "None";
        if (SelectedLoader == nextLoader)
        {
            return;
        }

        SelectedLoader = nextLoader;
        SelectedLoaderVersion = null;
        SyncLoaderRadios();

        if (CurrentPage == AddInstancePageKind.AdvancedInstall)
        {
            _ = RefreshAdvancedLoaderVersionsAsync();
        }
        
        UpdatePrimaryActionState();
    }

    private void SyncLoaderRadios()
    {
        var quickTarget = new[] { QuickNoneLoaderRadio, QuickNeoForgeLoaderRadio, QuickForgeLoaderRadio, QuickFabricLoaderRadio, QuickQuiltLoaderRadio }
            .FirstOrDefault(r => r.Label == SelectedLoader);
        var advancedTarget = new[] { AdvancedNoneLoaderRadio, AdvancedNeoForgeLoaderRadio, AdvancedForgeLoaderRadio, AdvancedFabricLoaderRadio, AdvancedQuiltLoaderRadio }
            .FirstOrDefault(r => r.Label == SelectedLoader);

        if (quickTarget is not null) quickTarget.Active = true;
        if (advancedTarget is not null) advancedTarget.Active = true;
    }

    private async Task RefreshAdvancedLoaderVersionsAsync()
    {
        LoaderVersionRequestGeneration++;
        var requestGeneration = LoaderVersionRequestGeneration;
        SelectedLoaderVersion = null;

        Gtk.Application.Invoke((_, _) =>
        {
            AdvancedLoaderListStatus.Text = "Loading versions...";
            foreach (var child in AdvancedLoaderVersionList.Children)
            {
                AdvancedLoaderVersionList.Remove(child);
                child.Destroy();
            }
        });

        if (SelectedLoader == "None" || string.IsNullOrWhiteSpace(SelectedVersion))
        {
             Gtk.Application.Invoke((_, _) =>
             {
                 if (requestGeneration == LoaderVersionRequestGeneration)
                 {
                     AdvancedLoaderListStatus.Text = SelectedLoader == "None" ? "Select a mod loader to see available versions." : "Select a Minecraft version first.";
                     UpdatePrimaryActionState();
                 }
             });
             return;
        }

        var loaderType = MapSelectedLoaderType(SelectedLoader);
        var result = await LoaderMetadataService.GetLoaderVersionsAsync(loaderType, BlockiumLauncher.Domain.ValueObjects.VersionId.Parse(SelectedVersion), CancellationToken.None).ConfigureAwait(false);

        Gtk.Application.Invoke((_, _) =>
        {
            if (requestGeneration != LoaderVersionRequestGeneration)
            {
                return;
            }

            if (result.IsFailure)
            {
                AdvancedLoaderListStatus.Text = $"Failed to load versions: {result.Error.Message}";
                UpdatePrimaryActionState();
                return;
            }

            AvailableLoaderVersions = result.Value
                .OrderByDescending(v => v.IsStable)
                .ThenByDescending(v => v.LoaderVersion, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (AvailableLoaderVersions.Count == 0)
            {
                AdvancedLoaderListStatus.Text = "No compatible loader versions found.";
                UpdatePrimaryActionState();
                return;
            }

            AdvancedLoaderListStatus.Text = string.Empty;
            for (var index = 0; index < AvailableLoaderVersions.Count; index++)
            {
                var v = AvailableLoaderVersions[index];
                var row = new LoaderVersionRow(v, index % 2 == 0);
                AdvancedLoaderVersionList.Add(row);
            }

            AdvancedLoaderVersionList.ShowAll();
            if (AdvancedLoaderVersionList.GetRowAtIndex(0) is LoaderVersionRow firstRow)
            {
                AdvancedLoaderVersionList.SelectRow(firstRow);
                SelectedLoaderVersion = firstRow.Version.LoaderVersion;
            }
            UpdatePrimaryActionState();
        });
    }

    private void ConfigureCatalogControls(CatalogPageState state)
    {
        state.SearchEntry.Changed += (_, _) => QueueCatalogRefresh(state);
        state.ProjectVersionButton.Clicked += (_, _) => TogglePopover(state.ProjectVersionPopover);
        state.VersionStatus.StyleContext.AddClass("settings-help");

        PopulateSortOptions(state, DefaultSorts);
        PopulateGameVersionOptions(state);
        PopulateLoaderOptions(state, []);
        PopulateCategoryOptions(state, []);
        ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
        ResetVersionSelection(state, "Choose a project to load its available versions.");
    }

    private async Task EnsureCatalogPageReadyAsync(CatalogPageState state)
    {
        var metadataTask = !state.MetadataLoaded && !state.MetadataLoading
            ? LoadCatalogMetadataAsync(state)
            : Task.CompletedTask;
        var resultsTask = !state.HasLoadedResults && !state.IsSearching
            ? RefreshCatalogResultsAsync(state)
            : Task.CompletedTask;

        await Task.WhenAll(metadataTask, resultsTask).ConfigureAwait(false);
    }

    private void ResetAndHide()
    {
        CloseOperationDialog();
        ResetWindowState();
        Destroy();
        LauncherWindowMemory.RequestAggressiveCleanup();
    }

    private void ResetWindowState()
    {
        IsResettingState = true;
        try
        {
            NameEntry.Text = string.Empty;
            SelectedIconPath = null;
            SelectedVersion = null;
            SelectedLoaderVersion = null;
            SelectedLoader = "None";
            QuickVersionSearchEntry.Text = string.Empty;
            AdvancedVersionSearchEntry.Text = string.Empty;
            AdvancedReleasesFilter.Active = true;
            AdvancedSnapshotsFilter.Active = false;
            AdvancedBetasFilter.Active = false;
            AdvancedAlphasFilter.Active = false;
            AdvancedExperimentsFilter.Active = false;
            ImportArchiveEntry.Text = string.Empty;
            ImportFolderEntry.Text = string.Empty;
            ImportCopyFilesOption.Active = true;
            ImportKeepMetadataOption.Active = true;
            QuickNoneLoaderRadio.Active = true;
            AdvancedNoneLoaderRadio.Active = true;

            ResetCatalogPageState(CurseForgePage);
            ResetCatalogPageState(ModrinthPage);

            PopulateVersionLists();
            SwitchToPage(AddInstancePageKind.QuickInstall);
            if (SourceList.GetRowAtIndex(0) is ListBoxRow firstRow)
            {
                SourceList.SelectRow(firstRow);
            }

            UpdatePrimaryActionState();
        }
        finally
        {
            IsResettingState = false;
        }
    }

    private void ResetCatalogPageState(CatalogPageState state)
    {
        CancelCatalogPageTimers(state);
        CancelCatalogIconLoads(state);

        state.SortPopover.Hide();
        state.CategoryPopover.Hide();
        state.VersionPopover.Hide();
        state.LoaderPopover.Hide();
        state.ProjectVersionPopover.Hide();

        state.SearchEntry.Text = string.Empty;
        state.SelectedSort = CatalogSearchSort.Relevance;
        state.SelectedGameVersions.Clear();
        state.SelectedLoaders.Clear();
        state.SelectedCategories.Clear();
        state.SelectedProject = null;
        state.SelectedFileId = null;
        state.AvailableFiles = [];
        state.DetailsProjectUrl = null;
        state.Results = [];
        state.Metadata = null;
        state.MetadataLoaded = false;
        state.MetadataLoading = false;
        state.AvailableSorts = [];
        state.AvailableGameVersions = [];
        state.AvailableLoaders = [];
        state.AvailableCategories = [];
        state.HasLoadedResults = false;
        state.IsSearching = false;
        state.CurrentOffset = 0;
        state.PageSize = CatalogPageSize;
        state.HasMoreResults = true;
        state.RequestVersion++;
        state.DetailsRequestVersion++;
        state.FilesRequestVersion++;
        PopulateSortOptions(state, DefaultSorts);
        PopulateGameVersionOptions(state, null);
        PopulateLoaderOptions(state, []);
        PopulateCategoryOptions(state, []);
        PopulatePackList(state, []);
        state.ListStatus.Text = "Search the provider catalog to load projects.";
        ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
        ResetVersionSelection(state, "Choose a project to load its available versions.");
    }

    private static void CancelCatalogPageTimers(CatalogPageState state)
    {
        if (state.SearchDebounceId is not null)
        {
            GLib.Source.Remove(state.SearchDebounceId.Value);
            state.SearchDebounceId = null;
        }
    }

    private static void CancelCatalogIconLoads(CatalogPageState state)
    {
        if (state.VisibleIconRefreshId is not null)
        {
            GLib.Source.Remove(state.VisibleIconRefreshId.Value);
            state.VisibleIconRefreshId = null;
        }

        state.IconLoadCancellationSource?.Cancel();
        state.IconLoadCancellationSource?.Dispose();
        state.IconLoadCancellationSource = null;
        state.LoadedIconProjectIds.Clear();
        state.InFlightIconProjectIds.Clear();
    }

    private async Task LoadCatalogMetadataAsync(CatalogPageState state)
    {
        state.MetadataLoading = true;
        Gtk.Application.Invoke((_, _) => state.ListStatus.Text = "Loading provider filters...");

        var result = await GetCatalogProviderMetadataUseCase.ExecuteAsync(new GetCatalogProviderMetadataRequest
        {
            Provider = state.Provider,
            ContentType = CatalogContentType.Modpack
        }).ConfigureAwait(false);

        state.MetadataLoading = false;
        state.MetadataLoaded = result.IsSuccess;

        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsSuccess)
            {
                state.Metadata = result.Value;
                PopulateSortOptions(state, result.Value.SortOptions.Count > 0 ? result.Value.SortOptions : DefaultSorts);
                PopulateLoaderOptions(state, result.Value.Loaders);
                PopulateCategoryOptions(state, result.Value.Categories);
                if (result.Value.GameVersions.Count > 0)
                {
                    PopulateGameVersionOptions(state, result.Value.GameVersions);
                }
                RebuildFilterPopovers(state);
            }
            else
            {
                PopulateSortOptions(state, DefaultSorts);
                PopulateLoaderOptions(state, []);
                PopulateCategoryOptions(state, []);
                state.ListStatus.Text = result.Error.Message;
                ResetProjectDetails(state, "Catalog unavailable", result.Error.Message);
            }
        });
    }

    private static Popover CreatePopover(Widget relativeTo)
    {
        var popover = new Popover(relativeTo)
        {
            BorderWidth = 10,
            Position = PositionType.Bottom
        };
        return popover;
    }

    private static void TogglePopover(Popover popover)
    {
        if (popover.IsVisible)
        {
            popover.Hide();
        }
        else
        {
            popover.ShowAll();
        }
    }

    private void RebuildSortPopover(CatalogPageState state)
    {
        state.SortPopover.Hide();
        ReplacePopoverContent(state.SortPopover, BuildSortPopoverContent(state));
    }

    private void RebuildProjectVersionPopover(CatalogPageState state)
    {
        state.ProjectVersionPopover.Hide();
        ReplacePopoverContent(state.ProjectVersionPopover, BuildProjectVersionPopoverContent(state));
    }

    private Widget BuildSortPopoverContent(CatalogPageState state)
    {
        var content = new Box(Orientation.Vertical, 8);
        content.StyleContext.AddClass("popover-content");

        var title = new Label("Sort by")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);

        RadioButton? group = null;
        foreach (var sort in state.AvailableSorts)
        {
            var button = group is null ? new RadioButton(GetSortLabel(sort)) : new RadioButton(group, GetSortLabel(sort));
            group ??= button;
            button.Active = sort == state.SelectedSort;
            button.Toggled += (_, _) =>
            {
                if (!button.Active)
                {
                    return;
                }

                state.SelectedSort = sort;
                UpdateSortButtonLabel(state);
                QueueCatalogRefresh(state, debounce: false);
                state.SortPopover.Hide();
            };
            content.PackStart(button, false, false, 0);
        }

        return content;
    }

    private static Box BuildSpecificFilterPopoverOuter(string titleText)
    {
        var outer = new Box(Orientation.Vertical, 10);
        outer.StyleContext.AddClass("popover-content");

        var title = new Label(titleText)
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        outer.PackStart(title, false, false, 0);
        return outer;
    }

    private static ScrolledWindow CreateFilterScroller(Widget content, int itemCount)
    {
        var visibleItems = Math.Clamp(itemCount, 1, 8);
        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            HeightRequest = 28 + (visibleItems * 32),
            WidthRequest = 180
        };
        scroller.Add(content);
        return scroller;
    }

    private Widget BuildCategoryPopoverContent(CatalogPageState state)
    {
        var outer = BuildSpecificFilterPopoverOuter("Categories");
        outer.PackStart(CreateFilterScroller(BuildFilterSection(state.AvailableCategories, state.SelectedCategories, state), state.AvailableCategories.Count), true, true, 0);
        return outer;
    }

    private Widget BuildVersionPopoverContent(CatalogPageState state)
    {
        var outer = BuildSpecificFilterPopoverOuter("Versions");
        outer.PackStart(CreateFilterScroller(BuildFilterSection(state.AvailableGameVersions, state.SelectedGameVersions, state), state.AvailableGameVersions.Count), true, true, 0);
        return outer;
    }

    private Widget BuildLoaderPopoverContent(CatalogPageState state)
    {
        var outer = BuildSpecificFilterPopoverOuter("Loaders");
        outer.PackStart(CreateFilterScroller(BuildFilterSection(state.AvailableLoaders, state.SelectedLoaders, state, ToDisplayName), state.AvailableLoaders.Count), true, true, 0);
        return outer;
    }

    private void RebuildFilterPopovers(CatalogPageState state)
    {
        state.CategoryPopover.Hide();
        state.VersionPopover.Hide();
        state.LoaderPopover.Hide();
        ReplacePopoverContent(state.CategoryPopover, BuildCategoryPopoverContent(state));
        ReplacePopoverContent(state.VersionPopover, BuildVersionPopoverContent(state));
        ReplacePopoverContent(state.LoaderPopover, BuildLoaderPopoverContent(state));
    }

    private Widget BuildFilterSection(
        IReadOnlyList<string> values,
        HashSet<string> selectedValues,
        CatalogPageState state,
        Func<string, string>? formatter = null)
    {
        var section = new Box(Orientation.Vertical, 6);

        if (values.Count == 0)
        {
            var empty = new Label("No options available")
            {
                Xalign = 0
            };
            empty.StyleContext.AddClass("settings-help");
            section.PackStart(empty, false, false, 0);
            return section;
        }

        foreach (var value in values)
        {
            var button = new CheckButton(formatter is null ? value : formatter(value))
            {
                Active = selectedValues.Contains(value)
            };
            button.StyleContext.AddClass("popover-check");
            button.Toggled += (_, _) =>
            {
                if (button.Active)
                {
                    selectedValues.Add(value);
                }
                else
                {
                    selectedValues.Remove(value);
                }

                UpdateFilterButtonLabels(state);
                QueueCatalogRefresh(state, debounce: false);
            };
            section.PackStart(button, false, false, 0);
        }

        return section;
    }

    private static void ReplacePopoverContent(Popover popover, Widget child)
    {
        popover.Hide();
        if (popover.Child is Widget existingChild)
        {
            popover.Remove(existingChild);
            existingChild.Destroy();
        }

        popover.Add(child);
    }

    private static void UpdateFilterButtonLabels(CatalogPageState state)
    {
        state.CategoryButton.Label = state.SelectedCategories.Count == 0 ? "Category" : $"Category ({state.SelectedCategories.Count})";
        state.VersionButton.Label = state.SelectedGameVersions.Count == 0 ? "Version" : $"Version ({state.SelectedGameVersions.Count})";
        state.LoaderButton.Label = state.SelectedLoaders.Count == 0 ? "Loader" : $"Loader ({state.SelectedLoaders.Count})";
    }

    private static void UpdateFilterButtonLabel(CatalogPageState state)
    {
        // Legacy, redirect to new versioned one
        UpdateFilterButtonLabels(state);
    }

    private static void UpdateSortButtonLabel(CatalogPageState state)
    {
        state.SortButton.Label = $"Sort: {GetSortLabel(state.SelectedSort)}";
    }

    private static void UpdateProjectVersionButtonLabel(CatalogPageState state)
    {
        if (!state.ProjectVersionButton.Sensitive || state.AvailableFiles.Count == 0 || string.IsNullOrWhiteSpace(state.SelectedFileId))
        {
            state.ProjectVersionButton.Label = "Select version";
            return;
        }

        var selectedFile = state.AvailableFiles.FirstOrDefault(file => string.Equals(file.FileId, state.SelectedFileId, StringComparison.Ordinal));
        state.ProjectVersionButton.Label = selectedFile is null
            ? "Select version"
            : AbbreviateVersionLabel(LauncherStructuredList.BuildSimpleCatalogFileLabel(selectedFile));
    }

    private void QueueCatalogRefresh(CatalogPageState state, bool debounce = true)
    {
        if (IsResettingState)
        {
            return;
        }

        if (state.SearchDebounceId is not null)
        {
            GLib.Source.Remove(state.SearchDebounceId.Value);
            state.SearchDebounceId = null;
        }

        if (!debounce)
        {
            _ = RefreshCatalogResultsAsync(state);
            return;
        }

        state.SearchDebounceId = GLib.Timeout.Add(250, () =>
        {
            state.SearchDebounceId = null;
            _ = RefreshCatalogResultsAsync(state);
            return false;
        });
    }

    private async Task RefreshCatalogResultsAsync(CatalogPageState state)
    {
        ResetCatalogPagingState(state);
        await LoadCatalogResultsAsync(state, append: false).ConfigureAwait(false);
    }

    private async Task LoadCatalogResultsAsync(CatalogPageState state, bool append)
    {
        if (state.IsSearching)
        {
            return;
        }

        if (append && (!state.HasLoadedResults || !state.HasMoreResults))
        {
            return;
        }

        state.IsSearching = true;
        state.RequestVersion++;
        var requestVersion = state.RequestVersion;
        state.IconLoadCancellationSource?.Cancel();
        state.IconLoadCancellationSource?.Dispose();
        state.IconLoadCancellationSource = new CancellationTokenSource();
        state.InFlightIconProjectIds.Clear();
        if (!append)
        {
            state.LoadedIconProjectIds.Clear();
        }
        var requestOffset = append ? state.CurrentOffset : 0;
        var preferredProjectId = state.SelectedProject?.ProjectId;

        Gtk.Application.Invoke((_, _) => state.ListStatus.Text = append ? "Loading more projects..." : "Loading projects...");

        var result = await SearchCatalogUseCase.ExecuteAsync(new SearchCatalogRequest
        {
            Provider = state.Provider,
            ContentType = CatalogContentType.Modpack,
            Query = string.IsNullOrWhiteSpace(state.SearchEntry.Text) ? null : state.SearchEntry.Text.Trim(),
            GameVersions = state.SelectedGameVersions.ToArray(),
            Loaders = state.SelectedLoaders.ToArray(),
            Categories = state.SelectedCategories.ToArray(),
            Sort = state.SelectedSort,
            Limit = state.PageSize,
            Offset = requestOffset
        }).ConfigureAwait(false);

        if (requestVersion != state.RequestVersion)
        {
            return;
        }

        state.IsSearching = false;
        state.HasLoadedResults = true;

        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsFailure)
            {
                if (!append)
                {
                    PopulatePackList(state, []);
                }
                state.ListStatus.Text = result.Error.Message;
                if (!append)
                {
                    ResetProjectDetails(state, "Catalog unavailable", result.Error.Message);
                }
                return;
            }

            var mergedResults = append
                ? MergeCatalogResults(state.Results, result.Value)
                : result.Value;

            state.HasMoreResults = result.Value.Count >= state.PageSize;
            state.CurrentOffset = mergedResults.Count;

            PopulatePackList(state, mergedResults, append, preferredProjectId);
            state.ListStatus.Text = mergedResults.Count == 0
                ? "No projects match the current search."
                : state.HasMoreResults
                    ? $"{mergedResults.Count} project(s) loaded."
                    : $"{mergedResults.Count} project(s) loaded. End of results.";
            QueueVisiblePackIconLoads(state);
        });
    }

    private static IReadOnlyList<CatalogProjectSummary> MergeCatalogResults(
        IReadOnlyList<CatalogProjectSummary> existing,
        IReadOnlyList<CatalogProjectSummary> incoming)
    {
        if (existing.Count == 0)
        {
            return incoming;
        }

        var merged = new List<CatalogProjectSummary>(existing.Count + incoming.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var project in existing)
        {
            if (seen.Add(project.ProjectId))
            {
                merged.Add(project);
            }
        }

        foreach (var project in incoming)
        {
            if (seen.Add(project.ProjectId))
            {
                merged.Add(project);
            }
        }

        return merged;
    }

    private static void ResetCatalogPagingState(CatalogPageState state)
    {
        state.CurrentOffset = 0;
        state.HasMoreResults = true;
    }

    private void PopulatePackList(CatalogPageState state, IReadOnlyList<CatalogProjectSummary> projects, bool append = false, string? preferredProjectId = null)
    {
        if (!append)
        {
            foreach (var child in state.PackList.Children.ToArray())
            {
                state.PackList.Remove(child);
                child.Destroy();
            }
        }

        var startIndex = append ? state.Results.Count : 0;
        state.Results = projects;

        for (var index = startIndex; index < projects.Count; index++)
        {
            var row = new PackRow(projects[index], index % 2 == 0);
            state.PackList.Add(row);
        }

        state.PackList.ShowAll();

        var preferredRow = FindPackRow(state, preferredProjectId);
        if (preferredRow is not null)
        {
            state.PackList.SelectRow(preferredRow);
        }
        else if (projects.Count == 0)
        {
            state.SelectedProject = null;
            ResetProjectDetails(state, "Nothing selected", "No project matches the current filters.");
        }
        else if (!append)
        {
            state.PackList.UnselectAll();
            state.SelectedProject = null;
            ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
        }

        UpdatePrimaryActionState();
    }

    private void QueueVisiblePackIconLoads(CatalogPageState state)
    {
        if (state.VisibleIconRefreshId is not null)
        {
            return;
        }

        state.VisibleIconRefreshId = GLib.Idle.Add(() =>
        {
            state.VisibleIconRefreshId = null;
            LoadVisiblePackIcons(state);
            return false;
        });
    }

    private void LoadVisiblePackIcons(CatalogPageState state)
    {
        if (state.PackScroller?.Vadjustment is not Adjustment adjustment)
        {
            return;
        }

        var requestVersion = state.RequestVersion;
        var cancellationToken = state.IconLoadCancellationSource?.Token ?? CancellationToken.None;
        var viewportTop = (int)Math.Max(0d, adjustment.Value - 120d);
        var viewportBottom = (int)Math.Max(adjustment.PageSize, adjustment.Value + adjustment.PageSize + 120d);
        var fallbackVisibleBudget = Math.Max(8, (int)Math.Ceiling(adjustment.PageSize / 64d) + 4);
        var fallbackIndex = 0;

        foreach (var child in state.PackList.Children)
        {
            if (child is not PackRow row ||
                row.IsDisposed ||
                string.IsNullOrWhiteSpace(row.Project.IconUrl))
            {
                continue;
            }

            if (state.LoadedIconProjectIds.Contains(row.Project.ProjectId) ||
                state.InFlightIconProjectIds.Contains(row.Project.ProjectId))
            {
                fallbackIndex++;
                continue;
            }

            var allocation = row.Allocation;
            var shouldLoad = allocation.Height > 0
                ? allocation.Y + allocation.Height >= viewportTop && allocation.Y <= viewportBottom
                : fallbackIndex < fallbackVisibleBudget;

            fallbackIndex++;
            if (!shouldLoad)
            {
                if (row.HasIcon)
                {
                    row.UnloadIcon();
                    state.LoadedIconProjectIds.Remove(row.Project.ProjectId);
                }
                continue;
            }

            state.InFlightIconProjectIds.Add(row.Project.ProjectId);
            _ = LoadPackIconAsync(state, row, requestVersion, cancellationToken);
        }
    }

    private static PackRow? FindPackRow(CatalogPageState state, string? projectId)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            return null;
        }

        foreach (var child in state.PackList.Children)
        {
            if (child is PackRow row &&
                string.Equals(row.Project.ProjectId, projectId, StringComparison.OrdinalIgnoreCase))
            {
                return row;
            }
        }

        return null;
    }

    private void MaybeLoadMoreCatalogResults(CatalogPageState state)
    {
        if (state.PackScroller?.Vadjustment is not Adjustment adjustment)
        {
            return;
        }

        if (state.IsSearching || !state.HasLoadedResults || !state.HasMoreResults || state.Results.Count == 0)
        {
            return;
        }

        var remaining = adjustment.Upper - (adjustment.Value + adjustment.PageSize);
        if (remaining > CatalogScrollLoadThreshold)
        {
            return;
        }

        _ = LoadCatalogResultsAsync(state, append: true);
    }

    private async Task LoadProjectDetailsAsync(CatalogPageState state, CatalogProjectSummary project)
    {
        var requestVersion = ++state.DetailsRequestVersion;

        Gtk.Application.Invoke((_, _) =>
        {
            SetProjectDetailsTitle(state, project.Title, project.ProjectUrl);
            state.DetailsMeta.Text = "Loading project details...";
            state.DescriptionView.SetContent(project.Description, CatalogDescriptionFormat.PlainText, project.ProjectUrl);
        });

        var result = await GetCatalogProjectDetailsUseCase.ExecuteAsync(new GetCatalogProjectDetailsRequest
        {
            Provider = state.Provider,
            ContentType = CatalogContentType.Modpack,
            ProjectId = project.ProjectId
        }).ConfigureAwait(false);

        if (requestVersion != state.DetailsRequestVersion)
        {
            return;
        }

        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsFailure)
            {
                SetProjectDetailsTitle(state, project.Title, project.ProjectUrl);
                state.DetailsMeta.Text = BuildSummaryMeta(project);
                state.DescriptionView.SetContent(project.Description, CatalogDescriptionFormat.PlainText, project.ProjectUrl);
                return;
            }

            var details = result.Value;
            SetProjectDetailsTitle(state, details.Title, details.ProjectUrl);
            state.DetailsMeta.Text = BuildDetailsMeta(details);
            state.DescriptionView.SetContent(
                string.IsNullOrWhiteSpace(details.DescriptionContent) ? details.Summary : details.DescriptionContent,
                string.IsNullOrWhiteSpace(details.DescriptionContent) ? CatalogDescriptionFormat.PlainText : details.DescriptionFormat,
                details.ProjectUrl);
        });
    }

    private async Task LoadProjectVersionsAsync(CatalogPageState state, CatalogProjectSummary project)
    {
        var requestVersion = ++state.FilesRequestVersion;

        Gtk.Application.Invoke((_, _) => ResetVersionSelection(state, "Loading available versions..."));

        var files = new List<CatalogFileSummary>();
        var offset = 0;

        while (true)
        {
            var result = await ListCatalogFilesUseCase.ExecuteAsync(new ListCatalogFilesRequest
            {
                Provider = state.Provider,
                ContentType = CatalogContentType.Modpack,
                ProjectId = project.ProjectId,
                Limit = 50,
                Offset = offset
            }).ConfigureAwait(false);

            if (requestVersion != state.FilesRequestVersion)
            {
                return;
            }

            if (result.IsFailure)
            {
                Gtk.Application.Invoke((_, _) => ResetVersionSelection(state, result.Error.Message));
                return;
            }

            files.AddRange(result.Value.Where(static file => !file.IsServerPack));
            if (result.Value.Count < 50)
            {
                break;
            }

            offset += result.Value.Count;
        }

        var filteredFiles = files
            .Where(file => MatchesSelectedFileFilters(file, state.SelectedGameVersions, state.SelectedLoaders))
            .OrderByDescending(static file => file.PublishedAtUtc)
            .ThenByDescending(static file => file.FileId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Gtk.Application.Invoke((_, _) =>
        {
            if (requestVersion != state.FilesRequestVersion)
            {
                return;
            }

            PopulateVersionSelection(state, filteredFiles);
        });
    }

    private static string BuildSummaryMeta(CatalogProjectSummary summary)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(summary.Author))
        {
            parts.Add(summary.Author);
        }

        if (summary.Downloads > 0)
        {
            parts.Add($"{summary.Downloads:N0} downloads");
        }

        if (summary.UpdatedAtUtc is not null)
        {
            parts.Add($"Updated {summary.UpdatedAtUtc:yyyy-MM-dd}");
        }

        if (summary.GameVersions.Count > 0)
        {
            parts.Add($"MC {summary.GameVersions[0]}");
        }

        if (summary.Loaders.Count > 0)
        {
            parts.Add(string.Join(", ", summary.Loaders.Take(2).Select(ToDisplayName)));
        }

        return parts.Count == 0 ? "Project details loaded." : string.Join("  •  ", parts);
    }

    private static string BuildDetailsMeta(CatalogProjectDetails details)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(details.Author))
        {
            parts.Add(details.Author);
        }

        if (details.Downloads > 0)
        {
            parts.Add($"{details.Downloads:N0} downloads");
        }

        if (details.UpdatedAtUtc is not null)
        {
            parts.Add($"Updated {details.UpdatedAtUtc:yyyy-MM-dd}");
        }

        if (details.GameVersions.Count > 0)
        {
            parts.Add($"MC {details.GameVersions[0]}");
        }

        if (details.Loaders.Count > 0)
        {
            parts.Add(string.Join(", ", details.Loaders.Take(2).Select(ToDisplayName)));
        }

        return parts.Count == 0 ? "Project details loaded." : string.Join("  •  ", parts);
    }

    private void ResetProjectDetails(CatalogPageState state, string title, string description)
    {
        SetProjectDetailsTitle(state, title, null);
        state.DetailsMeta.Text = description;
        state.DescriptionView.Unload();
        state.DescriptionView.SetContent(description, CatalogDescriptionFormat.PlainText);
    }

    private void ResetVersionSelection(CatalogPageState state, string statusText)
    {
        state.SelectedFileId = null;
        state.AvailableFiles = [];
        state.ProjectVersionButton.Sensitive = false;
        UpdateProjectVersionButtonLabel(state);
        ReplacePopoverContent(state.ProjectVersionPopover, BuildProjectVersionPopoverContent(state));
        state.VersionStatus.Text = statusText;
    }

    private void PopulateVersionSelection(CatalogPageState state, IReadOnlyList<CatalogFileSummary> files)
    {
        state.AvailableFiles = files;
        state.SelectedFileId = null;

        if (files.Count == 0)
        {
            state.ProjectVersionButton.Sensitive = false;
            UpdateProjectVersionButtonLabel(state);
            RebuildProjectVersionPopover(state);
            state.VersionStatus.Text = state.SelectedGameVersions.Count > 0 || state.SelectedLoaders.Count > 0
                ? "No modpack versions match the current filters."
                : "No modpack versions are available for this project.";
            UpdatePrimaryActionState();
            return;
        }

        state.ProjectVersionButton.Sensitive = true;
        state.SelectedFileId = files[0].FileId;
        UpdateProjectVersionButtonLabel(state);
        RebuildProjectVersionPopover(state);
        state.VersionStatus.Text = $"{files.Count} version(s) available.";
        UpdatePrimaryActionState();
    }

    private static bool MatchesSelectedFileFilters(CatalogFileSummary file, HashSet<string> selectedGameVersions, HashSet<string> selectedLoaders)
    {
        var versionMatches = selectedGameVersions.Count == 0 ||
                             file.GameVersions.Any(version => selectedGameVersions.Contains(version));
        var loaderMatches = selectedLoaders.Count == 0 ||
                            file.Loaders.Any(loader => selectedLoaders.Contains(loader));
        return versionMatches && loaderMatches;
    }

    private static string BuildVersionLabel(CatalogFileSummary file)
    {
        return LauncherStructuredList.BuildSimpleCatalogFileLabel(file);
#if false
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(file.DisplayName))
        {
            parts.Add(file.DisplayName.Trim());
        }
        else if (!string.IsNullOrWhiteSpace(file.FileName))
        {
            parts.Add(file.FileName.Trim());
        }

        if (file.PublishedAtUtc is not null)
        {
            parts.Add(file.PublishedAtUtc.Value.ToString("yyyy-MM-dd"));
        }

        if (file.GameVersions.Count > 0)
        {
            parts.Add("MC " + string.Join(", ", file.GameVersions.Take(2)));
        }

        if (file.Loaders.Count > 0)
        {
            parts.Add(string.Join(", ", file.Loaders.Take(2).Select(ToDisplayName)));
        }

        return parts.Count == 0 ? file.FileId : string.Join("  •  ", parts);
    }

#endif
    }
    private Widget BuildProjectVersionPopoverContent(CatalogPageState state)
    {
        return LauncherStructuredList.BuildCatalogFileSelectionPopover(
            "Modpack versions",
            state.AvailableFiles,
            state.SelectedFileId,
            BuildVersionLabel,
            file =>
            {
                state.SelectedFileId = file.FileId;
                UpdateProjectVersionButtonLabel(state);
                UpdatePrimaryActionState();
            });
    }

    private static string AbbreviateVersionLabel(string text)
    {
        const int maxLength = 54;
        if (string.IsNullOrWhiteSpace(text))
        {
            return "Select version";
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 1)].TrimEnd() + "…";
    }

    private static void SetProjectDetailsTitle(CatalogPageState state, string title, string? projectUrl)
    {
        state.DetailsTitle.Text = title;
        state.DetailsProjectUrl = string.IsNullOrWhiteSpace(projectUrl) ? null : projectUrl;
        state.DetailsTitleButton.Sensitive = !string.IsNullOrWhiteSpace(state.DetailsProjectUrl);
    }

    private static string GetSortId(CatalogSearchSort sort)
    {
        return sort switch
        {
            CatalogSearchSort.Downloads => "downloads",
            CatalogSearchSort.Follows => "follows",
            CatalogSearchSort.Newest => "newest",
            CatalogSearchSort.Updated => "updated",
            _ => "relevance"
        };
    }

    private static string GetSortLabel(CatalogSearchSort sort)
    {
        return sort switch
        {
            CatalogSearchSort.Downloads => "Downloads",
            CatalogSearchSort.Follows => "Followers",
            CatalogSearchSort.Newest => "Newest",
            CatalogSearchSort.Updated => "Recently updated",
            _ => "Relevance"
        };
    }

    private void PopulateSortOptions(CatalogPageState state, IReadOnlyList<CatalogSearchSort> sorts)
    {
        state.AvailableSorts = sorts.Count > 0 ? sorts.Distinct().ToArray() : DefaultSorts;
        if (!state.AvailableSorts.Contains(state.SelectedSort))
        {
            state.SelectedSort = CatalogSearchSort.Relevance;
        }

        RebuildSortPopover(state);
        UpdateSortButtonLabel(state);
    }

    private void PopulateGameVersionOptions(CatalogPageState state, IReadOnlyList<string>? versions = null)
    {
        state.AvailableGameVersions = (versions is { Count: > 0 }
            ? versions
            : MinecraftVersions
                .Where(version => version.Kind == ReleaseKind.Release)
                .Select(version => version.Version)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray())
            .Where(version => state.Provider != CatalogProvider.Modrinth || IsReleaseGameVersion(version))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        state.SelectedGameVersions.RemoveWhere(value => !state.AvailableGameVersions.Contains(value, StringComparer.OrdinalIgnoreCase));
        RebuildFilterPopovers(state);
        UpdateFilterButtonLabels(state);
    }

    private void PopulateLoaderOptions(CatalogPageState state, IReadOnlyList<string> loaders)
    {
        state.AvailableLoaders = (loaders.Count > 0 ? loaders : LoaderChoices.Where(choice => choice.Id != "none").Select(choice => choice.Id).ToArray())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        state.SelectedLoaders.RemoveWhere(value => !state.AvailableLoaders.Contains(value, StringComparer.OrdinalIgnoreCase));
        RebuildFilterPopovers(state);
        UpdateFilterButtonLabels(state);
    }

    private void PopulateCategoryOptions(CatalogPageState state, IReadOnlyList<string> categories)
    {
        state.AvailableCategories = categories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        state.SelectedCategories.RemoveWhere(value => !state.AvailableCategories.Contains(value, StringComparer.OrdinalIgnoreCase));
        RebuildFilterPopovers(state);
        UpdateFilterButtonLabels(state);
    }

    private static string ToDisplayName(string value)
    {
        return value.ToLowerInvariant() switch
        {
            "neoforge" => "NeoForge",
            "forge" => "Forge",
            "fabric" => "Fabric",
            "quilt" => "Quilt",
            _ => value
        };
    }

    private void BrowseArchive()
    {
        var path = DesktopShell.PickZipFile("Choose archive to import");
        if (!string.IsNullOrWhiteSpace(path))
        {
            ImportArchiveEntry.Text = path;
            UpdatePrimaryActionState();
        }
    }

    private void BrowseFolder()
    {
        var path = DesktopShell.PickDirectory("Choose instance folder to import");
        if (!string.IsNullOrWhiteSpace(path))
        {
            ImportFolderEntry.Text = path;
            UpdatePrimaryActionState();
        }
    }

    private async Task LoadPackIconAsync(CatalogPageState state, PackRow row, int requestVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.Project.IconUrl))
        {
            Gtk.Application.Invoke((_, _) =>
            {
                if (!row.IsDisposed && requestVersion == state.RequestVersion)
                {
                    row.SetIcon(null);
                }

                state.InFlightIconProjectIds.Remove(row.Project.ProjectId);
            });
            return;
        }

        Pixbuf? pixbuf = null;
        try
        {
            pixbuf = await ProviderMediaCacheService
                .LoadIconPixbufAsync(row.Project.IconUrl, row.Project.ProjectUrl, squareSize: 36, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }

        Gtk.Application.Invoke((_, _) =>
        {
            state.InFlightIconProjectIds.Remove(row.Project.ProjectId);

            if (!row.IsDisposed &&
                requestVersion == state.RequestVersion &&
                row.Parent is not null)
            {
                row.SetIcon(pixbuf);
                if (pixbuf is not null)
                {
                    state.LoadedIconProjectIds.Add(row.Project.ProjectId);
                }
            }
            else
            {
                pixbuf?.Dispose();
            }
        });
    }

    private void ChooseInstanceIcon()
    {
        var path = DesktopShell.PickPngFile("Choose instance icon");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SelectedIconPath = path;
        UpdateInstanceIconPreview();
    }



    private void UpdateInstanceIconPreview()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(SelectedIconPath) && File.Exists(SelectedIconPath))
            {
                using var original = new Pixbuf(SelectedIconPath);
                InstanceIconImage.Pixbuf = ScaleToSquare(original, 72);
                InstanceIconImage.Show();
                InstanceIconText.Hide();
                return;
            }
        }
        catch
        {
        }

        InstanceIconImage.Clear();
        InstanceIconImage.Hide();
        InstanceIconText.Text = "Add";
        InstanceIconText.Show();
    }

    private void SwitchToPage(AddInstancePageKind pageKind)
    {
        CurrentPage = pageKind;
        ContentStack.VisibleChildName = pageKind.ToString();
        UpdatePrimaryActionState();

        if (pageKind == AddInstancePageKind.CurseForge)
        {
            _ = EnsureCatalogPageReadyAsync(CurseForgePage);
        }
        else if (pageKind == AddInstancePageKind.Modrinth)
        {
            _ = EnsureCatalogPageReadyAsync(ModrinthPage);
        }
    }

    private void UpdatePrimaryActionState()
    {
        PrimaryActionButton.Label = CurrentPage == AddInstancePageKind.QuickInstall || CurrentPage == AddInstancePageKind.AdvancedInstall ? "Create" : "Import";
        var request = BuildOperationRequest();
        var hasResolvedName = !string.IsNullOrWhiteSpace(request.InstanceName);

        PrimaryActionButton.Sensitive = !IsSubmitting && (CurrentPage switch
        {
            AddInstancePageKind.QuickInstall => hasResolvedName && !string.IsNullOrWhiteSpace(request.SelectedVersion),
            AddInstancePageKind.AdvancedInstall => hasResolvedName && !string.IsNullOrWhiteSpace(request.SelectedVersion) && (request.SelectedLoader == "None" || !string.IsNullOrWhiteSpace(request.SelectedLoaderVersion)),
            AddInstancePageKind.Import => hasResolvedName && (!string.IsNullOrWhiteSpace(request.ImportArchivePath) || !string.IsNullOrWhiteSpace(request.ImportFolderPath)),
            AddInstancePageKind.CurseForge => CurseForgePage.SelectedProject is not null && !string.IsNullOrWhiteSpace(CurseForgePage.SelectedFileId),
            AddInstancePageKind.Modrinth => ModrinthPage.SelectedProject is not null && !string.IsNullOrWhiteSpace(ModrinthPage.SelectedFileId),
            _ => false
        });
    }

    private void BeginPrimaryAction()
    {
        if (IsSubmitting)
        {
            return;
        }

        var request = BuildOperationRequest();
        IsSubmitting = true;
        ActiveOperationCancellationSource?.Dispose();
        ActiveOperationCancellationSource = new CancellationTokenSource();
        CancelButton.Sensitive = false;
        UpdatePrimaryActionState();
        ShowOperationDialog(GetInitialOperationTitle(), GetInitialOperationBody());
        StartOperationProgressPolling();

        GLib.Idle.Add(() =>
        {
            _ = RunPrimaryActionAsync(request, ActiveOperationCancellationSource.Token);
            return false;
        });
    }

    private async Task RunPrimaryActionAsync(OperationRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var result = await Task.Run(
                    () => ExecuteOperationAsync(request, cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            Gtk.Application.Invoke((_, _) =>
            {
                StopOperationProgressPolling();
                CloseOperationDialog();
                CancelButton.Sensitive = true;
                ActiveOperationCancellationSource?.Dispose();
                ActiveOperationCancellationSource = null;

                if (!result.IsSuccess)
                {
                    ShowMessage("Add Instance", result.ErrorMessage ?? "The requested action failed.", MessageType.Error);
                    IsSubmitting = false;
                    UpdatePrimaryActionState();
                    return;
                }

                IsSubmitting = false;
                UpdatePrimaryActionState();

                if (result.RequiresManualCompletion)
                {
                    return;
                }

                ShowMessage("Add Instance", result.InfoMessage ?? BuildSuccessMessage(request.InstanceName), MessageType.Info);

                InstanceBrowserRefreshService.RequestRefresh(result.InstanceId);
                ResetAndHide();
            });
        }
        catch (OperationCanceledException)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                StopOperationProgressPolling();
                CloseOperationDialog();
                CancelButton.Sensitive = true;
                ActiveOperationCancellationSource?.Dispose();
                ActiveOperationCancellationSource = null;
                IsSubmitting = false;
                UpdatePrimaryActionState();
                ShowMessage("Add Instance", "The create/import operation was cancelled.", MessageType.Info);
            });
        }
        catch (Exception exception)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                StopOperationProgressPolling();
                CloseOperationDialog();
                CancelButton.Sensitive = true;
                ActiveOperationCancellationSource?.Dispose();
                ActiveOperationCancellationSource = null;
                IsSubmitting = false;
                UpdatePrimaryActionState();
                ShowMessage("Add Instance", exception.Message, MessageType.Error);
            });
        }
    }

    private Task<InstanceCreationOutcome> ExecuteOperationAsync(OperationRequest request, CancellationToken cancellationToken)
    {
        return request.Page switch
        {
            AddInstancePageKind.QuickInstall => CreateInstanceAsync(request, cancellationToken),
            AddInstancePageKind.AdvancedInstall => CreateInstanceAsync(request, cancellationToken),
            AddInstancePageKind.Import => ImportFromLocalSourceAsync(request, cancellationToken),
            AddInstancePageKind.CurseForge => ImportCatalogProjectAsync(request, cancellationToken),
            AddInstancePageKind.Modrinth => ImportCatalogProjectAsync(request, cancellationToken),
            _ => Task.FromResult(InstanceCreationOutcome.Failure("Unsupported page."))
        };
    }

    private OperationRequest BuildOperationRequest()
    {
        var rawName = NameEntry.Text?.Trim();
        var importArchivePath = ImportArchiveEntry.Text?.Trim() ?? string.Empty;
        var importFolderPath = ImportFolderEntry.Text?.Trim() ?? string.Empty;

        var catalogIconUrl = CurrentPage == AddInstancePageKind.CurseForge ? CurseForgePage.SelectedProject?.IconUrl : CurrentPage == AddInstancePageKind.Modrinth ? ModrinthPage.SelectedProject?.IconUrl : null;
        var catalogTitle = CurrentPage == AddInstancePageKind.CurseForge ? CurseForgePage.SelectedProject?.Title : CurrentPage == AddInstancePageKind.Modrinth ? ModrinthPage.SelectedProject?.Title : null;
        var instanceName = ResolveRequestedInstanceName(rawName, catalogTitle, SelectedVersion, importArchivePath, importFolderPath);

        return new OperationRequest(
            Page: CurrentPage,
            InstanceName: instanceName,
            SelectedVersion: SelectedVersion,
            SelectedLoader: SelectedLoader,
            SelectedLoaderVersion: SelectedLoaderVersion,
            SelectedGameVersions: CurrentPage == AddInstancePageKind.CurseForge ? CurseForgePage.SelectedGameVersions.ToArray() : CurrentPage == AddInstancePageKind.Modrinth ? ModrinthPage.SelectedGameVersions.ToArray() : [],
            SelectedLoaders: CurrentPage == AddInstancePageKind.CurseForge ? CurseForgePage.SelectedLoaders.ToArray() : CurrentPage == AddInstancePageKind.Modrinth ? ModrinthPage.SelectedLoaders.ToArray() : [],
            SelectedIconPath: SelectedIconPath,
            FallbackIconUrl: catalogIconUrl,
            ImportArchivePath: importArchivePath,
            ImportFolderPath: importFolderPath,
            CopyImportSource: ImportCopyFilesOption.Active,
            Provider: CurrentPage switch
            {
                AddInstancePageKind.CurseForge => CatalogProvider.CurseForge,
                AddInstancePageKind.Modrinth => CatalogProvider.Modrinth,
                _ => null
            },
            SelectedProjectId: CurrentPage switch
            {
                AddInstancePageKind.CurseForge => CurseForgePage.SelectedProject?.ProjectId,
                AddInstancePageKind.Modrinth => ModrinthPage.SelectedProject?.ProjectId,
                _ => null
            },
            SelectedFileId: CurrentPage switch
            {
                AddInstancePageKind.CurseForge => CurseForgePage.SelectedFileId,
                AddInstancePageKind.Modrinth => ModrinthPage.SelectedFileId,
                _ => null
            });
    }

    private async Task<InstanceCreationOutcome> CreateInstanceAsync(OperationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceName) || string.IsNullOrWhiteSpace(request.SelectedVersion))
        {
            return InstanceCreationOutcome.Failure("Choose a Minecraft version first.");
        }

        var loaderType = MapSelectedLoaderType(request.SelectedLoader);
        string? loaderVersion = null;
        if (loaderType != LoaderType.Vanilla)
        {
            if (!string.IsNullOrWhiteSpace(request.SelectedLoaderVersion))
            {
                loaderVersion = request.SelectedLoaderVersion;
            }
            else
            {
                UpdateOperationDialog("Preparing loader", $"Resolving the best {loaderType} version for Minecraft {request.SelectedVersion}.");
                var loaderResolution = await ResolvePreferredLoaderVersionAsync(loaderType, request.SelectedVersion, cancellationToken).ConfigureAwait(false);
                if (loaderResolution.IsFailure)
                {
                    return InstanceCreationOutcome.Failure(loaderResolution.Error.Message);
                }

                loaderVersion = loaderResolution.Value;
            }
        }

        UpdateOperationDialog("Creating instance", $"Installing {request.InstanceName} and downloading the required game files.");
        var installResult = await InstallInstanceUseCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = request.InstanceName,
            GameVersion = request.SelectedVersion,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            DownloadRuntime = true
        }, cancellationToken).ConfigureAwait(false);

        if (installResult.IsFailure)
        {
            return InstanceCreationOutcome.Failure(installResult.Error.Message);
        }

        UpdateOperationDialog("Finalizing instance", "Writing launcher metadata and refreshing the instance browser.");
        await PersistSelectedIconAsync(installResult.Value.Instance, request.SelectedIconPath, cancellationToken).ConfigureAwait(false);
        return InstanceCreationOutcome.Success(installResult.Value.Instance.InstanceId);
    }

    private async Task<InstanceCreationOutcome> ImportCatalogProjectAsync(OperationRequest request, CancellationToken cancellationToken)
    {
        if (request.Provider is null || string.IsNullOrWhiteSpace(request.SelectedProjectId) || string.IsNullOrWhiteSpace(request.InstanceName))
        {
            return InstanceCreationOutcome.Failure("Choose a project first.");
        }

        var catalogIconPath = request.SelectedIconPath;
        if (string.IsNullOrWhiteSpace(catalogIconPath) && !string.IsNullOrWhiteSpace(request.FallbackIconUrl))
        {
            UpdateOperationDialog("Downloading icon", "Fetching the selected modpack's original icon.");
            try
            {
                var cachedEntry = await ProviderMediaCacheService
                    .GetOrCreateCachedEntryAsync(request.FallbackIconUrl, null, cancellationToken)
                    .ConfigureAwait(false);
                if (cachedEntry is not null)
                {
                    catalogIconPath = cachedEntry.FilePath;
                }
            }
            catch
            {
            }
        }

        UpdateOperationDialog(
            $"Importing from {GetProviderDisplayName(request.Provider.Value)}",
            $"Downloading and installing the selected modpack as {request.InstanceName}.");
        var result = await ImportCatalogModpackUseCase.ExecuteAsync(new ImportCatalogModpackRequest
        {
            Provider = request.Provider.Value,
            ProjectId = request.SelectedProjectId,
            FileId = request.SelectedFileId,
            GameVersions = request.SelectedGameVersions,
            Loaders = request.SelectedLoaders,
            InstanceName = request.InstanceName,
            DownloadRuntime = true,
            WaitForManualDownloads = false,
            Progress = CreateModpackProgressReporter(cancellationToken),
            BlockedFilesPromptAsync = request.Provider.Value == CatalogProvider.CurseForge
                ? PromptForBlockedFilesAsync
                : null
        }, cancellationToken).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return InstanceCreationOutcome.Failure(result.Error.Message);
        }

        UpdateOperationDialog("Finalizing import", "Writing launcher metadata and indexing the imported instance.");
        await PersistSelectedIconAsync(result.Value.Instance, catalogIconPath, cancellationToken).ConfigureAwait(false);

        if (result.Value.PendingManualDownloads.Count > 0)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                ManualDownloadWindow.TransientFor = this;
                ManualDownloadWindow.BeginTracking(
                    result.Value.Instance,
                    result.Value.DownloadsDirectory,
                    result.Value.PendingManualDownloads,
                    trackingResult => HandleManualDownloadTrackingCompleted(request.InstanceName, trackingResult));
                ManualDownloadWindow.Present();
            });

            return InstanceCreationOutcome.PendingManualCompletion(result.Value.Instance.InstanceId);
        }

        return result.Value.WasManualDownloadStepSkipped
            ? InstanceCreationOutcome.Success(
                result.Value.Instance.InstanceId,
                $"Imported '{request.InstanceName}' from {GetProviderDisplayName(request.Provider.Value)}. Some blocked provider files were skipped, so the instance may be incomplete until those files are added manually.")
            : InstanceCreationOutcome.Success(result.Value.Instance.InstanceId, null);
    }

    private async Task<InstanceCreationOutcome> ImportFromLocalSourceAsync(OperationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceName))
        {
            return InstanceCreationOutcome.Failure("Choose an instance name first.");
        }

        if (!string.IsNullOrWhiteSpace(request.ImportArchivePath))
        {
            UpdateOperationDialog("Importing archive", "Extracting the archive and preparing a new launcher instance.");
            var archiveResult = await ImportArchiveInstanceUseCase.ExecuteAsync(new ImportArchiveInstanceRequest
            {
                ArchivePath = request.ImportArchivePath,
                InstanceName = request.InstanceName,
                DownloadRuntime = true,
                WaitForManualDownloads = false,
                Progress = CreateModpackProgressReporter(cancellationToken),
                BlockedFilesPromptAsync = PromptForBlockedFilesAsync
            }, cancellationToken).ConfigureAwait(false);

            if (archiveResult.IsFailure)
            {
                return InstanceCreationOutcome.Failure(archiveResult.Error.Message);
            }

            UpdateOperationDialog("Finalizing import", "Writing launcher metadata and indexing the imported instance.");
            await PersistSelectedIconAsync(archiveResult.Value.Instance, request.SelectedIconPath, cancellationToken).ConfigureAwait(false);

            if (archiveResult.Value.PendingManualDownloads.Count > 0)
            {
                Gtk.Application.Invoke((_, _) =>
                {
                    ManualDownloadWindow.TransientFor = this;
                    ManualDownloadWindow.BeginTracking(
                        archiveResult.Value.Instance,
                        archiveResult.Value.DownloadsDirectory,
                        archiveResult.Value.PendingManualDownloads,
                        trackingResult => HandleManualDownloadTrackingCompleted(request.InstanceName, trackingResult));
                    ManualDownloadWindow.Present();
                });

                return InstanceCreationOutcome.PendingManualCompletion(archiveResult.Value.Instance.InstanceId);
            }

            return archiveResult.Value.WasManualDownloadStepSkipped
                ? InstanceCreationOutcome.Success(
                    archiveResult.Value.Instance.InstanceId,
                    $"Imported '{request.InstanceName}'. Some blocked provider files were skipped, so the instance may be incomplete until those files are added manually.")
                : InstanceCreationOutcome.Success(archiveResult.Value.Instance.InstanceId, null);
        }

        if (!string.IsNullOrWhiteSpace(request.ImportFolderPath))
        {
            UpdateOperationDialog("Importing folder", "Copying the selected folder into launcher storage.");
            var folderResult = await ImportInstanceUseCase.ExecuteAsync(new ImportInstanceRequest
            {
                SourceDirectory = request.ImportFolderPath,
                InstanceName = request.InstanceName,
                CopyInsteadOfMove = request.CopyImportSource
            }, cancellationToken).ConfigureAwait(false);

            if (folderResult.IsFailure)
            {
                return InstanceCreationOutcome.Failure(folderResult.Error.Message);
            }

            UpdateOperationDialog("Finalizing import", "Writing launcher metadata and indexing the imported instance.");
            await PersistSelectedIconAsync(folderResult.Value.Instance, request.SelectedIconPath, cancellationToken).ConfigureAwait(false);
            return InstanceCreationOutcome.Success(folderResult.Value.Instance.InstanceId);
        }

        return InstanceCreationOutcome.Failure("Choose an archive or folder to import.");
    }

    private async Task<Result<string>> ResolvePreferredLoaderVersionAsync(LoaderType loaderType, string selectedVersion, CancellationToken cancellationToken)
    {
        var loaderVersions = await LoaderMetadataService
            .GetLoaderVersionsAsync(loaderType, BlockiumLauncher.Domain.ValueObjects.VersionId.Parse(selectedVersion), cancellationToken)
            .ConfigureAwait(false);
        if (loaderVersions.IsFailure)
        {
            return Result<string>.Failure(loaderVersions.Error);
        }

        var preferred = loaderVersions.Value
            .OrderByDescending(static version => version.IsStable)
            .ThenByDescending(static version => version.LoaderVersion, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();

        return preferred is null
            ? Result<string>.Failure(InstallErrors.LoaderNotFound)
            : Result<string>.Success(preferred.LoaderVersion);
    }

    private LoaderType MapSelectedLoaderType(string selectedLoader)
    {
        return selectedLoader switch
        {
            "NeoForge" => LoaderType.NeoForge,
            "Forge" => LoaderType.Forge,
            "Fabric" => LoaderType.Fabric,
            "Quilt" => LoaderType.Quilt,
            _ => LoaderType.Vanilla
        };
    }

    private async Task PersistSelectedIconAsync(LauncherInstance instance, string? selectedIconPath, CancellationToken cancellationToken)
    {
        var effectiveIconPath = ResolveSelectedIconPathForPersistence(selectedIconPath);
        if (string.IsNullOrWhiteSpace(effectiveIconPath) || !File.Exists(effectiveIconPath))
        {
            return;
        }

        var iconDirectory = System.IO.Path.Combine(instance.InstallLocation, ".blockium");
        Directory.CreateDirectory(iconDirectory);
        var iconPath = System.IO.Path.Combine(iconDirectory, "icon.png");
        File.Copy(effectiveIconPath, iconPath, overwrite: true);

        instance.ChangeIconKey(iconPath);
        await InstanceRepository.SaveAsync(instance, cancellationToken).ConfigureAwait(false);
        await InstanceContentMetadataService.ReindexAsync(instance, cancellationToken).ConfigureAwait(false);
    }

    private void ShowMessage(string title, string message, MessageType messageType)
    {
        LauncherGtkChrome.ShowMessage(this, title, message, messageType);
    }

    private Task<BlockedModpackFilesPromptResult> PromptForBlockedFilesAsync(
        BlockedModpackFilesPromptRequest promptRequest,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(promptRequest);

        if (IsShuttingDown || cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(new BlockedModpackFilesPromptResult
            {
                Decision = BlockedModpackFilesDecision.Cancel
            });
        }

        Gtk.Application.Invoke((_, _) => CloseOperationDialog());

        return BlockedFilesPromptDialog
            .PromptAsync(this, promptRequest, cancellationToken)
            .ContinueWith(task =>
            {
                if (task.IsCanceled || cancellationToken.IsCancellationRequested)
                {
                    return new BlockedModpackFilesPromptResult
                    {
                        Decision = BlockedModpackFilesDecision.Cancel
                    };
                }

                if (task.IsFaulted)
                {
                    throw task.Exception?.GetBaseException() ?? new InvalidOperationException("Blocked files prompt failed.");
                }

                var result = task.Result;
                if (!IsShuttingDown && result.Decision != BlockedModpackFilesDecision.Cancel)
                {
                    Gtk.Application.Invoke((_, _) =>
                        ShowOperationDialog(
                            "Continuing import",
                            result.Decision == BlockedModpackFilesDecision.SkipMissing
                                ? "Skipping unresolved blocked files and resuming the modpack import."
                                : "Resuming the modpack import with the files you downloaded."));
                }

                return result;
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    private IProgress<ModpackImportProgress> CreateModpackProgressReporter(CancellationToken cancellationToken)
    {
        return new Progress<ModpackImportProgress>(update =>
        {
            if (IsShuttingDown || cancellationToken.IsCancellationRequested || !IsSubmitting)
            {
                return;
            }

            Gtk.Application.Invoke((_, _) =>
            {
                if (IsShuttingDown || cancellationToken.IsCancellationRequested || !IsSubmitting)
                {
                    return;
                }

                var title = ResolveModpackProgressTitle(update);
                var fraction = ResolveModpackProgressFraction(update);
                UpdateOperationDialogCore(
                    title,
                    ResolveModpackProgressBody(update),
                    fraction);
            });
        });
    }

    private static string ResolveModpackProgressTitle(ModpackImportProgress update)
    {
        if (!string.IsNullOrWhiteSpace(update.Title))
        {
            return update.Title;
        }

        return update.Phase switch
        {
            ModpackImportPhase.ResolvingModpack => "Resolving modpack",
            ModpackImportPhase.DownloadingArchive => "Downloading modpack",
            ModpackImportPhase.ExtractingArchive => "Extracting modpack",
            ModpackImportPhase.CheckingCurseForgeFiles => "Checking CurseForge files",
            ModpackImportPhase.WaitingForBlockedFilesDecision => "Waiting for blocked files",
            ModpackImportPhase.PreparingInstanceRuntime => "Preparing instance runtime",
            ModpackImportPhase.DownloadingAllowedFiles => "Downloading mod files",
            ModpackImportPhase.CopyingOverrides => "Copying overrides",
            ModpackImportPhase.Finalizing => "Finalizing import",
            ModpackImportPhase.Completed => "Import complete",
            ModpackImportPhase.SkippedManual => "Import completed with skipped files",
            ModpackImportPhase.Failed => "Import failed",
            ModpackImportPhase.Canceled => "Import canceled",
            _ => "Working"
        };
    }

    private static string ResolveModpackProgressBody(ModpackImportProgress update)
    {
        if (!string.IsNullOrWhiteSpace(update.StatusText))
        {
            return update.StatusText;
        }

        return update.Phase switch
        {
            ModpackImportPhase.ResolvingModpack => "Resolving the selected modpack file.",
            ModpackImportPhase.DownloadingArchive => "Downloading the selected modpack archive.",
            ModpackImportPhase.ExtractingArchive => "Extracting the modpack archive and reading its manifest.",
            ModpackImportPhase.CheckingCurseForgeFiles => "Checking which CurseForge files can be downloaded automatically.",
            ModpackImportPhase.WaitingForBlockedFilesDecision => "Waiting for manual download confirmation before continuing.",
            ModpackImportPhase.PreparingInstanceRuntime => "Preparing the base instance runtime before the modpack files are applied.",
            ModpackImportPhase.DownloadingAllowedFiles => "Downloading provider files for the selected modpack.",
            ModpackImportPhase.CopyingOverrides => "Copying modpack overrides into the prepared instance.",
            ModpackImportPhase.Finalizing => "Writing launcher metadata and finishing the import.",
            ModpackImportPhase.Completed => "The modpack import has completed successfully.",
            ModpackImportPhase.SkippedManual => "The import finished after skipping blocked provider files.",
            ModpackImportPhase.Failed => "The modpack import failed.",
            ModpackImportPhase.Canceled => "The modpack import was canceled.",
            _ => "Working on your request."
        };
    }

    private static double? ResolveModpackProgressFraction(ModpackImportProgress update)
    {
        if (update.TotalBytes.HasValue && update.TotalBytes.Value > 0 && update.CurrentBytes.HasValue)
        {
            return Math.Clamp((double)update.CurrentBytes.Value / update.TotalBytes.Value, 0d, 1d);
        }

        if (update.TotalFileCount.HasValue && update.TotalFileCount.Value > 0 && update.CurrentFileCount.HasValue)
        {
            return Math.Clamp((double)update.CurrentFileCount.Value / update.TotalFileCount.Value, 0d, 1d);
        }

        if (update.BlockedFileCount.HasValue && update.BlockedFileCount.Value > 0 && update.ResolvedBlockedFileCount.HasValue)
        {
            return Math.Clamp((double)update.ResolvedBlockedFileCount.Value / update.BlockedFileCount.Value, 0d, 1d);
        }

        return null;
    }

    private void HandleManualDownloadTrackingCompleted(string instanceName, ManualDownloadWindow.ManualDownloadTrackingResult result)
    {
        if (result.InstanceId is InstanceId instanceId)
        {
            InstanceBrowserRefreshService.RequestRefresh(instanceId);
        }

        if (result.IsCompleted)
        {
            ShowMessage("Add Instance", BuildSuccessMessage(instanceName), MessageType.Info);
        }
        else if (result.WasSkipped)
        {
            var remainingText = result.RemainingFileCount <= 0
                ? "Some provider-managed downloads were skipped."
                : $"Skipped {result.RemainingFileCount} required manual download(s).";
            ShowMessage("Add Instance", $"{remainingText} The instance was added, but it may be incomplete until those files are downloaded.", MessageType.Info);
        }

        ResetAndHide();
    }

    private string BuildSuccessMessage(string instanceName)
    {
        var safeInstanceName = string.IsNullOrWhiteSpace(instanceName) ? "instance" : instanceName;
        return CurrentPage switch
        {
            AddInstancePageKind.QuickInstall => $"Created '{safeInstanceName}' successfully.",
            AddInstancePageKind.AdvancedInstall => $"Created '{safeInstanceName}' successfully.",
            AddInstancePageKind.Import => $"Imported '{safeInstanceName}' successfully.",
            AddInstancePageKind.CurseForge => $"Imported '{safeInstanceName}' from CurseForge successfully.",
            AddInstancePageKind.Modrinth => $"Imported '{safeInstanceName}' from Modrinth successfully.",
            _ => $"Finished creating '{safeInstanceName}'."
        };
    }

    private static bool UsesStructuredModpackFlow(OperationRequest request)
    {
        return request.Page == AddInstancePageKind.CurseForge ||
               request.Page == AddInstancePageKind.Modrinth ||
               (request.Page == AddInstancePageKind.Import && !string.IsNullOrWhiteSpace(request.ImportArchivePath));
    }

    private string GetInitialOperationTitle()
    {
        return CurrentPage switch
        {
            AddInstancePageKind.QuickInstall => "Creating instance",
            AddInstancePageKind.AdvancedInstall => "Creating instance",
            AddInstancePageKind.Import => "Importing instance",
            AddInstancePageKind.CurseForge => "Importing from CurseForge",
            AddInstancePageKind.Modrinth => "Importing from Modrinth",
            _ => "Working"
        };
    }

    private string GetInitialOperationBody()
    {
        return CurrentPage switch
        {
            AddInstancePageKind.QuickInstall => "Preparing the install plan and resolving required game files.",
            AddInstancePageKind.AdvancedInstall => "Preparing the install plan and resolving required game files.",
            AddInstancePageKind.Import => "Preparing the selected archive or folder for import.",
            AddInstancePageKind.CurseForge => "Resolving the selected CurseForge modpack and starting the download.",
            AddInstancePageKind.Modrinth => "Resolving the selected Modrinth modpack and starting the download.",
            _ => "Working on your request."
        };
    }

    private static string GetProviderDisplayName(CatalogProvider provider)
    {
        return provider switch
        {
            CatalogProvider.CurseForge => "CurseForge",
            CatalogProvider.Modrinth => "Modrinth",
            _ => provider.ToString()
        };
    }

    private void ShowOperationDialog(string title, string body)
    {
        EnsureOperationDialog();
        UpdateOperationDialogCore(title, body, null);
        OperationDialog?.ShowAll();
        OperationDialog?.ShowNow();
        OperationDialog?.Present();
        while (Gtk.Application.EventsPending())
        {
            Gtk.Application.RunIteration();
        }
    }

    private void UpdateOperationDialog(string title, string body)
    {
        Gtk.Application.Invoke((_, _) => UpdateOperationDialogCore(title, body, null));
    }

    private void UpdateOperationDialogCore(string title, string body, double? fraction)
    {
        EnsureOperationDialog();
        if (OperationTitleLabel is not null)
        {
            OperationTitleLabel.Text = title;
        }

        if (OperationBodyLabel is not null)
        {
            OperationBodyLabel.Text = body;
        }

        if (OperationProgressBar is not null)
        {
            if (fraction.HasValue)
            {
                OperationProgressBar.Fraction = Math.Clamp(fraction.Value, 0.0, 1.0);
            }
            else
            {
                OperationProgressBar.Pulse();
            }
        }

        if (OperationCancelButton is not null)
        {
            OperationCancelButton.Sensitive = IsSubmitting && ActiveOperationCancellationSource is { IsCancellationRequested: false };
        }
    }

    private void EnsureOperationDialog()
    {
        if (OperationDialog is not null)
        {
            OperationProgressBar?.Pulse();
            if (OperationCancelButton is not null)
            {
                OperationCancelButton.Sensitive = IsSubmitting && ActiveOperationCancellationSource is { IsCancellationRequested: false };
            }
            return;
        }

        void RequestCancellation()
        {
            if (ActiveOperationCancellationSource is null || ActiveOperationCancellationSource.IsCancellationRequested)
            {
                return;
            }

            ActiveOperationCancellationSource.Cancel();
            if (OperationCancelButton is not null)
            {
                OperationCancelButton.Sensitive = false;
            }

            UpdateOperationDialogCore("Cancelling operation", "Stopping downloads and cleaning up the in-progress install.", null);
        }

        var dialog = LauncherGtkChrome.CreateProgressDialog(this, "Working", string.Empty, 420, RequestCancellation);

        var content = dialog.ContentArea;
        content.Spacing = 0;

        var root = new Box(Orientation.Vertical, 0);
        root.StyleContext.AddClass("launcher-window-root");

        var shell = new EventBox();
        shell.StyleContext.AddClass("launcher-section-shell");

        var body = new Box(Orientation.Vertical, 14)
        {
            MarginTop = 18,
            MarginBottom = 18,
            MarginStart = 18,
            MarginEnd = 18
        };

        var text = new Box(Orientation.Vertical, 6);

        var title = new Label("Working")
        {
            Xalign = 0,
            Wrap = true
        };
        title.StyleContext.AddClass("settings-section-title");

        var description = new Label("Please wait while BlockiumLauncher completes the requested action.")
        {
            Xalign = 0,
            Wrap = true
        };
        description.StyleContext.AddClass("settings-help");

        text.PackStart(title, false, false, 0);
        text.PackStart(description, false, false, 0);

        var progressBar = new ProgressBar
        {
            Hexpand = true,
            MarginTop = 6
        };

        var actions = new Box(Orientation.Horizontal, 10)
        {
            Halign = Align.End
        };

        var cancelButton = new Button("Cancel")
        {
            Sensitive = true
        };
        cancelButton.StyleContext.AddClass("action-button");
        cancelButton.Clicked += (_, _) => RequestCancellation();

        actions.PackStart(cancelButton, false, false, 0);

        body.PackStart(text, false, false, 0);
        body.PackStart(progressBar, false, false, 0);
        body.PackStart(actions, false, false, 0);
        shell.Add(body);
        root.PackStart(shell, true, true, 0);
        content.PackStart(root, true, true, 0);

        OperationDialog = dialog;
        OperationProgressBar = progressBar;
        OperationTitleLabel = title;
        OperationBodyLabel = description;
        OperationCancelButton = cancelButton;
    }

    private void CloseOperationDialog()
    {
        StopOperationProgressPolling();

        if (OperationDialog is not null)
        {
            OperationDialog.Hide();
            OperationDialog.Destroy();
        }

        OperationDialog = null;
        OperationProgressBar = null;
        OperationTitleLabel = null;
        OperationBodyLabel = null;
        OperationCancelButton = null;
    }

    private void StartOperationProgressPolling()
    {
        StopOperationProgressPolling();
        LastObservedOperationLine = null;
        OperationProgressPollSourceId = GLib.Timeout.Add(650, () =>
        {
            if (!IsSubmitting)
            {
                OperationProgressPollSourceId = null;
                return false;
            }

            TryUpdateOperationProgressFromLogs();
            return true;
        });
    }

    private void StopOperationProgressPolling()
    {
        if (OperationProgressPollSourceId is null)
        {
            return;
        }

        GLib.Source.Remove(OperationProgressPollSourceId.Value);
        OperationProgressPollSourceId = null;
        LastObservedOperationLine = null;
    }

    private void TryUpdateOperationProgressFromLogs()
    {
        try
        {
            var logPath = LauncherPaths.LatestLogFilePath;
            if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            {
                return;
            }

            var lastRelevantLine = File.ReadLines(logPath)
                .Reverse()
                .FirstOrDefault(static line =>
                    !string.IsNullOrWhiteSpace(line) &&
                    (line.Contains("event=LibrariesProgress", StringComparison.Ordinal) ||
                     line.Contains("event=AssetsProgress", StringComparison.Ordinal) ||
                     line.Contains("event=PrepareStarted", StringComparison.Ordinal)));

            if (string.IsNullOrWhiteSpace(lastRelevantLine) ||
                string.Equals(lastRelevantLine, LastObservedOperationLine, StringComparison.Ordinal))
            {
                return;
            }

            LastObservedOperationLine = lastRelevantLine;

            if (TryParseOperationProgress(lastRelevantLine, out var title, out var body))
            {
                UpdateOperationDialogCore(title, body, null);
            }
        }
        catch
        {
        }
    }

    private static bool TryParseOperationProgress(string line, out string title, out string body)
    {
        title = string.Empty;
        body = string.Empty;

        if (line.Contains("event=LibrariesProgress", StringComparison.Ordinal))
        {
            if (TryExtractProgressNumbers(line, "DownloadedLibraries", out var downloaded, out var total))
            {
                title = "Downloading libraries";
                body = $"Downloaded {downloaded:N0} of {total:N0} libraries.";
                return true;
            }
        }

        if (line.Contains("event=AssetsProgress", StringComparison.Ordinal))
        {
            if (TryExtractProgressNumbers(line, "DownloadedAssets", out var downloaded, out var total))
            {
                title = "Downloading game assets";
                body = $"Downloaded {downloaded:N0} of {total:N0} assets.";
                return true;
            }
        }

        if (line.Contains("event=PrepareStarted", StringComparison.Ordinal))
        {
            title = "Preparing instance";
            body = "Resolving the install plan and preparing the staged files.";
            return true;
        }

        return false;
    }

    private static bool TryExtractProgressNumbers(string line, string downloadedKey, out int downloaded, out int total)
    {
        downloaded = 0;
        total = 0;

        var downloadedToken = downloadedKey + " = ";
        var totalToken = "Total = ";

        var downloadedStart = line.IndexOf(downloadedToken, StringComparison.Ordinal);
        var totalStart = line.IndexOf(totalToken, StringComparison.Ordinal);
        if (downloadedStart < 0 || totalStart < 0)
        {
            return false;
        }

        downloadedStart += downloadedToken.Length;
        totalStart += totalToken.Length;

        var downloadedSlice = line[downloadedStart..];
        var totalSlice = line[totalStart..];

        var downloadedText = new string(downloadedSlice.TakeWhile(char.IsDigit).ToArray());
        var totalText = new string(totalSlice.TakeWhile(char.IsDigit).ToArray());

        return int.TryParse(downloadedText, out downloaded) &&
               int.TryParse(totalText, out total);
    }

    private enum AddInstancePageKind
    {
        QuickInstall,
        AdvancedInstall,
        Modrinth,
        CurseForge,
        Import
    }

    private enum ReleaseKind
    {
        Release,
        Snapshot,
        Beta,
        Alpha,
        Experiment
    }

    private sealed record OperationRequest(
        AddInstancePageKind Page,
        string InstanceName,
        string? SelectedVersion,
        string SelectedLoader,
        string? SelectedLoaderVersion,
        IReadOnlyList<string> SelectedGameVersions,
        IReadOnlyList<string> SelectedLoaders,
        string? SelectedIconPath,
        string? FallbackIconUrl,
        string ImportArchivePath,
        string ImportFolderPath,
        bool CopyImportSource,
        CatalogProvider? Provider,
        string? SelectedProjectId,
        string? SelectedFileId);

    private sealed record LoaderChoice(string Id, string Label);
    private sealed record MinecraftVersionOption(string Version, string Released, ReleaseKind Kind);

    private static MinecraftVersionOption MapVersionOption(VersionSummary version)
    {
        return new MinecraftVersionOption(
            version.VersionId.ToString(),
            version.ReleasedAtUtc == DateTimeOffset.MinValue ? string.Empty : version.ReleasedAtUtc.ToString("yyyy-MM-dd"),
            ClassifyReleaseKind(version));
    }

    private static ReleaseKind ClassifyReleaseKind(VersionSummary version)
    {
        if (version.IsRelease)
        {
            return ReleaseKind.Release;
        }

        var value = version.VersionId.ToString();
        if (value.Contains('w', StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseKind.Snapshot;
        }

        if (value.StartsWith("b", StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseKind.Beta;
        }

        if (value.StartsWith("a", StringComparison.OrdinalIgnoreCase))
        {
            return ReleaseKind.Alpha;
        }

        return ReleaseKind.Experiment;
    }

    private sealed record InstanceCreationOutcome(bool IsSuccess, bool RequiresManualCompletion, InstanceId? InstanceId, string? InfoMessage, string? ErrorMessage)
    {
        public static InstanceCreationOutcome Success(InstanceId instanceId, string? infoMessage = null)
            => new(true, false, instanceId, infoMessage, null);

        public static InstanceCreationOutcome PendingManualCompletion(InstanceId instanceId, string? infoMessage = null)
            => new(true, true, instanceId, infoMessage, null);

        public static InstanceCreationOutcome Failure(string message)
            => new(false, false, null, null, message);
    }

    private sealed class CatalogPageState
    {
        public CatalogPageState(CatalogProvider provider)
        {
            Provider = provider;
        }

        public CatalogProvider Provider { get; }
        public Entry SearchEntry { get; } = new() { Hexpand = true };
        public Button SortButton { get; } = new("Sort");
        public Button CategoryButton { get; } = new("Category");
        public Button VersionButton { get; } = new("Version");
        public Button LoaderButton { get; } = new("Loader");
        public Popover SortPopover { get; set; } = null!;
        public Popover CategoryPopover { get; set; } = null!;
        public Popover VersionPopover { get; set; } = null!;
        public Popover LoaderPopover { get; set; } = null!;
        public ListBox PackList { get; } = new() { SelectionMode = SelectionMode.Single };
        public ScrolledWindow? PackScroller { get; set; }
        public Label ListStatus { get; set; } = new();
        public Button DetailsTitleButton { get; set; } = new();
        public Label DetailsTitle { get; set; } = new();
        public Label DetailsMeta { get; set; } = new();
        public string? DetailsProjectUrl { get; set; }
        public CatalogDescriptionView DescriptionView { get; set; } = null!;
        public Button ProjectVersionButton { get; } = new("Select version") { Hexpand = true, Sensitive = false };
        public Popover ProjectVersionPopover { get; set; } = null!;
        public Label VersionStatus { get; } = new() { Xalign = 0, Wrap = true };
        public CatalogProviderMetadata? Metadata { get; set; }
        public IReadOnlyList<CatalogSearchSort> AvailableSorts { get; set; } = [];
        public IReadOnlyList<string> AvailableGameVersions { get; set; } = [];
        public IReadOnlyList<string> AvailableLoaders { get; set; } = [];
        public IReadOnlyList<string> AvailableCategories { get; set; } = [];
        public HashSet<string> SelectedGameVersions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedLoaders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedCategories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CatalogSearchSort SelectedSort { get; set; } = CatalogSearchSort.Relevance;
        public IReadOnlyList<CatalogProjectSummary> Results { get; set; } = [];
        public CatalogProjectSummary? SelectedProject { get; set; }
        public IReadOnlyList<CatalogFileSummary> AvailableFiles { get; set; } = [];
        public string? SelectedFileId { get; set; }
        public uint? SearchDebounceId { get; set; }
        public bool MetadataLoaded { get; set; }
        public bool MetadataLoading { get; set; }
        public bool HasLoadedResults { get; set; }
        public bool IsSearching { get; set; }
        public int CurrentOffset { get; set; }
        public int PageSize { get; set; } = CatalogPageSize;
        public bool HasMoreResults { get; set; } = true;
        public int RequestVersion { get; set; }
        public int DetailsRequestVersion { get; set; }
        public int FilesRequestVersion { get; set; }
        public CancellationTokenSource? IconLoadCancellationSource { get; set; }
        public uint? VisibleIconRefreshId { get; set; }
        public HashSet<string> LoadedIconProjectIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InFlightIconProjectIds { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class SourceRow : ListBoxRow
    {
        public SourceRow(AddInstancePageKind pageKind, string title)
        {
            PageKind = pageKind;

            var body = new Box(Orientation.Horizontal, 10);
            var accent = new EventBox { WidthRequest = 3 };
            accent.StyleContext.AddClass("settings-nav-accent");

            var label = new Label(title)
            {
                Xalign = 0,
                MarginTop = 10,
                MarginBottom = 10,
                MarginEnd = 12
            };
            label.StyleContext.AddClass("settings-nav-text");

            body.PackStart(accent, false, false, 0);
            body.PackStart(label, true, true, 0);
            Add(body);
        }

        public AddInstancePageKind PageKind { get; }
    }

    private sealed class VersionRow : ListBoxRow
    {
        public VersionRow(MinecraftVersionOption version, bool useEvenTone)
        {
            Version = version;
            StyleContext.AddClass(useEvenTone ? "add-instance-version-row-even" : "add-instance-version-row-odd");
            Add(CreateVersionRowContent(CreateCell(version.Version), CreateCell(version.Released), CreateCell(version.Kind.ToString().ToLowerInvariant())));
        }

        public MinecraftVersionOption Version { get; }

        private static Label CreateCell(string text)
        {
            return new Label(text) { Xalign = 0 };
        }
    }

    private sealed class LoaderVersionRow : ListBoxRow
    {
        public LoaderVersionRow(LoaderVersionSummary version, bool useEvenTone)
        {
            Version = version;
            StyleContext.AddClass(useEvenTone ? "add-instance-version-row-even" : "add-instance-version-row-odd");
            Add(CreateVersionRowContent(CreateCell(version.LoaderVersion), CreateCell(version.IsStable ? "Stable" : "Unstable"), CreateCell(string.Empty)));
        }

        public LoaderVersionSummary Version { get; }

        private static Label CreateCell(string text)
        {
            return new Label(text) { Xalign = 0 };
        }
    }

    private sealed class PackRow : ListBoxRow
    {
        private readonly Image IconImage = new();
        private readonly Label Placeholder = new("MP");

        public PackRow(CatalogProjectSummary project, bool useEvenTone)
        {
            Project = project;
            Destroyed += (_, _) =>
            {
                ClearIcon();
                IsDisposed = true;
            };
            StyleContext.AddClass(useEvenTone ? "add-instance-pack-row-even" : "add-instance-pack-row-odd");

            var layout = new Box(Orientation.Horizontal, 0)
            {
                MarginTop = 4,
                MarginBottom = 4,
                MarginStart = 8,
                MarginEnd = 8
            };
            layout.StyleContext.AddClass("add-instance-item-shell");
            layout.StyleContext.AddClass("add-instance-pack-row-body");

            var iconCell = new EventBox
            {
                WidthRequest = 52,
                HeightRequest = 52,
                Vexpand = false,
                Hexpand = false
            };
            iconCell.StyleContext.AddClass("add-instance-pack-icon-cell");

            var iconOverlay = new Overlay();
            iconOverlay.WidthRequest = 52;
            iconOverlay.HeightRequest = 52;
            IconImage.Halign = Align.Center;
            IconImage.Valign = Align.Center;
            Placeholder.Halign = Align.Center;
            Placeholder.Valign = Align.Center;
            Placeholder.StyleContext.AddClass("add-instance-pack-icon-placeholder");
            IconImage.Hide();
            iconOverlay.Add(IconImage);
            iconOverlay.AddOverlay(Placeholder);
            iconCell.Add(iconOverlay);

            var textLayout = new Box(Orientation.Vertical, 3)
            {
                MarginTop = 8,
                MarginBottom = 8,
                MarginStart = 10,
                MarginEnd = 10,
                Valign = Align.Center
            };
            var title = new Label(project.Title)
            {
                Xalign = 0,
                Wrap = true,
                MaxWidthChars = 28
            };
            title.StyleContext.AddClass("add-instance-pack-title");

            textLayout.PackStart(title, false, false, 0);
            layout.PackStart(iconCell, false, false, 0);
            layout.PackStart(textLayout, true, true, 0);
            Add(layout);
        }

        public CatalogProjectSummary Project { get; }
        public bool IsDisposed { get; private set; }
        public bool HasIcon => IconImage.Pixbuf is not null;

        public void SetIcon(Pixbuf? pixbuf)
        {
            if (IsDisposed)
            {
                pixbuf?.Dispose();
                return;
            }

            if (pixbuf is null)
            {
                ClearIcon();
                IconImage.Hide();
                Placeholder.Show();
                return;
            }

            var previous = IconImage.Pixbuf;
            IconImage.Pixbuf = pixbuf;
            if (previous is not null && !ReferenceEquals(previous, pixbuf))
            {
                previous.Dispose();
            }
            Placeholder.Hide();
            IconImage.Show();
        }

        public void UnloadIcon()
        {
            if (IsDisposed)
            {
                return;
            }

            ClearIcon();
            IconImage.Hide();
            Placeholder.Show();
        }

        private void ClearIcon()
        {
            var previous = IconImage.Pixbuf;
            IconImage.Pixbuf = null;
            previous?.Dispose();
        }

        private static string BuildCompatibility(CatalogProjectSummary project)
        {
            var parts = new List<string>();
            if (project.GameVersions.Count > 0)
            {
                parts.Add($"MC {project.GameVersions[0]}");
            }

            if (project.Loaders.Count > 0)
            {
                parts.Add(string.Join(", ", project.Loaders.Take(2).Select(ToDisplayName)));
            }

            return parts.Count == 0 ? "Provider modpack" : string.Join(" • ", parts);
        }

        private static string BuildSummary(CatalogProjectSummary project)
        {
            return string.IsNullOrWhiteSpace(project.Description)
                ? "No summary available."
                : project.Description;
        }

        private static string BuildPackMeta(CatalogProjectSummary project)
        {
            var parts = new List<string>();
            if (project.Downloads > 0)
            {
                parts.Add($"{project.Downloads:N0} downloads");
            }

            if (project.Follows > 0)
            {
                parts.Add($"{project.Follows:N0} followers");
            }

            if (project.UpdatedAtUtc is not null)
            {
                parts.Add($"Updated {project.UpdatedAtUtc:yyyy-MM-dd}");
            }

            return parts.Count == 0 ? "Metadata unavailable." : string.Join(" • ", parts);
        }

        private static string BuildSubtitle(CatalogProjectSummary project)
        {
            var parts = new List<string>();
            if (project.GameVersions.Count > 0)
            {
                parts.Add($"MC {project.GameVersions[0]}");
            }

            if (project.Loaders.Count > 0)
            {
                parts.Add(string.Join(", ", project.Loaders.Take(2).Select(ToDisplayName)));
            }

            return parts.Count == 0 ? project.Description : string.Join(" • ", parts);
        }

        private static string BuildMeta(CatalogProjectSummary project)
        {
            var parts = new List<string>();
            if (project.Downloads > 0)
            {
                parts.Add($"{project.Downloads:N0} downloads");
            }

            if (project.Follows > 0)
            {
                parts.Add($"{project.Follows:N0} followers");
            }

            if (project.UpdatedAtUtc is not null)
            {
                parts.Add($"Updated {project.UpdatedAtUtc:yyyy-MM-dd}");
            }

            return parts.Count == 0 ? project.Description : string.Join(" • ", parts);
        }
    }

    private static Widget CreateVersionRowContent(Widget versionCell, Widget releasedCell, Widget typeCell)
    {
        return LauncherStructuredList.CreateRowContent(
            new LauncherStructuredList.CellDefinition(versionCell, Expand: true, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(releasedCell, WidthRequest: 168, ShowTrailingDivider: true),
            new LauncherStructuredList.CellDefinition(typeCell, WidthRequest: 120));
    }

    private static Pixbuf ScaleToSquare(Pixbuf original, int size)
    {
        if (original.Width == original.Height)
        {
            return original.ScaleSimple(size, size, InterpType.Bilinear);
        }

        double ratio = (double)original.Width / original.Height;
        int targetW, targetH;
        if (ratio > 1) { targetW = (int)(size * ratio); targetH = size; }
        else { targetW = size; targetH = (int)(size / ratio); }

        using var intermediate = original.ScaleSimple(targetW, targetH, InterpType.Bilinear);
        var scaled = new Pixbuf(Colorspace.Rgb, true, 8, size, size);
        scaled.Fill(0x00000000);
        intermediate.CopyArea((targetW - size) / 2, (targetH - size) / 2, size, size, scaled, 0, 0);
        return scaled;
    }

    private static string ResolveRequestedInstanceName(
        string? explicitName,
        string? catalogTitle,
        string? selectedVersion,
        string importArchivePath,
        string importFolderPath)
    {
        var candidates = new[]
        {
            explicitName,
            catalogTitle,
            selectedVersion,
            GetImportSourceDisplayName(importArchivePath, importFolderPath)
        };

        foreach (var candidate in candidates)
        {
            var normalized = NormalizeInstanceName(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return string.Empty;
    }

    private static string NormalizeInstanceName(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var normalized = value.Trim();
        return normalized.Length > InstanceNameMaxLength
            ? normalized[..InstanceNameMaxLength].TrimEnd()
            : normalized;
    }

    private static string? GetImportSourceDisplayName(string importArchivePath, string importFolderPath)
    {
        if (!string.IsNullOrWhiteSpace(importArchivePath))
        {
            return System.IO.Path.GetFileNameWithoutExtension(importArchivePath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(importFolderPath))
        {
            var trimmedPath = importFolderPath.Trim().TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            return string.IsNullOrWhiteSpace(trimmedPath) ? null : System.IO.Path.GetFileName(trimmedPath);
        }

        return null;
    }

    private bool IsReleaseGameVersion(string version)
    {
        return MinecraftVersions.Any(item =>
            item.Kind == ReleaseKind.Release &&
            string.Equals(item.Version, version, StringComparison.OrdinalIgnoreCase));
    }


    private static string? ResolveSelectedIconPathForPersistence(string? selectedIconPath)
    {
        if (!string.IsNullOrWhiteSpace(selectedIconPath) && File.Exists(selectedIconPath))
        {
            return selectedIconPath;
        }

        return ResolveBundledDefaultInstanceIconPath();
    }

    private static string? ResolveBundledDefaultInstanceIconPath()
    {
        var outputPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Images", DefaultInstanceIconFileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var sourcePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Images", DefaultInstanceIconFileName));
        return File.Exists(sourcePath) ? sourcePath : null;
    }

}
