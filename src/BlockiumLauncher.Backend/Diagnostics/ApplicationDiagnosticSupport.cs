using BlockiumLauncher.Application.Abstractions.Diagnostics;
using BlockiumLauncher.Application.Abstractions.Instances;
using BlockiumLauncher.Application.Abstractions.Services;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.Domain.Enums;
using BlockiumLauncher.Shared.Errors;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Application.Diagnostics
{
    public sealed class DefaultOperationContextFactory : IOperationContextFactory
    {
        public static readonly DefaultOperationContextFactory Instance = new();

        public OperationContext Create(string OperationName)
        {
            return new OperationContext
            {
                OperationId = Guid.NewGuid().ToString("N"),
                OperationName = OperationName,
                StartedAtUtc = DateTimeOffset.UtcNow
            };
        }
    }

    public sealed class NoOpJavaRuntimeResolver : IJavaRuntimeResolver
    {
        public static readonly NoOpJavaRuntimeResolver Instance = new();

        public Task<Result<string>> ResolveExecutablePathAsync(
            string MinecraftVersion,
            LoaderType loaderType,
            int? preferredJavaMajor = null,
            bool skipCompatibilityChecks = false,
            CancellationToken CancellationToken = default)
        {
            return Task.FromResult(Result<string>.Failure(new Error(
                "Java.ResolveUnavailable",
                "Automatic Java resolution is not available.")));
        }
    }

    public sealed class NoOpSecretRedactor : ISecretRedactor
    {
        public static readonly NoOpSecretRedactor Instance = new();

        public string Redact(string Value)
        {
            return Value;
        }
    }

    public sealed class NullStructuredLogger : IStructuredLogger
    {
        public static readonly NullStructuredLogger Instance = new();

        public void Info(OperationContext Context, string Source, string EventName, string Message, object? Data = null)
        {
        }

        public void Warning(OperationContext Context, string Source, string EventName, string Message, object? Data = null)
        {
        }

        public void Error(OperationContext Context, string Source, string EventName, string Message, object? Data = null, Exception? Exception = null)
        {
        }
    }

    public sealed class NoOpInstanceContentMetadataService : IInstanceContentMetadataService
    {
        public static readonly NoOpInstanceContentMetadataService Instance = new();

        private NoOpInstanceContentMetadataService()
        {
        }

        public Task<InstanceContentMetadata?> GetAsync(LauncherInstance instance, bool reindexIfMissing = false, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<InstanceContentMetadata?>(null);
        }

        public Task<InstanceContentMetadata> ReindexAsync(LauncherInstance instance, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> SetModEnabledAsync(LauncherInstance instance, string modReference, bool enabled, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> SetContentEnabledAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, bool enabled, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> DeleteContentAsync(LauncherInstance instance, InstanceContentCategory category, string contentReference, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> RecordLaunchAsync(LauncherInstance instance, DateTimeOffset startedAtUtc, DateTimeOffset exitedAtUtc, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }

        public Task<InstanceContentMetadata> ApplySourcesAsync(
            LauncherInstance instance,
            IReadOnlyDictionary<string, ContentSourceMetadata> sourcesByRelativePath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new InstanceContentMetadata());
        }
    }
}
