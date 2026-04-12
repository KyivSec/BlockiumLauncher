using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Utilities;

internal static class LauncherGtkChrome
{
    public static HeaderBar CreateHeaderBar(string titleText, string subtitleText, bool allowWindowControls, bool showCloseButton = true)
    {
        var bar = new HeaderBar
        {
            ShowCloseButton = showCloseButton,
            HasSubtitle = false,
            DecorationLayout = allowWindowControls ? ":minimize,maximize,close" : ":close"
        };
        bar.StyleContext.AddClass("topbar-shell");

        var title = new Label(titleText)
        {
            Xalign = 0,
            Ellipsize = Pango.EllipsizeMode.End
        };
        title.StyleContext.AddClass("settings-title");

        var titleHost = new Box(Orientation.Horizontal, 0)
        {
            Halign = Align.Start,
            Valign = Align.Center,
            Hexpand = !allowWindowControls
        };
        titleHost.StyleContext.AddClass("topbar-content");
        titleHost.PackStart(title, false, false, 0);

        if (allowWindowControls)
        {
            bar.PackStart(titleHost);
        }
        else
        {
            bar.CustomTitle = titleHost;
        }

        return bar;
    }

    private static HeaderBar CreateDialogHeaderBar(string titleText, string subtitleText, bool showCloseButton = true)
    {
        var bar = new HeaderBar
        {
            ShowCloseButton = showCloseButton,
            HasSubtitle = false,
            DecorationLayout = ":close"
        };
        bar.StyleContext.AddClass("topbar-shell");
        bar.StyleContext.AddClass("dialog-topbar-shell");

        var title = new Label(titleText)
        {
            Xalign = 0,
            Ellipsize = Pango.EllipsizeMode.End
        };
        title.StyleContext.AddClass("settings-title");

        var titleHost = new Box(Orientation.Horizontal, 0)
        {
            Halign = Align.Fill,
            Valign = Align.Center,
            Hexpand = true
        };
        titleHost.StyleContext.AddClass("topbar-content");
        titleHost.PackStart(title, false, false, 0);

        bar.CustomTitle = titleHost;
        return bar;
    }

    public static Dialog CreateFormDialog(Gtk.Window owner, string titleText, string subtitleText, bool resizable = false, int width = 420)
    {
        var dialog = CreateDecoratedDialog(owner, titleText, subtitleText, width, modal: true, resizable: resizable, showCloseButton: true);
        dialog.SkipTaskbarHint = true;
        return dialog;
    }

    public static Dialog CreateProgressDialog(Gtk.Window owner, string titleText, string subtitleText, int width = 430, System.Action? onCloseRequested = null)
    {
        return CreateDecoratedDialog(owner, titleText, subtitleText, width, modal: true, resizable: false, showCloseButton: true, deletable: true, onCloseRequested: onCloseRequested);
    }

    public static void ShowMessage(Gtk.Window? owner, string title, string message, MessageType messageType)
    {
        using var dialog = CreateDecoratedDialog(owner, title, string.Empty, 430, modal: true, resizable: false, showCloseButton: true);

        var content = CreateDialogContentRoot(dialog);
        var shell = CreateDialogBody(out var body);

        var messageLabel = new Label(message)
        {
            Xalign = 0,
            Wrap = true,
            Selectable = true
        };
        messageLabel.StyleContext.AddClass("settings-help");

        var footer = CreateFooter();
        var okButton = new Button("OK");
        okButton.StyleContext.AddClass(messageType == MessageType.Error ? "danger-button" : "primary-button");
        okButton.Clicked += (_, _) => dialog.Respond(ResponseType.Ok);

        footer.PackStart(okButton, false, false, 0);
        body.PackStart(messageLabel, false, false, 0);
        body.PackStart(footer, false, false, 0);
        content.PackStart(shell, true, true, 0);

        dialog.ShowAll();
        dialog.Run();
    }

    public static bool Confirm(Gtk.Window owner, string title, string message, string confirmText = "Confirm", bool danger = false)
    {
        using var dialog = CreateDecoratedDialog(owner, title, string.Empty, 460, modal: true, resizable: false, showCloseButton: true);

        var content = CreateDialogContentRoot(dialog);
        var shell = CreateDialogBody(out var body);

        var messageLabel = new Label(message)
        {
            Xalign = 0,
            Wrap = true,
            Selectable = true
        };
        messageLabel.StyleContext.AddClass("settings-help");

        var footer = CreateFooter();

        var cancelButton = new Button("Cancel");
        cancelButton.StyleContext.AddClass("action-button");
        cancelButton.Clicked += (_, _) => dialog.Respond(ResponseType.Cancel);

        var confirmButton = new Button(confirmText);
        confirmButton.StyleContext.AddClass(danger ? "danger-button" : "primary-button");
        confirmButton.Clicked += (_, _) => dialog.Respond(ResponseType.Ok);

        footer.PackStart(cancelButton, false, false, 0);
        footer.PackStart(confirmButton, false, false, 0);
        body.PackStart(messageLabel, false, false, 0);
        body.PackStart(footer, false, false, 0);
        content.PackStart(shell, true, true, 0);

        dialog.ShowAll();
        return (ResponseType)dialog.Run() == ResponseType.Ok;
    }

