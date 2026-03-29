using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class MainWindow : Window
{
    public MainWindow() : base("BlockiumLauncher")
    {
        SetDefaultSize(1100, 700);

        DeleteEvent += (_, Args) =>
        {
            Args.RetVal = false;
            Gtk.Application.Quit();
        };
    }
}
