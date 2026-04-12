using System.Runtime.InteropServices;

namespace BlockiumLauncher.UI.GtkSharp.Utilities;

internal static class LauncherNativeDialogs
{
    private const uint OkIconError = 0x00000010;

    public static bool TryShowError(string title, string message)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            MessageBoxW(IntPtr.Zero, message, title, OkIconError);
            return true;
        }
        catch
        {
            return false;
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string lpText, string lpCaption, uint uType);
}
