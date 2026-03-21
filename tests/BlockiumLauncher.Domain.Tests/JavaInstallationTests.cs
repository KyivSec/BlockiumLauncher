using Xunit;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;

namespace BlockiumLauncher.Domain.Tests;

public sealed class JavaInstallationTests
{
    [Fact]
    public void Create_BlankExecutablePath_Throws()
    {
        Action Act = () => _ = JavaInstallation.Create(
            JavaInstallationId.New(),
            " ",
            "21.0.9",
            JavaArchitecture.X64,
            "Adoptium");

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Create_BlankVersion_Throws()
    {
        Action Act = () => _ = JavaInstallation.Create(
            JavaInstallationId.New(),
            @"C:\Java\bin\java.exe",
            " ",
            JavaArchitecture.X64,
            "Adoptium");

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void Create_BlankVendor_Throws()
    {
        Action Act = () => _ = JavaInstallation.Create(
            JavaInstallationId.New(),
            @"C:\Java\bin\java.exe",
            "21.0.9",
            JavaArchitecture.X64,
            " ");

        Assert.Throws<ArgumentException>(Act);
    }

    [Fact]
    public void MarkInvalid_FlipsValidity()
    {
        var TestJavaInstallation = JavaInstallation.Create(
            JavaInstallationId.New(),
            @"C:\Java\bin\java.exe",
            "21.0.9",
            JavaArchitecture.X64,
            "Adoptium");

        TestJavaInstallation.MarkInvalid();

        Assert.False(TestJavaInstallation.IsValid);
    }

    [Fact]
    public void MarkValid_RestoresValidity()
    {
        var TestJavaInstallation = JavaInstallation.Create(
            JavaInstallationId.New(),
            @"C:\Java\bin\java.exe",
            "21.0.9",
            JavaArchitecture.X64,
            "Adoptium",
            IsValid: false);

        TestJavaInstallation.MarkValid();

        Assert.True(TestJavaInstallation.IsValid);
    }
}
