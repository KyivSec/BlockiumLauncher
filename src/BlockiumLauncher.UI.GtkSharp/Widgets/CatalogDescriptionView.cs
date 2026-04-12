using System.Net;
using System.Text;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.UI.GtkSharp.Services;
using Gdk;
using Gtk;
using HtmlAgilityPack;
using Markdig;

namespace BlockiumLauncher.UI.GtkSharp.Widgets;

internal sealed class CatalogDescriptionView : ScrolledWindow
{
    private const int ImageLoadViewportMargin = 220;
    private const int MaximumConcurrentImageLoads = 2;
    private const int MaximumStructuredHtmlLength = 120_000;
    private const int MaximumStructuredHtmlNodeCount = 2_500;
    private const int MaximumStructuredImageCount = 20;
    private const int MaximumRenderedBlocks = 320;

    private static readonly MarkdownPipeline MarkdownPipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    private readonly ProviderMediaCacheService mediaCacheService;
    private readonly Viewport viewport;
    private readonly Box content = new(Orientation.Vertical, 10)
    {
        MarginTop = 10,
        MarginBottom = 10,
        MarginStart = 10,
        MarginEnd = 10
    };
    private readonly List<DescriptionImageRequest> imageRequests = [];

    private CancellationTokenSource? contentLoadCancellationSource;
    private Uri? contentBaseUri;
    private int contentGeneration;
    private int activeImageLoads;
    private bool imageLoadPassQueued;
    private int renderedBlockCount;
    private bool blockLimitReached;

    public CatalogDescriptionView(ProviderMediaCacheService mediaCacheService)
    {
        this.mediaCacheService = mediaCacheService ?? throw new ArgumentNullException(nameof(mediaCacheService));

        HscrollbarPolicy = PolicyType.Never;
        VscrollbarPolicy = PolicyType.Automatic;
        Hexpand = true;
        Vexpand = true;

        viewport = new Viewport(null, null)
        {
            ShadowType = ShadowType.None
        };
        viewport.Add(content);
        Add(viewport);

        Vadjustment.ValueChanged += (_, _) => QueueVisibleImageLoads();
        SizeAllocated += (_, _) => QueueVisibleImageLoads();
        Destroyed += (_, _) => Unload();
    }

    public void SetContent(string contentText, CatalogDescriptionFormat format, string? baseUrl = null)
    {
        Unload();

        contentLoadCancellationSource = new CancellationTokenSource();
        contentBaseUri = Uri.TryCreate(baseUrl, UriKind.Absolute, out var resolvedBaseUri)
            ? resolvedBaseUri
            : null;

        if (string.IsNullOrWhiteSpace(contentText))
        {
            content.PackStart(CreateParagraphLabel("No description is available for this project yet."), false, false, 0);
            ShowAll();
            return;
        }

        RenderPlainTextContent(ConvertDescriptionToPlainText(contentText, format));

        ShowAll();
    }

    public void Unload()
    {
        contentGeneration++;
        CancelPendingImageLoads();
        activeImageLoads = 0;
        imageLoadPassQueued = false;
        imageRequests.Clear();
        contentBaseUri = null;
        renderedBlockCount = 0;
        blockLimitReached = false;
        ClearContentChildren();
    }

    private void CancelPendingImageLoads()
    {
        if (contentLoadCancellationSource is null)
        {
            return;
        }

        contentLoadCancellationSource.Cancel();
        contentLoadCancellationSource.Dispose();
        contentLoadCancellationSource = null;
    }

    private void ClearContentChildren()
    {
        foreach (var child in content.Children.ToArray())
        {
            content.Remove(child);
            ClearWidgetTree(child);
        }
    }

    private static void ClearWidgetTree(Widget widget)
    {
        if (widget is Container container)
        {
            foreach (var child in container.Children.ToArray())
            {
                container.Remove(child);
                ClearWidgetTree(child);
            }
        }

        if (widget is Image image)
        {
            image.Clear();
        }

        widget.Destroy();
    }

    private void RenderPlainTextContent(string contentText)
    {
        foreach (var block in ParsePlainTextBlocks(contentText))
        {
            content.PackStart(CreateParagraphLabel(block), false, false, 0);
        }
    }

    private static string ConvertDescriptionToPlainText(string contentText, CatalogDescriptionFormat format)
    {
        return format switch
        {
            CatalogDescriptionFormat.Markdown => ConvertHtmlToPlainText(Markdown.ToHtml(contentText, MarkdownPipeline)),
            CatalogDescriptionFormat.Html => ConvertHtmlToPlainText(contentText),
            _ => contentText
        };
    }

