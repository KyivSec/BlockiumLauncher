using Xunit;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Shared.Tests;

public sealed class ResultOfTTests
{
    [Fact]
    public void Success_ReturnsValue()
    {
        var Result = Result<int>.Success(42);

        Assert.True(Result.IsSuccess);
        Assert.False(Result.IsFailure);
        Assert.Equal(Error.None, Result.Error);
        Assert.Equal(42, Result.Value);
    }

    [Fact]
    public void Failure_ReturnsFailure()
    {
        var Error = new Error("Test.Code", "Test message");
        var Result = Result<int>.Failure(Error);

        Assert.False(Result.IsSuccess);
        Assert.True(Result.IsFailure);
        Assert.Equal(Error, Result.Error);
    }

    [Fact]
    public void Failure_ValueAccess_Throws()
    {
        var Result = Result<int>.Failure(new Error("Test.Code", "Test message"));

        Assert.Throws<InvalidOperationException>(() => _ = Result.Value);
    }

    [Fact]
    public void Failure_WithNoneError_Throws()
    {
        Assert.Throws<ArgumentException>(() => Result<int>.Failure(Error.None));
    }
}
