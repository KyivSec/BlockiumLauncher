using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime;

namespace BlockiumLauncher.UI.GtkSharp.Utilities;

internal static class LauncherWindowMemory
{
    public static void RequestAggressiveCleanup()
    {
        GLib.Idle.Add(() =>
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            TrimProcessWorkingSet();
            return false;
        });
    }

    private static void TrimProcessWorkingSet()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            using var process = Process.GetCurrentProcess();
            EmptyWorkingSet(process.Handle);
        }
        catch
        {
        }
    }

    [DllImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyWorkingSet(nint hProcess);
}
