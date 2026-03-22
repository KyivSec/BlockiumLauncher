using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.UseCases.Install;

public sealed class InstallPlanBuilder
{
    private readonly IVersionManifestService VersionManifestService;
    private readonly ILoaderMetadataService LoaderMetadataService;

    public InstallPlanBuilder(
        IVersionManifestService VersionManifestService,
        ILoaderMetadataService LoaderMetadataService)
    {
        this.VersionManifestService = VersionManifestService ?? throw new ArgumentNullException(nameof(VersionManifestService));
        this.LoaderMetadataService = LoaderMetadataService ?? throw new ArgumentNullException(nameof(LoaderMetadataService));
    }

    public async Task<Result<InstallPlan>> BuildAsync(
        InstallInstanceRequest Request,
        CancellationToken CancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(Request.InstanceName) ||
                string.IsNullOrWhiteSpace(Request.GameVersion))
            {
                return Result<InstallPlan>.Failure(InstallErrors.InvalidRequest);
            }

            var TargetDirectory = ResolveTargetDirectory(Request);
            if (string.IsNullOrWhiteSpace(TargetDirectory))
            {
                return Result<InstallPlan>.Failure(InstallErrors.TargetPathInvalid);
            }

            var VersionExists = await TryValidateGameVersionAsync(Request.GameVersion, CancellationToken).ConfigureAwait(false);
            if (!VersionExists)
            {
                return Result<InstallPlan>.Failure(InstallErrors.VersionNotFound);
            }

            var IsVanilla = Request.LoaderType == LoaderType.Vanilla;
            if (!IsVanilla)
            {
                if (string.IsNullOrWhiteSpace(Request.LoaderVersion))
                {
                    return Result<InstallPlan>.Failure(InstallErrors.InvalidRequest);
                }

                var LoaderExists = await TryValidateLoaderVersionAsync(Request.LoaderType, Request.GameVersion, Request.LoaderVersion!, CancellationToken).ConfigureAwait(false);
                if (!LoaderExists)
                {
                    return Result<InstallPlan>.Failure(InstallErrors.LoaderNotFound);
                }
            }

            var Steps = new List<InstallPlanStep>
            {
                new()
                {
                    Kind = InstallPlanStepKind.CreateDirectory,
                    Destination = ".minecraft",
                    Description = "Create base instance directory"
                },
                new()
                {
                    Kind = InstallPlanStepKind.CreateDirectory,
                    Destination = ".minecraft\\mods",
                    Description = "Create mods directory"
                },
                new()
                {
                    Kind = InstallPlanStepKind.CreateDirectory,
                    Destination = ".minecraft\\config",
                    Description = "Create config directory"
                },
                new()
                {
                    Kind = InstallPlanStepKind.WriteMetadata,
                    Destination = ".blockium\\install-plan.json",
                    Description = "Write install metadata"
                }
            };

            var Plan = new InstallPlan
            {
                InstanceName = Request.InstanceName.Trim(),
                GameVersion = Request.GameVersion.Trim(),
                LoaderType = Request.LoaderType,
                LoaderVersion = string.IsNullOrWhiteSpace(Request.LoaderVersion) ? null : Request.LoaderVersion.Trim(),
                TargetDirectory = TargetDirectory,
                Steps = Steps
            };

