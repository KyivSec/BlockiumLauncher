using Xunit;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Shared.Tests;

public sealed class ResultTests
{
    [Fact]
    public void Success_ReturnsSuccessWithNoneError()
    {
        var Result = global::BlockiumLauncher.Shared.Results.Result.Success();

        Assert.True(Result.IsSuccess);
        Assert.False(Result.IsFailure);
        Assert.Equal(Error.None, Result.Error);
    }

    [Fact]
    public void Failure_ReturnsFailureWithProvidedError()
    {
        var Error = new Error("Test.Code", "Test message");
        var Result = global::BlockiumLauncher.Shared.Results.Result.Failure(Error);

        Assert.False(Result.IsSuccess);
        Assert.True(Result.IsFailure);
        Assert.Equal(Error, Result.Error);
    }

    [Fact]
    public void Failure_WithNoneError_Throws()
    {
        Assert.Throws<ArgumentException>(() => global::BlockiumLauncher.Shared.Results.Result.Failure(Error.None));
    }
}
