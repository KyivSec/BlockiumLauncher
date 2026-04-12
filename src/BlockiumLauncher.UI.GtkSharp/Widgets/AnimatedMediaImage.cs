using BlockiumLauncher.UI.GtkSharp.Services;
using Gdk;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Widgets;

internal sealed class AnimatedMediaImage : Image
{
    private readonly IReadOnlyList<ProviderMediaCacheService.ProviderMediaAnimationFrame> frames;
    private bool disposed;

    public AnimatedMediaImage(IReadOnlyList<ProviderMediaCacheService.ProviderMediaAnimationFrame> frames)
    {
        this.frames = frames ?? throw new ArgumentNullException(nameof(frames));

        Halign = Align.Start;
        Valign = Align.Start;

        if (frames.Count > 0)
        {
            Pixbuf = frames[0].Pixbuf;
        }

        Destroyed += (_, _) => DisposeFrames();
    }

    private void DisposeFrames()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var frame in frames)
        {
            frame.Dispose();
        }
    }
}
