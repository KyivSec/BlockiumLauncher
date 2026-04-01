using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using Gdk;
using Gtk;
using HtmlAgilityPack;

namespace BlockiumLauncher.UI.GtkSharp.Widgets;

internal sealed class CatalogDescriptionView : ScrolledWindow
{
    private static readonly HttpClient ImageHttpClient = CreateImageHttpClient();

    private readonly Box content = new(Orientation.Vertical, 10)
    {
        MarginTop = 10,
        MarginBottom = 10,
        MarginStart = 10,
        MarginEnd = 10
    };

    public CatalogDescriptionView()
    {
        HscrollbarPolicy = PolicyType.Never;
        VscrollbarPolicy = PolicyType.Automatic;
        Hexpand = true;
        Vexpand = true;

        var viewport = new Viewport(null, null);
        viewport.ShadowType = ShadowType.None;
        viewport.Add(content);
        Add(viewport);
    }

    public void SetContent(string contentText, CatalogDescriptionFormat format)
    {
        foreach (var child in content.Children.ToArray())
        {
            content.Remove(child);
            child.Destroy();
        }

        if (string.IsNullOrWhiteSpace(contentText))
        {
            content.PackStart(CreateParagraphLabel("No description is available for this project yet."), false, false, 0);
            ShowAll();
            return;
        }

        switch (format)
        {
            case CatalogDescriptionFormat.Html:
                RenderHtmlContent(contentText);
                break;
            case CatalogDescriptionFormat.Markdown:
                RenderMarkdownContent(contentText);
                break;
            default:
                RenderPlainTextContent(contentText);
                break;
        }

        ShowAll();
    }

    private void RenderPlainTextContent(string contentText)
    {
        foreach (var block in ParsePlainTextBlocks(contentText))
        {
            content.PackStart(CreateParagraphLabel(block.Text), false, false, 0);
        }
    }

    private void RenderMarkdownContent(string contentText)
    {
        foreach (var block in ParseMarkdownBlocks(contentText))
        {
            content.PackStart(CreateMarkdownBlock(block), false, false, 0);
        }
    }

    private void RenderHtmlContent(string contentText)
    {
        var document = new HtmlDocument
        {
            OptionFixNestedTags = true
        };
        document.LoadHtml(contentText);

        var root = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var renderedAny = false;
        foreach (var child in root.ChildNodes)
        {
            renderedAny |= AppendHtmlNode(content, child, 0);
        }

        if (!renderedAny)
        {
            content.PackStart(CreateParagraphLabel(HtmlEntity.DeEntitize(root.InnerText).Trim()), false, false, 0);
        }
    }

    private bool AppendHtmlNode(Box parent, HtmlNode node, int listDepth)
    {
        if (node.NodeType == HtmlNodeType.Comment)
        {
            return false;
        }

        if (node.NodeType == HtmlNodeType.Text)
        {
            var text = HtmlEntity.DeEntitize(node.InnerText);
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            parent.PackStart(CreateParagraphLabel(text.Trim()), false, false, 0);
            return true;
        }

        if (IsIgnoredNode(node.Name))
        {
            return false;
        }

        switch (node.Name.ToLowerInvariant())
        {
            case "body":
            case "main":
            case "article":
            case "section":
            case "div":
                return AppendContainerNode(parent, node, listDepth);
            case "p":
                parent.PackStart(CreateMarkupLabel(BuildInlineMarkup(node)), false, false, 0);
                return true;
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                parent.PackStart(CreateHeading(node.InnerText, GetHeadingLevel(node.Name)), false, false, 0);
                return true;
            case "ul":
            case "ol":
                parent.PackStart(CreateList(node, ordered: node.Name.Equals("ol", StringComparison.OrdinalIgnoreCase), listDepth), false, false, 0);
                return true;
            case "blockquote":
                parent.PackStart(CreateBlockQuote(node), false, false, 0);
                return true;
            case "pre":
                parent.PackStart(CreateCodeBlock(node.InnerText), false, false, 0);
                return true;
            case "table":
                parent.PackStart(CreateTable(node), false, false, 0);
                return true;
            case "hr":
                parent.PackStart(new Separator(Orientation.Horizontal), false, false, 4);
                return true;
            case "img":
                parent.PackStart(CreateImageWidget(node), false, false, 0);
                return true;
            default:
                if (HasBlockChildren(node))
                {
                    return AppendContainerNode(parent, node, listDepth);
                }

                parent.PackStart(CreateMarkupLabel(BuildInlineMarkup(node)), false, false, 0);
                return true;
        }
    }

