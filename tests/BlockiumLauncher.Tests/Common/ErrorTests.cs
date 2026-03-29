using Xunit;
using BlockiumLauncher.Shared.Errors;

namespace BlockiumLauncher.Shared.Tests;

public sealed class ErrorTests
{
    [Fact]
    public void Constructor_StoresValues()
    {
        var Error = new Error("Test.Code", "Test message", "Details");

        Assert.Equal("Test.Code", Error.Code);
        Assert.Equal("Test message", Error.Message);
        Assert.Equal("Details", Error.Details);
    }

    [Fact]
    public void Constructor_BlankCode_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Error(" ", "Test message"));
    }

    [Fact]
    public void Constructor_BlankMessage_Throws()
    {
        Assert.Throws<ArgumentException>(() => new Error("Test.Code", " "));
    }

    [Fact]
    public void None_HasExpectedValues()
    {
        Assert.Equal("None", Error.None.Code);
        Assert.Equal("No error.", Error.None.Message);
        Assert.Null(Error.None.Details);
    }
}
