using Xunit;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Tests;

public sealed class LauncherAccountTests
{
    [Fact]
    public void CreateOffline_DoesNotRequireRefreshToken()
    {
        var TestLauncherAccount = LauncherAccount.CreateOffline(
            AccountId.New(),
            "Player",
            "AccessRef");

        Assert.Equal(AccountProvider.Offline, TestLauncherAccount.Provider);
        Assert.Null(TestLauncherAccount.RefreshTokenRef);
    }

    [Fact]
    public void CreateMicrosoft_RequiresRefreshToken()
    {
        Action Act = () => _ = LauncherAccount.CreateMicrosoft(
            AccountId.New(),
            "Player",
            "AccessRef",
            " ");

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void MarkRemoved_ClearsDefault()
    {
        var TestLauncherAccount = LauncherAccount.CreateOffline(
            AccountId.New(),
            "Player",
            "AccessRef",
            IsDefault: true);

        TestLauncherAccount.MarkRemoved();

        Assert.False(TestLauncherAccount.IsDefault);
        Assert.Equal(AccountState.Removed, TestLauncherAccount.State);
    }

    [Fact]
    public void RemovedAccount_CannotBeValidated()
    {
        var TestLauncherAccount = LauncherAccount.CreateOffline(
            AccountId.New(),
            "Player",
            "AccessRef");

        TestLauncherAccount.MarkRemoved();

        Action Act = () => TestLauncherAccount.MarkValidated(DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(Act);
    }
}
