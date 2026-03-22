using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Storage;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class TempWorkspace : ITempWorkspace
{
    public TempWorkspace(string RootPath)
    {
        this.RootPath = RootPath ?? throw new ArgumentNullException(nameof(RootPath));
        Directory.CreateDirectory(this.RootPath);
    }

    public string RootPath { get; }

    public string GetPath(string RelativePath)
    {
        if (string.IsNullOrWhiteSpace(RelativePath))
        {
            return RootPath;
        }

        var NormalizedRelativePath = RelativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        return Path.Combine(RootPath, NormalizedRelativePath);
    }

    public Task CreateDirectoryAsync(string RelativePath, CancellationToken CancellationToken = default)
    {
        CancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(GetPath(RelativePath));
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (Directory.Exists(RootPath))
            {
                Directory.Delete(RootPath, true);
            }
        }
        catch
        {
        }

        return ValueTask.CompletedTask;
    }
}