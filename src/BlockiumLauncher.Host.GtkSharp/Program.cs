using Gtk;
using Microsoft.Extensions.DependencyInjection;
using BlockiumLauncher.Host.GtkSharp.Windows;

var Services = new ServiceCollection();

Services.AddSingleton<MainWindow>();

using var ServiceProvider = Services.BuildServiceProvider();

Application.Init();

var MainWindow = ServiceProvider.GetRequiredService<MainWindow>();
MainWindow.ShowAll();

Application.Run();