            return Result<InstallPlan>.Success(Plan);
        }
        catch
        {
            return Result<InstallPlan>.Failure(InstallErrors.Unexpected);
        }
    }

    private static string ResolveTargetDirectory(InstallInstanceRequest Request)
    {
        if (!string.IsNullOrWhiteSpace(Request.TargetDirectory))
        {
            return Path.GetFullPath(Request.TargetDirectory.Trim());
        }

        var SafeName = string.Join(
            "_",
            Request.InstanceName
                .Trim()
                .Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "instances", SafeName));
    }

    private async Task<bool> TryValidateGameVersionAsync(string GameVersion, CancellationToken CancellationToken)
    {
        var Values = await CollectValuesAsync(VersionManifestService, CancellationToken).ConfigureAwait(false);
        if (Values.Count == 0)
        {
            return true;
        }

        return Values.Any(Value => MatchesVersion(Value, GameVersion));
    }

    private async Task<bool> TryValidateLoaderVersionAsync(LoaderType LoaderType, string GameVersion, string LoaderVersion, CancellationToken CancellationToken)
    {
        var Values = await CollectValuesAsync(LoaderMetadataService, CancellationToken).ConfigureAwait(false);
        if (Values.Count == 0)
        {
            return true;
        }

        return Values.Any(Value => MatchesLoaderVersion(Value, LoaderType, GameVersion, LoaderVersion));
    }

    private static async Task<List<object?>> CollectValuesAsync(object Service, CancellationToken CancellationToken)
    {
        var Results = new List<object?>();

        var Methods = Service.GetType()
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(Method => Method.ReturnType != typeof(void))
            .Where(Method => Method.Name.Contains("List", StringComparison.OrdinalIgnoreCase) ||
                             Method.Name.Contains("Get", StringComparison.OrdinalIgnoreCase) ||
                             Method.Name.Contains("Fetch", StringComparison.OrdinalIgnoreCase) ||
                             Method.Name.Contains("Load", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        foreach (var Method in Methods)
        {
            var Parameters = Method.GetParameters();
            var Arguments = new object?[Parameters.Length];
            var Valid = true;

            for (var Index = 0; Index < Parameters.Length; Index++)
            {
                var Parameter = Parameters[Index];

                if (Parameter.ParameterType == typeof(CancellationToken))
                {
                    Arguments[Index] = CancellationToken;
                    continue;
                }

                if (Parameter.HasDefaultValue)
                {
                    Arguments[Index] = Parameter.DefaultValue;
                    continue;
                }

                Valid = false;
                break;
            }

            if (!Valid)
            {
                continue;
            }

            try
            {
                var InvocationResult = Method.Invoke(Service, Arguments);
                if (InvocationResult is Task TaskResult)
                {
                    await TaskResult.ConfigureAwait(false);
                    InvocationResult = GetTaskResult(TaskResult);
                }

                FlattenValue(InvocationResult, Results);
            }
            catch
            {
            }
        }

        return Results;
    }

    private static object? GetTaskResult(Task TaskResult)
    {
        return TaskResult.GetType().GetProperty("Result", BindingFlags.Instance | BindingFlags.Public)?.GetValue(TaskResult);
    }

    private static void FlattenValue(object? Value, List<object?> Results)
    {
        if (Value is null)
        {
            return;
        }

        var Type = Value.GetType();
        if (Type.IsGenericType && Type.Name.StartsWith("Result", StringComparison.OrdinalIgnoreCase))
        {
            var ValueProperty = Type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
            FlattenValue(ValueProperty?.GetValue(Value), Results);
            return;
        }

        if (Value is string)
        {
            Results.Add(Value);
            return;
        }

        if (Value is IEnumerable EnumerableValue)
        {
            foreach (var Item in EnumerableValue)
            {
                FlattenValue(Item, Results);
            }

            return;
        }

        Results.Add(Value);
    }

    private static bool MatchesVersion(object? Value, string GameVersion)
    {
        if (Value is null)
        {
            return false;
        }

        if (Value is string Text)
        {
            return string.Equals(Text, GameVersion, StringComparison.OrdinalIgnoreCase);
        }

        var Type = Value.GetType();
        foreach (var PropertyName in new[] { "Version", "Id", "Name" })
        {
            var Property = Type.GetProperty(PropertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
            var PropertyValue = Property?.GetValue(Value)?.ToString();
            if (string.Equals(PropertyValue, GameVersion, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var VersionIdProperty = Type.GetProperty("VersionId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase);
        var VersionIdValue = VersionIdProperty?.GetValue(Value)?.ToString();
        return string.Equals(VersionIdValue, GameVersion, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesLoaderVersion(object? Value, LoaderType LoaderType, string GameVersion, string LoaderVersion)
    {
        if (Value is null)
        {
            return false;
        }

        var Type = Value.GetType();

        var LoaderTypeValue = Type.GetProperty("LoaderType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(Value)?.ToString();
        var VersionIdValue = Type.GetProperty("VersionId", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(Value)?.ToString();
        var LoaderVersionValue = Type.GetProperty("LoaderVersion", BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase)?.GetValue(Value)?.ToString();

        if (!string.IsNullOrWhiteSpace(LoaderTypeValue) &&
            !string.Equals(LoaderTypeValue, LoaderType.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(VersionIdValue, GameVersion, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(LoaderVersionValue, LoaderVersion, StringComparison.OrdinalIgnoreCase);
    }
}