    private void RenderHtmlContent(string contentText)
    {
        if (contentText.Length > MaximumStructuredHtmlLength)
        {
            RenderPlainTextContent(ConvertHtmlToPlainText(contentText));
            AppendTruncatedNotice("Description simplified for performance.");
            return;
        }

        var document = new HtmlDocument
        {
            OptionFixNestedTags = true,
            OptionAutoCloseOnEnd = true
        };
        document.LoadHtml(contentText);

        var root = document.DocumentNode.SelectSingleNode("//body") ?? document.DocumentNode;
        var nodeCount = root.DescendantsAndSelf().Count();
        var imageCount = root
            .Descendants()
            .Count(static node => node.Name.Equals("img", StringComparison.OrdinalIgnoreCase) || node.Name.Equals("picture", StringComparison.OrdinalIgnoreCase));
        if (nodeCount > MaximumStructuredHtmlNodeCount || imageCount > MaximumStructuredImageCount)
        {
            RenderPlainTextContent(HtmlEntity.DeEntitize(root.InnerText));
            AppendTruncatedNotice("Description simplified for performance.");
            return;
        }

        var renderedAny = false;
        foreach (var child in root.ChildNodes)
        {
            if (blockLimitReached)
            {
                break;
            }

            renderedAny |= AppendHtmlNode(content, child, 0);
        }

        if (!renderedAny)
        {
            var fallbackText = HtmlEntity.DeEntitize(root.InnerText).Trim();
            content.PackStart(
                string.IsNullOrWhiteSpace(fallbackText)
                    ? CreateParagraphLabel("No description is available for this project yet.")
                    : CreateParagraphLabel(fallbackText),
                false,
                false,
                0);
        }

        if (blockLimitReached)
        {
            AppendTruncatedNotice("Description truncated for performance.");
        }
    }

