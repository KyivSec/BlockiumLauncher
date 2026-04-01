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
using System.Net.Http;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class AddInstanceWindow : Gtk.Window
{
    private static readonly HttpClient PackIconHttpClient = CreatePackIconHttpClient();

    private readonly SearchCatalogUseCase SearchCatalogUseCase;
    private readonly GetCatalogProjectDetailsUseCase GetCatalogProjectDetailsUseCase;
    private readonly GetCatalogProviderMetadataUseCase GetCatalogProviderMetadataUseCase;
    private readonly ImportCatalogModpackUseCase ImportCatalogModpackUseCase;
    private readonly ImportArchiveInstanceUseCase ImportArchiveInstanceUseCase;
    private readonly ImportInstanceUseCase ImportInstanceUseCase;
    private readonly InstallInstanceUseCase InstallInstanceUseCase;
    private readonly IVersionManifestService VersionManifestService;
    private readonly ILoaderMetadataService LoaderMetadataService;
    private readonly IInstanceRepository InstanceRepository;
    private readonly IInstanceContentMetadataService InstanceContentMetadataService;
    private readonly ILauncherPaths LauncherPaths;
    private readonly InstanceBrowserRefreshService InstanceBrowserRefreshService;
    private readonly Dictionary<string, Pixbuf?> PackIconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> PendingPackIcons = new(StringComparer.OrdinalIgnoreCase);

    private readonly Entry NameEntry = new()
    {
        PlaceholderText = "New instance",
        Hexpand = true
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
    private Spinner? OperationSpinner;
    private Label? OperationTitleLabel;
    private Label? OperationBodyLabel;
    private uint? OperationProgressPollSourceId;
    private string? LastObservedOperationLine;

    private readonly ListBox VersionList = new()
    {
        SelectionMode = SelectionMode.Single
    };

    private readonly Entry VersionSearchEntry = new()
    {
        PlaceholderText = "Search versions"
    };

    private readonly CheckButton ReleasesFilter = new("Releases") { Active = true };
    private readonly CheckButton SnapshotsFilter = new("Snapshots");
    private readonly CheckButton BetasFilter = new("Betas");
    private readonly CheckButton AlphasFilter = new("Alphas");
    private readonly CheckButton ExperimentsFilter = new("Experiments");

    private readonly Label LoaderPreviewTitle = new()
    {
        Xalign = 0.5f,
        Yalign = 0.5f,
        Wrap = true,
        Justify = Justification.Center
    };

    private readonly Label LoaderPreviewBody = new()
    {
        Xalign = 0.5f,
        Yalign = 0,
        Wrap = true,
        Justify = Justification.Center
    };

    private readonly RadioButton NoneLoaderRadio = new("None");
    private readonly RadioButton NeoForgeLoaderRadio;
    private readonly RadioButton ForgeLoaderRadio;
    private readonly RadioButton FabricLoaderRadio;
    private readonly RadioButton QuiltLoaderRadio;

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
    private string? SelectedIconPath;
    private bool IsResettingState;
    private bool IsSubmitting;
    private bool VersionsLoaded;
    private bool VersionsLoading;
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
        ImportCatalogModpackUseCase importCatalogModpackUseCase,
        ImportArchiveInstanceUseCase importArchiveInstanceUseCase,
        ImportInstanceUseCase importInstanceUseCase,
        InstallInstanceUseCase installInstanceUseCase,
        IVersionManifestService versionManifestService,
        ILoaderMetadataService loaderMetadataService,
        IInstanceRepository instanceRepository,
        IInstanceContentMetadataService instanceContentMetadataService,
        ILauncherPaths launcherPaths,
        InstanceBrowserRefreshService instanceBrowserRefreshService) : base("Add Instance")
    {
        SearchCatalogUseCase = searchCatalogUseCase ?? throw new ArgumentNullException(nameof(searchCatalogUseCase));
        GetCatalogProjectDetailsUseCase = getCatalogProjectDetailsUseCase ?? throw new ArgumentNullException(nameof(getCatalogProjectDetailsUseCase));
        GetCatalogProviderMetadataUseCase = getCatalogProviderMetadataUseCase ?? throw new ArgumentNullException(nameof(getCatalogProviderMetadataUseCase));
        ImportCatalogModpackUseCase = importCatalogModpackUseCase ?? throw new ArgumentNullException(nameof(importCatalogModpackUseCase));
        ImportArchiveInstanceUseCase = importArchiveInstanceUseCase ?? throw new ArgumentNullException(nameof(importArchiveInstanceUseCase));
        ImportInstanceUseCase = importInstanceUseCase ?? throw new ArgumentNullException(nameof(importInstanceUseCase));
        InstallInstanceUseCase = installInstanceUseCase ?? throw new ArgumentNullException(nameof(installInstanceUseCase));
        VersionManifestService = versionManifestService ?? throw new ArgumentNullException(nameof(versionManifestService));
        LoaderMetadataService = loaderMetadataService ?? throw new ArgumentNullException(nameof(loaderMetadataService));
        InstanceRepository = instanceRepository ?? throw new ArgumentNullException(nameof(instanceRepository));
        InstanceContentMetadataService = instanceContentMetadataService ?? throw new ArgumentNullException(nameof(instanceContentMetadataService));
        LauncherPaths = launcherPaths ?? throw new ArgumentNullException(nameof(launcherPaths));
        InstanceBrowserRefreshService = instanceBrowserRefreshService ?? throw new ArgumentNullException(nameof(instanceBrowserRefreshService));

        NeoForgeLoaderRadio = new RadioButton(NoneLoaderRadio, "NeoForge");
        ForgeLoaderRadio = new RadioButton(NoneLoaderRadio, "Forge");
        FabricLoaderRadio = new RadioButton(NoneLoaderRadio, "Fabric");
        QuiltLoaderRadio = new RadioButton(NoneLoaderRadio, "Quilt");

        CurseForgePage = CreateCatalogPageState(CatalogProvider.CurseForge, "Search CurseForge modpacks");
        ModrinthPage = CreateCatalogPageState(CatalogProvider.Modrinth, "Search Modrinth modpacks");

        SetDefaultSize(1180, 760);
        Resizable = true;
        WindowPosition = WindowPosition.Center;

        DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            if (!IsSubmitting)
            {
                ResetAndHide();
            }
        };

        ApplyFieldStyles(NameEntry);
        ApplyFieldStyles(VersionSearchEntry, isSearchField: true);
        ApplyFieldStyles(ImportArchiveEntry, isReadOnly: true);
        ApplyFieldStyles(ImportFolderEntry, isReadOnly: true);
        ApplyFieldStyles(CurseForgePage.SearchEntry, isSearchField: true);
        ApplyFieldStyles(ModrinthPage.SearchEntry, isSearchField: true);

        PrimaryActionButton.StyleContext.AddClass("primary-button");
        PrimaryActionButton.StyleContext.AddClass("add-instance-footer-primary");
        CancelButton.StyleContext.AddClass("action-button");
        CancelButton.StyleContext.AddClass("add-instance-footer-secondary");

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
        PopulateVersionList();
        ConfigureCatalogControls(CurseForgePage);
        ConfigureCatalogControls(ModrinthPage);
        UpdateLoaderPreview();
        UpdateInstanceIconPreview();
        UpdatePrimaryActionState();
    }

    public void PresentFrom(Gtk.Window owner)
    {
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

    private void HookEvents()
    {
        NameEntry.Changed += (_, _) => UpdatePrimaryActionState();
        VersionSearchEntry.Changed += (_, _) => PopulateVersionList();

        VersionList.RowSelected += (_, args) =>
        {
            SelectedVersion = (args.Row as VersionRow)?.Version.Version;
            UpdatePrimaryActionState();
        };

        foreach (var filter in new[] { ReleasesFilter, SnapshotsFilter, BetasFilter, AlphasFilter, ExperimentsFilter })
        {
            filter.StyleContext.AddClass("add-instance-filter-check");
            filter.Toggled += (_, _) => PopulateVersionList();
        }

        foreach (var button in new[] { NoneLoaderRadio, NeoForgeLoaderRadio, ForgeLoaderRadio, FabricLoaderRadio, QuiltLoaderRadio })
        {
            button.Toggled += HandleLoaderChanged;
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
        state.FilterButton.StyleContext.AddClass("popover-menu-button");
        state.SortButton.Clicked += (_, _) => TogglePopover(state.SortPopover);
        state.FilterButton.Clicked += (_, _) => TogglePopover(state.FilterPopover);
        state.SortPopover = CreatePopover(state.SortButton);
        state.FilterPopover = CreatePopover(state.FilterButton);
        state.SelectedSort = CatalogSearchSort.Relevance;
        RebuildSortPopover(state);
        RebuildFilterPopover(state);
        return state;
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

        var title = new Label("Add Instance")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-title");
        bar.PackStart(title);
        return bar;
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
        ContentStack.AddNamed(BuildPlatformPage(
            ModrinthPage,
            "Modrinth",
            "Browse Modrinth modpacks with provider metadata, real search, and embedded descriptions."), AddInstancePageKind.Modrinth.ToString());
        ContentStack.AddNamed(BuildPlatformPage(
            CurseForgePage,
            "CurseForge",
            "Discover CurseForge modpacks with API-backed search, provider sorting, and real filters."), AddInstancePageKind.CurseForge.ToString());
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
        var subtitle = CreatePageSubtitle("Pick a Minecraft version first, then choose an optional mod loader from the same page.");

        var top = new Paned(Orientation.Horizontal)
        {
            WideHandle = false,
            Position = 820
        };
        top.StyleContext.AddClass("add-instance-pane");
        top.Pack1(BuildVersionBrowser(), true, false);
        top.Pack2(BuildReleaseFilters(), false, false);

        var bottom = new Paned(Orientation.Horizontal)
        {
            WideHandle = false,
            Position = 910
        };
        bottom.StyleContext.AddClass("add-instance-pane");
        bottom.Pack1(BuildLoaderPreview(), true, false);
        bottom.Pack2(BuildLoaderChooser(), false, false);

        page.PackStart(title, false, false, 0);
        page.PackStart(subtitle, false, false, 0);
        page.PackStart(top, true, true, 0);
        page.PackStart(bottom, true, true, 0);

        return page;
    }

    private Widget BuildVersionBrowser()
    {
        var card = CreateCardShell();
        var layout = CreateCardContentBox();
        layout.PackStart(BuildSectionHeader("Version", "Choose the target Minecraft version for the instance."), false, false, 0);
        layout.PackStart(BuildVersionHeaderRow(), false, false, 0);

        VersionList.StyleContext.AddClass("add-instance-version-list");

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            Hexpand = true,
            Vexpand = true
        };
        scroller.Add(VersionList);

        layout.PackStart(scroller, true, true, 0);
        layout.PackStart(BuildLabeledField("Search", VersionSearchEntry, null, additionalLabelClass: "add-instance-search-label"), false, false, 0);

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

    private Widget BuildReleaseFilters()
    {
        var card = CreateCardShell();
        card.WidthRequest = 196;

        var layout = CreateCardContentBox();
        layout.PackStart(BuildSectionHeader("Filter", "Choose which release streams stay visible."), false, false, 0);

        foreach (var filter in new[] { ReleasesFilter, SnapshotsFilter, BetasFilter, AlphasFilter, ExperimentsFilter })
        {
            layout.PackStart(filter, false, false, 0);
        }

        layout.PackStart(new Box(Orientation.Vertical, 0), true, true, 0);
        card.Add(layout);
        return card;
    }

    private Widget BuildLoaderPreview()
    {
        var card = CreateCardShell();
        var layout = CreateCardContentBox(12);
        layout.PackStart(BuildSectionHeader("Loader", "Quick install keeps all supported mod loaders in a single place."), false, false, 0);

        var preview = new EventBox
        {
            Hexpand = true,
            Vexpand = true
        };
        preview.StyleContext.AddClass("add-instance-loader-preview");

        var previewContent = new Box(Orientation.Vertical, 10)
        {
            Halign = Align.Center,
            Valign = Align.Center,
            MarginTop = 20,
            MarginBottom = 20,
            MarginStart = 20,
            MarginEnd = 20
        };

        LoaderPreviewTitle.StyleContext.AddClass("add-instance-loader-preview-title");
        LoaderPreviewBody.StyleContext.AddClass("settings-help");

        previewContent.PackStart(LoaderPreviewTitle, false, false, 0);
        previewContent.PackStart(LoaderPreviewBody, false, false, 0);
        preview.Add(previewContent);

        layout.PackStart(preview, true, true, 0);
        card.Add(layout);
        return card;
    }

    private Widget BuildLoaderChooser()
    {
        var card = CreateCardShell();
        card.WidthRequest = 188;

        var layout = CreateCardContentBox();
        layout.StyleContext.AddClass("add-instance-loader-choice-shell");
        layout.PackStart(BuildSectionHeader("Mod Loader", "Optional"), false, false, 0);

        foreach (var button in new[] { NoneLoaderRadio, NeoForgeLoaderRadio, ForgeLoaderRadio, FabricLoaderRadio, QuiltLoaderRadio })
        {
            layout.PackStart(button, false, false, 0);
        }

        layout.PackStart(new Box(Orientation.Vertical, 0), true, true, 0);
        card.Add(layout);
        return card;
    }

    private Widget BuildImportPage()
    {
        var page = CreatePageBox();

        page.PackStart(CreatePageTitle("Import"), false, false, 0);
        page.PackStart(CreatePageSubtitle("Import an existing archive or folder. Pick one source and the launcher will handle the rest later."), false, false, 0);

        var pathsCard = CreateCardShell();
        var pathLayout = CreateCardContentBox(12);
        pathLayout.PackStart(BuildSectionHeader("Source", "Choose an archive or folder from your device."), false, false, 0);
        pathLayout.PackStart(BuildPickerRow("Archive", ImportArchiveEntry, "Browse", BrowseArchive), false, false, 0);
        pathLayout.PackStart(BuildPickerRow("Folder", ImportFolderEntry, "Browse", BrowseFolder), false, false, 0);
        pathsCard.Add(pathLayout);

        var optionsCard = CreateCardShell();
        var optionsLayout = CreateCardContentBox(10);
        optionsLayout.PackStart(BuildSectionHeader("Options", "Keep the import flow predictable and launcher-safe."), false, false, 0);
        optionsLayout.PackStart(ImportCopyFilesOption, false, false, 0);
        optionsLayout.PackStart(ImportKeepMetadataOption, false, false, 0);
        optionsCard.Add(optionsLayout);

        page.PackStart(pathsCard, false, false, 0);
        page.PackStart(optionsCard, false, false, 0);
        page.PackStart(new Box(Orientation.Vertical, 0), true, true, 0);
        return page;
    }

    private Widget BuildPlatformPage(CatalogPageState state, string titleText, string subtitleText)
    {
        state.DetailsTitle = new Label("Nothing selected")
        {
            Xalign = 0,
            Wrap = true
        };
        state.DetailsTitle.StyleContext.AddClass("settings-page-title");

        state.DetailsMeta = new Label("Choose a project to inspect its details.")
        {
            Xalign = 0,
            Wrap = true
        };
        state.DetailsMeta.StyleContext.AddClass("settings-help");

        state.DescriptionView = new CatalogDescriptionView();
        state.DescriptionView.StyleContext.AddClass("catalog-description-view");
        state.ListStatus = new Label("Loading projects...")
        {
            Xalign = 0,
            Wrap = true
        };
        state.ListStatus.StyleContext.AddClass("settings-help");

        var page = CreatePageBox();
        page.PackStart(CreatePageTitle(titleText), false, false, 0);
        page.PackStart(CreatePageSubtitle(subtitleText), false, false, 0);

        var searchCard = CreateCardShell();
        var searchLayout = CreateCardContentBox(10);
        searchLayout.PackStart(BuildSectionHeader("Search", "Use provider search, sort, and multi-select filters to narrow the results."), false, false, 0);
        searchLayout.PackStart(state.SearchEntry, false, false, 0);

        var controlsRow = new Box(Orientation.Horizontal, 8);
        controlsRow.PackStart(state.SortButton, false, false, 0);
        controlsRow.PackStart(state.FilterButton, false, false, 0);
        controlsRow.PackStart(new Box(Orientation.Horizontal, 0), true, true, 0);
        searchLayout.PackStart(controlsRow, false, false, 0);
        searchCard.Add(searchLayout);

        var browserSplit = new Paned(Orientation.Horizontal)
        {
            WideHandle = false,
            Position = 650
        };
        browserSplit.Pack1(BuildPackListCard(state), true, false);
        browserSplit.Pack2(BuildPackDetailsCard(state), true, false);

        page.PackStart(searchCard, false, false, 0);
        page.PackStart(browserSplit, true, true, 0);
        return page;
    }

    private Widget BuildPackListCard(CatalogPageState state)
    {
        var card = CreateCardShell();
        var layout = CreateCardContentBox(10);
        layout.PackStart(BuildSectionHeader("Packs", "Search results update from the provider APIs using the current filters."), false, false, 0);

        state.PackList.StyleContext.AddClass("add-instance-pack-list");
        state.PackList.RowSelected += (_, args) =>
        {
            state.SelectedProject = (args.Row as PackRow)?.Project;
            UpdatePrimaryActionState();

            if (state.SelectedProject is not null)
            {
                _ = LoadProjectDetailsAsync(state, state.SelectedProject);
            }
            else
            {
                ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
            }
        };

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            Hexpand = true,
            Vexpand = true
        };
        scroller.Add(state.PackList);

        layout.PackStart(scroller, true, true, 0);
        layout.PackStart(state.ListStatus, false, false, 0);
        card.Add(layout);
        return card;
    }

    private Widget BuildPackDetailsCard(CatalogPageState state)
    {
        var card = CreateCardShell();
        card.WidthRequest = 360;

        var layout = CreateCardContentBox(12);
        layout.PackStart(BuildSectionHeader("Details", "Descriptions render inside the launcher with a safe rich-text host."), false, false, 0);
        layout.PackStart(state.DetailsTitle, false, false, 0);
        layout.PackStart(state.DetailsMeta, false, false, 0);
        layout.PackStart(state.DescriptionView, true, true, 0);
        card.Add(layout);
        return card;
    }

    private Widget BuildFooter()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("add-instance-footer");

        var layout = new Box(Orientation.Horizontal, 10)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 14,
            MarginEnd = 14
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
            MarginTop = 14,
            MarginBottom = 14,
            MarginStart = 14,
            MarginEnd = 14
        };
    }

    private static Box CreatePageBox()
    {
        var page = new Box(Orientation.Vertical, 14)
        {
            MarginTop = 14,
            MarginBottom = 14,
            MarginStart = 14,
            MarginEnd = 14
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

    private static Label CreatePageSubtitle(string text)
    {
        var label = new Label(text)
        {
            Xalign = 0,
            Wrap = true
        };
        label.StyleContext.AddClass("settings-help");
        return label;
    }

    private Widget BuildSectionHeader(string titleText, string subtitleText)
    {
        var box = new Box(Orientation.Vertical, 4)
        {
            MarginBottom = 2
        };

        var title = new Label(titleText)
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-section-title");

        var subtitle = new Label(subtitleText)
        {
            Xalign = 0,
            Wrap = true
        };
        subtitle.StyleContext.AddClass("settings-help");

        box.PackStart(title, false, false, 0);
        box.PackStart(subtitle, false, false, 0);
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
            PopulateVersionList();
            PopulateGameVersionOptions(CurseForgePage, versions.Where(static version => version.Kind == ReleaseKind.Release).Select(static version => version.Version).ToArray());
            PopulateGameVersionOptions(ModrinthPage, versions.Where(static version => version.Kind == ReleaseKind.Release).Select(static version => version.Version).ToArray());
        });
    }

    private void PopulateVersionList()
    {
        foreach (var row in VersionList.Children.ToArray())
        {
            VersionList.Remove(row);
            row.Destroy();
        }

        var normalizedSearch = VersionSearchEntry.Text?.Trim() ?? string.Empty;

        var visibleVersions = MinecraftVersions
            .Where(version => IsReleaseKindEnabled(version.Kind))
            .Where(version => string.IsNullOrWhiteSpace(normalizedSearch)
                || version.Version.Contains(normalizedSearch, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        for (var index = 0; index < visibleVersions.Length; index++)
        {
            var version = visibleVersions[index];
            var row = new VersionRow(version, index % 2 == 0);
            VersionList.Add(row);

            if (SelectedVersion == version.Version)
            {
                VersionList.SelectRow(row);
            }
        }

        if (SelectedVersion is not null && visibleVersions.All(version => version.Version != SelectedVersion))
        {
            SelectedVersion = null;
        }

        VersionList.ShowAll();
        UpdatePrimaryActionState();
    }

    private bool IsReleaseKindEnabled(ReleaseKind kind)
    {
        return kind switch
        {
            ReleaseKind.Release => ReleasesFilter.Active,
            ReleaseKind.Snapshot => SnapshotsFilter.Active,
            ReleaseKind.Beta => BetasFilter.Active,
            ReleaseKind.Alpha => AlphasFilter.Active,
            ReleaseKind.Experiment => ExperimentsFilter.Active,
            _ => true
        };
    }

    private void HandleLoaderChanged(object? sender, EventArgs e)
    {
        var selectedButton = new[]
        {
            NoneLoaderRadio,
            NeoForgeLoaderRadio,
            ForgeLoaderRadio,
            FabricLoaderRadio,
            QuiltLoaderRadio
        }.FirstOrDefault(button => button.Active);

        SelectedLoader = selectedButton?.Label ?? "None";
        UpdateLoaderPreview();
    }

    private void UpdateLoaderPreview()
    {
        if (SelectedLoader == "None")
        {
            LoaderPreviewTitle.Text = "No mod loader is selected.";
            LoaderPreviewBody.Text = "This instance will stay close to vanilla until you choose Fabric, Forge, NeoForge, or Quilt.";
            return;
        }

        LoaderPreviewTitle.Text = $"{SelectedLoader} is selected.";
        LoaderPreviewBody.Text = $"Quick install will resolve the latest compatible {SelectedLoader} loader version when you create the instance.";
    }

    private void ConfigureCatalogControls(CatalogPageState state)
    {
        state.SearchEntry.Changed += (_, _) => QueueCatalogRefresh(state);
        PopulateSortOptions(state, DefaultSorts);
        PopulateGameVersionOptions(state);
        PopulateLoaderOptions(state, []);
        PopulateCategoryOptions(state, []);
        ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
    }

    private async Task EnsureCatalogPageReadyAsync(CatalogPageState state)
    {
        if (!state.MetadataLoaded && !state.MetadataLoading)
        {
            await LoadCatalogMetadataAsync(state).ConfigureAwait(false);
        }

        if (!state.HasLoadedResults && !state.IsSearching)
        {
            await RefreshCatalogResultsAsync(state).ConfigureAwait(false);
        }
    }

    private void ResetAndHide()
    {
        CloseOperationDialog();
        ResetWindowState();
        Hide();
    }

    private void ResetWindowState()
    {
        IsResettingState = true;
        try
        {
            NameEntry.Text = string.Empty;
            SelectedIconPath = null;
            SelectedVersion = null;
            SelectedLoader = "None";
            VersionSearchEntry.Text = string.Empty;
            ReleasesFilter.Active = true;
            SnapshotsFilter.Active = false;
            BetasFilter.Active = false;
            AlphasFilter.Active = false;
            ExperimentsFilter.Active = false;
            ImportArchiveEntry.Text = string.Empty;
            ImportFolderEntry.Text = string.Empty;
            ImportCopyFilesOption.Active = true;
            ImportKeepMetadataOption.Active = true;
            NoneLoaderRadio.Active = true;

            ResetCatalogPageState(CurseForgePage);
            ResetCatalogPageState(ModrinthPage);

            UpdateInstanceIconPreview();
            PopulateVersionList();
            SwitchToPage(AddInstancePageKind.QuickInstall);
            if (SourceList.GetRowAtIndex(0) is ListBoxRow firstRow)
            {
                SourceList.SelectRow(firstRow);
            }

            UpdateLoaderPreview();
            UpdatePrimaryActionState();
        }
        finally
        {
            IsResettingState = false;
        }
    }

    private void ResetCatalogPageState(CatalogPageState state)
    {
        if (state.SearchDebounceId is not null)
        {
            GLib.Source.Remove(state.SearchDebounceId.Value);
            state.SearchDebounceId = null;
        }

        state.SortPopover.Hide();
        state.FilterPopover.Hide();
        state.SearchEntry.Text = string.Empty;
        state.SelectedSort = CatalogSearchSort.Relevance;
        state.SelectedGameVersions.Clear();
        state.SelectedLoaders.Clear();
        state.SelectedCategories.Clear();
        state.SelectedProject = null;
        state.Results = [];
        state.RequestVersion++;
        state.DetailsRequestVersion++;
        PopulateSortOptions(state, state.AvailableSorts.Count > 0 ? state.AvailableSorts : DefaultSorts);
        PopulateGameVersionOptions(state, state.AvailableGameVersions.Count > 0 ? state.AvailableGameVersions : null);
        PopulateLoaderOptions(state, state.AvailableLoaders);
        PopulateCategoryOptions(state, state.AvailableCategories);
        PopulatePackList(state, []);
        state.ListStatus.Text = "Search the provider catalog to load projects.";
        ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
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
        if (popover.Visible)
        {
            popover.Hide();
            return;
        }

        popover.ShowAll();
    }

    private void RebuildSortPopover(CatalogPageState state)
    {
        ReplacePopoverContent(state.SortPopover, BuildSortPopoverContent(state));
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

    private void RebuildFilterPopover(CatalogPageState state)
    {
        ReplacePopoverContent(state.FilterPopover, BuildFilterPopoverContent(state));
    }

    private Widget BuildFilterPopoverContent(CatalogPageState state)
    {
        var outer = new Box(Orientation.Vertical, 10);
        outer.StyleContext.AddClass("popover-content");

        var title = new Label("Filters")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        outer.PackStart(title, false, false, 0);

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            HeightRequest = 340
        };

        var content = new Box(Orientation.Vertical, 10);
        content.PackStart(BuildFilterSection("Versions", state.AvailableGameVersions, state.SelectedGameVersions, state), false, false, 0);
        content.PackStart(BuildFilterSection("Loaders", state.AvailableLoaders, state.SelectedLoaders, state, ToDisplayName), false, false, 0);
        content.PackStart(BuildFilterSection("Categories", state.AvailableCategories, state.SelectedCategories, state), false, false, 0);

        scroller.Add(content);
        outer.PackStart(scroller, true, true, 0);
        return outer;
    }

    private Widget BuildFilterSection(
        string titleText,
        IReadOnlyList<string> values,
        HashSet<string> selectedValues,
        CatalogPageState state,
        Func<string, string>? formatter = null)
    {
        var section = new Box(Orientation.Vertical, 6);

        var title = new Label(titleText)
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("filter-section-title");
        section.PackStart(title, false, false, 0);

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

                UpdateFilterButtonLabel(state);
                QueueCatalogRefresh(state, debounce: false);
            };
            section.PackStart(button, false, false, 0);
        }

        return section;
    }

    private static void ReplacePopoverContent(Popover popover, Widget child)
    {
        if (popover.Child is Widget existingChild)
        {
            popover.Remove(existingChild);
            existingChild.Destroy();
        }

        popover.Add(child);
    }

    private static void UpdateSortButtonLabel(CatalogPageState state)
    {
        state.SortButton.Label = $"Sort: {GetSortLabel(state.SelectedSort)}";
    }

    private static void UpdateFilterButtonLabel(CatalogPageState state)
    {
        var count = state.SelectedGameVersions.Count + state.SelectedLoaders.Count + state.SelectedCategories.Count;
        state.FilterButton.Label = count > 0 ? $"Filters ({count})" : "Filters";
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
        state.IsSearching = true;
        state.RequestVersion++;
        var requestVersion = state.RequestVersion;

        Gtk.Application.Invoke((_, _) => state.ListStatus.Text = "Loading projects...");

        var result = await SearchCatalogUseCase.ExecuteAsync(new SearchCatalogRequest
        {
            Provider = state.Provider,
            ContentType = CatalogContentType.Modpack,
            Query = string.IsNullOrWhiteSpace(state.SearchEntry.Text) ? null : state.SearchEntry.Text.Trim(),
            GameVersions = state.SelectedGameVersions.ToArray(),
            Loaders = state.SelectedLoaders.ToArray(),
            Categories = state.SelectedCategories.ToArray(),
            Sort = state.SelectedSort,
            Limit = 25,
            Offset = 0
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
                PopulatePackList(state, []);
                state.ListStatus.Text = result.Error.Message;
                ResetProjectDetails(state, "Catalog unavailable", result.Error.Message);
                return;
            }

            PopulatePackList(state, result.Value);
            state.ListStatus.Text = result.Value.Count == 0
                ? "No projects match the current search."
                : $"{result.Value.Count} project(s) loaded.";
        });
    }

    private void PopulatePackList(CatalogPageState state, IReadOnlyList<CatalogProjectSummary> projects)
    {
        foreach (var child in state.PackList.Children.ToArray())
        {
            state.PackList.Remove(child);
            child.Destroy();
        }

        state.Results = projects;
        state.SelectedProject = null;

        for (var index = 0; index < projects.Count; index++)
        {
            var row = new PackRow(projects[index], index % 2 == 0);
            state.PackList.Add(row);
            _ = LoadPackIconAsync(row);
        }

        state.PackList.ShowAll();

        if (state.PackList.GetRowAtIndex(0) is ListBoxRow firstRow)
        {
            state.PackList.SelectRow(firstRow);
        }
        else
        {
            ResetProjectDetails(state, "Nothing selected", "No project matches the current filters.");
        }

        UpdatePrimaryActionState();
    }

    private async Task LoadProjectDetailsAsync(CatalogPageState state, CatalogProjectSummary project)
    {
        var requestVersion = ++state.DetailsRequestVersion;

        Gtk.Application.Invoke((_, _) =>
        {
            state.DetailsTitle.Text = project.Title;
            state.DetailsMeta.Text = "Loading project details...";
            state.DescriptionView.SetContent(project.Description, CatalogDescriptionFormat.PlainText);
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
                state.DetailsTitle.Text = project.Title;
                state.DetailsMeta.Text = BuildSummaryMeta(project);
                state.DescriptionView.SetContent(project.Description, CatalogDescriptionFormat.PlainText);
                return;
            }

            var details = result.Value;
            state.DetailsTitle.Text = details.Title;
            state.DetailsMeta.Text = BuildDetailsMeta(details);
            state.DescriptionView.SetContent(
                string.IsNullOrWhiteSpace(details.DescriptionContent) ? details.Summary : details.DescriptionContent,
                string.IsNullOrWhiteSpace(details.DescriptionContent) ? CatalogDescriptionFormat.PlainText : details.DescriptionFormat);
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
        state.DetailsTitle.Text = title;
        state.DetailsMeta.Text = description;
        state.DescriptionView.SetContent(description, CatalogDescriptionFormat.PlainText);
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
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        state.SelectedGameVersions.RemoveWhere(value => !state.AvailableGameVersions.Contains(value, StringComparer.OrdinalIgnoreCase));
        RebuildFilterPopover(state);
        UpdateFilterButtonLabel(state);
    }

    private void PopulateLoaderOptions(CatalogPageState state, IReadOnlyList<string> loaders)
    {
        state.AvailableLoaders = (loaders.Count > 0 ? loaders : LoaderChoices.Where(choice => choice.Id != "none").Select(choice => choice.Id).ToArray())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        state.SelectedLoaders.RemoveWhere(value => !state.AvailableLoaders.Contains(value, StringComparer.OrdinalIgnoreCase));
        RebuildFilterPopover(state);
        UpdateFilterButtonLabel(state);
    }

    private void PopulateCategoryOptions(CatalogPageState state, IReadOnlyList<string> categories)
    {
        state.AvailableCategories = categories
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        state.SelectedCategories.RemoveWhere(value => !state.AvailableCategories.Contains(value, StringComparer.OrdinalIgnoreCase));
        RebuildFilterPopover(state);
        UpdateFilterButtonLabel(state);
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

    private async Task LoadPackIconAsync(PackRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Project.IconUrl))
        {
            row.SetIcon(null);
            return;
        }

        var iconUrl = row.Project.IconUrl.Trim();
        if (PackIconCache.TryGetValue(iconUrl, out var cachedIcon))
        {
            row.SetIcon(cachedIcon);
            return;
        }

        lock (PendingPackIcons)
        {
            if (!PendingPackIcons.Add(iconUrl))
            {
                return;
            }
        }

        Pixbuf? pixbuf = null;
        try
        {
            using var response = await PackIconHttpClient.GetAsync(iconUrl).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
            {
                var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                using var loader = new PixbufLoader("image/png");
                loader.Write(bytes);
                loader.Close();
                pixbuf = loader.Pixbuf?.ScaleSimple(40, 40, InterpType.Bilinear);
            }
        }
        catch
        {
        }

        lock (PendingPackIcons)
        {
            PendingPackIcons.Remove(iconUrl);
            PackIconCache[iconUrl] = pixbuf;
        }

        Gtk.Application.Invoke((_, _) =>
        {
            if (row.Project.IconUrl is not null &&
                string.Equals(row.Project.IconUrl.Trim(), iconUrl, StringComparison.OrdinalIgnoreCase))
            {
                row.SetIcon(pixbuf);
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
                InstanceIconImage.Pixbuf = new Pixbuf(SelectedIconPath, 72, 72, true);
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
        PrimaryActionButton.Label = CurrentPage == AddInstancePageKind.QuickInstall ? "Create" : "Import";

        var hasName = !string.IsNullOrWhiteSpace(NameEntry.Text);
        PrimaryActionButton.Sensitive = !IsSubmitting && (CurrentPage switch
        {
            AddInstancePageKind.QuickInstall => hasName && !string.IsNullOrWhiteSpace(SelectedVersion),
            AddInstancePageKind.Import => hasName && (!string.IsNullOrWhiteSpace(ImportArchiveEntry.Text) || !string.IsNullOrWhiteSpace(ImportFolderEntry.Text)),
            AddInstancePageKind.CurseForge => hasName && CurseForgePage.SelectedProject is not null,
            AddInstancePageKind.Modrinth => hasName && ModrinthPage.SelectedProject is not null,
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
        CancelButton.Sensitive = false;
        UpdatePrimaryActionState();
        ShowOperationDialog(GetInitialOperationTitle(), GetInitialOperationBody());
        StartOperationProgressPolling();

        GLib.Idle.Add(() =>
        {
            _ = RunPrimaryActionAsync(request);
            return false;
        });
    }

    private async Task RunPrimaryActionAsync(OperationRequest request)
    {
        try
        {
            var result = await Task.Run(
                    () => ExecuteOperationAsync(request))
                .ConfigureAwait(false);

            Gtk.Application.Invoke((_, _) =>
            {
                StopOperationProgressPolling();
                CloseOperationDialog();
                CancelButton.Sensitive = true;

                if (!result.IsSuccess)
                {
                    ShowMessage("Add Instance", result.ErrorMessage ?? "The requested action failed.", MessageType.Error);
                    IsSubmitting = false;
                    UpdatePrimaryActionState();
                    return;
                }

                ShowMessage("Add Instance", result.InfoMessage ?? BuildSuccessMessage(), MessageType.Info);

                InstanceBrowserRefreshService.RequestRefresh(result.InstanceId);
                IsSubmitting = false;
                ResetAndHide();
            });
        }
        catch (Exception exception)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                StopOperationProgressPolling();
                CloseOperationDialog();
                CancelButton.Sensitive = true;
                IsSubmitting = false;
                UpdatePrimaryActionState();
                ShowMessage("Add Instance", exception.Message, MessageType.Error);
            });
        }
    }

    private Task<InstanceCreationOutcome> ExecuteOperationAsync(OperationRequest request)
    {
        return request.Page switch
        {
            AddInstancePageKind.QuickInstall => CreateQuickInstallAsync(request),
            AddInstancePageKind.Import => ImportFromLocalSourceAsync(request),
            AddInstancePageKind.CurseForge => ImportCatalogProjectAsync(request),
            AddInstancePageKind.Modrinth => ImportCatalogProjectAsync(request),
            _ => Task.FromResult(InstanceCreationOutcome.Failure("Unsupported page."))
        };
    }

    private OperationRequest BuildOperationRequest()
    {
        return new OperationRequest(
            Page: CurrentPage,
            InstanceName: NameEntry.Text?.Trim() ?? string.Empty,
            SelectedVersion: SelectedVersion,
            SelectedLoader: SelectedLoader,
            SelectedIconPath: SelectedIconPath,
            ImportArchivePath: ImportArchiveEntry.Text?.Trim() ?? string.Empty,
            ImportFolderPath: ImportFolderEntry.Text?.Trim() ?? string.Empty,
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
            });
    }

    private async Task<InstanceCreationOutcome> CreateQuickInstallAsync(OperationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.InstanceName) || string.IsNullOrWhiteSpace(request.SelectedVersion))
        {
            return InstanceCreationOutcome.Failure("Choose an instance name and Minecraft version first.");
        }

        var loaderType = MapSelectedLoaderType(request.SelectedLoader);
        string? loaderVersion = null;
        if (loaderType != LoaderType.Vanilla)
        {
            UpdateOperationDialog("Preparing loader", $"Resolving the best {loaderType} version for Minecraft {request.SelectedVersion}.");
            var loaderResolution = await ResolvePreferredLoaderVersionAsync(loaderType, request.SelectedVersion).ConfigureAwait(false);
            if (loaderResolution.IsFailure)
            {
                return InstanceCreationOutcome.Failure(loaderResolution.Error.Message);
            }

            loaderVersion = loaderResolution.Value;
        }

        UpdateOperationDialog("Creating instance", $"Installing {request.InstanceName} and downloading the required game files.");
        var installResult = await InstallInstanceUseCase.ExecuteAsync(new InstallInstanceRequest
        {
            InstanceName = request.InstanceName,
            GameVersion = request.SelectedVersion,
            LoaderType = loaderType,
            LoaderVersion = loaderVersion,
            DownloadRuntime = true
        }).ConfigureAwait(false);

        if (installResult.IsFailure)
        {
            return InstanceCreationOutcome.Failure(installResult.Error.Message);
        }

        UpdateOperationDialog("Finalizing instance", "Writing launcher metadata and refreshing the instance browser.");
        await PersistSelectedIconAsync(installResult.Value.Instance, request.SelectedIconPath).ConfigureAwait(false);
        return InstanceCreationOutcome.Success(installResult.Value.Instance.InstanceId);
    }

    private async Task<InstanceCreationOutcome> ImportCatalogProjectAsync(OperationRequest request)
    {
        if (request.Provider is null || string.IsNullOrWhiteSpace(request.SelectedProjectId) || string.IsNullOrWhiteSpace(request.InstanceName))
        {
            return InstanceCreationOutcome.Failure("Choose a project and instance name first.");
        }

        UpdateOperationDialog(
            $"Importing from {GetProviderDisplayName(request.Provider.Value)}",
            $"Downloading and installing the selected modpack as {request.InstanceName}.");
        var result = await ImportCatalogModpackUseCase.ExecuteAsync(new ImportCatalogModpackRequest
        {
            Provider = request.Provider.Value,
            ProjectId = request.SelectedProjectId,
            InstanceName = request.InstanceName,
            DownloadRuntime = true,
            WaitForManualDownloads = false
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            return InstanceCreationOutcome.Failure(result.Error.Message);
        }

        UpdateOperationDialog("Finalizing import", "Writing launcher metadata and indexing the imported instance.");
        await PersistSelectedIconAsync(result.Value.Instance, request.SelectedIconPath).ConfigureAwait(false);

        var infoMessage = result.Value.PendingManualDownloads.Count > 0
            ? $"Imported '{result.Value.Instance.Name}'. {result.Value.PendingManualDownloads.Count} file(s) still require manual download in {result.Value.DownloadsDirectory}."
            : null;

        return InstanceCreationOutcome.Success(result.Value.Instance.InstanceId, infoMessage);
    }

    private async Task<InstanceCreationOutcome> ImportFromLocalSourceAsync(OperationRequest request)
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
                WaitForManualDownloads = false
            }).ConfigureAwait(false);

            if (archiveResult.IsFailure)
            {
                return InstanceCreationOutcome.Failure(archiveResult.Error.Message);
            }

            UpdateOperationDialog("Finalizing import", "Writing launcher metadata and indexing the imported instance.");
            await PersistSelectedIconAsync(archiveResult.Value.Instance, request.SelectedIconPath).ConfigureAwait(false);

            var infoMessage = archiveResult.Value.PendingManualDownloads.Count > 0
                ? $"Imported '{archiveResult.Value.Instance.Name}'. {archiveResult.Value.PendingManualDownloads.Count} file(s) still require manual download in {archiveResult.Value.DownloadsDirectory}."
                : null;

            return InstanceCreationOutcome.Success(archiveResult.Value.Instance.InstanceId, infoMessage);
        }

        if (!string.IsNullOrWhiteSpace(request.ImportFolderPath))
        {
            UpdateOperationDialog("Importing folder", "Copying the selected folder into launcher storage.");
            var folderResult = await ImportInstanceUseCase.ExecuteAsync(new ImportInstanceRequest
            {
                SourceDirectory = request.ImportFolderPath,
                InstanceName = request.InstanceName,
                CopyInsteadOfMove = request.CopyImportSource
            }).ConfigureAwait(false);

            if (folderResult.IsFailure)
            {
                return InstanceCreationOutcome.Failure(folderResult.Error.Message);
            }

            UpdateOperationDialog("Finalizing import", "Writing launcher metadata and indexing the imported instance.");
            await PersistSelectedIconAsync(folderResult.Value.Instance, request.SelectedIconPath).ConfigureAwait(false);
            return InstanceCreationOutcome.Success(folderResult.Value.Instance.InstanceId);
        }

        return InstanceCreationOutcome.Failure("Choose an archive or folder to import.");
    }

    private async Task<Result<string>> ResolvePreferredLoaderVersionAsync(LoaderType loaderType, string selectedVersion)
    {
        var loaderVersions = await LoaderMetadataService
            .GetLoaderVersionsAsync(loaderType, BlockiumLauncher.Domain.ValueObjects.VersionId.Parse(selectedVersion), CancellationToken.None)
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

    private async Task PersistSelectedIconAsync(LauncherInstance instance, string? selectedIconPath)
    {
        if (string.IsNullOrWhiteSpace(selectedIconPath) || !File.Exists(selectedIconPath))
        {
            return;
        }

        var iconDirectory = System.IO.Path.Combine(instance.InstallLocation, ".blockium");
        Directory.CreateDirectory(iconDirectory);
        var iconPath = System.IO.Path.Combine(iconDirectory, "icon.png");
        File.Copy(selectedIconPath, iconPath, overwrite: true);

        instance.ChangeIconKey(iconPath);
        await InstanceRepository.SaveAsync(instance, CancellationToken.None).ConfigureAwait(false);
        await InstanceContentMetadataService.ReindexAsync(instance, CancellationToken.None).ConfigureAwait(false);
    }

    private void ShowMessage(string title, string message, MessageType messageType)
    {
        using var dialog = new MessageDialog(this, DialogFlags.Modal, messageType, ButtonsType.Ok, message)
        {
            Title = title
        };
        dialog.Run();
        dialog.Hide();
    }

    private string BuildSuccessMessage()
    {
        var instanceName = string.IsNullOrWhiteSpace(NameEntry.Text) ? "instance" : NameEntry.Text.Trim();
        return CurrentPage switch
        {
            AddInstancePageKind.QuickInstall => $"Created '{instanceName}' successfully.",
            AddInstancePageKind.Import => $"Imported '{instanceName}' successfully.",
            AddInstancePageKind.CurseForge => $"Imported '{instanceName}' from CurseForge successfully.",
            AddInstancePageKind.Modrinth => $"Imported '{instanceName}' from Modrinth successfully.",
            _ => $"Finished creating '{instanceName}'."
        };
    }

    private string GetInitialOperationTitle()
    {
        return CurrentPage switch
        {
            AddInstancePageKind.QuickInstall => "Creating instance",
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
        UpdateOperationDialogCore(title, body);
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
        Gtk.Application.Invoke((_, _) => UpdateOperationDialogCore(title, body));
    }

    private void UpdateOperationDialogCore(string title, string body)
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
    }

    private void EnsureOperationDialog()
    {
        if (OperationDialog is not null)
        {
            OperationSpinner?.Start();
            return;
        }

        var dialog = new Dialog
        {
            Title = "Working",
            TransientFor = this,
            Modal = true,
            DestroyWithParent = true,
            Resizable = false,
            WindowPosition = WindowPosition.CenterOnParent,
            Deletable = false,
            TypeHint = WindowTypeHint.Dialog
        };
        dialog.KeepAbove = true;
        dialog.SetDefaultSize(420, 0);
        dialog.Titlebar = BuildDialogHeaderBar("Working");

        var content = dialog.ContentArea;
        content.Spacing = 0;

        var body = new Box(Orientation.Horizontal, 14)
        {
            MarginTop = 18,
            MarginBottom = 18,
            MarginStart = 18,
            MarginEnd = 18
        };

        var spinner = new Spinner
        {
            WidthRequest = 28,
            HeightRequest = 28,
            Halign = Align.Center,
            Valign = Align.Start
        };
        spinner.Start();

        var text = new Box(Orientation.Vertical, 6)
        {
            Hexpand = true
        };

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
        body.PackStart(spinner, false, false, 0);
        body.PackStart(text, true, true, 0);
        content.PackStart(body, true, true, 0);

        OperationDialog = dialog;
        OperationSpinner = spinner;
        OperationTitleLabel = title;
        OperationBodyLabel = description;
    }

    private Widget BuildDialogHeaderBar(string title)
    {
        var bar = new HeaderBar
        {
            ShowCloseButton = false,
            HasSubtitle = false
        };
        bar.StyleContext.AddClass("topbar-shell");

        var label = new Label(title)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("settings-title");
        bar.PackStart(label);
        return bar;
    }

    private void CloseOperationDialog()
    {
        StopOperationProgressPolling();

        if (OperationSpinner is not null)
        {
            OperationSpinner.Stop();
        }

        if (OperationDialog is not null)
        {
            OperationDialog.Hide();
            OperationDialog.Destroy();
        }

        OperationDialog = null;
        OperationSpinner = null;
        OperationTitleLabel = null;
        OperationBodyLabel = null;
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
                UpdateOperationDialogCore(title, body);
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
        string? SelectedIconPath,
        string ImportArchivePath,
        string ImportFolderPath,
        bool CopyImportSource,
        CatalogProvider? Provider,
        string? SelectedProjectId);

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

    private sealed record InstanceCreationOutcome(bool IsSuccess, InstanceId? InstanceId, string? InfoMessage, string? ErrorMessage)
    {
        public static InstanceCreationOutcome Success(InstanceId instanceId, string? infoMessage = null)
            => new(true, instanceId, infoMessage, null);

        public static InstanceCreationOutcome Failure(string message)
            => new(false, null, null, message);
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
        public Button FilterButton { get; } = new("Filters");
        public Popover SortPopover { get; set; } = null!;
        public Popover FilterPopover { get; set; } = null!;
        public ListBox PackList { get; } = new() { SelectionMode = SelectionMode.Single };
        public Label ListStatus { get; set; } = new();
        public Label DetailsTitle { get; set; } = new();
        public Label DetailsMeta { get; set; } = new();
        public CatalogDescriptionView DescriptionView { get; set; } = new();
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
        public uint? SearchDebounceId { get; set; }
        public bool MetadataLoaded { get; set; }
        public bool MetadataLoading { get; set; }
        public bool HasLoadedResults { get; set; }
        public bool IsSearching { get; set; }
        public int RequestVersion { get; set; }
        public int DetailsRequestVersion { get; set; }
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

    private sealed class PackRow : ListBoxRow
    {
        private readonly Image IconImage = new();
        private readonly Label Placeholder = new("MP");

        public PackRow(CatalogProjectSummary project, bool useEvenTone)
        {
            Project = project;
            StyleContext.AddClass(useEvenTone ? "add-instance-pack-row-even" : "add-instance-pack-row-odd");

            var layout = new Box(Orientation.Horizontal, 12)
            {
                MarginTop = 10,
                MarginBottom = 10,
                MarginStart = 12,
                MarginEnd = 12
            };
            layout.StyleContext.AddClass("add-instance-pack-row-body");

            var iconShell = new EventBox
            {
                WidthRequest = 40,
                HeightRequest = 40,
                VisibleWindow = true
            };
            iconShell.StyleContext.AddClass("add-instance-pack-icon-shell");
            var iconOverlay = new Overlay();
            IconImage.Halign = Align.Center;
            IconImage.Valign = Align.Center;
            Placeholder.Halign = Align.Center;
            Placeholder.Valign = Align.Center;
            Placeholder.StyleContext.AddClass("add-instance-pack-icon-placeholder");
            IconImage.Hide();
            iconOverlay.Add(IconImage);
            iconOverlay.AddOverlay(Placeholder);
            iconShell.Add(iconOverlay);

            var textLayout = new Box(Orientation.Vertical, 4);
            var title = new Label(project.Title) { Xalign = 0, Wrap = true };
            title.StyleContext.AddClass("settings-section-title");

            var subtitle = new Label(BuildSubtitle(project)) { Xalign = 0, Wrap = true };
            subtitle.StyleContext.AddClass("settings-help");

            var meta = new Label(BuildMeta(project)) { Xalign = 0, Wrap = true };
            meta.StyleContext.AddClass("settings-help");

            textLayout.PackStart(title, false, false, 0);
            textLayout.PackStart(subtitle, false, false, 0);
            textLayout.PackStart(meta, false, false, 0);
            layout.PackStart(iconShell, false, false, 0);
            layout.PackStart(textLayout, true, true, 0);
            Add(layout);
        }

        public CatalogProjectSummary Project { get; }

        public void SetIcon(Pixbuf? pixbuf)
        {
            IconImage.Pixbuf = pixbuf;
            if (pixbuf is null)
            {
                IconImage.Hide();
                Placeholder.Show();
                return;
            }

            Placeholder.Hide();
            IconImage.Show();
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
        var content = new Box(Orientation.Horizontal, 0);
        content.PackStart(WrapVersionCell(versionCell, true, -1, true), true, true, 0);
        content.PackStart(WrapVersionCell(releasedCell, false, 168, true), false, false, 0);
        content.PackStart(WrapVersionCell(typeCell, false, 120), false, false, 0);
        return content;
    }

    private static Widget WrapVersionCell(Widget child, bool expand, int widthRequest, bool showTrailingDivider = false)
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("add-instance-version-cell");
        if (showTrailingDivider)
        {
            shell.StyleContext.AddClass("add-instance-version-cell-divider");
        }

        if (widthRequest > 0)
        {
            shell.WidthRequest = widthRequest;
        }

        var box = new Box(Orientation.Horizontal, 0)
        {
            MarginTop = 8,
            MarginBottom = 8,
            MarginStart = 12,
            MarginEnd = 12,
            Hexpand = expand
        };
        box.PackStart(child, true, true, 0);
        shell.Add(box);
        return shell;
    }

    private static HttpClient CreatePackIconHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlockiumLauncher/0.1");
        return httpClient;
    }
}
