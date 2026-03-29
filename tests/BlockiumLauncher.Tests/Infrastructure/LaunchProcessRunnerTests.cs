using BlockiumLauncher.Application.UseCases.Launch;
using BlockiumLauncher.Contracts.Launch;
using BlockiumLauncher.Infrastructure.Launch;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Launch;

public sealed class LaunchProcessRunnerTests
{
    [Fact]
    public async Task StartAsync_CapturesOutput_And_ExitCode()
    {
        var Runner = new LaunchProcessRunner();
        var Plan = CreateDotnetInfoPlan();

        var StartResult = await Runner.StartAsync(Plan);
        Assert.True(StartResult.IsSuccess);

        var LaunchId = StartResult.Value.LaunchId;

        for (var Attempt = 0; Attempt < 100; Attempt++)
        {
            await Task.Delay(100);
            var StatusResult = await Runner.GetStatusAsync(LaunchId);
            Assert.True(StatusResult.IsSuccess);

            if (StatusResult.Value.HasExited)
            {
                Assert.NotNull(StatusResult.Value.ExitCode);
                Assert.True(StatusResult.Value.OutputLines.Count > 0);
                return;
            }
        }

        Assert.Fail("Process did not exit in time.");
    }

    [Fact]
    public async Task StopAsync_Stops_LongRunning_Process()
    {
        var Runner = new LaunchProcessRunner();
        var Plan = CreateLongRunningPlan();

        var StartResult = await Runner.StartAsync(Plan);
        Assert.True(StartResult.IsSuccess);
        Assert.True(StartResult.Value.IsRunning);

        var StopResult = await Runner.StopAsync(StartResult.Value.LaunchId);
        Assert.True(StopResult.IsSuccess);

        for (var Attempt = 0; Attempt < 50; Attempt++)
        {
            await Task.Delay(100);
            var StatusResult = await Runner.GetStatusAsync(StartResult.Value.LaunchId);
            Assert.True(StatusResult.IsSuccess);

            if (StatusResult.Value.HasExited)
            {
                Assert.False(StatusResult.Value.IsRunning);
                return;
            }
        }

        Assert.Fail("Process did not stop in time.");
    }

    private static string GetDotnetExecutablePath()
    {
        var DotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(DotnetRoot))
        {
            var Candidate = Path.Combine(DotnetRoot, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (File.Exists(Candidate))
            {
                return Candidate;
            }
        }

        if (OperatingSystem.IsWindows())
        {
            var Candidate = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "dotnet.exe");
            if (File.Exists(Candidate))
            {
                return Candidate;
            }
        }
        else
        {
            foreach (var Candidate in new[] { "/usr/bin/dotnet", "/usr/local/bin/dotnet" })
            {
                if (File.Exists(Candidate))
                {
                    return Candidate;
                }
            }
        }

        throw new FileNotFoundException("Could not resolve the dotnet executable path.");
    }

    private static LaunchPlanDto CreateDotnetInfoPlan()
    {
        return new LaunchPlanDto
        {
            InstanceId = "instance-1",
            AccountId = "offline-1",
            JavaExecutablePath = GetDotnetExecutablePath(),
            WorkingDirectory = Directory.GetCurrentDirectory(),
            MainClass = "--info",
            JvmArguments = [],
            GameArguments = [],
            EnvironmentVariables = [],
            IsDryRun = false
        };
    }

    private static LaunchPlanDto CreateLongRunningPlan()
    {
        if (OperatingSystem.IsWindows())
        {
            var CmdPath = Environment.GetEnvironmentVariable("ComSpec");
            if (string.IsNullOrWhiteSpace(CmdPath) || !File.Exists(CmdPath))
            {
                throw new FileNotFoundException("Could not resolve cmd.exe.");
            }

            return new LaunchPlanDto
            {
                InstanceId = "instance-1",
                AccountId = "offline-1",
                JavaExecutablePath = CmdPath,
                WorkingDirectory = Directory.GetCurrentDirectory(),
                MainClass = "/c",
                JvmArguments = [],
                GameArguments =
                [
                    new LaunchArgumentDto { Value = "ping" },
                    new LaunchArgumentDto { Value = "127.0.0.1" },
                    new LaunchArgumentDto { Value = "-n" },
                    new LaunchArgumentDto { Value = "30" }
                ],
                EnvironmentVariables = [],
                IsDryRun = false
            };
        }

        return new LaunchPlanDto
        {
            InstanceId = "instance-1",
            AccountId = "offline-1",
            JavaExecutablePath = "/bin/sh",
            WorkingDirectory = Directory.GetCurrentDirectory(),
            MainClass = "-c",
            JvmArguments = [],
            GameArguments =
            [
                new LaunchArgumentDto { Value = "sleep 30" }
            ],
            EnvironmentVariables = [],
            IsDryRun = false
        };
    }
}