using BlockiumLauncher.Application.UseCases.Common;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Utilities;

internal static class LauncherStructuredList
{
    internal sealed record CellDefinition(Widget Child, bool Expand = false, int WidthRequest = -1, bool ShowTrailingDivider = false);

    public static Widget CreateHeaderRow(params CellDefinition[] cells)
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("add-instance-list-header");
        shell.Add(CreateRowContent(cells));
        return shell;
    }

    public static Widget CreateRowContent(params CellDefinition[] cells)
    {
        var content = new Box(Orientation.Horizontal, 0);
        content.StyleContext.AddClass("add-instance-item-shell");
        for (var index = 0; index < cells.Length; index++)
        {
            var cell = cells[index];
            content.PackStart(WrapCell(cell.Child, cell.Expand, cell.WidthRequest, cell.ShowTrailingDivider), cell.Expand, cell.Expand, 0);
        }

        return content;
    }

    public static Label CreateHeaderLabel(string text)
    {
        var label = new Label(text)
        {
            Xalign = 0
        };
        label.StyleContext.AddClass("add-instance-header-label");
        return label;
    }

    public static Label CreateCellLabel(string text, bool selectable = false, bool wrap = false)
    {
        return new Label(text)
        {
            Xalign = 0,
            Selectable = selectable,
            Wrap = wrap,
            Ellipsize = wrap ? Pango.EllipsizeMode.None : Pango.EllipsizeMode.End
        };
    }

    public static string BuildSimpleCatalogFileLabel(CatalogFileSummary file)
    {
        if (!string.IsNullOrWhiteSpace(file.DisplayName))
        {
            return file.DisplayName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(file.FileName))
        {
            return file.FileName.Trim();
        }

        return file.FileId;
    }

    public static Widget BuildCatalogFileSelectionPopover(
        string titleText,
        IReadOnlyList<CatalogFileSummary> files,
        string? selectedFileId,
        Func<CatalogFileSummary, string> labelBuilder,
        Action<CatalogFileSummary> onSelected)
    {
        var outer = new Box(Orientation.Vertical, 10);
        outer.StyleContext.AddClass("popover-content");

        var title = new Label(titleText)
        {
            Xalign = 0
        };
        title.StyleContext.AddClass("popover-title");
        outer.PackStart(title, false, false, 0);

        if (files.Count == 0)
        {
            var empty = new Label("No versions available.")
            {
                Xalign = 0,
                Wrap = true
            };
            empty.StyleContext.AddClass("settings-caption");
            outer.PackStart(empty, false, false, 0);
            return outer;
        }

        var content = new Box(Orientation.Vertical, 6);
        RadioButton? group = null;
        foreach (var file in files)
        {
            var button = group is null
                ? new RadioButton(labelBuilder(file))
                : new RadioButton(group, labelBuilder(file));
            group ??= button;
            button.Active = string.Equals(file.FileId, selectedFileId, StringComparison.Ordinal);
            button.StyleContext.AddClass("popover-check");
            button.Toggled += (_, _) =>
            {
                if (!button.Active)
                {
                    return;
                }

                onSelected(file);
            };
            content.PackStart(button, false, false, 0);
        }

        var visibleItems = Math.Clamp(files.Count, 1, 8);
        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            WidthRequest = 360,
            HeightRequest = 18 + (visibleItems * 34)
        };
        scroller.Add(content);
        outer.PackStart(scroller, true, true, 0);
        return outer;
    }

    private static Widget WrapCell(Widget child, bool expand, int widthRequest, bool showTrailingDivider = false)
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
            MarginStart = 10,
            MarginEnd = 10,
            Hexpand = expand
        };
        box.PackStart(child, true, true, 0);
        shell.Add(box);
        return shell;
    }
}
