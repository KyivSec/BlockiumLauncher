using BlockiumLauncher.Application.Abstractions.Paths;
using BlockiumLauncher.Backend.Composition;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Styling;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using BlockiumLauncher.UI.GtkSharp.Windows;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using System.Threading;

var services = new ServiceCollection();

services.AddBlockiumLauncherBackend();
services.AddSingleton<LauncherUiSettingsStore>();
services.AddSingleton<LauncherThemeCatalogService>();
services.AddSingleton<LauncherUiPreferencesService>();
services.AddSingleton<LauncherGtkThemeService>();
services.AddSingleton<ProviderMediaCacheService>();
services.AddSingleton<ContentArchiveIconCacheService>();
services.AddSingleton<InstanceBrowserRefreshService>();
services.AddSingleton<LaunchController>();
services.AddTransient<AddInstanceWindow>();
services.AddTransient<SkinCustomizationWindow>();
services.AddTransient<AccountsWindow>();
services.AddTransient<SettingsWindow>();
services.AddTransient<EditInstanceWindow>();
services.AddTransient<ManualDownloadWindow>();
services.AddTransient<CatalogContentBrowserWindow>();
services.AddSingleton<MainWindow>();

var serviceProvider = services.BuildServiceProvider();
var launcherPaths = serviceProvider.GetRequiredService<ILauncherPaths>();
MainWindow? mainWindow = null;
var gtkInitialized = false;
var gtkMainLoopRunning = false;
var crashDialogActive = 0;

void AppendCrashLog(string origin, Exception exception)
{
    try
    {
        var logDirectory = Path.GetDirectoryName(launcherPaths.LatestLogFilePath);
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        var builder = new StringBuilder()
            .Append('[')
            .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"))
            .Append("] GTK unhandled exception (")
            .Append(origin)
            .AppendLine(")")
            .AppendLine(exception.ToString())
            .AppendLine();

        File.AppendAllText(launcherPaths.LatestLogFilePath, builder.ToString());
    }
    catch
    {
    }
}

void ShowCrashDialog(string origin, Exception exception)
{
    AppendCrashLog(origin, exception);

    if (Interlocked.Exchange(ref crashDialogActive, 1) == 1)
    {
        return;
    }

    try
    {
        var title = $"Unexpected launcher error ({origin})";

        if (!gtkInitialized || !gtkMainLoopRunning)
        {
            try
            {
                var details = new StringBuilder()
                    .AppendLine(title)
                    .AppendLine()
                    .AppendLine(exception.ToString())
                    .AppendLine()
                    .Append("Log file: ")
                    .Append(launcherPaths.LatestLogFilePath)
                    .ToString();

                if (!LauncherNativeDialogs.TryShowError(title, details))
                {
                    Console.Error.WriteLine(details);
                }
            }
            finally
            {
                Interlocked.Exchange(ref crashDialogActive, 0);
            }

            return;
        }

        void PresentCrashDialog()
        {
            try
            {
                LauncherGtkChrome.ShowCrashDialog(mainWindow, title, exception);
            }
            finally
            {
                Interlocked.Exchange(ref crashDialogActive, 0);
            }
        }

        Application.Invoke((_, _) => PresentCrashDialog());
    }
    catch
    {
        Interlocked.Exchange(ref crashDialogActive, 0);
    }
}

try
{
    var uiPreferences = serviceProvider.GetRequiredService<LauncherUiPreferencesService>();
    await uiPreferences.InitializeAsync();

    LauncherFontBootstrapper.Initialize();

    Application.Init();
    gtkInitialized = true;

    AppDomain.CurrentDomain.UnhandledException += (_, args) =>
    {
        if (args.ExceptionObject is Exception exception)
        {
            ShowCrashDialog("AppDomain", exception);
        }
    };

    TaskScheduler.UnobservedTaskException += (_, args) =>
    {
        ShowCrashDialog("TaskScheduler", args.Exception);
        args.SetObserved();
    };

    serviceProvider.GetRequiredService<LauncherGtkThemeService>().Initialize();

    mainWindow = serviceProvider.GetRequiredService<MainWindow>();
    mainWindow.ShowAll();

    gtkMainLoopRunning = true;
    try
    {
        Application.Run();
    }
    finally
    {
        gtkMainLoopRunning = false;
    }
}
catch (Exception exception)
{
    ShowCrashDialog("Startup", exception);
}

// Intentionally do not dispose the root service provider on shutdown.
//
// GTK windows resolved through the root provider are already destroyed by the
// normal application shutdown flow. Letting the DI container dispose those
// Gtk.Window instances again during provider teardown causes GObject toggle-ref
// assertions and can crash the process on exit.
