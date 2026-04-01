using BlockiumLauncher.Application.UseCases.Skins;
using Cairo;
using Gdk;
using Gtk;
using SharpGLTF.Schema2;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace BlockiumLauncher.UI.GtkSharp.Widgets;

public sealed class SkinPreviewArea : DrawingArea
{
    private const float ModelScale = 16f;
    private static readonly Lazy<GlbMeshModel> ClassicPlayerModel = new(() => LoadGlbModel("steve.glb"));
    private static readonly Lazy<GlbMeshModel> SlimPlayerModel = new(() => LoadGlbModel("alex.glb"));
    private static readonly Lazy<GlbMeshModel> CapeModel = new(() => LoadGlbModel(
        "cape_and_elytra.glb",
        node => node.Name?.Contains("cape", StringComparison.OrdinalIgnoreCase) == true));
    private static readonly Lazy<Pixbuf?> PreviewBackground = new(LoadPreviewBackground);

    private string? SkinPath;
    private string? CapePath;
    private string? LoadedSkinPath;
    private string? LoadedCapePath;
    private SixLabors.ImageSharp.Image<Rgba32>? SkinTexture;
    private SixLabors.ImageSharp.Image<Rgba32>? CapeTexture;
    private bool TexturesDirty = true;
    private SkinModelType ModelType = SkinModelType.Classic;
    private bool IsDragging;
    private double LastPointerX;
    private double LastPointerY;
    private float Yaw = -28f;
    private float Pitch = 18f;
    private byte[]? FramePixels;
    private float[]? DepthBuffer;
    private ImageSurface? FrameSurface;
    private int CachedFrameWidth;
    private int CachedFrameHeight;
    private Pixbuf? ScaledBackground;
    private int ScaledBackgroundWidth;
    private int ScaledBackgroundHeight;

    public SkinPreviewArea()
    {
        Hexpand = false;
        Vexpand = false;
        WidthRequest = 320;
        HeightRequest = 320;

        AddEvents((int)(
            EventMask.ButtonPressMask |
            EventMask.ButtonReleaseMask |
            EventMask.PointerMotionMask |
            EventMask.Button1MotionMask));

        Drawn += HandleDrawn;
        ButtonPressEvent += HandleButtonPress;
        ButtonReleaseEvent += HandleButtonRelease;
        MotionNotifyEvent += HandleMotion;
        Destroyed += (_, _) => ReleaseCachedSurfaces();
    }

    public void SetPreview(string? skinPath, string? capePath, SkinModelType modelType)
    {
        SkinPath = skinPath;
        CapePath = capePath;
        ModelType = modelType;
        TexturesDirty = true;
        QueueDraw();
    }

    public void ResetOrientation()
    {
        Yaw = -28f;
        Pitch = 18f;
        QueueDraw();
    }

    private void HandleDrawn(object o, DrawnArgs args)
    {
        var cr = args.Cr;
        var width = Math.Max(Allocation.Width, 1);
        var height = Math.Max(Allocation.Height, 1);

        DrawPreviewBackground(cr, width, height);

        EnsureTextures();
        EnsureFrameBuffers(width, height);
        Array.Clear(FramePixels!, 0, FramePixels!.Length);
        Array.Fill(DepthBuffer!, float.PositiveInfinity);

        RenderModel(FramePixels!, width, height, width * 4, DepthBuffer!);
        FrameSurface!.Flush();
        FrameSurface.MarkDirty();

        cr.SetSourceSurface(FrameSurface, 0, 0);
        cr.Paint();
    }

    private void HandleButtonPress(object o, ButtonPressEventArgs args)
    {
        if (args.Event.Button != 1)
        {
            return;
        }

        IsDragging = true;
        LastPointerX = args.Event.X;
        LastPointerY = args.Event.Y;
    }

    private void HandleButtonRelease(object o, ButtonReleaseEventArgs args)
    {
        if (args.Event.Button == 1)
        {
            IsDragging = false;
        }
    }

    private void HandleMotion(object o, MotionNotifyEventArgs args)
    {
        if (!IsDragging)
        {
            return;
        }

        var deltaX = args.Event.X - LastPointerX;
        var deltaY = args.Event.Y - LastPointerY;
        LastPointerX = args.Event.X;
        LastPointerY = args.Event.Y;

        Yaw -= (float)(deltaX * 0.65);
        Pitch = Math.Clamp(Pitch - (float)(deltaY * 0.45), -35f, 35f);
        QueueDraw();
    }

