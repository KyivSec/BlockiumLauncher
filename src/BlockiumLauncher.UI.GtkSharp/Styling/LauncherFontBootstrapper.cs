using System.Runtime.InteropServices;

namespace BlockiumLauncher.UI.GtkSharp.Styling;

internal static class LauncherFontBootstrapper
{
    private const uint FR_PRIVATE = 0x10;
    private static bool IsInitialized;

    public const string PreferredBodyFontFamily = "\"MiSans Latin\", \"Segoe UI\", \"Noto Sans\", sans-serif";
    public const string PreferredTitleFontFamily = "\"MiSans Latin\", \"Segoe UI\", \"Noto Sans\", sans-serif";

    public static void Initialize()
    {
        if (IsInitialized)
        {
            return;
        }

        IsInitialized = true;

        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        RegisterPrivateFont("MiSansLatin-Regular.ttf");
        RegisterPrivateFont("MiSansLatin-Demibold.ttf");
    }

    private static void RegisterPrivateFont(string fileName)
    {
        var fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", fileName);
        if (!File.Exists(fontPath))
        {
            return;
        }

        try
        {
            AddFontResourceEx(fontPath, FR_PRIVATE, IntPtr.Zero);
        }
        catch
        {
            // Fall back to the native font stack if private registration fails.
        }
    }

    [DllImport("gdi32.dll", EntryPoint = "AddFontResourceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int AddFontResourceEx(string name, uint fl, IntPtr res);
}