    private bool AppendHtmlNode(Box parent, HtmlNode node, int listDepth)
    {
        if (blockLimitReached)
        {
            return false;
        }

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
            TrackRenderedBlock();
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
            case "aside":
                return AppendContainerNode(parent, node, listDepth);
            case "p":
            case "caption":
            case "figcaption":
                parent.PackStart(CreateFlowBlock(node, listDepth), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "h1":
            case "h2":
            case "h3":
            case "h4":
            case "h5":
            case "h6":
                parent.PackStart(CreateHeading(node.InnerText, GetHeadingLevel(node.Name)), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "ul":
            case "ol":
                parent.PackStart(CreateList(node, node.Name.Equals("ol", StringComparison.OrdinalIgnoreCase), listDepth), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "blockquote":
                parent.PackStart(CreateBlockQuote(node, listDepth), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "pre":
                parent.PackStart(CreateCodeBlock(node.InnerText), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "table":
                parent.PackStart(CreateTable(node, listDepth), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "dl":
                parent.PackStart(CreateDefinitionList(node, listDepth), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "figure":
                parent.PackStart(CreateFigure(node, listDepth), false, false, 0);
                TrackRenderedBlock();
                return true;
            case "hr":
                parent.PackStart(new Separator(Orientation.Horizontal), false, false, 4);
                TrackRenderedBlock();
                return true;
            case "img":
            case "picture":
                parent.PackStart(CreateImageWidget(node), false, false, 0);
                TrackRenderedBlock();
                return true;
            default:
                if (HasBlockChildren(node))
                {
                    return AppendContainerNode(parent, node, listDepth);
                }

                var markup = BuildInlineMarkup(node);
                if (string.IsNullOrWhiteSpace(markup))
                {
                    return false;
                }

                parent.PackStart(CreateMarkupLabel(markup), false, false, 0);
                TrackRenderedBlock();
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
                TrackRenderedBlock();
                return true;
            }
        }

        return renderedAny;
    }

    private Widget CreateFlowBlock(HtmlNode node, int listDepth)
    {
        var block = new Box(Orientation.Vertical, 6)
        {
            Hexpand = true
        };

        if (!AppendMixedContent(block, node, listDepth))
        {
            block.PackStart(CreateParagraphLabel(HtmlEntity.DeEntitize(node.InnerText.Trim())), false, false, 0);
        }

        return block;
    }

    private bool AppendMixedContent(Box parent, HtmlNode node, int listDepth)
    {
        var inlineBuilder = new StringBuilder();
        var renderedAny = false;

        void FlushInline()
        {
            var markup = inlineBuilder.ToString().Trim();
            inlineBuilder.Clear();

            if (string.IsNullOrWhiteSpace(markup))
            {
                return;
            }

            parent.PackStart(CreateMarkupLabel(markup), false, false, 0);
            renderedAny = true;
            TrackRenderedBlock();
        }

        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Comment)
            {
                continue;
            }

            if (child.NodeType == HtmlNodeType.Text)
            {
                inlineBuilder.Append(GLib.Markup.EscapeText(HtmlEntity.DeEntitize(child.InnerText)));
                continue;
            }

            var name = child.Name.ToLowerInvariant();
            if (IsIgnoredNode(name))
            {
                continue;
            }

            if (name == "br")
            {
                inlineBuilder.Append('\n');
                continue;
            }

            if (name == "img" || name == "picture")
            {
                FlushInline();
                parent.PackStart(CreateImageWidget(child), false, false, 0);
                renderedAny = true;
                continue;
            }

            if (IsInlineNode(name))
            {
                AppendInlineMarkup(inlineBuilder, child);
                continue;
            }

            FlushInline();
            renderedAny |= AppendHtmlNode(parent, child, listDepth + 1);
        }

        FlushInline();
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
            var bullet = new Label(ordered ? $"{index}." : "\u2022")
            {
                Xalign = 0,
                Valign = Align.Start
            };
            bullet.StyleContext.AddClass("catalog-description-text");

            var itemBox = new Box(Orientation.Vertical, 4)
            {
                Hexpand = true
            };

            if (!AppendMixedContent(itemBox, item, listDepth + 1))
            {
                var fallback = BuildInlineMarkup(item);
                if (!string.IsNullOrWhiteSpace(fallback))
                {
                    itemBox.PackStart(CreateMarkupLabel(fallback), false, false, 0);
                }
            }

            row.PackStart(bullet, false, false, 0);
            row.PackStart(itemBox, true, true, 0);
            box.PackStart(row, false, false, 0);
            index++;
        }

        return box;
    }

    private Widget CreateDefinitionList(HtmlNode listNode, int listDepth)
    {
        var box = new Box(Orientation.Vertical, 8);
        foreach (var child in listNode.ChildNodes.Where(static child => child.NodeType != HtmlNodeType.Comment))
        {
            switch (child.Name.ToLowerInvariant())
            {
                case "dt":
                    var heading = CreateMarkupLabel($"<b>{GLib.Markup.EscapeText(HtmlEntity.DeEntitize(child.InnerText.Trim()))}</b>");
                    box.PackStart(heading, false, false, 0);
                    break;
                case "dd":
                    var row = new Box(Orientation.Vertical, 4)
                    {
                        MarginStart = 16
                    };
                    AppendMixedContent(row, child, listDepth + 1);
                    box.PackStart(row, false, false, 0);
                    break;
                default:
                    AppendHtmlNode(box, child, listDepth + 1);
                    break;
            }
        }

        return box;
    }

    private Widget CreateFigure(HtmlNode node, int listDepth)
    {
        var box = new Box(Orientation.Vertical, 8)
        {
            Hexpand = true
        };

        foreach (var child in node.ChildNodes)
        {
            AppendHtmlNode(box, child, listDepth + 1);
        }

        return box;
    }

    private Widget CreateBlockQuote(HtmlNode node, int listDepth)
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

        if (!AppendMixedContent(inner, node, listDepth + 1))
        {
            var markup = BuildInlineMarkup(node);
            if (!string.IsNullOrWhiteSpace(markup))
            {
                inner.PackStart(CreateMarkupLabel(markup), false, false, 0);
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

    private Widget CreateTable(HtmlNode tableNode, int listDepth)
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
                var shell = new EventBox();
                shell.StyleContext.AddClass("asset-thumb-shell");

                var contentBox = new Box(Orientation.Vertical, 6)
                {
                    MarginTop = 8,
                    MarginBottom = 8,
                    MarginStart = 10,
                    MarginEnd = 10,
                    Hexpand = true
                };

                if (!AppendMixedContent(contentBox, cellNode, listDepth + 1))
                {
                    contentBox.PackStart(CreateMarkupLabel(BuildInlineMarkup(cellNode)), false, false, 0);
                }

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
        shell.StyleContext.AddClass("catalog-description-image-shell");

        var container = new Box(Orientation.Vertical, 0)
        {
            MarginTop = 8,
            MarginBottom = 8,
            MarginStart = 8,
            MarginEnd = 8
        };

        var imageReference = ResolveImageReference(node);
        var placeholder = new Label(string.IsNullOrWhiteSpace(imageReference.AltText) ? "Image available" : imageReference.AltText)
        {
            Xalign = 0,
            Wrap = true
        };
        placeholder.StyleContext.AddClass("catalog-description-text");
        container.PackStart(placeholder, false, false, 0);
        shell.Add(container);

        if (!string.IsNullOrWhiteSpace(imageReference.Source))
        {
            imageRequests.Add(new DescriptionImageRequest(shell, container, placeholder, imageReference.Source, contentGeneration));
        }

        return shell;
    }

    private void QueueVisibleImageLoads()
    {
        if (imageLoadPassQueued || imageRequests.Count == 0 || contentLoadCancellationSource is null)
        {
            return;
        }

        imageLoadPassQueued = true;
        GLib.Idle.Add(() =>
        {
            imageLoadPassQueued = false;
            TryStartVisibleImageLoads();
            return false;
        });
    }

    private void TryStartVisibleImageLoads()
    {
        if (contentLoadCancellationSource is null)
        {
            return;
        }

        while (activeImageLoads < MaximumConcurrentImageLoads)
        {
            var request = imageRequests.FirstOrDefault(IsReadyToLoad);
            if (request is null)
            {
                return;
            }

            request.State = DescriptionImageLoadState.Loading;
            activeImageLoads++;
            _ = LoadImageAsync(request, contentLoadCancellationSource.Token);
        }
    }

    private bool IsReadyToLoad(DescriptionImageRequest request)
    {
        return request.Generation == contentGeneration &&
               request.State == DescriptionImageLoadState.Pending &&
               request.Container.Parent is not null &&
               request.Placeholder.Parent is not null &&
               IsImageRequestNearViewport(request);
    }

    private bool IsImageRequestNearViewport(DescriptionImageRequest request)
    {
        if (Vadjustment is not Adjustment adjustment)
        {
            return true;
        }

        var y = request.Shell.Allocation.Y;
        if (request.Shell.Parent is not null &&
            request.Shell.TranslateCoordinates(content, 0, 0, out _, out var translatedY))
        {
            y = translatedY;
        }

        var top = y;
        var height = request.Shell.Allocation.Height > 0 ? request.Shell.Allocation.Height : 120;
        var bottom = top + height;
        var viewportTop = adjustment.Value - ImageLoadViewportMargin;
        var viewportBottom = adjustment.Value + adjustment.PageSize + ImageLoadViewportMargin;
        return bottom >= viewportTop && top <= viewportBottom;
    }

    private async Task LoadImageAsync(DescriptionImageRequest request, CancellationToken cancellationToken)
    {
        Pixbuf? pixbuf = null;

        try
        {
            var maxWidth = GetPreferredImageWidth();
            pixbuf = await mediaCacheService
                .LoadDescriptionPosterAsync(request.Source, contentBaseUri?.ToString(), maxWidth, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
        finally
        {
            Gtk.Application.Invoke((_, _) =>
            {
                activeImageLoads = Math.Max(0, activeImageLoads - 1);

                if (request.Generation != contentGeneration ||
                    request.Container.Parent is null ||
                    request.Shell.Parent is null ||
                    cancellationToken.IsCancellationRequested)
                {
                    pixbuf?.Dispose();
                    request.State = DescriptionImageLoadState.Failed;
                    QueueVisibleImageLoads();
                    return;
                }

                if (pixbuf is null)
                {
                    if (request.Placeholder is Label label)
                    {
                        label.Text = "Image unavailable.";
                    }

                    request.State = DescriptionImageLoadState.Failed;
                    QueueVisibleImageLoads();
                    return;
                }

                if (request.Placeholder.Parent is not null)
                {
                    request.Container.Remove(request.Placeholder);
                    request.Placeholder.Destroy();
                }

                var image = new Image(pixbuf)
                {
                    Halign = Align.Start,
                    Valign = Align.Start
                };
                request.Container.PackStart(image, false, false, 0);
                request.Container.ShowAll();
                request.State = DescriptionImageLoadState.Loaded;
                QueueVisibleImageLoads();
            });
        }
    }

    private int GetPreferredImageWidth()
    {
        var width = content.AllocatedWidth;
        if (width <= 0)
        {
            width = Allocation.Width;
        }

        return Math.Clamp(width - 32, 240, 760);
    }

    private void TrackRenderedBlock()
    {
        renderedBlockCount++;
        if (renderedBlockCount >= MaximumRenderedBlocks)
        {
            blockLimitReached = true;
        }
    }

    private void AppendTruncatedNotice(string message)
    {
        if (content.Children.OfType<Label>().Any(label => label.Text == message))
        {
            return;
        }

        var label = new Label(message)
        {
            Xalign = 0,
            Wrap = true
        };
        label.StyleContext.AddClass("settings-help");
        content.PackStart(label, false, false, 0);
    }

    private static string ConvertHtmlToPlainText(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var normalized = html
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</div>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase);

        var withoutTags = System.Text.RegularExpressions.Regex.Replace(
            normalized,
            "<[^>]+>",
            string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static CatalogImageReference ResolveImageReference(HtmlNode node)
    {
        if (node.Name.Equals("picture", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var sourceNode in node.Elements("source"))
            {
                var source = GetImageSourceCandidate(sourceNode);
                if (!string.IsNullOrWhiteSpace(source))
                {
                    return new CatalogImageReference(source, GetAltText(node));
                }
            }

            var imageNode = node.Element("img");
            if (imageNode is not null)
            {
                return ResolveImageReference(imageNode);
            }
        }

        return new CatalogImageReference(GetImageSourceCandidate(node), GetAltText(node));
    }

    private static string GetAltText(HtmlNode node)
    {
        if (node.Name.Equals("picture", StringComparison.OrdinalIgnoreCase))
        {
            return node.Element("img")?.GetAttributeValue("alt", "Image available") ?? "Image available";
        }

        return node.GetAttributeValue("alt", "Image available");
    }

    private static string GetImageSourceCandidate(HtmlNode node)
    {
        foreach (var attributeName in new[] { "src", "srcset", "data-src", "data-cfsrc", "data-original", "data-lazy-src" })
        {
            var value = node.GetAttributeValue(attributeName, string.Empty);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (attributeName.Equals("srcset", StringComparison.OrdinalIgnoreCase))
            {
                var srcsetCandidate = ParseBestSrcSetCandidate(value);
                if (!string.IsNullOrWhiteSpace(srcsetCandidate))
                {
                    return srcsetCandidate;
                }

                continue;
            }

            return value;
        }

        return string.Empty;
    }

    private static string ParseBestSrcSetCandidate(string srcset)
    {
        if (string.IsNullOrWhiteSpace(srcset))
        {
            return string.Empty;
        }

        var bestValue = string.Empty;
        var bestScore = double.MinValue;
        foreach (var candidate in srcset.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            var source = parts[0];
            var score = parts.Length > 1
                ? ParseSrcSetDescriptor(parts[1])
                : 0d;

            if (score >= bestScore)
            {
                bestScore = score;
                bestValue = source;
            }
        }

        return bestValue;
    }

    private static double ParseSrcSetDescriptor(string descriptor)
    {
        var normalized = descriptor.Trim().ToLowerInvariant();
        if (normalized.EndsWith('w') &&
            double.TryParse(normalized[..^1], out var widthDescriptor))
        {
            return widthDescriptor;
        }

        if (normalized.EndsWith('x') &&
            double.TryParse(normalized[..^1], out var densityDescriptor))
        {
            return densityDescriptor * 1000d;
        }

        return 0d;
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
        return name is "div" or "p" or "section" or "article" or "ul" or "ol" or "li" or "table" or "blockquote" or
            "pre" or "hr" or "img" or "picture" or "h1" or "h2" or "h3" or "h4" or "h5" or "h6" or "figure" or "dl" or "dd" or
            "dt" or "caption" or "figcaption";
    }

    private static bool IsInlineNode(string name)
    {
        return name is "a" or "abbr" or "b" or "code" or "em" or "i" or "kbd" or "mark" or "small" or "span" or
            "strong" or "sub" or "sup" or "u" or "tt" or "s" or "del" or "ins";
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

        if (name == "img" || name == "picture")
        {
            var alt = GetAltText(node);
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
            case "kbd":
                builder.Append($"<span font_family=\"monospace\">{text}</span>");
                break;
            case "del":
            case "s":
                builder.Append($"<s>{text}</s>");
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
            default:
                builder.Append(text);
                break;
        }
    }

    private static IReadOnlyList<string> ParsePlainTextBlocks(string contentText)
    {
        return contentText
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(text => WebUtility.HtmlDecode(text))
            .ToList();
    }

    private sealed class DescriptionImageRequest
    {
        public DescriptionImageRequest(EventBox shell, Box container, Widget placeholder, string source, int generation)
        {
            Shell = shell;
            Container = container;
            Placeholder = placeholder;
            Source = source;
            Generation = generation;
        }

        public EventBox Shell { get; }
        public Box Container { get; }
        public Widget Placeholder { get; }
        public string Source { get; }
        public int Generation { get; }
        public DescriptionImageLoadState State { get; set; }
    }

    private enum DescriptionImageLoadState
    {
        Pending,
        Loading,
        Loaded,
        Failed
    }

    private readonly record struct CatalogImageReference(string Source, string AltText);
}