    private void EnsureTextures()
    {
        if (!TexturesDirty &&
            string.Equals(LoadedSkinPath, SkinPath, StringComparison.Ordinal) &&
            string.Equals(LoadedCapePath, CapePath, StringComparison.Ordinal))
        {
            return;
        }

        SkinTexture?.Dispose();
        CapeTexture?.Dispose();
        SkinTexture = LoadNormalizedSkinOrFallback(SkinPath);
        CapeTexture = LoadCapeTexture(CapePath);
        LoadedSkinPath = SkinPath;
        LoadedCapePath = CapePath;
        TexturesDirty = false;
    }

    private void RenderModel(byte[] data, int width, int height, int stride, float[] zBuffer)
    {
        if (SkinTexture is null)
        {
            return;
        }

        var triangles = BuildTriangles(width, height);
        foreach (var triangle in triangles)
        {
            RasterizeTriangle(triangle, data, zBuffer, width, height, stride);
        }

        if (!HasVisiblePixels(data))
        {
            RenderFallbackSprite(data, width, height, stride);
        }
    }

    private List<TexturedTriangle> BuildTriangles(int width, int height)
    {
        var triangles = new List<TexturedTriangle>();
        var playerModel = ModelType == SkinModelType.Slim ? SlimPlayerModel.Value : ClassicPlayerModel.Value;

        AppendModelTriangles(triangles, playerModel, SkinTexture!, width, height);

        if (CapeTexture is not null)
        {
            AppendModelTriangles(triangles, CapeModel.Value, CapeTexture, width, height);
        }

        return triangles;
    }

    private void AppendModelTriangles(List<TexturedTriangle> triangles, GlbMeshModel model, SixLabors.ImageSharp.Image<Rgba32> texture, int width, int height)
    {
        foreach (var meshTriangle in model.Triangles)
        {
            triangles.Add(new TexturedTriangle(
                ProjectVertex(meshTriangle.P0, meshTriangle.Uv0.X, meshTriangle.Uv0.Y, width, height),
                ProjectVertex(meshTriangle.P1, meshTriangle.Uv1.X, meshTriangle.Uv1.Y, width, height),
                ProjectVertex(meshTriangle.P2, meshTriangle.Uv2.X, meshTriangle.Uv2.Y, width, height),
                texture));
        }
    }

    private ProjectedVertex ProjectVertex(Vector3 source, float u, float v, int width, int height)
    {
        var transformed = Transform(source);
        var viewportSize = Math.Min(width, height) - 28f;
        var focalLength = viewportSize * 1.24f;
        var scale = focalLength / MathF.Max(transformed.Z, 6f);
        var centerX = width / 2f;
        var centerY = height / 2f + 18f;

        return new ProjectedVertex(
            centerX + transformed.X * scale,
            centerY - transformed.Y * scale,
            transformed.Z,
            u,
            v);
    }

    private Vector3 Transform(Vector3 point)
    {
        var yawRadians = MathF.PI / 180f * Yaw;
        var pitchRadians = MathF.PI / 180f * Pitch;

        var cosYaw = MathF.Cos(yawRadians);
        var sinYaw = MathF.Sin(yawRadians);
        var cosPitch = MathF.Cos(pitchRadians);
        var sinPitch = MathF.Sin(pitchRadians);

        var yawRotated = new Vector3(
            point.X * cosYaw + point.Z * sinYaw,
            point.Y,
            -point.X * sinYaw + point.Z * cosYaw);

        return new Vector3(
            yawRotated.X,
            yawRotated.Y * cosPitch - yawRotated.Z * sinPitch,
            yawRotated.Y * sinPitch + yawRotated.Z * cosPitch + 48f);
    }

