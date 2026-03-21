using Xunit;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Tests;

public sealed class AccountIdTests
{
    [Fact]
    public void New_ReturnsNonEmptyValue()
    {
        var TestAccountId = AccountId.New();

        Assert.False(string.IsNullOrWhiteSpace(TestAccountId.Value));
    }

    [Fact]
    public void Constructor_BlankValue_Throws()
    {
        Action Act = () => _ = new AccountId(" ");

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        var TestAccountId = new AccountId("abc");

        Assert.Equal("abc", TestAccountId.ToString());
    }
}
