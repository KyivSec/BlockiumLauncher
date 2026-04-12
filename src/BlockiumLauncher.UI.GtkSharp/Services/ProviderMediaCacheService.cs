using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BlockiumLauncher.Application.Abstractions.Paths;
using Gdk;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Gif;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.Metadata;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Svg.Skia;

namespace BlockiumLauncher.UI.GtkSharp.Services;

public sealed class ProviderMediaCacheService
{
    private const int DefaultAnimationDelayMilliseconds = 100;
    private const int MinimumAnimationDelayMilliseconds = 40;
    private const int MaximumSvgDimension = 2048;
    private const int MaximumDescriptionPosterDimension = 1024;

    private static readonly HttpClient MediaHttpClient = CreateMediaHttpClient();
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromHours(12);
    private static readonly JsonSerializerOptions MetadataSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string mediaCacheDirectory;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> downloadGates = new(StringComparer.OrdinalIgnoreCase);

    public ProviderMediaCacheService(ILauncherPaths launcherPaths)
    {
        ArgumentNullException.ThrowIfNull(launcherPaths);

        mediaCacheDirectory = Path.Combine(launcherPaths.CacheDirectory, "provider-media");
        Directory.CreateDirectory(mediaCacheDirectory);
    }

    public bool TryResolveUri(string? source, string? baseUrl, out Uri uri)
    {
        uri = null!;

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        var normalizedSource = source.Trim();
        if (normalizedSource.StartsWith("//", StringComparison.Ordinal))
        {
            normalizedSource = $"https:{normalizedSource}";
        }

        if (Uri.TryCreate(normalizedSource, UriKind.Absolute, out var absoluteUri))
        {
            uri = absoluteUri;
            return true;
        }

        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri) &&
            Uri.TryCreate(baseUri, normalizedSource, out var relativeUri))
        {
            uri = relativeUri;
            return true;
        }

        return false;
    }

    public async Task<Pixbuf?> LoadPixbufAsync(
        string? source,
        string? baseUrl,
        int? maxWidth = null,
        int? squareSize = null,
        CancellationToken cancellationToken = default)
    {
        var media = await LoadMediaAsync(source, baseUrl, maxWidth, squareSize, cancellationToken).ConfigureAwait(false);
        if (media is null)
        {
            return null;
        }

        try
        {
            return media.PrimaryPixbuf?.Copy();
        }
        finally
        {
            media.Dispose();
        }
    }

    public async Task<Pixbuf?> LoadIconPixbufAsync(
        string? source,
        string? baseUrl,
        int squareSize,
        CancellationToken cancellationToken = default)
    {
        if (squareSize <= 0)
        {
            return null;
        }

        if (TryParseDataUri(source, out var dataUri))
        {
            return await LoadDataUriPosterAsync(dataUri, squareSize, cancellationToken).ConfigureAwait(false);
        }

        var entry = await GetOrCreateCachedEntryAsync(source, baseUrl, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        var pixbuf = TryLoadIconPixbuf(entry.DisplayFilePath, squareSize);
        if (pixbuf is not null)
        {
            return pixbuf;
        }

        InvalidateCacheEntry(entry.CacheKey);
        entry = await DownloadAndCacheAsync(entry.CacheKey, entry.ResolvedUri, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        return TryLoadIconPixbuf(entry.DisplayFilePath, squareSize);
    }

    public async Task<Pixbuf?> LoadDescriptionPosterAsync(
        string? source,
        string? baseUrl,
        int? maxWidth = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveMaxWidth = NormalizePosterWidth(maxWidth);

        if (TryParseDataUri(source, out var dataUri))
        {
            return await LoadDataUriPosterAsync(dataUri, effectiveMaxWidth, cancellationToken).ConfigureAwait(false);
        }

        var entry = await GetOrCreateCachedEntryAsync(source, baseUrl, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        var pixbuf = TryLoadPosterPixbuf(entry.DisplayFilePath, effectiveMaxWidth);
        if (pixbuf is not null)
        {
            return pixbuf;
        }

        InvalidateCacheEntry(entry.CacheKey);
        entry = await DownloadAndCacheAsync(entry.CacheKey, entry.ResolvedUri, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        return TryLoadPosterPixbuf(entry.DisplayFilePath, effectiveMaxWidth);
    }

    public async Task<ProviderMediaLoadResult?> LoadMediaAsync(
        string? source,
        string? baseUrl,
        int? maxWidth = null,
        int? squareSize = null,
        CancellationToken cancellationToken = default)
    {
        if (TryParseDataUri(source, out var dataUri))
        {
            return await LoadDataUriAsync(dataUri, maxWidth, squareSize, cancellationToken).ConfigureAwait(false);
        }

        var entry = await GetOrCreateCachedEntryAsync(source, baseUrl, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        var result = await TryLoadCachedMediaAsync(entry, maxWidth, squareSize, cancellationToken).ConfigureAwait(false);
        if (result is not null)
        {
            return result;
        }

        InvalidateCacheEntry(entry.CacheKey);
        entry = await DownloadAndCacheAsync(entry.CacheKey, entry.ResolvedUri, cancellationToken).ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        return await TryLoadCachedMediaAsync(entry, maxWidth, squareSize, cancellationToken).ConfigureAwait(false);
    }

    internal async Task<ProviderMediaCacheEntry?> GetOrCreateCachedEntryAsync(
        string? source,
        string? baseUrl,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveUri(source, baseUrl, out var resolvedUri))
        {
            return null;
        }

        var cacheKey = CreateCacheKey(resolvedUri);
        var cachedEntry = TryGetCachedEntry(cacheKey, resolvedUri);
        if (cachedEntry is not null)
        {
            if (DateTimeOffset.UtcNow - cachedEntry.CachedAtUtc >= RefreshInterval)
            {
                _ = RefreshInBackgroundAsync(cacheKey, resolvedUri);
            }

            return cachedEntry;
        }

        return await DownloadAndCacheAsync(cacheKey, resolvedUri, cancellationToken).ConfigureAwait(false);
    }

    private async Task RefreshInBackgroundAsync(string cacheKey, Uri resolvedUri)
    {
        try
        {
            await DownloadAndCacheAsync(cacheKey, resolvedUri, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    private async Task<ProviderMediaCacheEntry?> DownloadAndCacheAsync(
        string cacheKey,
        Uri resolvedUri,
        CancellationToken cancellationToken)
    {
        var gate = downloadGates.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var existing = TryGetCachedEntry(cacheKey, resolvedUri);
            var existingMetadata = TryReadMetadata(cacheKey);
            using var request = new HttpRequestMessage(HttpMethod.Get, resolvedUri);
            ApplyConditionalHeaders(request, existingMetadata);

            using var response = await MediaHttpClient
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotModified && existing is not null && existingMetadata is not null)
            {
                existingMetadata.CachedAtUtc = DateTimeOffset.UtcNow;
                await WriteMetadataAsync(cacheKey, existingMetadata, cancellationToken).ConfigureAwait(false);
                return existing with { CachedAtUtc = existingMetadata.CachedAtUtc };
            }

            if (!response.IsSuccessStatusCode)
            {
                return existing;
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var originalExtension = ResolveFileExtension(mediaType, resolvedUri);
            var originalPath = GetOriginalDataPath(cacheKey, originalExtension);
            var displayPath = GetDisplayDataPath(cacheKey);
            var tempOriginalPath = $"{originalPath}.tmp";
            var tempDisplayPath = $"{displayPath}.tmp";

            await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var targetStream = new FileStream(tempOriginalPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await sourceStream.CopyToAsync(targetStream, cancellationToken).ConfigureAwait(false);
            }

            var normalized = await NormalizeCachedFileAsync(tempOriginalPath, tempDisplayPath, mediaType, cancellationToken)
                .ConfigureAwait(false);
            if (!normalized.Success)
            {
                TryDeleteFile(tempOriginalPath);
                TryDeleteFile(tempDisplayPath);
                return existing;
            }

            ReplaceFile(originalPath, tempOriginalPath);
            ReplaceFile(displayPath, tempDisplayPath);
            DeleteLegacyCacheArtifacts(cacheKey, originalExtension);

            var metadata = new CachedMediaMetadata
            {
                SourceUri = resolvedUri.ToString(),
                MediaType = mediaType,
                CachedAtUtc = DateTimeOffset.UtcNow,
                OriginalExtension = originalExtension,
                DisplayExtension = ".png",
                MediaKind = normalized.MediaKind,
                Width = normalized.Width,
                Height = normalized.Height,
                FrameCount = normalized.FrameCount,
                ETag = response.Headers.ETag?.Tag,
                LastModifiedUtc = response.Content.Headers.LastModified
            };

            await WriteMetadataAsync(cacheKey, metadata, cancellationToken).ConfigureAwait(false);
            return CreateEntry(cacheKey, resolvedUri, metadata);
        }
        catch
        {
            return TryGetCachedEntry(cacheKey, resolvedUri);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<ProviderMediaLoadResult?> TryLoadCachedMediaAsync(
        ProviderMediaCacheEntry entry,
        int? maxWidth,
        int? squareSize,
        CancellationToken cancellationToken)
    {
        if (entry.FrameCount > 1)
        {
            var animated = await TryLoadAnimatedMediaAsync(entry, maxWidth, squareSize, cancellationToken).ConfigureAwait(false);
            if (animated is not null)
            {
                return animated;
            }
        }

        var pixbuf = TryLoadPixbufFromFile(entry.DisplayFilePath);
        if (pixbuf is null)
        {
            return null;
        }

        using (pixbuf)
        {
            return ProviderMediaLoadResult.FromStatic(TransformPixbuf(pixbuf, maxWidth, squareSize));
        }
    }

    private async Task<ProviderMediaLoadResult?> TryLoadAnimatedMediaAsync(
        ProviderMediaCacheEntry entry,
        int? maxWidth,
        int? squareSize,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(entry.OriginalFilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var image = await Image.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
            if (image.Frames.Count <= 1)
            {
                return null;
            }

            var frames = new List<ProviderMediaAnimationFrame>(image.Frames.Count);
            for (var index = 0; index < image.Frames.Count; index++)
            {
                using var frameImage = image.Frames.CloneFrame(index);
                using var framePixbuf = await CreatePixbufFromImageAsync(frameImage, cancellationToken).ConfigureAwait(false);
                if (framePixbuf is null)
                {
                    continue;
                }

                var transformed = TransformPixbuf(framePixbuf, maxWidth, squareSize);
                var delayMilliseconds = GetFrameDelayMilliseconds(frameImage.Frames.RootFrame.Metadata);
                frames.Add(new ProviderMediaAnimationFrame(transformed, delayMilliseconds));
            }

            return frames.Count switch
            {
                0 => null,
                1 => BuildStaticResultFromSingleFrame(frames),
                _ => ProviderMediaLoadResult.FromAnimation(frames)
            };
        }
        catch
        {
            return null;
        }
    }

    private async Task<ProviderMediaLoadResult?> LoadDataUriAsync(
        ParsedDataUri dataUri,
        int? maxWidth,
        int? squareSize,
        CancellationToken cancellationToken)
    {
        if (TryCreateSvgPoster(dataUri.Bytes, out var svgPixbuf))
        {
            using (svgPixbuf)
            {
                return ProviderMediaLoadResult.FromStatic(TransformPixbuf(svgPixbuf, maxWidth, squareSize));
            }
        }

        try
        {
            await using var imageStream = new MemoryStream(dataUri.Bytes, writable: false);
            using var image = await Image.LoadAsync(imageStream, cancellationToken).ConfigureAwait(false);
            if (image.Frames.Count > 1)
            {
                var frames = new List<ProviderMediaAnimationFrame>(image.Frames.Count);
                for (var index = 0; index < image.Frames.Count; index++)
                {
                    using var frameImage = image.Frames.CloneFrame(index);
                    using var framePixbuf = await CreatePixbufFromImageAsync(frameImage, cancellationToken).ConfigureAwait(false);
                    if (framePixbuf is null)
                    {
                        continue;
                    }

                    var transformed = TransformPixbuf(framePixbuf, maxWidth, squareSize);
                    var delayMilliseconds = GetFrameDelayMilliseconds(frameImage.Frames.RootFrame.Metadata);
                    frames.Add(new ProviderMediaAnimationFrame(transformed, delayMilliseconds));
                }

                return frames.Count switch
                {
                    0 => null,
                    1 => BuildStaticResultFromSingleFrame(frames),
                    _ => ProviderMediaLoadResult.FromAnimation(frames)
                };
            }

            using var pixbuf = await CreatePixbufFromImageAsync(image, cancellationToken).ConfigureAwait(false);
            if (pixbuf is null)
            {
                return null;
            }

            return ProviderMediaLoadResult.FromStatic(TransformPixbuf(pixbuf, maxWidth, squareSize));
        }
        catch
        {
            return null;
        }
    }

    private async Task<Pixbuf?> LoadDataUriPosterAsync(
        ParsedDataUri dataUri,
        int? maxWidth,
        CancellationToken cancellationToken)
    {
        if (TryCreateSvgPoster(dataUri.Bytes, out var svgPixbuf))
        {
            using (svgPixbuf)
            {
                return TransformPixbuf(svgPixbuf, maxWidth, null);
            }
        }

        try
        {
            await using var imageStream = new MemoryStream(dataUri.Bytes, writable: false);
            using var image = await Image.LoadAsync(imageStream, cancellationToken).ConfigureAwait(false);
            using var poster = image.Frames.CloneFrame(0);
            ResizePosterInPlace(poster, maxWidth);

            return await CreatePixbufFromImageAsync(poster, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    private ProviderMediaCacheEntry? TryGetCachedEntry(string cacheKey, Uri resolvedUri)
    {
        var metadata = TryReadMetadata(cacheKey);
        if (metadata is null)
        {
            return null;
        }

        var entry = CreateEntry(cacheKey, resolvedUri, metadata);
        if (entry is null)
        {
            InvalidateCacheEntry(cacheKey);
        }

        return entry;
    }

    private ProviderMediaCacheEntry? CreateEntry(string cacheKey, Uri resolvedUri, CachedMediaMetadata metadata)
    {
        var originalPath = GetOriginalDataPath(cacheKey, metadata.OriginalExtension);
        var displayPath = GetDisplayDataPath(cacheKey);
        if (!File.Exists(originalPath) || !File.Exists(displayPath))
        {
            return null;
        }

        return new ProviderMediaCacheEntry(
            cacheKey,
            resolvedUri,
            originalPath,
            displayPath,
            metadata.MediaType,
            metadata.MediaKind,
            metadata.CachedAtUtc,
            metadata.Width,
            metadata.Height,
            metadata.FrameCount,
            metadata.ETag,
            metadata.LastModifiedUtc);
    }

    private CachedMediaMetadata? TryReadMetadata(string cacheKey)
    {
        try
        {
            var metadataPath = GetMetadataPath(cacheKey);
            if (!File.Exists(metadataPath))
            {
                return null;
            }

            var metadata = JsonSerializer.Deserialize<CachedMediaMetadata>(
                File.ReadAllText(metadataPath),
                MetadataSerializerOptions);

            if (metadata is null ||
                string.IsNullOrWhiteSpace(metadata.OriginalExtension) ||
                string.IsNullOrWhiteSpace(metadata.DisplayExtension))
            {
                return null;
            }

            return metadata;
        }
        catch
        {
            return null;
        }
    }

    private Task WriteMetadataAsync(string cacheKey, CachedMediaMetadata metadata, CancellationToken cancellationToken)
    {
        var metadataPath = GetMetadataPath(cacheKey);
        var json = JsonSerializer.Serialize(metadata, MetadataSerializerOptions);
        return File.WriteAllTextAsync(metadataPath, json, cancellationToken);
    }

    private static void ApplyConditionalHeaders(HttpRequestMessage request, CachedMediaMetadata? existingMetadata)
    {
        if (!string.IsNullOrWhiteSpace(existingMetadata?.ETag))
        {
            request.Headers.TryAddWithoutValidation("If-None-Match", existingMetadata.ETag);
        }

        if (existingMetadata?.LastModifiedUtc is DateTimeOffset lastModified)
        {
            request.Headers.IfModifiedSince = lastModified;
        }
    }

    private async Task<NormalizedMediaResult> NormalizeCachedFileAsync(
        string originalTempPath,
        string displayTempPath,
        string? mediaType,
        CancellationToken cancellationToken)
    {
        try
        {
            var mediaKind = DetectMediaKind(mediaType, originalTempPath);
            if (mediaKind == ProviderMediaKind.Svg)
            {
                await RasterizeSvgFileAsync(originalTempPath, displayTempPath, cancellationToken).ConfigureAwait(false);
                using var pixbuf = TryLoadPixbufFromFile(displayTempPath);
                return pixbuf is null
                    ? NormalizedMediaResult.Failure()
                    : new NormalizedMediaResult(true, mediaKind, pixbuf.Width, pixbuf.Height, 1);
            }

            await using var stream = new FileStream(originalTempPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var image = await Image.LoadAsync(stream, cancellationToken).ConfigureAwait(false);
            var frameCount = image.Frames.Count;
            var isAnimated = frameCount > 1;

            using var poster = image.Frames.CloneFrame(0);
            ResizePosterInPlace(poster, MaximumDescriptionPosterDimension);
            await poster.SaveAsPngAsync(displayTempPath, new PngEncoder(), cancellationToken).ConfigureAwait(false);
            return new NormalizedMediaResult(
                true,
                isAnimated ? ProviderMediaKind.Animated : ProviderMediaKind.Static,
                poster.Width,
                poster.Height,
                frameCount);
        }
        catch
        {
            return NormalizedMediaResult.Failure();
        }
    }

    private static ProviderMediaKind DetectMediaKind(string? mediaType, string filePath)
    {
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var normalizedMediaType = mediaType.Trim().ToLowerInvariant();
            if (normalizedMediaType.Contains("svg", StringComparison.Ordinal))
            {
                return ProviderMediaKind.Svg;
            }

            if (normalizedMediaType.Contains("gif", StringComparison.Ordinal) ||
                normalizedMediaType.Contains("apng", StringComparison.Ordinal) ||
                normalizedMediaType.Contains("webp", StringComparison.Ordinal))
            {
                return ProviderMediaKind.Animated;
            }
        }

        return Path.GetExtension(filePath).Equals(".svg", StringComparison.OrdinalIgnoreCase)
            ? ProviderMediaKind.Svg
            : ProviderMediaKind.Static;
    }

    private static async Task RasterizeSvgFileAsync(string svgPath, string pngPath, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(svgPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = new byte[stream.Length];
        _ = await stream.ReadAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (!TryCreateSvgPoster(bytes, out var pixbuf))
        {
            throw new InvalidOperationException("Unable to rasterize SVG media.");
        }

        using (pixbuf)
        {
            pixbuf.Save(pngPath, "png");
        }
    }

    private static bool TryCreateSvgPoster(byte[] svgBytes, out Pixbuf pixbuf)
    {
        pixbuf = null!;

        try
        {
            using var memoryStream = new MemoryStream(svgBytes, writable: false);
            var svg = new SKSvg();
            var picture = svg.Load(memoryStream);
            if (picture is null)
            {
                return false;
            }

            var bounds = picture.CullRect;
            var sourceWidth = bounds.Width <= 0 ? 512f : bounds.Width;
            var sourceHeight = bounds.Height <= 0 ? 512f : bounds.Height;
            var scale = Math.Min(1f, MaximumSvgDimension / Math.Max(sourceWidth, sourceHeight));
            var width = Math.Max(1, (int)Math.Ceiling(sourceWidth * scale));
            var height = Math.Max(1, (int)Math.Ceiling(sourceHeight * scale));

            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var surface = SKSurface.Create(imageInfo);
            var canvas = surface.Canvas;
            canvas.Clear(SKColors.Transparent);
            canvas.Scale(scale, scale);
            canvas.Translate(-bounds.Left, -bounds.Top);
            canvas.DrawPicture(picture);
            canvas.Flush();

            using var image = surface.Snapshot();
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            using var loader = new PixbufLoader();
            loader.Write(data.ToArray());
            loader.Close();
            if (loader.Pixbuf is null)
            {
                return false;
            }

            pixbuf = loader.Pixbuf.Copy();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<Pixbuf?> CreatePixbufFromImageAsync(Image image, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new MemoryStream();
            await image.SaveAsPngAsync(stream, new PngEncoder(), cancellationToken).ConfigureAwait(false);
            using var loader = new PixbufLoader();
            loader.Write(stream.ToArray());
            loader.Close();
            return loader.Pixbuf?.Copy();
        }
        catch
        {
            return null;
        }
    }

    private static Pixbuf? TryLoadPosterPixbuf(string filePath, int? maxWidth)
    {
        var pixbuf = TryLoadPixbufFromFile(filePath);
        if (pixbuf is null)
        {
            return null;
        }

        if (maxWidth is not int width || width <= 0 || pixbuf.Width <= width)
        {
            return pixbuf;
        }

        using (pixbuf)
        {
            return TransformPixbuf(pixbuf, width, null);
        }
    }

    private static Pixbuf? TryLoadIconPixbuf(string filePath, int squareSize)
    {
        var pixbuf = TryLoadPixbufFromFile(filePath);
        if (pixbuf is null)
        {
            return null;
        }

        if (pixbuf.Width == squareSize && pixbuf.Height == squareSize)
        {
            return pixbuf;
        }

        using (pixbuf)
        {
            return ScaleToSquare(pixbuf, squareSize);
        }
    }

    private static int GetFrameDelayMilliseconds(ImageFrameMetadata metadata)
    {
        if (metadata.TryGetWebpFrameMetadata(out WebpFrameMetadata? webp))
        {
            return NormalizeAnimationDelay((int)webp.FrameDelay);
        }

        if (metadata.TryGetGifMetadata(out GifFrameMetadata? gif))
        {
            return NormalizeAnimationDelay(gif.FrameDelay * 10);
        }

        if (metadata.TryGetPngMetadata(out PngFrameMetadata? png))
        {
            return NormalizeAnimationDelay((int)Math.Round(png.FrameDelay.ToDouble() * 10d));
        }

        return DefaultAnimationDelayMilliseconds;
    }

    private static int NormalizeAnimationDelay(int delayMilliseconds)
    {
        if (delayMilliseconds <= 0)
        {
            return DefaultAnimationDelayMilliseconds;
        }

        return Math.Max(MinimumAnimationDelayMilliseconds, delayMilliseconds);
    }

    private static ProviderMediaLoadResult BuildStaticResultFromSingleFrame(
        IReadOnlyList<ProviderMediaAnimationFrame> frames)
    {
        var pixbuf = frames[0].Pixbuf.Copy();
        foreach (var frame in frames)
        {
            frame.Dispose();
        }

        return ProviderMediaLoadResult.FromStatic(pixbuf);
    }

    private static Pixbuf? TryLoadPixbufFromFile(string filePath)
    {
        try
        {
            return new Pixbuf(filePath);
        }
        catch
        {
            return null;
        }
    }

    private static Pixbuf TransformPixbuf(Pixbuf pixbuf, int? maxWidth, int? squareSize)
    {
        if (squareSize is int size and > 0)
        {
            using var scaled = ScaleToSquare(pixbuf, size);
            return scaled.Copy();
        }

        if (maxWidth is int width && width > 0 && pixbuf.Width > width)
        {
            var height = Math.Max(1, pixbuf.Height * width / pixbuf.Width);
            using var scaled = pixbuf.ScaleSimple(width, height, InterpType.Bilinear);
            return scaled.Copy();
        }

        return pixbuf.Copy();
    }

    private static int? NormalizePosterWidth(int? maxWidth)
    {
        if (maxWidth is not int width || width <= 0)
        {
            return MaximumDescriptionPosterDimension;
        }

        return Math.Clamp(width, 240, MaximumDescriptionPosterDimension);
    }

    private static void ResizePosterInPlace(Image image, int? maxDimension)
    {
        if (maxDimension is not int boundedDimension || boundedDimension <= 0)
        {
            return;
        }

        var largestDimension = Math.Max(image.Width, image.Height);
        if (largestDimension <= boundedDimension)
        {
            return;
        }

        var scale = (double)boundedDimension / largestDimension;
        var width = Math.Max(1, (int)Math.Round(image.Width * scale));
        var height = Math.Max(1, (int)Math.Round(image.Height * scale));
        image.Mutate(context => context.Resize(width, height));
    }

    private static bool TryParseDataUri(string? source, out ParsedDataUri dataUri)
    {
        dataUri = default;
        if (string.IsNullOrWhiteSpace(source) ||
            !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var separatorIndex = source.IndexOf(',', StringComparison.Ordinal);
        if (separatorIndex <= 0 || separatorIndex >= source.Length - 1)
        {
            return false;
        }

        var metadata = source[5..separatorIndex];
        var payload = source[(separatorIndex + 1)..];
        var mediaType = metadata.Split(';', 2, StringSplitOptions.TrimEntries)[0];

        try
        {
            var bytes = metadata.Contains(";base64", StringComparison.OrdinalIgnoreCase)
                ? Convert.FromBase64String(payload)
                : Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));

            dataUri = new ParsedDataUri(mediaType, bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private string GetMetadataPath(string cacheKey) => Path.Combine(mediaCacheDirectory, $"{cacheKey}.json");

    private string GetOriginalDataPath(string cacheKey, string extension) => Path.Combine(mediaCacheDirectory, $"{cacheKey}.source{extension}");

    private string GetDisplayDataPath(string cacheKey) => Path.Combine(mediaCacheDirectory, $"{cacheKey}.display.png");

    private void DeleteLegacyCacheArtifacts(string cacheKey, string keepExtension)
    {
        foreach (var path in Directory.EnumerateFiles(mediaCacheDirectory, $"{cacheKey}*"))
        {
            var fileName = Path.GetFileName(path);
            if (fileName.Equals($"{cacheKey}.json", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals($"{cacheKey}.display.png", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals($"{cacheKey}.source{keepExtension}", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryDeleteFile(path);
        }
    }

    private void InvalidateCacheEntry(string cacheKey)
    {
        TryDeleteFile(GetMetadataPath(cacheKey));
        foreach (var path in Directory.EnumerateFiles(mediaCacheDirectory, $"{cacheKey}*"))
        {
            TryDeleteFile(path);
        }
    }

    private static void ReplaceFile(string destinationPath, string tempPath)
    {
        TryDeleteFile(destinationPath);
        File.Move(tempPath, destinationPath);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static string CreateCacheKey(Uri uri)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(uri.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ResolveFileExtension(string? mediaType, Uri resolvedUri)
    {
        if (!string.IsNullOrWhiteSpace(mediaType))
        {
            var normalizedMediaType = mediaType.Trim().ToLowerInvariant();
            if (normalizedMediaType.Contains("png", StringComparison.Ordinal))
            {
                return ".png";
            }

            if (normalizedMediaType.Contains("jpeg", StringComparison.Ordinal) ||
                normalizedMediaType.Contains("jpg", StringComparison.Ordinal))
            {
                return ".jpg";
            }

            if (normalizedMediaType.Contains("gif", StringComparison.Ordinal))
            {
                return ".gif";
            }

            if (normalizedMediaType.Contains("bmp", StringComparison.Ordinal))
            {
                return ".bmp";
            }

            if (normalizedMediaType.Contains("webp", StringComparison.Ordinal))
            {
                return ".webp";
            }

            if (normalizedMediaType.Contains("svg", StringComparison.Ordinal))
            {
                return ".svg";
            }

            if (normalizedMediaType.Contains("tiff", StringComparison.Ordinal))
            {
                return ".tiff";
            }

            if (normalizedMediaType.Contains("icon", StringComparison.Ordinal) ||
                normalizedMediaType.Contains("ico", StringComparison.Ordinal))
            {
                return ".ico";
            }
        }

        var extension = Path.GetExtension(resolvedUri.AbsolutePath);
        if (!string.IsNullOrWhiteSpace(extension) && extension.Length <= 5)
        {
            return extension.ToLowerInvariant();
        }

        return ".img";
    }

    private static Pixbuf ScaleToSquare(Pixbuf original, int size)
    {
        if (original.Width == original.Height)
        {
            return original.ScaleSimple(size, size, InterpType.Bilinear);
        }

        var ratio = (double)original.Width / original.Height;
        int targetWidth;
        int targetHeight;
        if (ratio > 1)
        {
            targetWidth = (int)Math.Ceiling(size * ratio);
            targetHeight = size;
        }
        else
        {
            targetWidth = size;
            targetHeight = (int)Math.Ceiling(size / ratio);
        }

        using var intermediate = original.ScaleSimple(targetWidth, targetHeight, InterpType.Bilinear);
        var scaled = new Pixbuf(Colorspace.Rgb, true, 8, size, size);
        scaled.Fill(0x00000000);
        intermediate.CopyArea(
            Math.Max(0, (targetWidth - size) / 2),
            Math.Max(0, (targetHeight - size) / 2),
            size,
            size,
            scaled,
            0,
            0);
        return scaled;
    }

    private static HttpClient CreateMediaHttpClient()
    {
        var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BlockiumLauncher/0.1");
        return httpClient;
    }

    public sealed class ProviderMediaLoadResult : IDisposable
    {
        private ProviderMediaLoadResult(ProviderMediaKind kind, Pixbuf? pixbuf, IReadOnlyList<ProviderMediaAnimationFrame>? frames)
        {
            Kind = kind;
            Pixbuf = pixbuf;
            Frames = frames;
        }

        public ProviderMediaKind Kind { get; }
        public Pixbuf? Pixbuf { get; }
        public IReadOnlyList<ProviderMediaAnimationFrame>? Frames { get; }
        public Pixbuf? PrimaryPixbuf => Pixbuf ?? Frames?.FirstOrDefault()?.Pixbuf;

        public static ProviderMediaLoadResult FromStatic(Pixbuf pixbuf) =>
            new(ProviderMediaKind.Static, pixbuf, null);

        public static ProviderMediaLoadResult FromAnimation(IReadOnlyList<ProviderMediaAnimationFrame> frames) =>
            new(ProviderMediaKind.Animated, null, frames);

        public void Dispose()
        {
            Pixbuf?.Dispose();
            if (Frames is null)
            {
                return;
            }

            foreach (var frame in Frames)
            {
                frame.Dispose();
            }
        }
    }

    public sealed class ProviderMediaAnimationFrame : IDisposable
    {
        public ProviderMediaAnimationFrame(Pixbuf pixbuf, int delayMilliseconds)
        {
            Pixbuf = pixbuf;
            DelayMilliseconds = delayMilliseconds;
        }

        public Pixbuf Pixbuf { get; }
        public int DelayMilliseconds { get; }

        public void Dispose()
        {
            Pixbuf.Dispose();
        }
    }

    public enum ProviderMediaKind
    {
        Static,
        Animated,
        Svg
    }

    internal sealed record ProviderMediaCacheEntry(
        string CacheKey,
        Uri ResolvedUri,
        string OriginalFilePath,
        string DisplayFilePath,
        string? MediaType,
        ProviderMediaKind MediaKind,
        DateTimeOffset CachedAtUtc,
        int Width,
        int Height,
        int FrameCount,
        string? ETag,
        DateTimeOffset? LastModifiedUtc)
    {
        public string FilePath => DisplayFilePath;
    }

    private readonly record struct ParsedDataUri(string MediaType, byte[] Bytes);

    private readonly record struct NormalizedMediaResult(
        bool Success,
        ProviderMediaKind MediaKind,
        int Width,
        int Height,
        int FrameCount)
    {
        public static NormalizedMediaResult Failure() => new(false, ProviderMediaKind.Static, 0, 0, 0);
    }

    private sealed class CachedMediaMetadata
    {
        public string SourceUri { get; set; } = string.Empty;
        public string OriginalExtension { get; set; } = ".img";
        public string DisplayExtension { get; set; } = ".png";
        public string? MediaType { get; set; }
        public ProviderMediaKind MediaKind { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int FrameCount { get; set; }
        public DateTimeOffset CachedAtUtc { get; set; }
        public string? ETag { get; set; }
        public DateTimeOffset? LastModifiedUtc { get; set; }
    }
}
