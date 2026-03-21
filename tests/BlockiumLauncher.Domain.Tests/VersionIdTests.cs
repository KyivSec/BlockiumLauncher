using Xunit;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Tests;

public sealed class VersionIdTests
{
    [Fact]
    public void Constructor_StoresTrimmedValue()
    {
        var VersionId = new VersionId("  neoforge-21.1.219  ");

        Assert.Equal("neoforge-21.1.219", VersionId.Value);
    }

    [Fact]
    public void Constructor_BlankValue_Throws()
    {
        Assert.Throws<ArgumentException>(() => new VersionId(" "));
    }

    [Fact]
    public void ToString_ReturnsUnderlyingValue()
    {
        var VersionId = new VersionId("1.21.1");

        Assert.Equal("1.21.1", VersionId.ToString());
    }
}