    private static void RasterizeTriangle(TexturedTriangle triangle, byte[] pixels, float[] zBuffer, int width, int height, int stride)
    {
        var minX = Math.Max(0, (int)MathF.Floor(MathF.Min(triangle.V0.X, MathF.Min(triangle.V1.X, triangle.V2.X))));
        var minY = Math.Max(0, (int)MathF.Floor(MathF.Min(triangle.V0.Y, MathF.Min(triangle.V1.Y, triangle.V2.Y))));
        var maxX = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(triangle.V0.X, MathF.Max(triangle.V1.X, triangle.V2.X))));
        var maxY = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(triangle.V0.Y, MathF.Max(triangle.V1.Y, triangle.V2.Y))));

        var area = TriangleArea(triangle.V0, triangle.V1, triangle.V2);
        if (MathF.Abs(area) < 0.0001f)
        {
            return;
        }

        var isPositiveArea = area > 0;
        for (var y = minY; y <= maxY; y++)
        {
            var py = y + 0.5f;
            for (var x = minX; x <= maxX; x++)
            {
                var px = x + 0.5f;
                var rawW0 = EdgeFunction(triangle.V1, triangle.V2, px, py);
                var rawW1 = EdgeFunction(triangle.V2, triangle.V0, px, py);
                var rawW2 = EdgeFunction(triangle.V0, triangle.V1, px, py);

                if (isPositiveArea)
                {
                    if (rawW0 < 0 || rawW1 < 0 || rawW2 < 0)
                    {
                        continue;
                    }
                }
                else if (rawW0 > 0 || rawW1 > 0 || rawW2 > 0)
                {
                    continue;
                }

                var w0 = rawW0 / area;
                var w1 = rawW1 / area;
                var w2 = rawW2 / area;
                var invZ = w0 / triangle.V0.Z + w1 / triangle.V1.Z + w2 / triangle.V2.Z;
                if (invZ <= 0)
                {
                    continue;
                }

                var depth = 1f / invZ;
                var pixelIndex = y * width + x;
                if (depth >= zBuffer[pixelIndex])
                {
                    continue;
                }

                var u = (w0 * triangle.V0.U / triangle.V0.Z + w1 * triangle.V1.U / triangle.V1.Z + w2 * triangle.V2.U / triangle.V2.Z) * depth;
                var v = (w0 * triangle.V0.V / triangle.V0.Z + w1 * triangle.V1.V / triangle.V1.Z + w2 * triangle.V2.V / triangle.V2.Z) * depth;

                var texX = Math.Clamp((int)MathF.Floor(u * triangle.Texture.Width), 0, triangle.Texture.Width - 1);
                var texY = Math.Clamp((int)MathF.Floor(v * triangle.Texture.Height), 0, triangle.Texture.Height - 1);
                var sample = triangle.Texture[texX, texY];
                if (sample.A == 0)
                {
                    continue;
                }

                zBuffer[pixelIndex] = depth;
                var offset = y * stride + x * 4;
                var alpha = sample.A / 255f;
                pixels[offset + 0] = (byte)(sample.B * alpha);
                pixels[offset + 1] = (byte)(sample.G * alpha);
                pixels[offset + 2] = (byte)(sample.R * alpha);
                pixels[offset + 3] = sample.A;
            }
        }
    }

    private void RenderFallbackSprite(byte[] pixels, int width, int height, int stride)
    {
        if (SkinTexture is null)
        {
            return;
        }

        DrawTexturedRect(pixels, width, height, stride, SkinTexture, 8, 8, 8, 8, width / 2 - 42, height / 2 - 86, 84, 84);
        DrawTexturedRect(pixels, width, height, stride, SkinTexture, 20, 20, 8, 12, width / 2 - 42, height / 2 - 2, 84, 126);
        DrawTexturedRect(pixels, width, height, stride, SkinTexture, 44, 20, 4, 12, width / 2 - 84, height / 2 - 2, 42, 126);
        DrawTexturedRect(pixels, width, height, stride, SkinTexture, 36, 52, 4, 12, width / 2 + 42, height / 2 - 2, 42, 126);
        DrawTexturedRect(pixels, width, height, stride, SkinTexture, 4, 20, 4, 12, width / 2 - 42, height / 2 + 124, 42, 126);
        DrawTexturedRect(pixels, width, height, stride, SkinTexture, 20, 52, 4, 12, width / 2, height / 2 + 124, 42, 126);
    }

    private static void DrawTexturedRect(
        byte[] pixels,
        int width,
        int height,
        int stride,
        SixLabors.ImageSharp.Image<Rgba32> texture,
        int srcX,
        int srcY,
        int srcWidth,
        int srcHeight,
        int dstX,
        int dstY,
        int dstWidth,
        int dstHeight)
    {
        for (var y = 0; y < dstHeight; y++)
        {
            var py = dstY + y;
            if (py < 0 || py >= height)
            {
                continue;
            }

            var v = dstHeight <= 1 ? 0 : y / (float)(dstHeight - 1);
            var texY = srcY + Math.Clamp((int)MathF.Round(v * (srcHeight - 1)), 0, srcHeight - 1);
            for (var x = 0; x < dstWidth; x++)
            {
                var px = dstX + x;
                if (px < 0 || px >= width)
                {
                    continue;
                }

                var u = dstWidth <= 1 ? 0 : x / (float)(dstWidth - 1);
                var texX = srcX + Math.Clamp((int)MathF.Round(u * (srcWidth - 1)), 0, srcWidth - 1);
                var sample = texture[texX, texY];
                if (sample.A == 0)
                {
                    continue;
                }

                var offset = py * stride + px * 4;
                var alpha = sample.A / 255f;
                pixels[offset + 0] = (byte)(sample.B * alpha);
                pixels[offset + 1] = (byte)(sample.G * alpha);
                pixels[offset + 2] = (byte)(sample.R * alpha);
                pixels[offset + 3] = sample.A;
            }
        }
    }

    private static bool HasVisiblePixels(byte[] data)
    {
        for (var index = 3; index < data.Length; index += 4)
        {
            if (data[index] != 0)
            {
                return true;
            }
        }

        return false;
    }

    private static float TriangleArea(ProjectedVertex a, ProjectedVertex b, ProjectedVertex c)
    {
        return EdgeFunction(a, b, c.X, c.Y);
    }

    private static float EdgeFunction(ProjectedVertex a, ProjectedVertex b, float px, float py)
    {
        return (px - a.X) * (b.Y - a.Y) - (py - a.Y) * (b.X - a.X);
    }

    private void DrawPreviewBackground(Context cr, int width, int height)
    {
        var pixbuf = PreviewBackground.Value;
        if (pixbuf is null)
        {
            cr.SetSourceRGB(0.96, 0.98, 0.99);
            cr.Paint();
            return;
        }

        EnsureScaledBackground(pixbuf, width, height);
        Gdk.CairoHelper.SetSourcePixbuf(cr, ScaledBackground!, 0, 0);
        cr.Paint();
    }

    private void EnsureFrameBuffers(int width, int height)
    {
        if (FrameSurface is not null && CachedFrameWidth == width && CachedFrameHeight == height)
        {
            return;
        }

        ReleaseCachedFrameSurface();

        CachedFrameWidth = width;
        CachedFrameHeight = height;
        FramePixels = new byte[height * width * 4];
        DepthBuffer = new float[height * width];
        FrameSurface = new ImageSurface(FramePixels, Format.Argb32, width, height, width * 4);
    }

    private void EnsureScaledBackground(Pixbuf source, int width, int height)
    {
        if (ScaledBackground is not null && ScaledBackgroundWidth == width && ScaledBackgroundHeight == height)
        {
            return;
        }

        ScaledBackground?.Dispose();
        ScaledBackground = source.ScaleSimple(width, height, InterpType.Bilinear) ?? source.Copy();
        ScaledBackgroundWidth = width;
        ScaledBackgroundHeight = height;
    }

    private void ReleaseCachedSurfaces()
    {
        ReleaseCachedFrameSurface();
        ScaledBackground?.Dispose();
        ScaledBackground = null;
        ScaledBackgroundWidth = 0;
        ScaledBackgroundHeight = 0;
    }

    private void ReleaseCachedFrameSurface()
    {
        FrameSurface?.Dispose();
        FrameSurface = null;
        FramePixels = null;
        DepthBuffer = null;
        CachedFrameWidth = 0;
        CachedFrameHeight = 0;
    }

    private static SixLabors.ImageSharp.Image<Rgba32> LoadNormalizedSkinOrFallback(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return CreateFallbackSkin();
        }

        using var source = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        if (source.Width == 64 && source.Height == 64)
        {
            return source.Clone();
        }

        var normalized = new SixLabors.ImageSharp.Image<Rgba32>(64, 64);
        Fill(normalized, new Rgba32(0, 0, 0, 0));
        CopyRect(source, normalized, 0, 0, 64, 32, 0, 0);
        CopyRect(source, normalized, 0, 16, 16, 16, 16, 48);
        CopyRect(source, normalized, 40, 16, 16, 16, 32, 48);
        return normalized;
    }

    private static SixLabors.ImageSharp.Image<Rgba32>? LoadCapeTexture(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        using var source = SixLabors.ImageSharp.Image.Load<Rgba32>(path);
        return source.Clone();
    }

    private static void Fill(SixLabors.ImageSharp.Image<Rgba32> image, Rgba32 color)
    {
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                image[x, y] = color;
            }
        }
    }

    private static void CopyRect(SixLabors.ImageSharp.Image<Rgba32> source, SixLabors.ImageSharp.Image<Rgba32> destination, int srcX, int srcY, int width, int height, int dstX, int dstY)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                destination[dstX + x, dstY + y] = source[srcX + x, srcY + y];
            }
        }
    }

    private static SixLabors.ImageSharp.Image<Rgba32> CreateFallbackSkin()
    {
        var image = new SixLabors.ImageSharp.Image<Rgba32>(64, 64);
        Fill(image, new Rgba32(0, 0, 0, 0));

        var skin = new Rgba32(235, 203, 168, 255);
        var blue = new Rgba32(61, 130, 215, 255);
        var navy = new Rgba32(32, 72, 134, 255);

        FillRect(image, 8, 8, 8, 8, skin);
        FillRect(image, 20, 20, 8, 12, blue);
        FillRect(image, 44, 20, 4, 12, blue);
        FillRect(image, 36, 52, 4, 12, blue);
        FillRect(image, 4, 20, 4, 12, navy);
        FillRect(image, 20, 52, 4, 12, navy);

        return image;
    }

    private static void FillRect(SixLabors.ImageSharp.Image<Rgba32> image, int x, int y, int width, int height, Rgba32 color)
    {
        for (var offsetY = 0; offsetY < height; offsetY++)
        {
            for (var offsetX = 0; offsetX < width; offsetX++)
            {
                image[x + offsetX, y + offsetY] = color;
            }
        }
    }

    private static GlbMeshModel LoadGlbModel(string fileName, Func<SharpGLTF.Schema2.Node, bool>? nodeFilter = null)
    {
        var path = ResolveModelAssetPath(fileName);
        var model = ModelRoot.Load(path);
        var triangles = new List<MeshTriangle>();

        foreach (var node in model.LogicalNodes.Where(node => node.Mesh != null))
        {
            if (nodeFilter is not null && !nodeFilter(node))
            {
                continue;
            }

            var world = node.WorldMatrix;
            foreach (var primitive in node.Mesh!.Primitives)
            {
                var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
                var texCoords = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
                if (positions is null || texCoords is null || positions.Count == 0 || texCoords.Count == 0)
                {
                    continue;
                }

                var indices = primitive.GetIndices();
                for (var index = 0; index + 2 < indices.Count; index += 3)
                {
                    var i0 = Convert.ToInt32(indices[index]);
                    var i1 = Convert.ToInt32(indices[index + 1]);
                    var i2 = Convert.ToInt32(indices[index + 2]);

                    if (i0 >= positions.Count || i1 >= positions.Count || i2 >= positions.Count ||
                        i0 >= texCoords.Count || i1 >= texCoords.Count || i2 >= texCoords.Count)
                    {
                        continue;
                    }

                    triangles.Add(new MeshTriangle(
                        ConvertModelVertex(positions[i0], world),
                        ConvertModelVertex(positions[i1], world),
                        ConvertModelVertex(positions[i2], world),
                        ConvertTextureCoordinate(texCoords[i0]),
                        ConvertTextureCoordinate(texCoords[i1]),
                        ConvertTextureCoordinate(texCoords[i2])));
                }
            }
        }

        return new GlbMeshModel(triangles.ToArray());
    }

    private static Vector3 ConvertModelVertex(Vector3 position, Matrix4x4 world)
    {
        var transformed = Vector3.Transform(position, world);
        return new Vector3(
            -transformed.X * ModelScale,
            transformed.Y * ModelScale - 16f,
            -transformed.Z * ModelScale);
    }

    private static Vector2 ConvertTextureCoordinate(Vector2 texCoord)
    {
        return new Vector2(
            Math.Clamp(texCoord.X, 0f, 1f),
            Math.Clamp(texCoord.Y, 0f, 1f));
    }

    private static string ResolveModelAssetPath(string fileName)
    {
        var outputPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Models", "Binary", fileName);
        if (File.Exists(outputPath))
        {
            return outputPath;
        }

        var sourcePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Models", "Binary", fileName));
        if (File.Exists(sourcePath))
        {
            return sourcePath;
        }

        throw new FileNotFoundException($"Model asset '{fileName}' was not found.", outputPath);
    }

    private static Pixbuf? LoadPreviewBackground()
    {
        try
        {
            var outputPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "avatarbg.jpg");
            if (File.Exists(outputPath))
            {
                return new Pixbuf(outputPath);
            }

            var sourcePath = System.IO.Path.GetFullPath(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "Images", "avatarbg.jpg"));
            return File.Exists(sourcePath) ? new Pixbuf(sourcePath) : null;
        }
        catch
        {
            return null;
        }
    }

    private readonly record struct MeshTriangle(Vector3 P0, Vector3 P1, Vector3 P2, Vector2 Uv0, Vector2 Uv1, Vector2 Uv2);
    private sealed record GlbMeshModel(MeshTriangle[] Triangles);
    private readonly record struct ProjectedVertex(float X, float Y, float Z, float U, float V);
    private readonly record struct TexturedTriangle(ProjectedVertex V0, ProjectedVertex V1, ProjectedVertex V2, SixLabors.ImageSharp.Image<Rgba32> Texture);
}
