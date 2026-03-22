using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;
using BlockiumLauncher.Application.Abstractions.Storage;
using BlockiumLauncher.Application.UseCases.Install;
using BlockiumLauncher.Shared.Primitives;
using BlockiumLauncher.Shared.Results;

namespace BlockiumLauncher.Infrastructure.Storage;

public sealed class ZipArchiveExtractor : IArchiveExtractor
{
    public Task<Result<Unit>> ExtractAsync(string ArchivePath, string DestinationPath, CancellationToken CancellationToken = default)
    {
        try
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(ArchivePath) || !File.Exists(ArchivePath))
            {
                return Task.FromResult(Result<Unit>.Failure(InstallErrors.ExtractFailed));
            }

            Directory.CreateDirectory(DestinationPath);

            using var Archive = ZipFile.OpenRead(ArchivePath);
            var DestinationRoot = Path.GetFullPath(DestinationPath);

            foreach (var Entry in Archive.Entries)
            {
                CancellationToken.ThrowIfCancellationRequested();

                var FullPath = Path.GetFullPath(Path.Combine(DestinationRoot, Entry.FullName));
                if (!FullPath.StartsWith(DestinationRoot, StringComparison.OrdinalIgnoreCase))
                {
                    return Task.FromResult(Result<Unit>.Failure(InstallErrors.ExtractFailed));
                }

                if (string.IsNullOrEmpty(Entry.Name))
                {
                    Directory.CreateDirectory(FullPath);
                    continue;
                }

                var ParentDirectory = Path.GetDirectoryName(FullPath);
                if (!string.IsNullOrWhiteSpace(ParentDirectory))
                {
                    Directory.CreateDirectory(ParentDirectory);
                }

                Entry.ExtractToFile(FullPath, true);
            }

            return Task.FromResult(Result<Unit>.Success(Unit.Value));
        }
        catch
        {
            return Task.FromResult(Result<Unit>.Failure(InstallErrors.ExtractFailed));
        }
    }
}