    private bool AppendContainerNode(Box parent, HtmlNode node, int listDepth)
    {
        var renderedAny = false;
        foreach (var child in node.ChildNodes)
        {
            renderedAny |= AppendHtmlNode(parent, child, listDepth);
        }

        if (!renderedAny)
        {
            var markup = BuildInlineMarkup(node);
            if (!string.IsNullOrWhiteSpace(markup))
            {
                parent.PackStart(CreateMarkupLabel(markup), false, false, 0);
                return true;
            }
        }

        return renderedAny;
    }

    private Widget CreateHeading(string text, int level)
    {
        var size = level switch
        {
            1 => "x-large",
            2 => "large",
            3 => "medium",
            _ => "small"
        };

        var label = new Label
        {
            UseMarkup = true,
            Markup = $"<span size=\"{size}\" weight=\"bold\">{GLib.Markup.EscapeText(HtmlEntity.DeEntitize(text.Trim()))}</span>",
            Xalign = 0,
            Wrap = true
        };
        label.StyleContext.AddClass("catalog-description-heading");
        return label;
    }

    private Widget CreateList(HtmlNode listNode, bool ordered, int listDepth)
    {
        var box = new Box(Orientation.Vertical, 6);
        var index = 1;
        foreach (var item in listNode.Elements("li"))
        {
            var row = new Box(Orientation.Horizontal, 8);
            var bullet = new Label(ordered ? $"{index}." : "•")
            {
                Xalign = 0,
                Valign = Align.Start
            };
            bullet.StyleContext.AddClass("catalog-description-text");

            var itemBox = new Box(Orientation.Vertical, 4)
            {
                Hexpand = true
            };

            var inlineMarkup = BuildInlineMarkup(item);
            if (!string.IsNullOrWhiteSpace(inlineMarkup))
            {
                itemBox.PackStart(CreateMarkupLabel(inlineMarkup), false, false, 0);
            }

            foreach (var child in item.ChildNodes.Where(static child => IsBlockListChild(child.Name)))
            {
                AppendHtmlNode(itemBox, child, listDepth + 1);
            }

            row.PackStart(bullet, false, false, 0);
            row.PackStart(itemBox, true, true, 0);
            box.PackStart(row, false, false, 0);
            index++;
        }

        return box;
    }

    private Widget CreateBlockQuote(HtmlNode node)
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("catalog-description-code-shell");

