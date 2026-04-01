using BlockiumLauncher.Application.UseCases.Skins;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using BlockiumLauncher.UI.GtkSharp.Widgets;
using Gdk;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class SkinCustomizationWindow : Gtk.Window
{
    private readonly ListSkinAssetsUseCase ListSkinAssetsUseCase;
    private readonly ImportSkinAssetUseCase ImportSkinAssetUseCase;
    private readonly UpdateSkinModelUseCase UpdateSkinModelUseCase;
    private readonly ListCapeAssetsUseCase ListCapeAssetsUseCase;
    private readonly ImportCapeAssetUseCase ImportCapeAssetUseCase;
    private readonly GetAccountAppearanceUseCase GetAccountAppearanceUseCase;
    private readonly SetAccountAppearanceUseCase SetAccountAppearanceUseCase;

    private readonly SkinPreviewArea PreviewArea = new()
    {
        WidthRequest = 320,
        HeightRequest = 320
    };
    private readonly FlowBox SkinGrid = CreateAssetGrid();
    private readonly FlowBox CapeGrid = CreateAssetGrid();
    private readonly Notebook AssetNotebook = new()
    {
        Hexpand = true,
        Vexpand = true
    };
    private readonly Label SelectedSkinLabel = new() { Xalign = 0 };
    private readonly Label SelectedCapeLabel = new() { Xalign = 0 };
    private readonly Button ModelSelectorButton = new();
    private readonly Button ImportSkinButton = new("Import skin");
    private readonly Button ImportCapeButton = new("Import cape");
    private readonly Button CancelButton = new("Cancel");
    private readonly Button ApplyButton = new("Apply");

    private LauncherAccount? CurrentAccount;
    private string? DraftSkinId;
    private string? DraftCapeId;
    private SkinModelType? DraftModelType;
    private IReadOnlyList<SkinAssetSummary> SkinAssets = [];
    private IReadOnlyList<CapeAssetSummary> CapeAssets = [];
    private Popover? ModelSelectorPopover;
    private RadioButton? ClassicModelRadioButton;
    private RadioButton? SlimModelRadioButton;

    public event EventHandler<AccountAppearanceAppliedEventArgs>? AppearanceApplied;

    public SkinCustomizationWindow(
        ListSkinAssetsUseCase listSkinAssetsUseCase,
        ImportSkinAssetUseCase importSkinAssetUseCase,
        UpdateSkinModelUseCase updateSkinModelUseCase,
        ListCapeAssetsUseCase listCapeAssetsUseCase,
        ImportCapeAssetUseCase importCapeAssetUseCase,
        GetAccountAppearanceUseCase getAccountAppearanceUseCase,
        SetAccountAppearanceUseCase setAccountAppearanceUseCase) : base("Skin customization")
    {
        ListSkinAssetsUseCase = listSkinAssetsUseCase ?? throw new ArgumentNullException(nameof(listSkinAssetsUseCase));
        ImportSkinAssetUseCase = importSkinAssetUseCase ?? throw new ArgumentNullException(nameof(importSkinAssetUseCase));
        UpdateSkinModelUseCase = updateSkinModelUseCase ?? throw new ArgumentNullException(nameof(updateSkinModelUseCase));
        ListCapeAssetsUseCase = listCapeAssetsUseCase ?? throw new ArgumentNullException(nameof(listCapeAssetsUseCase));
        ImportCapeAssetUseCase = importCapeAssetUseCase ?? throw new ArgumentNullException(nameof(importCapeAssetUseCase));
        GetAccountAppearanceUseCase = getAccountAppearanceUseCase ?? throw new ArgumentNullException(nameof(getAccountAppearanceUseCase));
        SetAccountAppearanceUseCase = setAccountAppearanceUseCase ?? throw new ArgumentNullException(nameof(setAccountAppearanceUseCase));

        SetDefaultSize(1100, 720);
        Resizable = true;
        WindowPosition = WindowPosition.Center;

        DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            PrepareForHide();
            Hide();
        };

        Titlebar = BuildHeaderBar();
        Add(BuildRoot());
        ConfigureButtons();
        ConfigureDropTargets();
    }

    public void PresentForAccount(Gtk.Window owner, LauncherAccount account)
    {
        CurrentAccount = account ?? throw new ArgumentNullException(nameof(account));
        ShowAll();
        Present();
        _ = LoadAsync();
    }

    private void PrepareForHide()
    {
        ModelSelectorPopover?.Popdown();
    }

    private void ConfigureButtons()
    {
        ModelSelectorButton.StyleContext.AddClass("popover-menu-button");
        UpdateModelSelectorButton();
        ModelSelectorButton.Clicked += (_, _) =>
        {
            var popover = GetOrCreateModelPopover();
            popover.ShowAll();
            popover.Popup();
        };

        ImportSkinButton.StyleContext.AddClass("action-button");
        ImportSkinButton.Clicked += async (_, _) => await ImportSkinAsync().ConfigureAwait(false);

        ImportCapeButton.StyleContext.AddClass("action-button");
        ImportCapeButton.Clicked += async (_, _) => await ImportCapeAsync().ConfigureAwait(false);

        CancelButton.StyleContext.AddClass("action-button");
        CancelButton.Clicked += (_, _) =>
        {
            PrepareForHide();
            Hide();
        };

        ApplyButton.StyleContext.AddClass("action-button");
        ApplyButton.StyleContext.AddClass("primary-button");
        ApplyButton.Clicked += async (_, _) => await ApplyAsync().ConfigureAwait(false);
    }

    private void ConfigureDropTargets()
    {
        EnableDropTarget(SkinGrid, acceptsCapes: false);
        EnableDropTarget(CapeGrid, acceptsCapes: true);
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

        var title = new Label("Skin customization")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-title");

        var subtitle = new Label("Preview the selected appearance and browse saved skin or cape PNG files.")
        {
            Xalign = 0
        };
        subtitle.StyleContext.AddClass("settings-subtitle");

        content.PackStart(title, false, false, 0);
        content.PackStart(subtitle, false, false, 0);
        bar.PackStart(content);
        return bar;
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
        body.PackStart(BuildPreviewColumn(), false, false, 0);
        body.PackStart(BuildBrowserPane(), true, true, 0);
        return body;
    }

    private Widget BuildPreviewColumn()
    {
        var shell = new EventBox
        {
            WidthRequest = 340,
            Hexpand = false,
            Vexpand = true
        };
        shell.StyleContext.AddClass("settings-nav-shell");

        var content = new Box(Orientation.Vertical, 12)
        {
            MarginTop = 16,
            MarginBottom = 16,
            MarginStart = 16,
            MarginEnd = 16
        };

        var previewTitle = new Label("Preview")
        {
            Xalign = 0
        };
        previewTitle.StyleContext.AddClass("settings-page-title");

        var previewSubtitle = new Label("Drag left or right to rotate the character.")
        {
            Xalign = 0
        };
        previewSubtitle.StyleContext.AddClass("settings-subtitle");

        var previewFrame = new EventBox();
        previewFrame.StyleContext.AddClass("avatar-preview-frame");
        previewFrame.WidthRequest = 320;
        previewFrame.HeightRequest = 320;
        previewFrame.Halign = Align.Center;
        previewFrame.Valign = Align.Start;
        previewFrame.Add(PreviewArea);

        content.PackStart(previewTitle, false, false, 0);
        content.PackStart(previewSubtitle, false, false, 0);
        content.PackStart(previewFrame, false, false, 0);
        content.PackStart(BuildSelectionPane(), false, false, 0);
        content.PackStart(new Label { Vexpand = true }, true, true, 0);

        shell.Add(content);
        return shell;
    }

    private Widget BuildSelectionPane()
    {
        var pane = new Box(Orientation.Vertical, 8)
        {
            MarginTop = 4
        };

        var title = new Label("Selection")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-page-title");

        SelectedSkinLabel.StyleContext.AddClass("settings-caption");
        SelectedCapeLabel.StyleContext.AddClass("settings-caption");

        pane.PackStart(title, false, false, 0);
        pane.PackStart(CreateInspectorBlock("Skin", SelectedSkinLabel), false, false, 0);
        pane.PackStart(CreateInspectorBlock("Cape", SelectedCapeLabel), false, false, 0);
        pane.PackStart(CreateInspectorBlock("Model", ModelSelectorButton), false, false, 0);
        return pane;
    }

    private Widget BuildBrowserPane()
    {
        var shell = new EventBox
        {
            Hexpand = true,
            Vexpand = true
        };
        shell.StyleContext.AddClass("content-shell");

        AssetNotebook.AppendPage(BuildAssetPage(SkinGrid, acceptsCapes: false), new Label("Skins"));
        AssetNotebook.AppendPage(BuildAssetPage(CapeGrid, acceptsCapes: true), new Label("Capes"));

        var content = new Box(Orientation.Vertical, 12)
        {
            MarginTop = 16,
            MarginBottom = 16,
            MarginStart = 16,
            MarginEnd = 16
        };

        var title = new Label("Library")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-page-title");

        var subtitle = new Label("Choose a saved skin or cape from the launcher library.")
        {
            Xalign = 0
        };
        subtitle.StyleContext.AddClass("settings-subtitle");

        content.PackStart(title, false, false, 0);
        content.PackStart(subtitle, false, false, 0);
        content.PackStart(AssetNotebook, true, true, 0);

        shell.Add(content);
        return shell;
    }

    private Widget BuildAssetPage(FlowBox grid, bool acceptsCapes)
    {
        var container = new Box(Orientation.Vertical, 0);

        var scroller = new ScrolledWindow
        {
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("instance-scroller");
        scroller.HscrollbarPolicy = PolicyType.Never;
        scroller.VscrollbarPolicy = PolicyType.Automatic;
        scroller.Add(grid);
        EnableDropTarget(scroller, acceptsCapes);

        container.PackStart(scroller, true, true, 0);
        return container;
    }

    private Widget BuildFooter()
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("settings-footer");

        var content = new Box(Orientation.Horizontal, 8)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 16,
            MarginEnd = 16
        };

        content.PackStart(ImportSkinButton, false, false, 0);
        content.PackStart(ImportCapeButton, false, false, 0);
        content.PackStart(new Label { Hexpand = true }, true, true, 0);
        content.PackStart(CancelButton, false, false, 0);
        content.PackStart(ApplyButton, false, false, 0);

        shell.Add(content);
        return shell;
    }

    private static Widget CreateInspectorBlock(string title, Widget value)
    {
        var card = new Box(Orientation.Vertical, 6);
        card.StyleContext.AddClass("settings-card-muted");

        var label = new Label(title)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("settings-row-label");

        card.PackStart(label, false, false, 0);
        card.PackStart(value, false, false, 0);
        return card;
    }

    private async Task LoadAsync(bool preserveDraftSelection = false)
    {
        if (CurrentAccount is null)
        {
            return;
        }

        var skinsResult = await ListSkinAssetsUseCase.ExecuteAsync().ConfigureAwait(false);
        var capesResult = await ListCapeAssetsUseCase.ExecuteAsync().ConfigureAwait(false);
        var appearanceResult = await GetAccountAppearanceUseCase.ExecuteAsync(CurrentAccount.AccountId).ConfigureAwait(false);

        if (skinsResult.IsFailure || capesResult.IsFailure || appearanceResult.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to load customization", skinsResult.IsFailure
                ? skinsResult.Error.Message
                : capesResult.IsFailure
                    ? capesResult.Error.Message
                    : appearanceResult.Error.Message));
            return;
        }

        SkinAssets = skinsResult.Value;
        CapeAssets = capesResult.Value;

        if (!preserveDraftSelection)
        {
            DraftSkinId = appearanceResult.Value.SelectedSkinId;
            DraftCapeId = appearanceResult.Value.SelectedCapeId;
        }

        if (!string.IsNullOrWhiteSpace(DraftSkinId) &&
            SkinAssets.All(asset => !string.Equals(asset.SkinId, DraftSkinId, StringComparison.Ordinal)))
        {
            DraftSkinId = null;
        }

        if (!string.IsNullOrWhiteSpace(DraftCapeId) &&
            CapeAssets.All(asset => !string.Equals(asset.CapeId, DraftCapeId, StringComparison.Ordinal)))
        {
            DraftCapeId = null;
        }

        DraftModelType = GetSelectedSkinAsset()?.ModelType ?? DraftModelType;

        Gtk.Application.Invoke((_, _) =>
        {
            RefreshSkinGrid();
            RefreshCapeGrid();
            RefreshInspector();
            RefreshPreview();
        });
    }

    private void RefreshSkinGrid()
    {
        foreach (var child in SkinGrid.Children.Cast<Widget>().ToArray())
        {
            SkinGrid.Remove(child);
        }

        if (SkinAssets.Count == 0)
        {
            SkinGrid.Add(CreateEmptyTile("No skins"));
        }
        else
        {
            foreach (var skin in SkinAssets)
            {
                SkinGrid.Add(BuildAssetTile(
                    skin.SkinId,
                    skin.StoragePath,
                    FormatAssetLabel(System.IO.Path.GetFileNameWithoutExtension(skin.FileName)),
                    previewWidth: 78,
                    previewHeight: 78,
                    isSelected: string.Equals(skin.SkinId, DraftSkinId, StringComparison.Ordinal),
                    onClick: () =>
                    {
                        DraftSkinId = skin.SkinId;
                        DraftModelType = skin.ModelType;
                        RefreshSkinGrid();
                        RefreshInspector();
                        RefreshPreview();
                    }));
            }
        }

        SkinGrid.ShowAll();
    }

    private void RefreshCapeGrid()
    {
        foreach (var child in CapeGrid.Children.Cast<Widget>().ToArray())
        {
            CapeGrid.Remove(child);
        }

        CapeGrid.Add(BuildAssetTile(
            string.Empty,
            null,
            "No cape",
            previewWidth: 78,
            previewHeight: 42,
            isSelected: string.IsNullOrWhiteSpace(DraftCapeId),
            onClick: () =>
            {
                DraftCapeId = null;
                RefreshCapeGrid();
                RefreshInspector();
                RefreshPreview();
            }));

        foreach (var cape in CapeAssets)
        {
            CapeGrid.Add(BuildAssetTile(
                cape.CapeId,
                cape.StoragePath,
                FormatAssetLabel(System.IO.Path.GetFileNameWithoutExtension(cape.FileName)),
                previewWidth: 78,
                previewHeight: 42,
                isSelected: string.Equals(cape.CapeId, DraftCapeId, StringComparison.Ordinal),
                onClick: () =>
                {
                    DraftCapeId = cape.CapeId;
                    RefreshCapeGrid();
                    RefreshInspector();
                    RefreshPreview();
                }));
        }

        CapeGrid.ShowAll();
    }

    private Widget BuildAssetTile(
        string assetId,
        string? imagePath,
        string labelText,
        int previewWidth,
        int previewHeight,
        bool isSelected,
        System.Action onClick)
    {
        var button = new Button
        {
            Name = assetId,
            Relief = ReliefStyle.None,
            FocusOnClick = false,
            WidthRequest = 112,
            HeightRequest = 138
        };
        button.StyleContext.AddClass("asset-tile-button");
        if (isSelected)
        {
            button.StyleContext.AddClass("asset-tile-button-selected");
        }

        var content = new Box(Orientation.Vertical, 8)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 10,
            MarginEnd = 10
        };

        var thumbShell = new EventBox
        {
            WidthRequest = previewWidth,
            HeightRequest = previewHeight,
            Halign = Align.Center,
            Valign = Align.Center
        };
        thumbShell.StyleContext.AddClass("asset-thumb-shell");
        thumbShell.Add(CreateThumbnailWidget(imagePath, previewWidth, previewHeight));

        var label = new Label(labelText)
        {
            Xalign = 0.5f,
            Justify = Justification.Center,
            LineWrap = true,
            MaxWidthChars = 11,
            LineWrapMode = Pango.WrapMode.WordChar
        };
        label.StyleContext.AddClass("asset-tile-label");

        content.PackStart(thumbShell, false, false, 0);
        content.PackStart(label, false, false, 0);
        button.Add(content);
        button.Clicked += (_, _) => onClick();
        return button;
    }

    private static Widget CreateEmptyTile(string text)
    {
        var frame = new EventBox
        {
            WidthRequest = 112,
            HeightRequest = 138
        };
        frame.StyleContext.AddClass("settings-card-muted");

        var label = new Label(text)
        {
            Xalign = 0.5f,
            Yalign = 0.5f,
            Justify = Justification.Center,
            LineWrap = true
        };
        label.StyleContext.AddClass("settings-caption");

        frame.Add(label);
        return frame;
    }

    private static Widget CreateThumbnailWidget(string? path, int width, int height)
    {
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                using var source = new Pixbuf(path);
                var scaled = source.ScaleSimple(width, height, InterpType.Nearest);
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

        var placeholder = new EventBox
        {
            WidthRequest = width,
            HeightRequest = height
        };
        placeholder.StyleContext.AddClass("asset-thumb-placeholder");
        return placeholder;
    }

    private void RefreshInspector()
    {
        SelectedSkinLabel.Text = GetSelectedSkinAsset() is { } skin
            ? System.IO.Path.GetFileName(skin.FileName)
            : "Default fallback";
        SelectedCapeLabel.Text = GetSelectedCapeAsset() is { } cape
            ? System.IO.Path.GetFileName(cape.FileName)
            : "No cape";
        UpdateModelSelectorButton();
        ApplyButton.Sensitive = CurrentAccount is not null;
    }

    private void RefreshPreview()
    {
        var selectedSkin = GetSelectedSkinAsset();
        var selectedCape = GetSelectedCapeAsset();
        var modelType = DraftModelType ?? selectedSkin?.ModelType ?? SkinModelType.Classic;
        var fallbackSkin = CurrentAccount is null ? null : SkinImageUtilities.ResolveSkinPath(null, CurrentAccount.Username);

        PreviewArea.SetPreview(
            selectedSkin?.StoragePath ?? fallbackSkin,
            selectedCape?.StoragePath,
            modelType);
    }

    private void UpdateModelSelectorButton()
    {
        var hasSkin = GetSelectedSkinAsset() is not null;
        ModelSelectorButton.Sensitive = hasSkin;
        ModelSelectorButton.Label = !hasSkin
            ? "Default"
            : DraftModelType == SkinModelType.Slim ? "Slim" : "Classic";

        if (ClassicModelRadioButton is not null)
        {
            ClassicModelRadioButton.Active = !hasSkin || DraftModelType != SkinModelType.Slim;
        }

        if (SlimModelRadioButton is not null)
        {
            SlimModelRadioButton.Active = hasSkin && DraftModelType == SkinModelType.Slim;
        }
    }

    private Popover GetOrCreateModelPopover()
    {
        if (ModelSelectorPopover is not null)
        {
            return ModelSelectorPopover;
        }

        ModelSelectorPopover = new Popover(ModelSelectorButton)
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

        var title = new Label("Model")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        content.PackStart(title, false, false, 0);

        RadioButton? group = null;
        foreach (var option in new[]
                 {
                     (SkinModelType.Classic, "Classic"),
                     (SkinModelType.Slim, "Slim")
                 })
        {
            var radio = group is null ? new RadioButton(option.Item2) : new RadioButton(group, option.Item2);
            group ??= radio;
            radio.Active = DraftModelType == option.Item1 || (DraftModelType is null && option.Item1 == SkinModelType.Classic);
            radio.StyleContext.AddClass("popover-check");
            switch (option.Item1)
            {
                case SkinModelType.Classic:
                    ClassicModelRadioButton = radio;
                    break;
                case SkinModelType.Slim:
                    SlimModelRadioButton = radio;
                    break;
            }
            radio.Toggled += (_, _) =>
            {
                if (!radio.Active)
                {
                    return;
                }

                DraftModelType = option.Item1;
                UpdateModelSelectorButton();
                RefreshPreview();
                ModelSelectorPopover?.Popdown();
            };
            content.PackStart(radio, false, false, 0);
        }

        ModelSelectorPopover.Add(content);
        return ModelSelectorPopover;
    }

    private async Task ImportSkinAsync()
    {
        var fileName = ChoosePngFile("Import skin");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        await ImportSkinFileAsync(fileName).ConfigureAwait(false);
    }

    private async Task ImportCapeAsync()
    {
        var fileName = ChoosePngFile("Import cape");
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return;
        }

        await ImportCapeFileAsync(fileName).ConfigureAwait(false);
    }

    private async Task ApplyAsync()
    {
        if (CurrentAccount is null)
        {
            return;
        }

        var selectedSkin = GetSelectedSkinAsset();
        if (selectedSkin is not null && DraftModelType.HasValue && selectedSkin.ModelType != DraftModelType.Value)
        {
            var modelResult = await UpdateSkinModelUseCase.ExecuteAsync(new UpdateSkinModelRequest
            {
                SkinId = selectedSkin.SkinId,
                ModelType = DraftModelType.Value
            }).ConfigureAwait(false);

            if (modelResult.IsFailure)
            {
                Gtk.Application.Invoke((_, _) => ShowError("Unable to update skin model", modelResult.Error.Message));
                return;
            }
        }

        var result = await SetAccountAppearanceUseCase.ExecuteAsync(new SetAccountAppearanceRequest
        {
            AccountId = CurrentAccount.AccountId,
            SelectedSkinId = DraftSkinId,
            SelectedCapeId = DraftCapeId
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to save skin selection", result.Error.Message));
            return;
        }

        AppearanceApplied?.Invoke(this, new AccountAppearanceAppliedEventArgs(CurrentAccount.AccountId));
        Gtk.Application.Invoke((_, _) =>
        {
            PrepareForHide();
            Hide();
        });
    }

    private SkinAssetSummary? GetSelectedSkinAsset()
    {
        return SkinAssets.FirstOrDefault(asset => string.Equals(asset.SkinId, DraftSkinId, StringComparison.Ordinal));
    }

    private CapeAssetSummary? GetSelectedCapeAsset()
    {
        if (string.IsNullOrWhiteSpace(DraftCapeId))
        {
            return null;
        }

        return CapeAssets.FirstOrDefault(asset => string.Equals(asset.CapeId, DraftCapeId, StringComparison.Ordinal));
    }

    private static FlowBox CreateAssetGrid()
    {
        return new FlowBox
        {
            SelectionMode = SelectionMode.None,
            RowSpacing = 10,
            ColumnSpacing = 10,
            MarginTop = 4,
            MarginBottom = 4,
            MarginStart = 4,
            MarginEnd = 4,
            MinChildrenPerLine = 5,
            MaxChildrenPerLine = 7,
            Hexpand = true,
            Valign = Align.Start
        };
    }

    private void EnableDropTarget(Widget widget, bool acceptsCapes)
    {
        var targets = new[]
        {
            new TargetEntry("text/uri-list", 0, 0)
        };

        Gtk.Drag.DestSet(widget, DestDefaults.All, targets, Gdk.DragAction.Copy);
        widget.DragDataReceived += async (_, args) =>
        {
            var success = false;
            try
            {
                success = await HandleDropAsync(args, acceptsCapes).ConfigureAwait(false);
            }
            finally
            {
                Gtk.Application.Invoke((_, _) => Gtk.Drag.Finish(args.Context, success, false, args.Time));
            }
        };
    }

    private async Task<bool> HandleDropAsync(DragDataReceivedArgs args, bool acceptsCapes)
    {
        var filePaths = ExtractDroppedPngPaths(args.SelectionData?.Text);
        if (filePaths.Count == 0)
        {
            return false;
        }

        var firstError = string.Empty;
        var importedAny = false;

        foreach (var filePath in filePaths)
        {
            if (!acceptsCapes)
            {
                var skinResult = await ImportSkinAssetUseCase.ExecuteAsync(new ImportSkinAssetRequest
                {
                    SourceFilePath = filePath
                }).ConfigureAwait(false);

                if (skinResult.IsSuccess)
                {
                    DraftSkinId = skinResult.Value.SkinId;
                    DraftModelType = skinResult.Value.ModelType;
                    importedAny = true;
                    continue;
                }

                firstError = string.IsNullOrWhiteSpace(firstError) ? skinResult.Error.Message : firstError;
                continue;
            }

            var capeResult = await ImportCapeAssetUseCase.ExecuteAsync(new ImportCapeAssetRequest
            {
                SourceFilePath = filePath
            }).ConfigureAwait(false);

            if (capeResult.IsSuccess)
            {
                DraftCapeId = capeResult.Value.CapeId;
                importedAny = true;
                continue;
            }

            firstError = string.IsNullOrWhiteSpace(firstError) ? capeResult.Error.Message : firstError;
        }

        if (importedAny)
        {
            AssetNotebook.CurrentPage = acceptsCapes ? 1 : 0;
            await LoadAsync(preserveDraftSelection: true).ConfigureAwait(false);
        }
        else if (!string.IsNullOrWhiteSpace(firstError))
        {
            Gtk.Application.Invoke((_, _) => ShowError(
                acceptsCapes ? "Unable to import cape" : "Unable to import skin",
                firstError));
        }

        return importedAny;
    }

    private async Task ImportSkinFileAsync(string fileName)
    {
        var result = await ImportSkinAssetUseCase.ExecuteAsync(new ImportSkinAssetRequest
        {
            SourceFilePath = fileName
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to import skin", result.Error.Message));
            return;
        }

        DraftSkinId = result.Value.SkinId;
        DraftModelType = result.Value.ModelType;
        AssetNotebook.CurrentPage = 0;
        await LoadAsync(preserveDraftSelection: true).ConfigureAwait(false);
    }

    private async Task ImportCapeFileAsync(string fileName)
    {
        var result = await ImportCapeAssetUseCase.ExecuteAsync(new ImportCapeAssetRequest
        {
            SourceFilePath = fileName
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to import cape", result.Error.Message));
            return;
        }

        DraftCapeId = result.Value.CapeId;
        AssetNotebook.CurrentPage = 1;
        await LoadAsync(preserveDraftSelection: true).ConfigureAwait(false);
    }

    private static IReadOnlyList<string> ExtractDroppedPngPaths(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return [];
        }

        return rawText
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item) && !item.StartsWith('#'))
            .Select(item =>
            {
                if (Uri.TryCreate(item, UriKind.Absolute, out var uri) && uri.IsFile)
                {
                    return uri.LocalPath;
                }

                return item;
            })
            .Where(path => File.Exists(path) && string.Equals(System.IO.Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private FileChooserDialog CreatePngChooser(string title)
    {
        var dialog = new FileChooserDialog(title, this, FileChooserAction.Open);
        dialog.AddButton("Cancel", ResponseType.Cancel);
        dialog.AddButton("Import", ResponseType.Accept);

        var filter = new FileFilter
        {
            Name = "PNG images"
        };
        filter.AddPattern("*.png");
        dialog.AddFilter(filter);
        return dialog;
    }

    private string? ChoosePngFile(string title)
    {
        var nativeSelection = DesktopShell.PickPngFile(title);
        if (!string.IsNullOrWhiteSpace(nativeSelection))
        {
            return nativeSelection;
        }

        using var dialog = CreatePngChooser(title);
        if ((ResponseType)dialog.Run() != ResponseType.Accept)
        {
            return null;
        }

        return dialog.Filename;
    }

    private static string FormatAssetLabel(string fileNameWithoutExtension)
    {
        var normalized = fileNameWithoutExtension
            .Replace('_', ' ')
            .Replace('-', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return "Unnamed";
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            return SplitLongToken(words[0], 10);
        }

        var lines = new List<string>();
        var currentLine = string.Empty;
        foreach (var word in words)
        {
            if (string.IsNullOrEmpty(currentLine))
            {
                currentLine = word;
                continue;
            }

            if ((currentLine.Length + 1 + word.Length) <= 10)
            {
                currentLine += " " + word;
                continue;
            }

            lines.Add(currentLine);
            currentLine = word;
        }

        if (!string.IsNullOrEmpty(currentLine))
        {
            lines.Add(currentLine);
        }

        return string.Join(Environment.NewLine, lines.Select(line => line.Length > 10 ? SplitLongToken(line, 10) : line));
    }

    private static string SplitLongToken(string value, int chunkSize)
    {
        var chunks = new List<string>();
        for (var index = 0; index < value.Length; index += chunkSize)
        {
            chunks.Add(value.Substring(index, Math.Min(chunkSize, value.Length - index)));
        }

        return string.Join(Environment.NewLine, chunks);
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

public sealed class AccountAppearanceAppliedEventArgs : EventArgs
{
    public AccountAppearanceAppliedEventArgs(BlockiumLauncher.Domain.ValueObjects.AccountId accountId)
    {
        AccountId = accountId;
    }

    public BlockiumLauncher.Domain.ValueObjects.AccountId AccountId { get; }
}
