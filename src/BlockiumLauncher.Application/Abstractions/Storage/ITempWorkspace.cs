using System;
using System.Threading;
using System.Threading.Tasks;

namespace BlockiumLauncher.Application.Abstractions.Storage;

public interface ITempWorkspace : IAsyncDisposable
{
    string RootPath { get; }

    string GetPath(string RelativePath);

    Task CreateDirectoryAsync(string RelativePath, CancellationToken CancellationToken = default);
}