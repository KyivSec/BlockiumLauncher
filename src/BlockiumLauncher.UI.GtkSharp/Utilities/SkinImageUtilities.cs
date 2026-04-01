using Gdk;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Utilities;

internal static class SkinImageUtilities
{
    private const string AlexSkinPath = @"C:\Users\Admin\Desktop\Skins\alex.png";
    private const string SteveSkinPath = @"C:\Users\Admin\Desktop\Skins\steve.png";

    public static string GetFallbackSkinPath(string? username = null)
    {
        if (!string.IsNullOrWhiteSpace(username) &&
            username.Contains("alex", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(AlexSkinPath))
        {
            return AlexSkinPath;
        }

        if (File.Exists(SteveSkinPath))
        {
            return SteveSkinPath;
        }

        return AlexSkinPath;
    }

    public static Widget CreateSkinHeadWidget(string? skinPath, int size)
    {
        if (TryCreateHeadPixbuf(skinPath, size, out var pixbuf))
        {
            return new Image(pixbuf)
            {
                Halign = Align.Center,
                Valign = Align.Center
            };
        }

        var fallback = new DrawingArea
        {
            WidthRequest = size,
            HeightRequest = size,
            Halign = Align.Center,
            Valign = Align.Center
        };
        fallback.Drawn += (_, args) =>
        {
            args.Cr.SetSourceRGB(0.18, 0.48, 0.86);
            args.Cr.Rectangle(0, 0, size, size);
            args.Cr.Fill();
        };
        return fallback;
    }

    public static bool TryCreateHeadPixbuf(string? skinPath, int size, out Pixbuf? pixbuf)
    {
        pixbuf = null;
        var effectivePath = ResolveSkinPath(skinPath);
        if (effectivePath is null)
        {
            return false;
        }

        try
        {
            using var skin = new Pixbuf(effectivePath);
            using var face = new Pixbuf(skin, 8, 8, 8, 8);

            var head = new Pixbuf(Colorspace.Rgb, true, 8, 8, 8);
            head.Fill(0x00000000);
            face.CopyArea(0, 0, 8, 8, head, 0, 0);

            if (skin.Width >= 48 && skin.Height >= 16)
            {
                using var overlay = new Pixbuf(skin, 40, 8, 8, 8);
                overlay.Composite(head, 0, 0, 8, 8, 0, 0, 1, 1, InterpType.Nearest, 255);
            }

            pixbuf = head.ScaleSimple(size, size, InterpType.Nearest);
            return pixbuf is not null;
        }
        catch
        {
            pixbuf = null;
            return false;
        }
    }

    public static string? ResolveSkinPath(string? preferredSkinPath, string? username = null)
    {
        if (!string.IsNullOrWhiteSpace(preferredSkinPath) && File.Exists(preferredSkinPath))
        {
            return preferredSkinPath;
        }

        var fallback = GetFallbackSkinPath(username);
        return File.Exists(fallback) ? fallback : null;
    }
}
