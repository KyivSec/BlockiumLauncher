using Xunit;
using BlockiumLauncher.Shared.Primitives;

namespace BlockiumLauncher.Shared.Tests;

public sealed class UnitTests
{
    [Fact]
    public void Value_EqualsDefault()
    {
        Assert.Equal(Unit.Value, default(Unit));
    }

    [Fact]
    public void Equality_Works()
    {
        Assert.Equal(Unit.Value, Unit.Value);
    }
}
