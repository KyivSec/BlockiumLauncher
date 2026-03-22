using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Domain.ValueObjects;
using BlockiumLauncher.Shared.Results;
using Xunit;

namespace BlockiumLauncher.Application.Tests.Install;

public sealed class InstallPlanBuilderTests
{
    [Fact]
    public async Task BuildAsync_ReturnsFailure_ForInvalidRequest()
    {
        var Builder = new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService());

        var Result = await Builder.BuildAsync(new InstallInstanceRequest());

        Assert.True(Result.IsFailure);
        Assert.Equal("Install.InvalidRequest", Result.Error.Code);
    }

    [Fact]
    public async Task BuildAsync_ReturnsSuccess_ForVanillaRequest()
    {
        var Builder = new InstallPlanBuilder(new FakeVersionManifestService(), new FakeLoaderMetadataService());

        var Result = await Builder.BuildAsync(new InstallInstanceRequest
        {
            InstanceName = "Test",
            GameVersion = "1.20.1",
            LoaderType = LoaderType.Vanilla
        });

        Assert.True(Result.IsSuccess);
        Assert.Equal("Test", Result.Value.InstanceName);
        Assert.Equal("1.20.1", Result.Value.GameVersion);
        Assert.NotEmpty(Result.Value.Steps);
    }

    private sealed class FakeVersionManifestService : IVersionManifestService
    {
        public Task<Result<IReadOnlyList<VersionSummary>>> GetAvailableVersionsAsync(CancellationToken CancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<VersionSummary>>.Success(
            [
                new VersionSummary(CreateVersionId("1.20.1"), "1.20.1", true, DateTimeOffset.UtcNow)
            ]));

        public Task<Result<VersionSummary?>> GetVersionAsync(VersionId VersionId, CancellationToken CancellationToken = default)
            => Task.FromResult(Result<VersionSummary?>.Success(
                new VersionSummary(VersionId, VersionId.ToString(), true, DateTimeOffset.UtcNow)));
    }

    private sealed class FakeLoaderMetadataService : ILoaderMetadataService
    {
        public Task<Result<IReadOnlyList<LoaderVersionSummary>>> GetLoaderVersionsAsync(
            LoaderType LoaderType,
            VersionId VersionId,
            CancellationToken CancellationToken = default)
            => Task.FromResult(Result<IReadOnlyList<LoaderVersionSummary>>.Success(
            [
                new LoaderVersionSummary(LoaderType, VersionId, "0.15.0", true)
            ]));
    }

    private static VersionId CreateVersionId(string Value)
    {
        var Type = typeof(VersionId);

        var ParseMethod = Type.GetMethod("Parse", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (ParseMethod is not null)
        {
            return (VersionId)ParseMethod.Invoke(null, [Value])!;
        }

        var CreateMethod = Type.GetMethod("Create", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static, null, new[] { typeof(string) }, null);
        if (CreateMethod is not null)
        {
            return (VersionId)CreateMethod.Invoke(null, [Value])!;
        }

        var Constructor = Type.GetConstructor([typeof(string)]);
        if (Constructor is not null)
        {
            return (VersionId)Constructor.Invoke([Value]);
        }

        throw new InvalidOperationException("Could not create VersionId.");
    }
}