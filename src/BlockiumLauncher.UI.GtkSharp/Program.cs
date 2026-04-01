using BlockiumLauncher.Backend.Composition;
using BlockiumLauncher.UI.GtkSharp.Services;
using BlockiumLauncher.UI.GtkSharp.Styling;
using BlockiumLauncher.UI.GtkSharp.Windows;
using Gtk;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();

services.AddBlockiumLauncherBackend();
services.AddSingleton<LauncherUiSettingsStore>();
services.AddSingleton<LauncherUiPreferencesService>();
services.AddSingleton<LauncherGtkThemeService>();
services.AddSingleton<InstanceBrowserRefreshService>();
services.AddSingleton<AddInstanceWindow>();
services.AddSingleton<SkinCustomizationWindow>();
services.AddSingleton<AccountsWindow>();
services.AddSingleton<SettingsWindow>();
services.AddSingleton<MainWindow>();

await using var serviceProvider = services.BuildServiceProvider();

var uiPreferences = serviceProvider.GetRequiredService<LauncherUiPreferencesService>();
await uiPreferences.InitializeAsync();

LauncherFontBootstrapper.Initialize();

Application.Init();

serviceProvider.GetRequiredService<LauncherGtkThemeService>().Initialize();

var mainWindow = serviceProvider.GetRequiredService<MainWindow>();
mainWindow.ShowAll();

Application.Run();
