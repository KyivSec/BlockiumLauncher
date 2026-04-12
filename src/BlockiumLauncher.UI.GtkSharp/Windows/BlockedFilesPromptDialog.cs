using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Application.UseCases.Common;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

internal sealed class BlockedFilesPromptDialog : IDisposable
{
    private readonly Dialog dialog;
    private readonly BlockedModpackFilesPromptRequest request;
    private readonly Box fileList = new(Orientation.Vertical, 8);
    private readonly Label statusLabel;
    private readonly Button openMissingButton;
    private readonly Button reloadButton;
    private readonly Button continueButton;
    private readonly Dictionary<string, FileRowState> rows = new(StringComparer.OrdinalIgnoreCase);

    private FileSystemWatcher? watcher;
    private uint? rescanSourceId;
    private bool isDisposed;
    private bool scanInProgress;
    private bool rescanRequested;
    private int scanGeneration;
    private IReadOnlyList<PendingManualDownloadMatch> matches = [];

    private BlockedFilesPromptDialog(Gtk.Window owner, BlockedModpackFilesPromptRequest request)
    {
        this.request = request ?? throw new ArgumentNullException(nameof(request));

        dialog = LauncherGtkChrome.CreateFormDialog(
            owner,
            "Blocked CurseForge files",
            "CurseForge requires some files to be downloaded manually before the modpack import can continue.",
            resizable: true,
            width: 760);
        dialog.SetDefaultSize(760, 560);
        dialog.DeleteEvent += (_, args) =>
        {
            args.RetVal = true;
            dialog.Respond(ResponseType.Cancel);
        };

        var root = new Box(Orientation.Vertical, 0);
        root.StyleContext.AddClass("launcher-window-root");

        var shell = new EventBox();
        shell.StyleContext.AddClass("launcher-section-shell");

        var body = new Box(Orientation.Vertical, 14)
        {
            MarginTop = 18,
            MarginBottom = 18,
            MarginStart = 18,
            MarginEnd = 18
        };

        var summary = new Label(BuildSummaryText(request))
        {
            Xalign = 0,
            Wrap = true
        };
        summary.StyleContext.AddClass("settings-help");

        statusLabel = new Label("Checking the downloads folder for required files.")
        {
            Xalign = 0,
            Wrap = true
        };
        statusLabel.StyleContext.AddClass("manual-download-status");

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            MinContentHeight = 320,
            Hexpand = true,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("manual-download-list-scroller");
        scroller.Add(fileList);

        var footer = new Box(Orientation.Horizontal, 10)
        {
            Halign = Align.Fill
        };
        footer.StyleContext.AddClass("launcher-dialog-footer");

        openMissingButton = new Button("Open Missing");
        openMissingButton.StyleContext.AddClass("action-button");
        openMissingButton.Clicked += (_, _) => OpenMissingLinks();

        reloadButton = new Button("Reload");
        reloadButton.StyleContext.AddClass("action-button");
        reloadButton.Clicked += (_, _) => ScheduleRescan(immediate: true);

        var cancelButton = new Button("Cancel");
        cancelButton.StyleContext.AddClass("action-button");
        cancelButton.Clicked += (_, _) => dialog.Respond(ResponseType.Cancel);

        continueButton = new Button("Skip Missing and Continue");
        continueButton.StyleContext.AddClass("primary-button");
        continueButton.Clicked += (_, _) => dialog.Respond(ResponseType.Ok);

        footer.PackStart(openMissingButton, false, false, 0);
        footer.PackStart(reloadButton, false, false, 0);
        footer.PackEnd(continueButton, false, false, 0);
        footer.PackEnd(cancelButton, false, false, 0);

        body.PackStart(summary, false, false, 0);
        body.PackStart(statusLabel, false, false, 0);
        body.PackStart(scroller, true, true, 0);
        body.PackStart(footer, false, false, 0);
        shell.Add(body);
        root.PackStart(shell, true, true, 0);
        dialog.ContentArea.PackStart(root, true, true, 0);

        RebuildRows();
        UpdateStatusText();
        UpdateButtons();
    }