    public static Dialog CreateBusyDialog(Gtk.Window owner, string title, string message, int width = 380)
    {
        var dialog = CreateProgressDialog(owner, title, string.Empty, width);

        var content = CreateDialogContentRoot(dialog);
        var shell = CreateDialogBody(out var body);

        var messageLabel = new Label(message)
        {
            Xalign = 0,
            Wrap = true
        };
        messageLabel.StyleContext.AddClass("settings-help");

        var spinner = new Spinner
        {
            Halign = Align.Start
        };
        spinner.Start();

        body.PackStart(messageLabel, false, false, 0);
        body.PackStart(spinner, false, false, 0);
        content.PackStart(shell, true, true, 0);

        dialog.ShowAll();
        return dialog;
    }

    public static void ShowCrashDialog(Gtk.Window? owner, string title, Exception exception)
    {
        using var dialog = CreateDecoratedDialog(owner, title, "The launcher hit an unexpected error, but it is trying to keep running.", 760, modal: true, resizable: true, showCloseButton: true);
        dialog.SetDefaultSize(760, 480);

        var content = CreateDialogContentRoot(dialog);
        var shell = CreateDialogBody(out var body);

        var summary = new Label(exception.Message)
        {
            Xalign = 0,
            Wrap = true,
            Selectable = true
        };
        summary.StyleContext.AddClass("settings-help");

        var detailsView = new TextView
        {
            Editable = false,
            CursorVisible = false,
            WrapMode = WrapMode.WordChar,
            Monospace = true
        };
        detailsView.Buffer.Text = exception.ToString();
        detailsView.StyleContext.AddClass("launcher-crash-text");

        var scroller = new ScrolledWindow
        {
            Hexpand = true,
            Vexpand = true,
            MinContentHeight = 240
        };
        scroller.StyleContext.AddClass("launcher-crash-scroller");
        scroller.Add(detailsView);

        var footer = CreateFooter();
        var closeButton = new Button("Close");
        closeButton.StyleContext.AddClass("primary-button");
        closeButton.Clicked += (_, _) => dialog.Respond(ResponseType.Close);
        footer.PackStart(closeButton, false, false, 0);

        body.PackStart(summary, false, false, 0);
        body.PackStart(scroller, true, true, 0);
        body.PackStart(footer, false, false, 0);
        content.PackStart(shell, true, true, 0);

        dialog.ShowAll();
        dialog.Run();
    }

    private static Dialog CreateDecoratedDialog(Gtk.Window? owner, string titleText, string subtitleText, int width, bool modal, bool resizable, bool showCloseButton = true, bool deletable = true, System.Action? onCloseRequested = null)
    {
        var dialog = owner is null
            ? new Dialog()
            : new Dialog(string.Empty, owner, DialogFlags.DestroyWithParent);

        dialog.Modal = modal;
        dialog.Resizable = resizable;
        dialog.Title = titleText;
        dialog.WindowPosition = owner is null ? WindowPosition.Center : WindowPosition.CenterOnParent;
        dialog.TypeHint = Gdk.WindowTypeHint.Dialog;
        dialog.Titlebar = CreateDialogHeaderBar(titleText, subtitleText, showCloseButton: showCloseButton);
        dialog.SetDefaultSize(width, 0);
        dialog.ContentArea.BorderWidth = 0;
        dialog.ContentArea.Spacing = 0;
#pragma warning disable CS0612
        dialog.ActionArea.NoShowAll = true;
        dialog.ActionArea.Hide();
#pragma warning restore CS0612
        dialog.SkipTaskbarHint = owner is not null;
        dialog.DestroyWithParent = owner is not null;
        dialog.Deletable = deletable;
        if (onCloseRequested is not null)
        {
            dialog.DeleteEvent += (_, args) =>
            {
                args.RetVal = true;
                onCloseRequested();
            };
        }

        return dialog;
    }

    private static Box CreateDialogContentRoot(Dialog dialog)
    {
        var root = new Box(Orientation.Vertical, 0);
        root.StyleContext.AddClass("launcher-window-root");
        dialog.ContentArea.PackStart(root, true, true, 0);
        return root;
    }

    private static EventBox CreateDialogBody(out Box body)
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("launcher-section-shell");

        body = new Box(Orientation.Vertical, 10)
        {
            MarginTop = 12,
            MarginBottom = 12,
            MarginStart = 12,
            MarginEnd = 12
        };
        shell.Add(body);
        return shell;
    }

    private static Box CreateFooter()
    {
        var footer = new Box(Orientation.Horizontal, 8)
        {
            Halign = Align.End
        };
        footer.StyleContext.AddClass("launcher-dialog-footer");
        return footer;
    }
}