        var inner = new Box(Orientation.Vertical, 8)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 12,
            MarginEnd = 12
        };

        foreach (var child in node.ChildNodes)
        {
            if (!AppendHtmlNode(inner, child, 0))
            {
                var markup = BuildInlineMarkup(node);
                if (!string.IsNullOrWhiteSpace(markup))
                {
                    inner.PackStart(CreateMarkupLabel(markup), false, false, 0);
                }

                break;
            }
        }

        shell.Add(inner);
        return shell;
    }

    private Widget CreateCodeBlock(string code)
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("catalog-description-code-shell");

        var label = new Label(HtmlEntity.DeEntitize(code.Trim()))
        {
            Xalign = 0,
            Wrap = true,
            Selectable = true
        };
        label.StyleContext.AddClass("catalog-description-code");

        var box = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 10,
            MarginEnd = 10
        };
        box.PackStart(label, false, false, 0);
        shell.Add(box);
        return shell;
    }

    private Widget CreateTable(HtmlNode tableNode)
    {
        var outer = new EventBox();
        outer.StyleContext.AddClass("catalog-description-code-shell");

        var grid = new Grid
        {
            RowSpacing = 0,
            ColumnSpacing = 0,
            MarginTop = 8,
            MarginBottom = 8,
            MarginStart = 8,
            MarginEnd = 8
        };

        IEnumerable<HtmlNode> rows = tableNode.SelectNodes("./thead/tr|./tbody/tr|./tr")?.Cast<HtmlNode>()
            ?? tableNode.Descendants("tr");
        var rowIndex = 0;
        foreach (var rowNode in rows)
        {
            var cellIndex = 0;
            foreach (var cellNode in rowNode.ChildNodes.Where(static node => node.Name is "th" or "td"))
            {
                var label = CreateMarkupLabel(BuildInlineMarkup(cellNode));
                label.StyleContext.AddClass("catalog-description-text");

                var shell = new EventBox();
                shell.StyleContext.AddClass("asset-thumb-shell");
                var contentBox = new Box(Orientation.Vertical, 0)
                {
                    MarginTop = 8,
                    MarginBottom = 8,
                    MarginStart = 10,
                    MarginEnd = 10
                };
                contentBox.PackStart(label, false, false, 0);
                shell.Add(contentBox);

                grid.Attach(shell, cellIndex, rowIndex, 1, 1);
                cellIndex++;
            }

            rowIndex++;
        }

        outer.Add(grid);
        return outer;
    }

    private Widget CreateImageWidget(HtmlNode node)
    {
        var shell = new EventBox();
        shell.StyleContext.AddClass("asset-thumb-shell");

        var placeholder = new Label(node.GetAttributeValue("alt", "Image"))
        {
            Xalign = 0.5f,
            Yalign = 0.5f,
            Wrap = true
        };
        placeholder.StyleContext.AddClass("catalog-description-text");

        var container = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 8,
            MarginBottom = 8,
            MarginStart = 8,
            MarginEnd = 8
        };
        container.PackStart(placeholder, false, false, 0);
        shell.Add(container);

        var source = node.GetAttributeValue("src", string.Empty);
        if (!string.IsNullOrWhiteSpace(source))
        {
            _ = LoadImageAsync(container, placeholder, source);
        }

        return shell;
    }

    private async Task LoadImageAsync(Box container, Widget placeholder, string source)
    {
        try
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri))
            {
                return;
            }

            var bytes = await ImageHttpClient.GetByteArrayAsync(uri).ConfigureAwait(false);
            using var loader = new PixbufLoader();
            loader.Write(bytes);
            loader.Close();

            var pixbuf = loader.Pixbuf;
            if (pixbuf is null)
            {
                return;
            }

            var scaled = pixbuf.Width > 720
                ? pixbuf.ScaleSimple(720, Math.Max(1, pixbuf.Height * 720 / pixbuf.Width), InterpType.Bilinear)
                : pixbuf;

            Gtk.Application.Invoke((_, _) =>
            {
                container.Remove(placeholder);
                placeholder.Destroy();
                container.PackStart(new Image(scaled)
                {
                    Halign = Align.Start,
                    Valign = Align.Start
                }, false, false, 0);
                container.ShowAll();
            });
        }
        catch
        {
        }
    }

    private Label CreateMarkupLabel(string markup)
    {
        var label = new Label
        {
            UseMarkup = true,
            Markup = markup,
            Xalign = 0,
            Wrap = true,
            Selectable = true
        };
        label.StyleContext.AddClass("catalog-description-text");
        return label;
    }

    private Label CreateParagraphLabel(string text)
    {
        var label = new Label(HtmlEntity.DeEntitize(text))
        {
            Xalign = 0,
            Wrap = true,
            Selectable = true
        };
        label.StyleContext.AddClass("catalog-description-text");
        return label;
    }

    private static bool HasBlockChildren(HtmlNode node)
    {
        return node.ChildNodes.Any(child => IsBlockNode(child.Name));
    }

    private static bool IsIgnoredNode(string name)
    {
        return name.Equals("script", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("style", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("iframe", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("object", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("embed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBlockNode(string name)
    {
        return name is "div" or "p" or "section" or "article" or "ul" or "ol" or "li" or "table" or "blockquote" or "pre" or "hr" or "img" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6";
    }

    private static bool IsBlockListChild(string name)
    {
        return name is "ul" or "ol" or "div" or "p" or "blockquote" or "pre" or "table";
    }

    private static int GetHeadingLevel(string name)
    {
        return int.TryParse(name.AsSpan(1), out var level) ? level : 2;
    }

    private static string BuildInlineMarkup(HtmlNode node)
    {
        var builder = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            AppendInlineMarkup(builder, child);
        }

        var markup = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(markup)
            ? GLib.Markup.EscapeText(HtmlEntity.DeEntitize(node.InnerText.Trim()))
            : markup;
    }

    private static void AppendInlineMarkup(StringBuilder builder, HtmlNode node)
    {
        switch (node.NodeType)
        {
            case HtmlNodeType.Text:
                builder.Append(GLib.Markup.EscapeText(HtmlEntity.DeEntitize(node.InnerText)));
                return;
            case HtmlNodeType.Comment:
                return;
        }

        var name = node.Name.ToLowerInvariant();
        if (IsIgnoredNode(name))
        {
            return;
        }

        if (name == "br")
        {
            builder.Append('\n');
            return;
        }

        if (name == "img")
        {
            var alt = node.GetAttributeValue("alt", "image");
            builder.Append($"[Image: {GLib.Markup.EscapeText(alt)}]");
            return;
        }

        var inner = new StringBuilder();
        foreach (var child in node.ChildNodes)
        {
            AppendInlineMarkup(inner, child);
        }

        var text = inner.ToString();
        switch (name)
        {
            case "strong":
            case "b":
                builder.Append($"<b>{text}</b>");
                break;
            case "em":
            case "i":
                builder.Append($"<i>{text}</i>");
                break;
            case "u":
                builder.Append($"<u>{text}</u>");
                break;
            case "code":
            case "tt":
                builder.Append($"<span font_family=\"monospace\">{text}</span>");
                break;
            case "a":
                var href = node.GetAttributeValue("href", string.Empty);
                if (string.IsNullOrWhiteSpace(href))
                {
                    builder.Append(text);
                }
                else
                {
                    builder.Append($"<a href=\"{GLib.Markup.EscapeText(href)}\">{text}</a>");
                }
                break;
            case "span":
            case "small":
            case "sup":
            case "sub":
            default:
                builder.Append(text);
                break;
        }
    }

    private static IReadOnlyList<RenderBlock> ParsePlainTextBlocks(string contentText)
    {
        return contentText
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => new RenderBlock(RenderBlockKind.Paragraph, WebUtility.HtmlDecode(text)))
            .ToList();
    }

    private static IReadOnlyList<RenderBlock> ParseMarkdownBlocks(string contentText)
    {
        var blocks = new List<RenderBlock>();
        var lines = contentText.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
        var paragraph = new List<string>();
        var listItems = new List<string>();
        var codeLines = new List<string>();
        var inCodeBlock = false;

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            blocks.Add(new RenderBlock(RenderBlockKind.Paragraph, string.Join(" ", paragraph).Trim()));
            paragraph.Clear();
        }

        void FlushList()
        {
            if (listItems.Count == 0)
            {
                return;
            }

            blocks.Add(new RenderBlock(RenderBlockKind.List, string.Empty, Items: listItems.ToArray()));
            listItems.Clear();
        }

        void FlushCode()
        {
            if (codeLines.Count == 0)
            {
                return;
            }

            blocks.Add(new RenderBlock(RenderBlockKind.Code, string.Join("\n", codeLines)));
            codeLines.Clear();
        }

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();

            if (line.StartsWith("```", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushList();
                if (inCodeBlock)
                {
                    FlushCode();
                }

                inCodeBlock = !inCodeBlock;
                continue;
            }

            if (inCodeBlock)
            {
                codeLines.Add(line);
                continue;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                FlushList();
                continue;
            }

            if (line.StartsWith("#", StringComparison.Ordinal))
            {
                FlushParagraph();
                FlushList();
                var level = line.TakeWhile(static ch => ch == '#').Count();
                blocks.Add(new RenderBlock(RenderBlockKind.Heading, line[level..].Trim(), Level: Math.Clamp(level, 1, 3)));
                continue;
            }

            if (Regex.IsMatch(line, @"^\s*[-*+]\s+"))
            {
                FlushParagraph();
                listItems.Add(Regex.Replace(line, @"^\s*[-*+]\s+", string.Empty));
                continue;
            }

            paragraph.Add(line.Trim());
        }

        FlushParagraph();
        FlushList();
        FlushCode();

        if (blocks.Count == 0)
        {
            blocks.Add(new RenderBlock(RenderBlockKind.Paragraph, WebUtility.HtmlDecode(contentText.Trim())));
        }

        return blocks;
    }

    private Widget CreateMarkdownBlock(RenderBlock block)
    {
        return block.Kind switch
        {
            RenderBlockKind.Heading => CreateHeading(block.Text, block.Level),
            RenderBlockKind.List => CreateMarkdownList(block),
            RenderBlockKind.Code => CreateCodeBlock(block.Text),
            _ => CreateMarkupLabel(ApplyMarkdownInlineFormatting(block.Text))
        };
    }

    private Widget CreateMarkdownList(RenderBlock block)
    {
        var box = new Box(Orientation.Vertical, 4);
        foreach (var item in block.Items)
        {
            box.PackStart(CreateMarkupLabel($"• {ApplyMarkdownInlineFormatting(item)}"), false, false, 0);
        }

        return box;
    }

    private static string ApplyMarkdownInlineFormatting(string text)
    {
        var normalized = WebUtility.HtmlDecode(text);
        normalized = Regex.Replace(normalized, @"`([^`]+)`", "<span font_family=\"monospace\">$1</span>");
        normalized = Regex.Replace(normalized, @"\*\*([^\*]+)\*\*", "<b>$1</b>");
        normalized = Regex.Replace(normalized, @"\*([^\*]+)\*", "<i>$1</i>");
        normalized = Regex.Replace(normalized, @"\[(.+?)\]\((.+?)\)", "<a href=\"$2\">$1</a>");
        return GLib.Markup.EscapeText(normalized)
            .Replace("&lt;b&gt;", "<b>", StringComparison.Ordinal)
            .Replace("&lt;/b&gt;", "</b>", StringComparison.Ordinal)
            .Replace("&lt;i&gt;", "<i>", StringComparison.Ordinal)
            .Replace("&lt;/i&gt;", "</i>", StringComparison.Ordinal)
            .Replace("&lt;a href=&quot;", "<a href=\"", StringComparison.Ordinal)
            .Replace("&quot;&gt;", "\">", StringComparison.Ordinal)
            .Replace("&lt;/a&gt;", "</a>", StringComparison.Ordinal)
            .Replace("&lt;span font_family=&quot;monospace&quot;&gt;", "<span font_family=\"monospace\">", StringComparison.Ordinal)
            .Replace("&lt;/span&gt;", "</span>", StringComparison.Ordinal);
    }

    private enum RenderBlockKind
    {
        Paragraph,
        Heading,
        List,
        Code
    }

    private sealed record RenderBlock(RenderBlockKind Kind, string Text, int Level = 0, IReadOnlyList<string>? Items = null)
    {
        public IReadOnlyList<string> Items { get; init; } = Items ?? [];
    }

    private static HttpClient CreateImageHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlockiumLauncher/0.1");
        return httpClient;
    }
}
