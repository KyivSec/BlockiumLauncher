using Microsoft.Extensions.DependencyInjection;

var Services = new ServiceCollection();

Services.AddSingleton<CliBootstrapMarker>();

using var ServiceProvider = Services.BuildServiceProvider();

_ = ServiceProvider.GetRequiredService<CliBootstrapMarker>();

Console.WriteLine("BlockiumLauncher CLI bootstrap ready.");

internal sealed class CliBootstrapMarker;