    public static Task<BlockedModpackFilesPromptResult> PromptAsync(
        Gtk.Window owner,
        BlockedModpackFilesPromptRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(request);

        var completion = new TaskCompletionSource<BlockedModpackFilesPromptResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        Gtk.Application.Invoke((_, _) =>
        {
            try
            {
                using var prompt = new BlockedFilesPromptDialog(owner, request);
                completion.TrySetResult(prompt.Run(cancellationToken));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        return completion.Task;
    }

    public BlockedModpackFilesPromptResult Run(CancellationToken cancellationToken)
    {
        using var cancellationRegistration = cancellationToken.Register(() =>
            Gtk.Application.Invoke((_, _) =>
            {
                if (!isDisposed)
                {
                    dialog.Respond(ResponseType.Cancel);
                }
            }));

        StartWatcher();
        ScheduleRescan(immediate: true);
        dialog.ShowAll();

        var response = (ResponseType)dialog.Run();
        var remaining = GetRemainingFiles();
        var decision = response == ResponseType.Ok
            ? (remaining.Count == 0 ? BlockedModpackFilesDecision.Continue : BlockedModpackFilesDecision.SkipMissing)
            : BlockedModpackFilesDecision.Cancel;

        return new BlockedModpackFilesPromptResult
        {
            Decision = decision,
            Matches = matches
        };
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        StopWatcher();

        if (rescanSourceId is not null)
        {
            GLib.Source.Remove(rescanSourceId.Value);
            rescanSourceId = null;
        }

        dialog.Hide();
        dialog.Destroy();
    }

    private void RebuildRows()
    {
        foreach (var child in fileList.Children.ToArray())
        {
            fileList.Remove(child);
            child.Destroy();
        }

        rows.Clear();

        foreach (var file in request.Files)
        {
            var rowState = CreateRow(file);
            rows[rowState.Key] = rowState;
            fileList.PackStart(rowState.Shell, false, false, 0);
        }

        fileList.ShowAll();
    }

    private FileRowState CreateRow(PendingManualDownloadFile file)
    {
        var shell = new EventBox
        {
            VisibleWindow = true
        };
        shell.StyleContext.AddClass("manual-download-row-shell");

        var row = new Box(Orientation.Horizontal, 12)
        {
            MarginTop = 10,
            MarginBottom = 10,
            MarginStart = 12,
            MarginEnd = 12
        };

        var textColumn = new Box(Orientation.Vertical, 4)
        {
            Hexpand = true
        };

        var title = new Label(!string.IsNullOrWhiteSpace(file.ProjectName) ? file.ProjectName : file.DisplayName)
        {
            Xalign = 0,
            Wrap = true
        };
        title.StyleContext.AddClass("settings-field-label");

        var details = new Label(BuildDetailsText(file))
        {
            Xalign = 0,
            Wrap = true
        };
        details.StyleContext.AddClass("settings-help");

        var status = new Label("Waiting for download")
        {
            Xalign = 0
        };
        status.StyleContext.AddClass("manual-download-row-status");

        textColumn.PackStart(title, false, false, 0);
        textColumn.PackStart(details, false, false, 0);
        textColumn.PackStart(status, false, false, 0);

        var openButton = new Button("Open Link");
        openButton.StyleContext.AddClass("flat-inline-button");
        openButton.Clicked += (_, _) => OpenLink(file);

        row.PackStart(textColumn, true, true, 0);
        row.PackEnd(openButton, false, false, 0);
        shell.Add(row);

        return new FileRowState(BuildFileKey(file), file, shell, status, openButton);
    }

    private void StartWatcher()
    {
        StopWatcher();

        if (string.IsNullOrWhiteSpace(request.DownloadsDirectory))
        {
            return;
        }

        Directory.CreateDirectory(request.DownloadsDirectory);

        watcher = new FileSystemWatcher(request.DownloadsDirectory)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        watcher.Created += HandleWatchedDirectoryChanged;
        watcher.Changed += HandleWatchedDirectoryChanged;
        watcher.Deleted += HandleWatchedDirectoryChanged;
        watcher.Renamed += HandleWatchedDirectoryChanged;
        watcher.Error += HandleWatcherError;
    }

    private void StopWatcher()
    {
        if (watcher is null)
        {
            return;
        }

        watcher.EnableRaisingEvents = false;
        watcher.Created -= HandleWatchedDirectoryChanged;
        watcher.Changed -= HandleWatchedDirectoryChanged;
        watcher.Deleted -= HandleWatchedDirectoryChanged;
        watcher.Renamed -= HandleWatchedDirectoryChanged;
        watcher.Error -= HandleWatcherError;
        watcher.Dispose();
        watcher = null;
    }

    private void HandleWatchedDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        Gtk.Application.Invoke((_, _) => ScheduleRescan(immediate: false));
    }

    private void HandleWatcherError(object sender, ErrorEventArgs e)
    {
        Gtk.Application.Invoke((_, _) =>
        {
            if (isDisposed)
            {
                return;
            }

            statusLabel.Text = "Automatic folder watching was interrupted. Use Reload to check the downloads folder again.";
        });
    }

    private void ScheduleRescan(bool immediate)
    {
        if (isDisposed)
        {
            return;
        }

        if (scanInProgress)
        {
            rescanRequested = true;
            return;
        }

        if (rescanSourceId is not null)
        {
            GLib.Source.Remove(rescanSourceId.Value);
            rescanSourceId = null;
        }

        if (immediate)
        {
            BeginScan();
            return;
        }

        rescanSourceId = GLib.Timeout.Add(250, () =>
        {
            rescanSourceId = null;
            BeginScan();
            return false;
        });
    }

    private void BeginScan()
    {
        if (isDisposed || scanInProgress)
        {
            return;
        }

        scanInProgress = true;
        rescanRequested = false;
        UpdateStatusText();
        UpdateButtons();

        var generation = ++scanGeneration;
        var downloadsDirectory = request.DownloadsDirectory;
        var files = request.Files.ToArray();

        _ = Task.Run(() => PendingManualDownloadMatcher.FindMatches(downloadsDirectory, files))
            .ContinueWith(task =>
                Gtk.Application.Invoke((_, _) => ApplyScanResult(generation, task)),
                TaskScheduler.Default);
    }

    private void ApplyScanResult(int generation, Task<IReadOnlyList<PendingManualDownloadMatch>> task)
    {
        if (isDisposed || generation != scanGeneration)
        {
            return;
        }

        scanInProgress = false;
        matches = task.Status == TaskStatus.RanToCompletion ? task.Result : [];

        var matchedKeys = matches
            .Select(match => BuildFileKey(match.File))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows.Values)
        {
            var isResolved = matchedKeys.Contains(row.Key);
            row.StatusLabel.Text = isResolved ? "Ready to import" : "Waiting for download";
            row.OpenButton.Sensitive = !isResolved && !string.IsNullOrWhiteSpace(GetOpenableUrl(row.File));

            if (isResolved)
            {
                if (!row.Shell.StyleContext.HasClass("manual-download-row-shell-resolved"))
                {
                    row.Shell.StyleContext.AddClass("manual-download-row-shell-resolved");
                }

                if (!row.OpenButton.StyleContext.HasClass("manual-download-link-button-resolved"))
                {
                    row.OpenButton.StyleContext.AddClass("manual-download-link-button-resolved");
                }
            }
            else
            {
                if (row.Shell.StyleContext.HasClass("manual-download-row-shell-resolved"))
                {
                    row.Shell.StyleContext.RemoveClass("manual-download-row-shell-resolved");
                }

                if (row.OpenButton.StyleContext.HasClass("manual-download-link-button-resolved"))
                {
                    row.OpenButton.StyleContext.RemoveClass("manual-download-link-button-resolved");
                }
            }
        }

        UpdateStatusText();
        UpdateButtons();

        if (rescanRequested)
        {
            ScheduleRescan(immediate: true);
        }
    }

    private void UpdateStatusText()
    {
        if (scanInProgress)
        {
            statusLabel.Text = string.IsNullOrWhiteSpace(request.DownloadsDirectory)
                ? "Checking for downloaded files."
                : $"Checking {request.DownloadsDirectory} for required files.";
            return;
        }

        var remainingCount = GetRemainingFiles().Count;
        if (remainingCount == 0)
        {
            statusLabel.Text = "All required files are ready. Continue to finish the import.";
            return;
        }

        if (matches.Count > 0)
        {
            statusLabel.Text = $"Detected {matches.Count} of {request.Files.Count} required file(s). {remainingCount} still missing.";
            return;
        }

        statusLabel.Text = string.IsNullOrWhiteSpace(request.DownloadsDirectory)
            ? $"Waiting for {request.Files.Count} required file(s)."
            : $"Waiting for {request.Files.Count} required file(s) in {request.DownloadsDirectory}.";
    }

    private void UpdateButtons()
    {
        var remainingFiles = GetRemainingFiles();
        var unresolvedUrls = remainingFiles
            .Select(GetOpenableUrl)
            .Where(static url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        openMissingButton.Sensitive = !scanInProgress && unresolvedUrls.Length > 0;
        reloadButton.Sensitive = !scanInProgress;
        continueButton.Sensitive = !scanInProgress;
        continueButton.Label = remainingFiles.Count == 0 ? "Continue" : "Skip Missing and Continue";
    }

    private void OpenMissingLinks()
    {
        foreach (var url in GetRemainingFiles()
                     .Select(GetOpenableUrl)
                     .Where(static value => !string.IsNullOrWhiteSpace(value))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DesktopShell.OpenUrl(url!);
        }
    }

    private void OpenLink(PendingManualDownloadFile file)
    {
        var url = GetOpenableUrl(file);
        if (!string.IsNullOrWhiteSpace(url))
        {
            DesktopShell.OpenUrl(url);
        }
    }

    private IReadOnlyList<PendingManualDownloadFile> GetRemainingFiles()
    {
        var matchedKeys = matches
            .Select(match => BuildFileKey(match.File))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return request.Files
            .Where(file => !matchedKeys.Contains(BuildFileKey(file)))
            .ToArray();
    }

    private static string BuildSummaryText(BlockedModpackFilesPromptRequest request)
    {
        var providerName = request.Provider switch
        {
            CatalogProvider.CurseForge => "CurseForge",
            CatalogProvider.Modrinth => "Modrinth",
            _ => request.Provider.ToString()
        };

        if (string.IsNullOrWhiteSpace(request.DownloadsDirectory))
        {
            return $"{providerName} blocked {request.Files.Count} direct download(s). Download the listed files manually, then continue.";
        }

        return $"{providerName} blocked {request.Files.Count} direct download(s). Download the listed files into {request.DownloadsDirectory}, or skip the missing ones and continue.";
    }

    private static string BuildDetailsText(PendingManualDownloadFile file)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(file.FileName))
        {
            parts.Add(file.FileName);
        }

        if (!string.IsNullOrWhiteSpace(file.DestinationRelativePath))
        {
            parts.Add(file.DestinationRelativePath);
        }

        if (!string.IsNullOrWhiteSpace(file.ManifestFileName) &&
            !string.Equals(file.ManifestFileName, file.FileName, StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"Pack file: {file.ManifestFileName}");
        }

        return parts.Count == 0 ? "Manual download required." : string.Join("  •  ", parts);
    }

    private static string? GetOpenableUrl(PendingManualDownloadFile file)
    {
        return !string.IsNullOrWhiteSpace(file.DirectDownloadUrl)
            ? file.DirectDownloadUrl
            : !string.IsNullOrWhiteSpace(file.FilePageUrl)
                ? file.FilePageUrl
                : file.ProjectUrl;
    }

    private static string BuildFileKey(PendingManualDownloadFile file)
    {
        return string.Join("|", new[]
        {
            file.ProjectId,
            file.FileId,
            file.FileName,
            file.DestinationRelativePath
        });
    }

    private sealed record FileRowState(
        string Key,
        PendingManualDownloadFile File,
        EventBox Shell,
        Label StatusLabel,
        Button OpenButton);
}
