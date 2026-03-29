using Xunit;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Tests;

public sealed class InstanceIdTests
{
    [Fact]
    public void New_ReturnsNonEmptyValue()
    {
        var TestInstanceId = InstanceId.New();

        Assert.False(string.IsNullOrWhiteSpace(TestInstanceId.Value));
    }

    [Fact]
    public void Constructor_BlankValue_Throws()
    {
        Action Act = () => _ = new InstanceId(" ");

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        var TestInstanceId = new InstanceId("abc");

        Assert.Equal("abc", TestInstanceId.ToString());
    }
}
