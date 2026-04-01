using System.Diagnostics;
using System.Text;

namespace BlockiumLauncher.UI.GtkSharp.Utilities;

internal static class DesktopShell
{
    public static void OpenUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException("URL cannot be empty.", nameof(url));
        }

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo("open")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(url);
            Process.Start(startInfo);
            return;
        }

        var linuxStartInfo = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false
        };
        linuxStartInfo.ArgumentList.Add(url);
        Process.Start(linuxStartInfo);
    }

    public static void OpenDirectory(string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath))
        {
            throw new ArgumentException("Directory path cannot be empty.", nameof(directoryPath));
        }

        Directory.CreateDirectory(directoryPath);

        if (OperatingSystem.IsWindows())
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directoryPath,
                UseShellExecute = true
            });

            return;
        }

        if (OperatingSystem.IsMacOS())
        {
            var startInfo = new ProcessStartInfo("open")
            {
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add(directoryPath);
            Process.Start(startInfo);
            return;
        }

        var linuxStartInfo = new ProcessStartInfo("xdg-open")
        {
            UseShellExecute = false
        };
        linuxStartInfo.ArgumentList.Add(directoryPath);
        Process.Start(linuxStartInfo);
    }

    public static string? PickPngFile(string title)
    {
        if (OperatingSystem.IsWindows())
        {
            var escapedTitle = title.Replace("'", "''", StringComparison.Ordinal);
            var script =
                "Add-Type -AssemblyName System.Windows.Forms\n" +
                "$dialog = New-Object System.Windows.Forms.OpenFileDialog\n" +
                "$dialog.Filter = 'PNG images (*.png)|*.png'\n" +
                $"$dialog.Title = '{escapedTitle}'\n" +
                "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {\n" +
                "    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
                "    [Console]::Write($dialog.FileName)\n" +
                "}";

            return RunAndCaptureFileSelection(new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"-NoProfile -STA -Command \"{script.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            });
        }

        if (OperatingSystem.IsMacOS())
        {
            var escapedTitle = title.Replace("\"", "\\\"", StringComparison.Ordinal);
            return RunAndCaptureFileSelection(new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"-e \"POSIX path of (choose file with prompt \\\"{escapedTitle}\\\" of type {{\\\"public.png\\\"}})\""
            });
        }

        try
        {
            return RunAndCaptureFileSelection(new ProcessStartInfo("zenity")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"--file-selection --title=\"{title.Replace("\"", "\\\"", StringComparison.Ordinal)}\" --file-filter=\"PNG images | *.png\""
            });
        }
        catch
        {
            return null;
        }
    }

    public static string? PickZipFile(string title)
    {
        if (OperatingSystem.IsWindows())
        {
            var escapedTitle = title.Replace("'", "''", StringComparison.Ordinal);
            var script =
                "Add-Type -AssemblyName System.Windows.Forms\n" +
                "$dialog = New-Object System.Windows.Forms.OpenFileDialog\n" +
                "$dialog.Filter = 'Archives (*.zip;*.mrpack)|*.zip;*.mrpack|ZIP archives (*.zip)|*.zip|Modrinth packs (*.mrpack)|*.mrpack'\n" +
                $"$dialog.Title = '{escapedTitle}'\n" +
                "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {\n" +
                "    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
                "    [Console]::Write($dialog.FileName)\n" +
                "}";

            return RunAndCaptureFileSelection(new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"-NoProfile -STA -Command \"{script.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            });
        }

        if (OperatingSystem.IsMacOS())
        {
            var escapedTitle = title.Replace("\"", "\\\"", StringComparison.Ordinal);
            return RunAndCaptureFileSelection(new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"-e \"POSIX path of (choose file with prompt \\\"{escapedTitle}\\\")\""
            });
        }

        try
        {
            return RunAndCaptureFileSelection(new ProcessStartInfo("zenity")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"--file-selection --title=\"{title.Replace("\"", "\\\"", StringComparison.Ordinal)}\" --file-filter=\"Archives | *.zip *.mrpack\""
            });
        }
        catch
        {
            return null;
        }
    }

    public static string? PickDirectory(string title)
    {
        if (OperatingSystem.IsWindows())
        {
            var escapedTitle = title.Replace("'", "''", StringComparison.Ordinal);
            var script =
                "Add-Type -AssemblyName System.Windows.Forms\n" +
                "$dialog = New-Object System.Windows.Forms.FolderBrowserDialog\n" +
                $"$dialog.Description = '{escapedTitle}'\n" +
                "$dialog.UseDescriptionForTitle = $true\n" +
                "if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {\n" +
                "    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8\n" +
                "    [Console]::Write($dialog.SelectedPath)\n" +
                "}";

            return RunAndCaptureFileSelection(new ProcessStartInfo("powershell")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"-NoProfile -STA -Command \"{script.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            });
        }

        if (OperatingSystem.IsMacOS())
        {
            var escapedTitle = title.Replace("\"", "\\\"", StringComparison.Ordinal);
            return RunAndCaptureFileSelection(new ProcessStartInfo("osascript")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"-e \"POSIX path of (choose folder with prompt \\\"{escapedTitle}\\\")\""
            });
        }

        try
        {
            return RunAndCaptureFileSelection(new ProcessStartInfo("zenity")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
                Arguments = $"--file-selection --directory --title=\"{title.Replace("\"", "\\\"", StringComparison.Ordinal)}\""
            });
        }
        catch
        {
            return null;
        }
    }

    private static string? RunAndCaptureFileSelection(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return null;
        }

        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return null;
        }

        var result = output.Trim();
        return string.IsNullOrWhiteSpace(result) ? null : result;
    }
}
