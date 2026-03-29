using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Infrastructure.Java;
using Xunit;

namespace BlockiumLauncher.Infrastructure.Tests.Java;

public sealed class JavaVersionOutputParserTests
{
    [Fact]
    public void Parse_ParsesOpenJdk21Output()
    {
        var Output = """
        openjdk version "21.0.2" 2024-01-16 LTS
        OpenJDK Runtime Environment Temurin-21.0.2+13 (build 21.0.2+13-LTS)
        OpenJDK 64-Bit Server VM Temurin-21.0.2+13 (build 21.0.2+13-LTS, mixed mode, sharing)
        """;

        var Result = JavaVersionOutputParser.Parse(Output);

        Assert.True(Result.IsSuccess);
        Assert.Equal("21.0.2", Result.Value.Version);
        Assert.Equal(JavaArchitecture.X64, Result.Value.Architecture);
        Assert.Equal("Eclipse Adoptium", Result.Value.Vendor);
    }

    [Fact]
    public void Parse_ParsesOracleJava8Output()
    {
        var Output = """
        java version "1.8.0_381"
        Java(TM) SE Runtime Environment (build 1.8.0_381-b09)
        Java HotSpot(TM) 64-Bit Server VM (build 25.381-b09, mixed mode)
        """;

        var Result = JavaVersionOutputParser.Parse(Output);

        Assert.True(Result.IsSuccess);
        Assert.Equal("1.8.0_381", Result.Value.Version);
        Assert.Equal(JavaArchitecture.X64, Result.Value.Architecture);
        Assert.Equal("Unknown", Result.Value.Vendor);
    }

    [Fact]
    public void Parse_ParsesArm64Output()
    {
        var Output = """
        openjdk version "17.0.10" 2024-01-16
        OpenJDK Runtime Environment Microsoft-123456 (build 17.0.10+7)
        OpenJDK 64-Bit Server VM Microsoft-123456 (build 17.0.10+7, mixed mode, sharing, aarch64)
        """;

        var Result = JavaVersionOutputParser.Parse(Output);

        Assert.True(Result.IsSuccess);
        Assert.Equal(JavaArchitecture.Arm64, Result.Value.Architecture);
        Assert.Equal("Microsoft", Result.Value.Vendor);
    }

    [Fact]
    public void Parse_ReturnsFailure_ForMalformedOutput()
    {
        var Result = JavaVersionOutputParser.Parse("this is not java output");

        Assert.True(Result.IsFailure);
    }
}
