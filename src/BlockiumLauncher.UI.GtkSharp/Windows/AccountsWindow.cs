using BlockiumLauncher.Application.UseCases.Accounts;
using BlockiumLauncher.Application.UseCases.Skins;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using Gdk;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class AccountsWindow : Gtk.Window
{
    private readonly ListAccountsUseCase ListAccountsUseCase;
    private readonly AddAccountUseCase AddAccountUseCase;
    private readonly SetDefaultAccountUseCase SetDefaultAccountUseCase;
    private readonly RemoveAccountUseCase RemoveAccountUseCase;
    private readonly GetAccountAppearanceUseCase GetAccountAppearanceUseCase;
    private readonly ListSkinAssetsUseCase ListSkinAssetsUseCase;
    private readonly SkinCustomizationWindow SkinCustomizationWindow;

    private readonly ListBox AccountList = new()
    {
        SelectionMode = SelectionMode.Single,
        Hexpand = true,
        Vexpand = true
    };

    private readonly Button SetDefaultButton = new("Set default");
    private readonly Button DeleteButton = new("Delete");
    private readonly Button ChangeSkinButton = new("Change skin");

    private List<AccountRowModel> Accounts = [];
    private string? SelectedAccountId;

    public AccountsWindow(
        ListAccountsUseCase listAccountsUseCase,
        AddAccountUseCase addAccountUseCase,
        SetDefaultAccountUseCase setDefaultAccountUseCase,
        RemoveAccountUseCase removeAccountUseCase,
        GetAccountAppearanceUseCase getAccountAppearanceUseCase,
        ListSkinAssetsUseCase listSkinAssetsUseCase,
        SkinCustomizationWindow skinCustomizationWindow) : base("Accounts")
    {
        ListAccountsUseCase = listAccountsUseCase ?? throw new ArgumentNullException(nameof(listAccountsUseCase));
        AddAccountUseCase = addAccountUseCase ?? throw new ArgumentNullException(nameof(addAccountUseCase));
        SetDefaultAccountUseCase = setDefaultAccountUseCase ?? throw new ArgumentNullException(nameof(setDefaultAccountUseCase));
        RemoveAccountUseCase = removeAccountUseCase ?? throw new ArgumentNullException(nameof(removeAccountUseCase));
        GetAccountAppearanceUseCase = getAccountAppearanceUseCase ?? throw new ArgumentNullException(nameof(getAccountAppearanceUseCase));
        ListSkinAssetsUseCase = listSkinAssetsUseCase ?? throw new ArgumentNullException(nameof(listSkinAssetsUseCase));
        SkinCustomizationWindow = skinCustomizationWindow ?? throw new ArgumentNullException(nameof(skinCustomizationWindow));

        SetDefaultSize(820, 520);
        Resizable = true;
        WindowPosition = WindowPosition.Center;

        DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            SkinCustomizationWindow.Hide();
            Hide();
        };

        SkinCustomizationWindow.AppearanceApplied += async (_, args) =>
        {
            SelectedAccountId = args.AccountId.ToString();
            await RefreshAccountsAsync().ConfigureAwait(false);
        };

        Titlebar = BuildHeaderBar();
        Add(BuildRoot());
        UpdateButtons();
    }

    public void PresentFrom(Gtk.Window owner)
    {
        ShowAll();
        Present();
        _ = RefreshAccountsAsync();
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

        var title = new Label("Accounts")
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-title");

        var subtitle = new Label("Manage launcher profiles and customize offline account skins.")
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

        var layout = new Box(Orientation.Horizontal, 0);
        layout.PackStart(BuildAccountsListShell(), true, true, 0);
        layout.PackStart(BuildSidebar(), false, false, 0);

        root.Add(layout);
        return root;
    }

    private Widget BuildAccountsListShell()
    {
        var shell = new EventBox
        {
            Hexpand = true,
            Vexpand = true
        };
        shell.StyleContext.AddClass("content-shell");

        AccountList.StyleContext.AddClass("accounts-list");
        AccountList.RowSelected += (_, args) =>
        {
            SelectedAccountId = args.Row?.Name;
            UpdateButtons();
        };

        var scroller = new ScrolledWindow
        {
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("instance-scroller");
        scroller.HscrollbarPolicy = PolicyType.Never;
        scroller.VscrollbarPolicy = PolicyType.Automatic;
        scroller.Add(AccountList);

        var content = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };
        content.PackStart(scroller, true, true, 0);

        shell.Add(content);
        return shell;
    }

    private Widget BuildSidebar()
    {
        var shell = new EventBox
        {
            WidthRequest = 190,
            Hexpand = false,
            Vexpand = true
        };
        shell.StyleContext.AddClass("accounts-sidebar-shell");

        var content = new Box(Orientation.Vertical, 10)
        {
            MarginTop = 16,
            MarginBottom = 16,
            MarginStart = 16,
            MarginEnd = 16
        };

        var addButton = new Button("Add account");
        addButton.StyleContext.AddClass("action-button");
        addButton.Clicked += async (_, _) => await AddOfflineAccountAsync().ConfigureAwait(false);

        SetDefaultButton.StyleContext.AddClass("action-button");
        SetDefaultButton.Clicked += async (_, _) => await SetSelectedAccountAsDefaultAsync().ConfigureAwait(false);

        DeleteButton.StyleContext.AddClass("action-button");
        DeleteButton.StyleContext.AddClass("danger-button");
        DeleteButton.Clicked += async (_, _) => await DeleteSelectedAccountAsync().ConfigureAwait(false);

        ChangeSkinButton.StyleContext.AddClass("action-button");
        ChangeSkinButton.Clicked += (_, _) => OpenSkinCustomization();

        content.PackStart(addButton, false, false, 0);
        content.PackStart(SetDefaultButton, false, false, 0);
        content.PackStart(DeleteButton, false, false, 0);
        content.PackStart(ChangeSkinButton, false, false, 0);
        content.PackStart(new Label { Vexpand = true }, true, true, 0);

        shell.Add(content);
        return shell;
    }

    private async Task RefreshAccountsAsync()
    {
        var accountsResult = await ListAccountsUseCase.ExecuteAsync().ConfigureAwait(false);
        if (accountsResult.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to load accounts", accountsResult.Error.Message));
            return;
        }

        var skinsResult = await ListSkinAssetsUseCase.ExecuteAsync().ConfigureAwait(false);
        var skinMap = skinsResult.IsSuccess
            ? skinsResult.Value.ToDictionary(item => item.SkinId, item => item.StoragePath, StringComparer.Ordinal)
            : new Dictionary<string, string>(StringComparer.Ordinal);

        var models = new List<AccountRowModel>();
        foreach (var account in accountsResult.Value)
        {
            string? skinPath = null;
            var appearanceResult = await GetAccountAppearanceUseCase.ExecuteAsync(account.AccountId).ConfigureAwait(false);
            if (appearanceResult.IsSuccess &&
                !string.IsNullOrWhiteSpace(appearanceResult.Value.SelectedSkinId) &&
                skinMap.TryGetValue(appearanceResult.Value.SelectedSkinId, out var mappedPath))
            {
                skinPath = mappedPath;
            }

            models.Add(new AccountRowModel(account, skinPath));
        }

        Accounts = models;

        Gtk.Application.Invoke((_, _) =>
        {
            foreach (var child in AccountList.Children.Cast<Widget>().ToArray())
            {
                AccountList.Remove(child);
            }

            if (Accounts.Count == 0)
            {
                AccountList.Add(CreateEmptyRow());
            }
            else
            {
                foreach (var account in Accounts)
                {
                    AccountList.Add(BuildAccountRow(account));
                }
            }

            AccountList.ShowAll();
            RestoreSelection();
            UpdateButtons();
        });
    }

    private Widget BuildAccountRow(AccountRowModel account)
    {
        var row = new ListBoxRow
        {
            Name = account.Account.AccountId.ToString(),
            Selectable = true,
            Activatable = true
        };

        var content = new Box(Orientation.Horizontal, 12)
        {
            HeightRequest = 60,
            MarginTop = 8,
            MarginBottom = 8,
            MarginStart = 14,
            MarginEnd = 14,
            Hexpand = true
        };
        content.StyleContext.AddClass("account-row-body");

        content.PackStart(SkinImageUtilities.CreateSkinHeadWidget(account.SkinPath, 34), false, false, 0);

        var name = new Label(account.Account.Username)
        {
            Xalign = 0
        };
        name.StyleContext.AddClass("account-row-text");

        content.PackStart(name, true, true, 0);

        if (account.Account.IsDefault)
        {
            content.PackStart(CreateDefaultStarWidget(18), false, false, 0);
        }

        row.Add(content);
        return row;
    }

    private static Widget CreateEmptyRow()
    {
        var row = new ListBoxRow
        {
            Selectable = false,
            Activatable = false
        };

        var label = new Label("No launcher accounts yet. Add an offline account to get started.")
        {
            Xalign = 0,
            LineWrap = true
        };
        label.StyleContext.AddClass("settings-caption");

        var box = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 16,
            MarginBottom = 16,
            MarginStart = 14,
            MarginEnd = 14
        };
        box.PackStart(label, false, false, 0);
        row.Add(box);
        return row;
    }

    private Widget CreateDefaultStarWidget(int size)
    {
        var star = new DrawingArea
        {
            WidthRequest = size,
            HeightRequest = size,
            Halign = Align.End,
            Valign = Align.Center
        };
        star.Drawn += (_, args) =>
        {
            var cr = args.Cr;
            var radius = size * 0.6;
            var innerRadius = radius * 0.48;
            var center = size / 2.0;

            cr.SetSourceRGB(0.96, 0.78, 0.18);
            for (var point = 0; point < 10; point++)
            {
                var angle = -Math.PI / 2 + point * Math.PI / 5;
                var pointRadius = point % 2 == 0 ? radius : innerRadius;
                var x = center + Math.Cos(angle) * pointRadius;
                var y = center + Math.Sin(angle) * pointRadius;

                if (point == 0)
                {
                    cr.MoveTo(x, y);
                }
                else
                {
                    cr.LineTo(x, y);
                }
            }

            cr.ClosePath();
            cr.Fill();
        };
        return star;
    }

    private async Task AddOfflineAccountAsync()
    {
        using var dialog = new Dialog("Add offline account", this, DialogFlags.Modal | DialogFlags.DestroyWithParent);
        dialog.Titlebar = BuildDialogHeaderBar(
            "Add offline account",
            "Create a local offline account for launcher testing and customization.");
        var cancelButton = new Button("Cancel");
        cancelButton.StyleContext.AddClass("action-button");
        dialog.AddActionWidget(cancelButton, ResponseType.Cancel);

        var addButton = new Button("Add");
        addButton.StyleContext.AddClass("primary-button");
        dialog.AddActionWidget(addButton, ResponseType.Ok);
        dialog.DefaultResponse = ResponseType.Ok;
        dialog.Resizable = false;
        dialog.SkipTaskbarHint = true;
        dialog.WindowPosition = WindowPosition.CenterOnParent;

        var entry = new Entry
        {
            PlaceholderText = "Username",
            ActivatesDefault = true
        };
        entry.StyleContext.AddClass("app-field");

        var setDefault = new CheckButton("Set as default")
        {
            Active = Accounts.Count == 0
        };

        var card = new EventBox();
        card.StyleContext.AddClass("settings-card");

        var content = new Box(Orientation.Vertical, 8)
        {
            MarginTop = 14,
            MarginBottom = 14,
            MarginStart = 14,
            MarginEnd = 14
        };

        var entryLabel = new Label("Username")
        {
            Xalign = 0
        };
        entryLabel.StyleContext.AddClass("app-field-label");

        var helper = new Label("Offline only. This name will be shown in the launcher UI.")
        {
            Xalign = 0,
            Wrap = true
        };
        helper.StyleContext.AddClass("app-field-help");

        content.PackStart(entryLabel, false, false, 0);
        content.PackStart(entry, false, false, 0);
        content.PackStart(helper, false, false, 0);
        content.PackStart(setDefault, false, false, 0);

        card.Add(content);
        dialog.ContentArea.BorderWidth = 0;
        dialog.ContentArea.Spacing = 0;
        dialog.ContentArea.PackStart(card, false, false, 0);

        dialog.ShowAll();

        if ((ResponseType)dialog.Run() != ResponseType.Ok)
        {
            return;
        }

        var username = entry.Text?.Trim();

        if (string.IsNullOrWhiteSpace(username))
        {
            return;
        }

        var result = await AddAccountUseCase.ExecuteAsync(new AddAccountRequest
        {
            Provider = AccountProvider.Offline,
            Username = username,
            SetAsDefault = setDefault.Active
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to add account", result.Error.Message));
            return;
        }

        SelectedAccountId = result.Value.AccountId.ToString();
        await RefreshAccountsAsync().ConfigureAwait(false);
    }

    private static Widget BuildDialogHeaderBar(string titleText, string subtitleText)
    {
        var bar = new HeaderBar
        {
            ShowCloseButton = true,
            HasSubtitle = false,
            DecorationLayout = ":close"
        };
        bar.StyleContext.AddClass("topbar-shell");

        var content = new Box(Orientation.Vertical, 2)
        {
            Halign = Align.Start
        };
        content.StyleContext.AddClass("topbar-content");

        var title = new Label(titleText)
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("settings-title");

        var subtitle = new Label(subtitleText)
        {
            Xalign = 0
        };
        subtitle.StyleContext.AddClass("settings-subtitle");

        content.PackStart(title, false, false, 0);
        content.PackStart(subtitle, false, false, 0);
        bar.PackStart(content);
        return bar;
    }

    private async Task SetSelectedAccountAsDefaultAsync()
    {
        var account = GetSelectedAccount();
        if (account is null)
        {
            return;
        }

        var result = await SetDefaultAccountUseCase.ExecuteAsync(new SetDefaultAccountRequest
        {
            AccountId = account.AccountId
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to set default account", result.Error.Message));
            return;
        }

        SelectedAccountId = account.AccountId.ToString();
        await RefreshAccountsAsync().ConfigureAwait(false);
    }

    private async Task DeleteSelectedAccountAsync()
    {
        var account = GetSelectedAccount();
        if (account is null)
        {
            return;
        }

        using var confirm = new MessageDialog(
            this,
            DialogFlags.Modal,
            MessageType.Question,
            ButtonsType.YesNo,
            $"Delete account '{account.Username}'?")
        {
            Title = "Delete account"
        };

        if ((ResponseType)confirm.Run() != ResponseType.Yes)
        {
            return;
        }

        var result = await RemoveAccountUseCase.ExecuteAsync(new RemoveAccountRequest
        {
            AccountId = account.AccountId
        }).ConfigureAwait(false);

        if (result.IsFailure)
        {
            Gtk.Application.Invoke((_, _) => ShowError("Unable to delete account", result.Error.Message));
            return;
        }

        SelectedAccountId = null;
        await RefreshAccountsAsync().ConfigureAwait(false);
    }

    private void OpenSkinCustomization()
    {
        var account = GetSelectedAccount();
        if (account is null)
        {
            return;
        }

        if (account.Provider != AccountProvider.Offline)
        {
            ShowInfo("Not supported yet", "Skin customization is currently available for offline accounts only.");
            return;
        }

        SkinCustomizationWindow.PresentForAccount(this, account);
    }

    private void RestoreSelection()
    {
        if (SelectedAccountId is null && Accounts.Count > 0)
        {
            SelectedAccountId = Accounts[0].Account.AccountId.ToString();
        }

        foreach (var row in AccountList.Children.OfType<ListBoxRow>())
        {
            if (string.Equals(row.Name, SelectedAccountId, StringComparison.Ordinal))
            {
                AccountList.SelectRow(row);
                return;
            }
        }
    }

    private LauncherAccount? GetSelectedAccount()
    {
        var selectedId = AccountList.SelectedRow?.Name ?? SelectedAccountId;
        return Accounts.FirstOrDefault(item => string.Equals(item.Account.AccountId.ToString(), selectedId, StringComparison.Ordinal))?.Account;
    }

    private void UpdateButtons()
    {
        var account = GetSelectedAccount();
        var hasSelection = account is not null;
        SetDefaultButton.Sensitive = hasSelection;
        DeleteButton.Sensitive = hasSelection;
        ChangeSkinButton.Sensitive = hasSelection && account?.Provider == AccountProvider.Offline;
    }

    private void ShowInfo(string title, string message)
    {
        using var dialog = new MessageDialog(
            this,
            DialogFlags.Modal,
            MessageType.Info,
            ButtonsType.Ok,
            message)
        {
            Title = title
        };

        dialog.Run();
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

    private sealed record AccountRowModel(LauncherAccount Account, string? SkinPath);
}
