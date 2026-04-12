using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Instances;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using BlockiumLauncher.UI.GtkSharp.Widgets;
using Gdk;
using Gtk;
using Window = Gtk.Window;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class CatalogContentBrowserWindow : Window
{
    private readonly SearchCatalogUseCase searchCatalogUseCase;
    private readonly GetCatalogProjectDetailsUseCase getCatalogProjectDetailsUseCase;
    private readonly GetCatalogProviderMetadataUseCase getCatalogProviderMetadataUseCase;
    private readonly ListCatalogFilesUseCase listCatalogFilesUseCase;
    private readonly InstallCatalogContentUseCase installCatalogContentUseCase;
    private readonly ListInstanceContentUseCase listInstanceContentUseCase;
    private readonly ProviderMediaCacheService providerMediaCacheService;

    private readonly ListBox providerList = new() { SelectionMode = SelectionMode.Single };
    private readonly Stack contentStack = new() { Hexpand = true, Vexpand = true, TransitionType = StackTransitionType.None };
    private readonly Button installButton = new("Install");
    private readonly Button closeButton = new("Close");
    private readonly CatalogPageState modrinthPage = new(CatalogProvider.Modrinth);
    private readonly CatalogPageState curseForgePage = new(CatalogProvider.CurseForge);

    private CatalogContentType contentType;
    private LauncherInstance? instance;
    private InstanceId? instanceId;
    private CatalogPageState? currentPage;
    private bool isConfigured;
    private bool isSubmitting;
    private bool isClosing;

    public CatalogContentBrowserWindow(
        SearchCatalogUseCase searchCatalogUseCase,
        GetCatalogProjectDetailsUseCase getCatalogProjectDetailsUseCase,
        GetCatalogProviderMetadataUseCase getCatalogProviderMetadataUseCase,
        ListCatalogFilesUseCase listCatalogFilesUseCase,
        InstallCatalogContentUseCase installCatalogContentUseCase,
        ListInstanceContentUseCase listInstanceContentUseCase,
        ProviderMediaCacheService providerMediaCacheService) : base("Browse Content")
    {
        this.searchCatalogUseCase = searchCatalogUseCase ?? throw new ArgumentNullException(nameof(searchCatalogUseCase));
        this.getCatalogProjectDetailsUseCase = getCatalogProjectDetailsUseCase ?? throw new ArgumentNullException(nameof(getCatalogProjectDetailsUseCase));
        this.getCatalogProviderMetadataUseCase = getCatalogProviderMetadataUseCase ?? throw new ArgumentNullException(nameof(getCatalogProviderMetadataUseCase));
        this.listCatalogFilesUseCase = listCatalogFilesUseCase ?? throw new ArgumentNullException(nameof(listCatalogFilesUseCase));
        this.installCatalogContentUseCase = installCatalogContentUseCase ?? throw new ArgumentNullException(nameof(installCatalogContentUseCase));
        this.listInstanceContentUseCase = listInstanceContentUseCase ?? throw new ArgumentNullException(nameof(listInstanceContentUseCase));
        this.providerMediaCacheService = providerMediaCacheService ?? throw new ArgumentNullException(nameof(providerMediaCacheService));

        SetDefaultSize(1160, 760);
        Resizable = true;
        WindowPosition = WindowPosition.CenterOnParent;

        providerList.StyleContext.AddClass("settings-nav-list");
        providerList.StyleContext.AddClass("add-instance-source-list");
        providerList.RowSelected += (_, args) =>
        {
            if (args.Row is not ProviderRow row)
            {
                return;
            }

            currentPage = row.Provider == CatalogProvider.Modrinth ? modrinthPage : curseForgePage;
            contentStack.VisibleChildName = row.Provider.ToString();
            _ = EnsureCatalogPageReadyAsync(currentPage);
            UpdateInstallButtonState();
        };

        installButton.StyleContext.AddClass("action-button");
        installButton.StyleContext.AddClass("primary-button");
        closeButton.StyleContext.AddClass("action-button");
        installButton.Clicked += async (_, _) => await InstallSelectedContentAsync().ConfigureAwait(false);
        closeButton.Clicked += (_, _) => CloseWindow();

        Add(BuildRoot());
        DeleteEvent += (_, args) => { args.RetVal = true; CloseWindow(); };
        Destroyed += (_, _) =>
        {
            ShutdownPage(modrinthPage);
            ShutdownPage(curseForgePage);
            LauncherWindowMemory.RequestAggressiveCleanup();
        };
    }

    public event EventHandler? ContentInstalled;

    public void Configure(CatalogContentType contentType)
    {
        this.contentType = contentType;
        isConfigured = true;
        Titlebar = LauncherGtkChrome.CreateHeaderBar($"Browse {GetContentTitle(contentType)}", string.Empty, allowWindowControls: true);
        ConfigurePage(modrinthPage);
        ConfigurePage(curseForgePage);
        BuildProviderNavigation();
    }

    public void PresentForInstance(Window owner, LauncherInstance instance, InstanceId instanceId)
    {
        if (!isConfigured)
        {
            return;
        }

        TransientFor = owner;
        this.instance = instance;
        this.instanceId = instanceId;
        _ = LoadInstalledProjectIdsAsync();
        if (!Visible)
        {
            ShowAll();
        }

        if (providerList.SelectedRow is null && providerList.GetRowAtIndex(0) is ListBoxRow firstRow)
        {
            providerList.SelectRow(firstRow);
        }

        Present();
    }

    private Widget BuildRoot()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-shell");
        var layout = new Box(Orientation.Vertical, 0);
        layout.PackStart(BuildBody(), true, true, 0);
        layout.PackStart(BuildFooter(), false, false, 0);
        shell.Add(layout);
        return shell;
    }

    private Widget BuildBody()
    {
        var body = new Box(Orientation.Horizontal, 0);
        body.PackStart(BuildRail(), false, false, 0);
        body.PackStart(BuildPages(), true, true, 0);
        return body;
    }

    private Widget BuildRail()
    {
        var shell = new EventBox { WidthRequest = 118 };
        shell.StyleContext.AddClass("settings-nav-shell");
        shell.StyleContext.AddClass("add-instance-nav-shell");
        shell.StyleContext.AddClass("catalog-browser-nav-shell");
        var layout = new Box(Orientation.Vertical, 4) { MarginTop = 4, MarginBottom = 4, MarginStart = 2, MarginEnd = 2 };
        layout.PackStart(providerList, true, true, 0);
        shell.Add(layout);
        return shell;
    }

    private Widget BuildPages()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-content-shell");
        shell.StyleContext.AddClass("add-instance-content-shell");
        shell.StyleContext.AddClass("catalog-browser-content-shell");
        contentStack.AddNamed(BuildProviderPage(modrinthPage, "Modrinth"), CatalogProvider.Modrinth.ToString());
        contentStack.AddNamed(BuildProviderPage(curseForgePage, "CurseForge"), CatalogProvider.CurseForge.ToString());
        shell.Add(contentStack);
        return shell;
    }

    private Widget BuildFooter()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("add-instance-footer");
        var layout = new Box(Orientation.Horizontal, 10) { MarginTop = 10, MarginBottom = 10, MarginStart = 12, MarginEnd = 12 };
        layout.PackStart(new Box(Orientation.Horizontal, 0), true, true, 0);
        layout.PackEnd(closeButton, false, false, 0);
        layout.PackEnd(installButton, false, false, 0);
        shell.Add(layout);
        return shell;
    }

    private Widget BuildProviderPage(CatalogPageState state, string title)
    {
        state.SearchEntry.PlaceholderText = $"Search {title} {GetContentTitle(contentType).ToLowerInvariant()}";
        state.SortButton.StyleContext.AddClass("popover-menu-button");
        state.CategoryButton.StyleContext.AddClass("popover-menu-button");
        state.VersionButton.StyleContext.AddClass("popover-menu-button");
        state.LoaderButton.StyleContext.AddClass("popover-menu-button");
        state.ProjectVersionButton.StyleContext.AddClass("popover-menu-button");

        state.SortPopover = CreatePopover(state.SortButton);
        state.CategoryPopover = CreatePopover(state.CategoryButton);
        state.VersionPopover = CreatePopover(state.VersionButton);
        state.LoaderPopover = CreatePopover(state.LoaderButton);
        state.ProjectVersionPopover = CreatePopover(state.ProjectVersionButton);

        state.DetailsTitleButton = new Button { Relief = ReliefStyle.None, Halign = Align.Start, Sensitive = false, CanFocus = false, FocusOnClick = false };
        state.DetailsTitleButton.StyleContext.AddClass("settings-page-title-link");
        state.DetailsTitle = new Label("Nothing selected") { Xalign = 0, Wrap = true };
        state.DetailsTitle.StyleContext.AddClass("settings-page-title");
        state.DetailsTitleButton.Add(state.DetailsTitle);
        state.DetailsTitleButton.Clicked += (_, _) => { if (!string.IsNullOrWhiteSpace(state.DetailsProjectUrl)) DesktopShell.OpenUrl(state.DetailsProjectUrl); };
        state.DetailsMeta = new Label("Choose a project to inspect its details.") { Xalign = 0, Wrap = true };
        state.DetailsMeta.StyleContext.AddClass("settings-help");
        state.DescriptionView = new CatalogDescriptionView(providerMediaCacheService);
        state.ListStatus.StyleContext.AddClass("settings-help");

        var page = CreateBrowserPageBox();
        page.Hexpand = true;
        page.Vexpand = true;
        page.PackStart(CreatePageTitle(title), false, false, 0);

        var searchCard = CreateCardShell();
        var searchLayout = CreateBrowserCardContentBox(8, 6);
        searchLayout.PackStart(BuildSectionHeader("Search"), false, false, 0);
        searchLayout.PackStart(state.SearchEntry, false, false, 0);
        var controls = new Box(Orientation.Horizontal, 8);
        controls.PackStart(state.SortButton, false, false, 0);
        controls.PackStart(state.CategoryButton, false, false, 0);
        controls.PackStart(state.VersionButton, false, false, 0);
        controls.PackStart(state.LoaderButton, false, false, 0);
        controls.PackStart(new Box(Orientation.Horizontal, 0), true, true, 0);
        searchLayout.PackStart(controls, false, false, 0);
        searchCard.Add(searchLayout);

        state.ProjectList.StyleContext.AddClass("add-instance-pack-list");
        var listScroller = new ScrolledWindow { Hexpand = true, Vexpand = true, HscrollbarPolicy = PolicyType.Never, VscrollbarPolicy = PolicyType.Automatic };
        listScroller.StyleContext.AddClass("add-instance-list-scroller");
        listScroller.Add(state.ProjectList);
        var listCard = CreateCardShell();
        listCard.Hexpand = true;
        listCard.Vexpand = true;
        var listLayout = CreateBrowserCardContentBox(8, 0);
        listLayout.MarginTop = 0;
        listLayout.MarginBottom = 0;
        listLayout.PackStart(BuildSectionHeader("Results"), false, false, 0);
        listLayout.PackStart(listScroller, true, true, 0);
        listLayout.PackStart(state.ListStatus, false, false, 0);
        listCard.Add(listLayout);

        var detailsCard = CreateCardShell();
        detailsCard.WidthRequest = 286;
        detailsCard.Hexpand = false;
        detailsCard.Vexpand = true;
        var detailsLayout = CreateBrowserCardContentBox(10, 8);
        detailsLayout.PackStart(BuildSectionHeader("Details"), false, false, 0);
        detailsLayout.PackStart(state.DetailsTitleButton, false, false, 0);
        detailsLayout.PackStart(state.DetailsMeta, false, false, 0);
        detailsLayout.PackStart(state.DescriptionView, true, true, 0);
        detailsLayout.PackStart(BuildLabeledField("Version", state.ProjectVersionButton), false, false, 0);
        detailsLayout.PackStart(state.VersionStatus, false, false, 0);
        detailsCard.Add(detailsLayout);

        var split = new Box(Orientation.Horizontal, 6) { Hexpand = true, Vexpand = true };
        split.PackStart(listCard, true, true, 0);
        split.PackStart(detailsCard, false, false, 0);
        page.PackStart(searchCard, false, false, 0);
        page.PackStart(split, true, true, 0);
        return page;
    }

    private void ConfigurePage(CatalogPageState state)
    {
        state.SearchEntry.Changed += (_, _) => QueueCatalogRefresh(state);
        state.SortButton.Clicked += (_, _) => TogglePopover(state.SortPopover);
        state.CategoryButton.Clicked += (_, _) => TogglePopover(state.CategoryPopover);
        state.VersionButton.Clicked += (_, _) => TogglePopover(state.VersionPopover);
        state.LoaderButton.Clicked += (_, _) => TogglePopover(state.LoaderPopover);
        state.ProjectVersionButton.Clicked += (_, _) => TogglePopover(state.ProjectVersionPopover);
        state.ProjectList.RowSelected += (_, args) =>
        {
            state.SelectedProject = (args.Row as CatalogProjectRow)?.Project;
            UpdateInstallButtonState();
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
    }

    private void BuildProviderNavigation()
    {
        foreach (var child in providerList.Children.ToArray())
        {
            providerList.Remove(child);
            child.Destroy();
        }

        providerList.Add(new ProviderRow(CatalogProvider.Modrinth, "Modrinth"));
        providerList.Add(new ProviderRow(CatalogProvider.CurseForge, "CurseForge"));
        providerList.ShowAll();
        if (providerList.GetRowAtIndex(0) is ListBoxRow firstRow)
        {
            providerList.SelectRow(firstRow);
        }
    }

    private async Task EnsureCatalogPageReadyAsync(CatalogPageState state)
    {
        if (!state.MetadataLoaded && !state.MetadataLoading)
        {
            await LoadMetadataAsync(state).ConfigureAwait(false);
        }

        if (!state.HasLoadedResults && !state.IsSearching)
        {
            await LoadProjectsAsync(state).ConfigureAwait(false);
        }
    }

    private async Task LoadMetadataAsync(CatalogPageState state)
    {
        state.MetadataLoading = true;
        Gtk.Application.Invoke((_, _) => state.ListStatus.Text = "Loading provider filters...");
        var result = await getCatalogProviderMetadataUseCase.ExecuteAsync(new GetCatalogProviderMetadataRequest
        {
            Provider = state.Provider,
            ContentType = contentType
        }).ConfigureAwait(false);
        state.MetadataLoading = false;
        state.MetadataLoaded = result.IsSuccess;

        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsFailure)
            {
                state.ListStatus.Text = result.Error.Message;
                return;
            }

            state.Metadata = result.Value;
            state.AvailableSorts = result.Value.SortOptions.Count > 0 ? result.Value.SortOptions : DefaultSorts;
            state.AvailableCategories = result.Value.Categories;
            state.AvailableLoaders = result.Value.Loaders;
            state.AvailableGameVersions = result.Value.GameVersions;
            ApplyDefaultFilters(state);
            RebuildSortPopover(state);
            RebuildFilterPopovers(state);
            UpdateSortButtonLabel(state);
            UpdateFilterButtonLabels(state);
        });
    }

    private void ApplyDefaultFilters(CatalogPageState state)
    {
        state.SelectedGameVersions.Clear();
        state.SelectedLoaders.Clear();
        if (instance is null)
        {
            return;
        }

        var gameVersion = instance.GameVersion.ToString();
        if (state.AvailableGameVersions.Contains(gameVersion, StringComparer.OrdinalIgnoreCase))
        {
            state.SelectedGameVersions.Add(gameVersion);
        }

        if (contentType == CatalogContentType.Mod)
        {
            var loader = GetLoaderText(instance.LoaderType);
            if (!string.IsNullOrWhiteSpace(loader) && state.AvailableLoaders.Contains(loader, StringComparer.OrdinalIgnoreCase))
            {
                state.SelectedLoaders.Add(loader);
            }
        }
    }

    private async Task LoadProjectsAsync(CatalogPageState state)
    {
        state.IsSearching = true;
        state.ListStatus.Text = "Loading projects...";
        var result = await searchCatalogUseCase.ExecuteAsync(new SearchCatalogRequest
        {
            Provider = state.Provider,
            ContentType = contentType,
            Query = string.IsNullOrWhiteSpace(state.SearchEntry.Text) ? null : state.SearchEntry.Text.Trim(),
            GameVersions = state.SelectedGameVersions.ToArray(),
            Loaders = state.SelectedLoaders.ToArray(),
            Categories = state.SelectedCategories.ToArray(),
            Sort = state.SelectedSort,
            Limit = 100
        }).ConfigureAwait(false);
        state.IsSearching = false;
        state.HasLoadedResults = true;

        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsFailure)
            {
                state.ListStatus.Text = result.Error.Message;
                return;
            }

            state.Results = result.Value;
            PopulateProjectList(state, result.Value);
            state.ListStatus.Text = result.Value.Count == 0
                ? $"No {GetContentTitle(contentType).ToLowerInvariant()} found."
                : $"{result.Value.Count} project(s) loaded.";
        });
    }

    private void PopulateProjectList(CatalogPageState state, IReadOnlyList<CatalogProjectSummary> projects)
    {
        state.ProjectListVersion++;
        state.IconLoadCancellationSource?.Cancel();
        state.IconLoadCancellationSource?.Dispose();
        state.IconLoadCancellationSource = new CancellationTokenSource();
        foreach (var child in state.ProjectList.Children.ToArray())
        {
            state.ProjectList.Remove(child);
            child.Destroy();
        }

        for (var index = 0; index < projects.Count; index++)
        {
            state.ProjectList.Add(new CatalogProjectRow(projects[index], index % 2 == 0, state.InstalledProjectIds.Contains(projects[index].ProjectId)));
        }

        state.ProjectList.ShowAll();
        state.ProjectList.UnselectAll();
        state.SelectedProject = null;
        ResetProjectDetails(state, "Nothing selected", "Choose a project to inspect its details.");
        ResetVersionSelection(state, "Choose a project to load its available versions.");
        UpdateInstallButtonState();
        QueueProjectIconLoads(state);
    }

    private async Task LoadProjectDetailsAsync(CatalogPageState state, CatalogProjectSummary project)
    {
        SetProjectDetailsTitle(state, project.Title, project.ProjectUrl);
        state.DetailsMeta.Text = "Loading project details...";
        state.DescriptionView.SetContent(project.Description, CatalogDescriptionFormat.PlainText, project.ProjectUrl);

        var result = await getCatalogProjectDetailsUseCase.ExecuteAsync(new GetCatalogProjectDetailsRequest
        {
            Provider = state.Provider,
            ContentType = contentType,
            ProjectId = project.ProjectId
        }).ConfigureAwait(false);

        Gtk.Application.Invoke((_, _) =>
        {
            if (result.IsFailure)
            {
                state.DetailsMeta.Text = BuildProjectMeta(project);
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
        Gtk.Application.Invoke((_, _) => ResetVersionSelection(state, "Loading available versions..."));
        var files = new List<CatalogFileSummary>();
        var offset = 0;
        while (true)
        {
            var result = await listCatalogFilesUseCase.ExecuteAsync(new ListCatalogFilesRequest
            {
                Provider = state.Provider,
                ContentType = contentType,
                ProjectId = project.ProjectId,
                Limit = 50,
                Offset = offset
            }).ConfigureAwait(false);

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

        var filtered = files
            .Where(file => (state.SelectedGameVersions.Count == 0 || file.GameVersions.Any(version => state.SelectedGameVersions.Contains(version))) &&
                           (state.SelectedLoaders.Count == 0 || file.Loaders.Any(loader => state.SelectedLoaders.Contains(loader))))
            .OrderByDescending(static file => file.PublishedAtUtc)
            .ToArray();

        Gtk.Application.Invoke((_, _) =>
        {
            state.AvailableFiles = filtered;
            state.SelectedFileId = filtered.FirstOrDefault()?.FileId;
            state.ProjectVersionButton.Sensitive = filtered.Length > 0;
            UpdateProjectVersionButtonLabel(state);
            RebuildProjectVersionPopover(state);
            state.VersionStatus.Text = filtered.Length == 0 ? "No versions match the current filters." : $"{filtered.Length} version(s) available.";
            UpdateInstallButtonState();
        });
    }

    private void QueueCatalogRefresh(CatalogPageState state)
    {
        if (state.SearchDebounceId is not null)
        {
            GLib.Source.Remove(state.SearchDebounceId.Value);
            state.SearchDebounceId = null;
        }

        state.SearchDebounceId = GLib.Timeout.Add(250, () =>
        {
            state.SearchDebounceId = null;
            _ = LoadProjectsAsync(state);
            return false;
        });
    }

    private void RebuildSortPopover(CatalogPageState state)
    {
        ReplacePopoverContent(state.SortPopover, BuildSortPopoverContent(state));
    }

    private Widget BuildSortPopoverContent(CatalogPageState state)
    {
        var content = new Box(Orientation.Vertical, 8);
        content.StyleContext.AddClass("popover-content");
        var title = new Label("Sort by") { Xalign = 0 };
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
                if (!button.Active) return;
                state.SelectedSort = sort;
                UpdateSortButtonLabel(state);
                QueueCatalogRefresh(state);
            };
            content.PackStart(button, false, false, 0);
        }

        return content;
    }

    private void RebuildFilterPopovers(CatalogPageState state)
    {
        ReplacePopoverContent(state.CategoryPopover, BuildFilterPopover("Categories", state.AvailableCategories, state.SelectedCategories, state, null));
        ReplacePopoverContent(state.VersionPopover, BuildFilterPopover("Versions", state.AvailableGameVersions, state.SelectedGameVersions, state, null));
        ReplacePopoverContent(state.LoaderPopover, BuildFilterPopover("Loaders", state.AvailableLoaders, state.SelectedLoaders, state, ToDisplayName));
    }

    private Widget BuildFilterPopover(string titleText, IReadOnlyList<string> values, HashSet<string> selectedValues, CatalogPageState state, Func<string, string>? formatter)
    {
        var outer = new Box(Orientation.Vertical, 10);
        outer.StyleContext.AddClass("popover-content");
        var title = new Label(titleText) { Xalign = 0 };
        title.StyleContext.AddClass("popover-title");
        outer.PackStart(title, false, false, 0);
        var section = new Box(Orientation.Vertical, 6);
        foreach (var value in values)
        {
            var button = new CheckButton(formatter is null ? value : formatter(value)) { Active = selectedValues.Contains(value) };
            button.StyleContext.AddClass("popover-check");
            button.Toggled += (_, _) =>
            {
                if (button.Active) selectedValues.Add(value);
                else selectedValues.Remove(value);
                UpdateFilterButtonLabels(state);
                QueueCatalogRefresh(state);
            };
            section.PackStart(button, false, false, 0);
        }

        var scroller = new ScrolledWindow { HscrollbarPolicy = PolicyType.Never, VscrollbarPolicy = PolicyType.Automatic, WidthRequest = 180, HeightRequest = 28 + (Math.Clamp(values.Count, 1, 8) * 32) };
        scroller.Add(section);
        outer.PackStart(scroller, true, true, 0);
        return outer;
    }

    private void RebuildProjectVersionPopover(CatalogPageState state)
    {
        ReplacePopoverContent(state.ProjectVersionPopover, LauncherStructuredList.BuildCatalogFileSelectionPopover(
            $"{GetContentTitle(contentType)} versions",
            state.AvailableFiles,
            state.SelectedFileId,
            BuildVersionLabel,
            file =>
            {
                state.SelectedFileId = file.FileId;
                UpdateProjectVersionButtonLabel(state);
                UpdateInstallButtonState();
            }));
    }

    private async Task InstallSelectedContentAsync()
    {
        if (instance is null || instanceId is null || currentPage?.SelectedProject is null || string.IsNullOrWhiteSpace(currentPage.SelectedFileId) || isSubmitting)
        {
            return;
        }

        isSubmitting = true;
        UpdateInstallButtonState();
        var result = await installCatalogContentUseCase.ExecuteAsync(new InstallCatalogContentRequest
        {
            Provider = currentPage.Provider,
            ContentType = contentType,
            InstanceId = instanceId.Value,
            ProjectId = currentPage.SelectedProject.ProjectId,
            FileId = currentPage.SelectedFileId,
            GameVersion = instance.GameVersion.ToString(),
            Loader = GetLoaderText(instance.LoaderType)
        }).ConfigureAwait(false);

        Gtk.Application.Invoke((_, _) =>
        {
            isSubmitting = false;
            UpdateInstallButtonState();
            if (result.IsFailure)
            {
                LauncherGtkChrome.ShowMessage(this, "Unable to install content", result.Error.Message, MessageType.Error);
                return;
            }

            ContentInstalled?.Invoke(this, EventArgs.Empty);
            currentPage.InstalledProjectIds.Add(currentPage.SelectedProject.ProjectId);
            RefreshInstalledMarkers(currentPage);
            LauncherGtkChrome.ShowMessage(this, "Content installed", $"{result.Value.File.DisplayName} was added to {instance.Name}.", MessageType.Info);
        });
    }

    private async Task LoadInstalledProjectIdsAsync()
    {
        if (instanceId is null)
        {
            return;
        }

        var result = await listInstanceContentUseCase.ExecuteAsync(new ListInstanceContentRequest { InstanceId = instanceId.Value }).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return;
        }

        var modrinthInstalled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var curseForgeInstalled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in GetItemsForContentType(result.Value, contentType))
        {
            if (string.IsNullOrWhiteSpace(item.Source?.ProjectId))
            {
                continue;
            }

            switch (item.Source.Provider)
            {
                case ContentOriginProvider.Modrinth:
                    modrinthInstalled.Add(item.Source.ProjectId);
                    break;
                case ContentOriginProvider.CurseForge:
                    curseForgeInstalled.Add(item.Source.ProjectId);
                    break;
            }
        }

        Gtk.Application.Invoke((_, _) =>
        {
            modrinthPage.InstalledProjectIds.Clear();
            curseForgePage.InstalledProjectIds.Clear();
            foreach (var projectId in modrinthInstalled)
            {
                modrinthPage.InstalledProjectIds.Add(projectId);
            }

            foreach (var projectId in curseForgeInstalled)
            {
                curseForgePage.InstalledProjectIds.Add(projectId);
            }

            RefreshInstalledMarkers(modrinthPage);
            RefreshInstalledMarkers(curseForgePage);
        });
    }

    private static IReadOnlyList<InstanceFileMetadata> GetItemsForContentType(InstanceContentMetadata metadata, CatalogContentType contentType)
    {
        return contentType switch
        {
            CatalogContentType.Mod => metadata.Mods,
            CatalogContentType.ResourcePack => metadata.ResourcePacks,
            CatalogContentType.Shader => metadata.Shaders,
            _ => []
        };
    }

    private static void RefreshInstalledMarkers(CatalogPageState state)
    {
        foreach (var child in state.ProjectList.Children)
        {
            if (child is CatalogProjectRow row)
            {
                row.SetInstalled(state.InstalledProjectIds.Contains(row.Project.ProjectId));
            }
        }
    }

    private void UpdateInstallButtonState()
    {
        installButton.Sensitive = !isSubmitting && currentPage?.SelectedProject is not null && !string.IsNullOrWhiteSpace(currentPage.SelectedFileId);
        installButton.Label = isSubmitting ? "Installing..." : "Install";
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

    private static Popover CreatePopover(Widget relativeTo) => new(relativeTo) { BorderWidth = 10, Position = PositionType.Bottom };
    private static void TogglePopover(Popover popover) { if (popover.IsVisible) popover.Hide(); else popover.ShowAll(); }

    private static string GetSortLabel(CatalogSearchSort sort) => sort switch
    {
        CatalogSearchSort.Downloads => "Downloads",
        CatalogSearchSort.Follows => "Followers",
        CatalogSearchSort.Newest => "Newest",
        CatalogSearchSort.Updated => "Recently updated",
        _ => "Relevance"
    };

    private static void UpdateSortButtonLabel(CatalogPageState state) => state.SortButton.Label = $"Sort: {GetSortLabel(state.SelectedSort)}";
    private static void UpdateFilterButtonLabels(CatalogPageState state)
    {
        state.CategoryButton.Label = state.SelectedCategories.Count == 0 ? "Category" : $"Category ({state.SelectedCategories.Count})";
        state.VersionButton.Label = state.SelectedGameVersions.Count == 0 ? "Version" : $"Version ({state.SelectedGameVersions.Count})";
        state.LoaderButton.Label = state.SelectedLoaders.Count == 0 ? "Loader" : $"Loader ({state.SelectedLoaders.Count})";
    }

    private static void UpdateProjectVersionButtonLabel(CatalogPageState state)
    {
        var selected = state.AvailableFiles.FirstOrDefault(file => string.Equals(file.FileId, state.SelectedFileId, StringComparison.Ordinal));
        state.ProjectVersionButton.Label = selected is null ? "Select version" : AbbreviateVersionLabel(LauncherStructuredList.BuildSimpleCatalogFileLabel(selected));
    }

    private static string BuildVersionLabel(CatalogFileSummary file)
    {
        return LauncherStructuredList.BuildSimpleCatalogFileLabel(file);
#if false
        var parts = new List<string> { string.IsNullOrWhiteSpace(file.DisplayName) ? file.FileName : file.DisplayName };
        if (file.PublishedAtUtc is not null) parts.Add(file.PublishedAtUtc.Value.ToString("yyyy-MM-dd"));
        if (file.GameVersions.Count > 0) parts.Add("MC " + string.Join(", ", file.GameVersions.Take(2)));
        if (file.Loaders.Count > 0) parts.Add(string.Join(", ", file.Loaders.Take(2).Select(ToDisplayName)));
        return string.Join("  •  ", parts.Where(static part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string AbbreviateVersionLabel(string text) => string.IsNullOrWhiteSpace(text) || text.Length <= 54 ? text : text[..53].TrimEnd() + "…";
#endif
    }
    private static string AbbreviateVersionLabel(string text)
        => string.IsNullOrWhiteSpace(text) || text.Length <= 54 ? text : text[..53].TrimEnd() + "...";

    private static void SetProjectDetailsTitle(CatalogPageState state, string title, string? projectUrl) { state.DetailsTitle.Text = title; state.DetailsProjectUrl = projectUrl; state.DetailsTitleButton.Sensitive = !string.IsNullOrWhiteSpace(projectUrl); }
    private static string BuildProjectMeta(CatalogProjectSummary project) => string.Join(" • ", new[] { project.Author, project.Downloads > 0 ? $"{project.Downloads:N0} downloads" : null, project.GameVersions.FirstOrDefault() is { Length: > 0 } gameVersion ? $"MC {gameVersion}" : null }.Where(static part => !string.IsNullOrWhiteSpace(part)));
    private static string BuildDetailsMeta(CatalogProjectDetails details) => string.Join(" • ", new[] { details.Author, details.Downloads > 0 ? $"{details.Downloads:N0} downloads" : null, details.GameVersions.FirstOrDefault() is { Length: > 0 } gameVersion ? $"MC {gameVersion}" : null }.Where(static part => !string.IsNullOrWhiteSpace(part)));
    private static string ToDisplayName(string value) => value.ToLowerInvariant() switch { "neoforge" => "NeoForge", "forge" => "Forge", "fabric" => "Fabric", "quilt" => "Quilt", _ => value };
    private static string GetLoaderText(LoaderType loaderType) => loaderType switch { LoaderType.Forge => "forge", LoaderType.Fabric => "fabric", LoaderType.Quilt => "quilt", LoaderType.NeoForge => "neoforge", _ => string.Empty };
    private static string GetContentTitle(CatalogContentType contentType) => contentType switch { CatalogContentType.Mod => "Mods", CatalogContentType.ResourcePack => "Resource Packs", CatalogContentType.Shader => "Shader Packs", _ => "Content" };

    private static Widget BuildLabeledField(string labelText, Widget field)
    {
        var box = new Box(Orientation.Vertical, 6) { Hexpand = true };
        var label = new Label(labelText) { Xalign = 0 };
        label.StyleContext.AddClass("app-field-label");
        label.StyleContext.AddClass("add-instance-field-label");
        box.PackStart(label, false, false, 0);
        box.PackStart(field, false, false, 0);
        return box;
    }

    private static EventBox CreateCardShell()
    {
        var card = new EventBox();
        card.StyleContext.AddClass("settings-card");
        card.StyleContext.AddClass("add-instance-card");
        card.StyleContext.AddClass("catalog-browser-card");
        return card;
    }

    private static Box CreateCardContentBox(int spacing = 8) => new(Orientation.Vertical, spacing) { MarginTop = 8, MarginBottom = 8, MarginStart = 8, MarginEnd = 8 };
    private static Box CreatePageBox() => new(Orientation.Vertical, 8) { MarginTop = 6, MarginBottom = 6, MarginStart = 6, MarginEnd = 6 };
    private static Box CreateBrowserCardContentBox(int spacing, int horizontalMargin) => new(Orientation.Vertical, spacing) { MarginTop = 6, MarginBottom = 6, MarginStart = horizontalMargin, MarginEnd = horizontalMargin };
    private static Box CreateBrowserPageBox() => new(Orientation.Vertical, 6) { MarginTop = 4, MarginBottom = 4, MarginStart = 0, MarginEnd = 4 };
    private static Label CreatePageTitle(string text) { var label = new Label(text) { Xalign = 0 }; label.StyleContext.AddClass("settings-page-title"); return label; }
    private static Label BuildSectionHeader(string title) { var label = new Label(title) { Xalign = 0 }; label.StyleContext.AddClass("settings-section-title"); return label; }

    private void QueueProjectIconLoads(CatalogPageState state)
    {
        var requestVersion = state.ProjectListVersion;
        var cancellationToken = state.IconLoadCancellationSource?.Token ?? CancellationToken.None;
        foreach (var child in state.ProjectList.Children)
        {
            if (child is CatalogProjectRow row &&
                !row.IsDisposed &&
                !row.HasIcon &&
                !string.IsNullOrWhiteSpace(row.Project.IconUrl))
            {
                _ = LoadProjectIconAsync(state, row, requestVersion, cancellationToken);
            }
        }
    }

    private async Task LoadProjectIconAsync(CatalogPageState state, CatalogProjectRow row, int requestVersion, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(row.Project.IconUrl))
        {
            return;
        }

        Pixbuf? pixbuf = null;
        try
        {
            pixbuf = await providerMediaCacheService
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
            if (!row.IsDisposed &&
                requestVersion == state.ProjectListVersion &&
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

    private void ShutdownPage(CatalogPageState state)
    {
        if (state.SearchDebounceId is uint debounceId) GLib.Source.Remove(debounceId);
        state.IconLoadCancellationSource?.Cancel();
        state.IconLoadCancellationSource?.Dispose();
        state.DescriptionView?.Unload();
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
        state.AvailableFiles = [];
        state.SelectedFileId = null;
        state.ProjectVersionButton.Sensitive = false;
        ReplacePopoverContent(state.ProjectVersionPopover, LauncherStructuredList.BuildCatalogFileSelectionPopover($"{GetContentTitle(contentType)} versions", [], null, static _ => string.Empty, static _ => { }));
        state.ProjectVersionButton.Label = "Select version";
        state.VersionStatus.Text = statusText;
    }

    private void CloseWindow()
    {
        if (isClosing) return;
        isClosing = true;
        Destroy();
    }

    private static readonly CatalogSearchSort[] DefaultSorts =
    [
        CatalogSearchSort.Relevance,
        CatalogSearchSort.Downloads,
        CatalogSearchSort.Follows,
        CatalogSearchSort.Newest,
        CatalogSearchSort.Updated
    ];

    private sealed class CatalogPageState
    {
        public CatalogPageState(CatalogProvider provider)
        {
            Provider = provider;
        }

        public CatalogProvider Provider { get; }
        public Entry SearchEntry { get; } = new() { PlaceholderText = "Search" };
        public Button SortButton { get; } = new("Sort");
        public Button CategoryButton { get; } = new("Category");
        public Button VersionButton { get; } = new("Version");
        public Button LoaderButton { get; } = new("Loader");
        public Popover SortPopover { get; set; } = null!;
        public Popover CategoryPopover { get; set; } = null!;
        public Popover VersionPopover { get; set; } = null!;
        public Popover LoaderPopover { get; set; } = null!;
        public Button ProjectVersionButton { get; } = new("Select version") { Hexpand = true, Sensitive = false };
        public Popover ProjectVersionPopover { get; set; } = null!;
        public ListBox ProjectList { get; } = new() { SelectionMode = SelectionMode.Single };
        public Label ListStatus { get; } = new() { Xalign = 0, Wrap = true };
        public Button DetailsTitleButton { get; set; } = null!;
        public Label DetailsTitle { get; set; } = null!;
        public Label DetailsMeta { get; set; } = null!;
        public CatalogDescriptionView DescriptionView { get; set; } = null!;
        public Label VersionStatus { get; } = new() { Xalign = 0, Wrap = true };
        public string? DetailsProjectUrl { get; set; }
        public CatalogProjectSummary? SelectedProject { get; set; }
        public IReadOnlyList<CatalogFileSummary> AvailableFiles { get; set; } = [];
        public string? SelectedFileId { get; set; }
        public IReadOnlyList<CatalogSearchSort> AvailableSorts { get; set; } = DefaultSorts;
        public IReadOnlyList<string> AvailableCategories { get; set; } = [];
        public IReadOnlyList<string> AvailableGameVersions { get; set; } = [];
        public IReadOnlyList<string> AvailableLoaders { get; set; } = [];
        public HashSet<string> SelectedCategories { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedGameVersions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> SelectedLoaders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> InstalledProjectIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public CatalogSearchSort SelectedSort { get; set; } = CatalogSearchSort.Relevance;
        public IReadOnlyList<CatalogProjectSummary> Results { get; set; } = [];
        public bool MetadataLoaded { get; set; }
        public bool MetadataLoading { get; set; }
        public bool HasLoadedResults { get; set; }
        public bool IsSearching { get; set; }
        public uint? SearchDebounceId { get; set; }
        public CatalogProviderMetadata? Metadata { get; set; }
        public int ProjectListVersion { get; set; }
        public CancellationTokenSource? IconLoadCancellationSource { get; set; }
    }

    private sealed class ProviderRow : ListBoxRow
    {
        public ProviderRow(CatalogProvider provider, string title)
        {
            Provider = provider;
            var body = new Box(Orientation.Horizontal, 8);
            var accent = new EventBox { WidthRequest = 2 };
            accent.StyleContext.AddClass("settings-nav-accent");
            var label = new Label(title) { Xalign = 0 };
            label.StyleContext.AddClass("settings-nav-text");
            var content = new Box(Orientation.Horizontal, 0) { MarginTop = 6, MarginBottom = 6, MarginStart = 8, MarginEnd = 8, Hexpand = true };
            content.StyleContext.AddClass("settings-nav-row-body");
            content.PackStart(label, true, true, 0);
            body.PackStart(accent, false, false, 0);
            body.PackStart(content, true, true, 0);
            Add(body);
        }

        public CatalogProvider Provider { get; }
    }

    private sealed class CatalogProjectRow : ListBoxRow
    {
        private readonly Image iconImage = new();
        private readonly Label placeholder = new("PK");
        private readonly Label installedLabel = new("(installed)") { Xalign = 0, NoShowAll = true };

        public CatalogProjectRow(CatalogProjectSummary project, bool useEvenTone, bool installed)
        {
            Project = project;
            Destroyed += (_, _) =>
            {
                ClearIcon();
                IsDisposed = true;
            };
            StyleContext.AddClass(useEvenTone ? "add-instance-version-row-even" : "add-instance-version-row-odd");
            var title = new Label(project.Title) { Xalign = 0, Wrap = true, MaxWidthChars = 28 };
            title.StyleContext.AddClass("settings-row-label");
            installedLabel.StyleContext.AddClass("catalog-installed-label");
            var titleRow = new Box(Orientation.Horizontal, 6);
            titleRow.PackStart(title, false, false, 0);
            titleRow.PackStart(installedLabel, false, false, 0);
            var subtitle = new Label(string.Join(" • ", new[] { project.GameVersions.FirstOrDefault() is { Length: > 0 } version ? $"MC {version}" : null, project.Loaders.Count > 0 ? string.Join(", ", project.Loaders.Take(2).Select(ToDisplayName)) : null }.Where(static part => !string.IsNullOrWhiteSpace(part)))) { Xalign = 0, Wrap = true };
            subtitle.StyleContext.AddClass("settings-help");
            var iconCell = new EventBox { WidthRequest = 52, HeightRequest = 52, Vexpand = false, Hexpand = false };
            iconCell.StyleContext.AddClass("add-instance-pack-icon-cell");
            var iconOverlay = new Overlay { WidthRequest = 52, HeightRequest = 52 };
            iconImage.Halign = Align.Center;
            iconImage.Valign = Align.Center;
            placeholder.Halign = Align.Center;
            placeholder.Valign = Align.Center;
            placeholder.StyleContext.AddClass("add-instance-pack-icon-placeholder");
            iconImage.Hide();
            iconOverlay.Add(iconImage);
            iconOverlay.AddOverlay(placeholder);
            iconCell.Add(iconOverlay);
            var textContent = new Box(Orientation.Vertical, 3) { MarginTop = 8, MarginBottom = 8, MarginStart = 10, MarginEnd = 10, Valign = Align.Center, Hexpand = true };
            textContent.PackStart(titleRow, false, false, 0);
            textContent.PackStart(subtitle, false, false, 0);
            Add(LauncherStructuredList.CreateRowContent(
                new LauncherStructuredList.CellDefinition(iconCell, WidthRequest: 60, ShowTrailingDivider: true),
                new LauncherStructuredList.CellDefinition(textContent, Expand: true)));
            SetInstalled(installed);
        }

        public CatalogProjectSummary Project { get; }
        public bool IsDisposed { get; private set; }
        public bool HasIcon => iconImage.Pixbuf is not null;

        public void SetInstalled(bool installed)
        {
            if (installed)
            {
                installedLabel.Show();
            }
            else
            {
                installedLabel.Hide();
            }
        }

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
                iconImage.Hide();
                placeholder.Show();
                return;
            }

            var previous = iconImage.Pixbuf;
            iconImage.Pixbuf = pixbuf;
            if (previous is not null && !ReferenceEquals(previous, pixbuf))
            {
                previous.Dispose();
            }

            placeholder.Hide();
            iconImage.Show();
        }

        private void ClearIcon()
        {
            var previous = iconImage.Pixbuf;
            iconImage.Pixbuf = null;
            previous?.Dispose();
        }
    }
}
