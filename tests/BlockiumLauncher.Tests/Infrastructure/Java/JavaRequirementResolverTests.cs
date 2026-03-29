using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Java;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Java;

public sealed class JavaRequirementResolverTests
{
    [Theory]
    [InlineData("1.16.5", 8)]
    [InlineData("1.17.1", 16)]
    [InlineData("1.18.2", 17)]
    [InlineData("1.20.4", 17)]
    public void GetRequiredJavaMajor_ReturnsExpectedReleaseTarget(string gameVersion, int expectedMajor)
    {
        var resolver = new JavaRequirementResolver();

        var result = resolver.GetRequiredJavaMajor(gameVersion, LoaderType.Vanilla);

        Assert.Equal(expectedMajor, result);
    }

    [Fact]
    public void IsSatisfiedBy_ReturnsTrue_WhenInstalledMajorMatchesRequirement()
    {
        var resolver = new JavaRequirementResolver();

        var result = resolver.IsSatisfiedBy(17, "1.20.1", LoaderType.Fabric);

        Assert.True(result);
    }

    [Fact]
    public void IsSatisfiedBy_ReturnsFalse_WhenInstalledMajorDoesNotMatchRequirement()
    {
        var resolver = new JavaRequirementResolver();

        var result = resolver.IsSatisfiedBy(8, "1.20.1", LoaderType.NeoForge);

        Assert.False(result);
    }
}
