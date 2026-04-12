using BlockiumLauncher.Application.UseCases.Catalog;
using BlockiumLauncher.Domain.Entities;
using BlockiumLauncher.UI.GtkSharp.Utilities;
using Gtk;

namespace BlockiumLauncher.UI.GtkSharp.Windows;

public sealed class ManualDownloadWindow : Window
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(750);

    private readonly ResumeCatalogModpackImportUseCase ResumeCatalogModpackImportUseCase;
    private readonly Box _fileList = new(Orientation.Vertical, 8);
    private readonly Label _subtitleLabel;
    private readonly Label _statusLabel;
    private readonly Button _openMissingButton;
    private readonly Button _reloadButton;
    private readonly Dictionary<string, ManualDownloadRowState> _rows = new(StringComparer.OrdinalIgnoreCase);
    private Action<ManualDownloadTrackingResult>? _completionCallback;

    private LauncherInstance? _trackedInstance;
    private string _downloadsDirectory = string.Empty;
    private IReadOnlyList<PendingManualDownloadFile> _files = [];
    private uint? _scanTimerId;
    private bool _resumeInProgress;
    private bool _isShuttingDown;
    private int _trackingGeneration;

    public ManualDownloadWindow(
        ResumeCatalogModpackImportUseCase resumeCatalogModpackImportUseCase) : base("Manual Downloads Required")
    {
        ResumeCatalogModpackImportUseCase = resumeCatalogModpackImportUseCase ?? throw new ArgumentNullException(nameof(resumeCatalogModpackImportUseCase));

        SetDefaultSize(720, 540);
        WindowPosition = WindowPosition.CenterOnParent;
        Modal = true;
        Resizable = true;
        TypeHint = Gdk.WindowTypeHint.Dialog;
        Titlebar = LauncherGtkChrome.CreateHeaderBar("Manual downloads", "Complete provider-required downloads and let the launcher import them automatically.", allowWindowControls: false);

        var root = new EventBox();
        root.StyleContext.AddClass("settings-shell");

        var layout = new Box(Orientation.Vertical, 0);

        var header = new Box(Orientation.Vertical, 10) { Margin = 24 };
        var title = new Label("Additional Downloads Required") { Xalign = 0 };
        title.StyleContext.AddClass("settings-page-title");

        _subtitleLabel = new Label("The launcher will watch your downloads folder and import files as soon as they appear.")
        {
            Xalign = 0,
            Wrap = true
        };
        _subtitleLabel.StyleContext.AddClass("settings-help");

        _statusLabel = new Label("Waiting for required files.")
        {
            Xalign = 0,
            Wrap = true
        };
        _statusLabel.StyleContext.AddClass("manual-download-status");

        header.PackStart(title, false, false, 0);
        header.PackStart(_subtitleLabel, false, false, 0);
        header.PackStart(_statusLabel, false, false, 0);
        layout.PackStart(header, false, false, 0);

        var scroller = new ScrolledWindow
        {
            HscrollbarPolicy = PolicyType.Never,
            VscrollbarPolicy = PolicyType.Automatic,
            MarginStart = 24,
            MarginEnd = 24,
            MarginBottom = 16,
            Vexpand = true
        };
        scroller.StyleContext.AddClass("manual-download-list-scroller");
        scroller.Add(_fileList);
        layout.PackStart(scroller, true, true, 0);

        var footer = new ActionBar();
        footer.StyleContext.AddClass("settings-footer");

        _openMissingButton = new Button("Open Missing")
        {
            WidthRequest = 140
        };
        _openMissingButton.StyleContext.AddClass("action-button");
        _openMissingButton.Clicked += (_, _) => OpenAllLinks();

        _reloadButton = new Button("Reload")
        {
            WidthRequest = 108
        };
        _reloadButton.StyleContext.AddClass("action-button");
        _reloadButton.Clicked += (_, _) => TriggerImmediateRescan();

        var closeButton = new Button("Skip Remaining")
        {
            WidthRequest = 144
        };
        closeButton.StyleContext.AddClass("action-button");
        closeButton.Clicked += (_, _) => SkipRemainingAndHide();

        footer.PackStart(_openMissingButton);
        footer.PackStart(_reloadButton);
        footer.PackEnd(closeButton);
        layout.PackEnd(footer, false, false, 0);

        root.Add(layout);
        Add(root);

        DeleteEvent += (_, args) =>
        {
            SkipRemainingAndHide();
            args.RetVal = true;
        };

        Destroyed += (_, _) =>
        {
            ShutdownForApplicationExit();
            LauncherWindowMemory.RequestAggressiveCleanup();
        };
    }

    public void BeginTracking(
        LauncherInstance instance,
        string downloadsDirectory,
        IReadOnlyList<PendingManualDownloadFile> files,
        Action<ManualDownloadTrackingResult>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(files);

        StopTracking();

        _trackedInstance = instance;
        _downloadsDirectory = downloadsDirectory;
        _files = files.ToArray();
        _completionCallback = onCompleted;
        _trackingGeneration++;

        _subtitleLabel.Text = string.IsNullOrWhiteSpace(downloadsDirectory)
            ? "The launcher will watch your downloads folder and import files as soon as they appear."
            : $"The launcher is watching {downloadsDirectory} and will import matching files automatically.";

        RebuildRows();
        UpdateStatusText(files.Count == 0
            ? "No manual downloads are pending."
            : $"Waiting for {files.Count} required file(s). Download them and leave them in your Downloads folder.");
        UpdateFooterButtonState();

        ShowAll();
        Present();
        StartTracking();
    }

    public void ShutdownForApplicationExit()
    {
        if (_isShuttingDown)
        {
            return;
        }

        _isShuttingDown = true;
        _completionCallback = null;
        StopTracking();
        try
        {
            if (Visible)
            {
                Destroy();
            }
        }
        catch
        {
        }
    }

    private void StartTracking()
    {
        if (_isShuttingDown)
        {
            return;
        }

        StopScanTimer();

        var generation = _trackingGeneration;
        _ = ScanAndResumeAsync(generation);

        _scanTimerId = GLib.Timeout.Add((uint)PollInterval.TotalMilliseconds, () =>
        {
            _ = ScanAndResumeAsync(generation);
            return _scanTimerId is not null && generation == _trackingGeneration;
        });
    }

    private void StopTracking()
    {
        _trackingGeneration++;
        StopScanTimer();
        _resumeInProgress = false;
        _trackedInstance = null;
        _downloadsDirectory = string.Empty;
        _files = [];
    }

    private void StopScanTimer()
    {
        if (_scanTimerId is not null)
        {
            GLib.Source.Remove(_scanTimerId.Value);
            _scanTimerId = null;
        }
    }

    private void RebuildRows()
    {
        foreach (var child in _fileList.Children.ToArray())
        {
            _fileList.Remove(child);
            child.Destroy();
        }

        _rows.Clear();

        foreach (var file in _files)
        {
            var rowState = CreateFileRow(file);
            _rows[rowState.Key] = rowState;
            _fileList.PackStart(rowState.Shell, false, false, 0);
        }

        UpdateOpenMissingButtonState();
        UpdateFooterButtonState();
        _fileList.ShowAll();
    }

    private ManualDownloadRowState CreateFileRow(PendingManualDownloadFile file)
    {
        var key = BuildFileKey(file);

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

        var name = new Label(file.DisplayName)
        {
            Xalign = 0,
            Ellipsize = Pango.EllipsizeMode.End,
            Hexpand = true
        };
        name.StyleContext.AddClass("settings-field-label");

        var details = new Label(BuildDetailsText(file))
        {
            Xalign = 0,
            Wrap = true
        };
        details.StyleContext.AddClass("settings-help");

        var status = new Label("Pending download")
        {
            Xalign = 0
        };
        status.StyleContext.AddClass("manual-download-row-status");

        textColumn.PackStart(name, false, false, 0);
        textColumn.PackStart(details, false, false, 0);
        textColumn.PackStart(status, false, false, 0);

        var openButton = new Button("Open Link");
        openButton.StyleContext.AddClass("flat-inline-button");
        openButton.Clicked += (_, _) => OpenLink(file);

        row.PackStart(textColumn, true, true, 0);
        row.PackEnd(openButton, false, false, 0);
        shell.Add(row);

        return new ManualDownloadRowState(key, file, shell, openButton, status);
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

        return parts.Count == 0 ? "Manual download required." : string.Join("  •  ", parts);
    }

    private async Task ScanAndResumeAsync(int generation)
    {
        if (_resumeInProgress ||
            _isShuttingDown ||
            generation != _trackingGeneration ||
            _trackedInstance is null ||
            string.IsNullOrWhiteSpace(_downloadsDirectory))
        {
            return;
        }

        _resumeInProgress = true;
        Gtk.Application.Invoke((_, _) => UpdateFooterButtonState());

        try
        {
            var result = await ResumeCatalogModpackImportUseCase.ExecuteAsync(new ResumeCatalogModpackImportRequest
            {
                InstanceId = _trackedInstance.InstanceId.ToString(),
                DownloadsDirectory = _downloadsDirectory,
                WaitForManualDownloads = false,
                WaitTimeout = TimeSpan.Zero
            }).ConfigureAwait(false);

            Gtk.Application.Invoke((_, _) => ApplyResumeResult(generation, result));
        }
        catch (Exception exception)
        {
            Gtk.Application.Invoke((_, _) =>
            {
                if (generation != _trackingGeneration)
                {
                    return;
                }

                UpdateStatusText($"Automatic import paused: {exception.Message}");
            });
        }
        finally
        {
            _resumeInProgress = false;
            Gtk.Application.Invoke((_, _) => UpdateFooterButtonState());
        }
    }

    private void ApplyResumeResult(int generation, Shared.Results.Result<ResumeCatalogModpackImportResult> result)
    {
        if (_isShuttingDown || generation != _trackingGeneration)
        {
            return;
        }

        if (result.IsFailure)
        {
            if (string.Equals(result.Error.Code, "Catalog.NoPendingManualDownloads", StringComparison.OrdinalIgnoreCase))
            {
                MarkAllResolved();
                CompleteTracking();
                return;
            }

            UpdateStatusText(result.Error.Message);
            return;
        }

        var remainingFiles = result.Value.PendingManualDownloads;
        var remainingKeys = remainingFiles
            .Select(BuildFileKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var row in _rows.Values)
        {
            if (!remainingKeys.Contains(row.Key))
            {
                MarkRowResolved(row);
            }
        }

        _files = remainingFiles.ToArray();
        UpdateOpenMissingButtonState();
        UpdateFooterButtonState();

        if (result.Value.ImportedFiles.Count > 0)
        {
            UpdateStatusText($"Imported {result.Value.ImportedFiles.Count} file(s). {remainingFiles.Count} remaining.");
        }
        else if (remainingFiles.Count > 0)
        {
            UpdateStatusText($"Watching {_downloadsDirectory} for {remainingFiles.Count} remaining file(s).");
        }

        if (result.Value.IsCompleted)
        {
            CompleteTracking(result.Value.Instance.InstanceId);
        }
    }

    private void MarkAllResolved()
    {
        foreach (var row in _rows.Values)
        {
            MarkRowResolved(row);
        }

        _files = [];
        UpdateOpenMissingButtonState();
        UpdateFooterButtonState();
    }

    private void MarkRowResolved(ManualDownloadRowState row)
    {
        row.StatusLabel.Text = "Detected and imported";
        row.OpenButton.Sensitive = false;

        var shellContext = row.Shell.StyleContext;
        if (!shellContext.HasClass("manual-download-row-shell-resolved"))
        {
            shellContext.AddClass("manual-download-row-shell-resolved");
        }

        var buttonContext = row.OpenButton.StyleContext;
        if (!buttonContext.HasClass("manual-download-link-button-resolved"))
        {
            buttonContext.AddClass("manual-download-link-button-resolved");
        }
    }

    private void CompleteTracking(BlockiumLauncher.Domain.ValueObjects.InstanceId? preferredInstanceId = null)
    {
        if (_isShuttingDown)
        {
            return;
        }

        UpdateStatusText("All required files were detected and imported.");
        var instanceId = preferredInstanceId ?? _trackedInstance?.InstanceId;
        var completionCallback = _completionCallback;
        _completionCallback = null;
        StopTracking();
        Destroy();
        completionCallback?.Invoke(new ManualDownloadTrackingResult(instanceId, IsCompleted: true, WasSkipped: false, RemainingFileCount: 0));
    }

    private void SkipRemainingAndHide()
    {
        if (_isShuttingDown)
        {
            return;
        }

        var instanceId = _trackedInstance?.InstanceId;
        var remainingFileCount = _files.Count;
        var completionCallback = _completionCallback;
        _completionCallback = null;
        StopTracking();
        Destroy();
        completionCallback?.Invoke(new ManualDownloadTrackingResult(instanceId, IsCompleted: false, WasSkipped: true, RemainingFileCount: remainingFileCount));
    }

    private void OpenLink(PendingManualDownloadFile file)
    {
        var url = GetOpenableUrl(file);
        if (!string.IsNullOrWhiteSpace(url))
        {
            DesktopShell.OpenUrl(url);
        }
    }

    private void OpenAllLinks()
    {
        var urls = _files
            .Select(GetOpenableUrl)
            .Where(static url => !string.IsNullOrWhiteSpace(url))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var url in urls)
        {
            DesktopShell.OpenUrl(url!);
        }
    }

    private static string? GetOpenableUrl(PendingManualDownloadFile file)
    {
        return !string.IsNullOrWhiteSpace(file.DirectDownloadUrl)
            ? file.DirectDownloadUrl
            : !string.IsNullOrWhiteSpace(file.FilePageUrl)
                ? file.FilePageUrl
                : file.ProjectUrl;
    }

    private void UpdateOpenMissingButtonState()
    {
        _openMissingButton.Sensitive = _files.Any(file => !string.IsNullOrWhiteSpace(GetOpenableUrl(file)));
    }

    private void UpdateFooterButtonState()
    {
        _reloadButton.Sensitive = !_resumeInProgress && _trackedInstance is not null && !string.IsNullOrWhiteSpace(_downloadsDirectory);
        _openMissingButton.Sensitive = !_resumeInProgress && _files.Any(file => !string.IsNullOrWhiteSpace(GetOpenableUrl(file)));
    }

    private void UpdateStatusText(string text)
    {
        _statusLabel.Text = text;
    }

    private void TriggerImmediateRescan()
    {
        if (_trackedInstance is null || string.IsNullOrWhiteSpace(_downloadsDirectory))
        {
            return;
        }

        UpdateStatusText($"Checking {_downloadsDirectory} for newly downloaded files...");
        _ = ScanAndResumeAsync(_trackingGeneration);
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

    private sealed record ManualDownloadRowState(
        string Key,
        PendingManualDownloadFile File,
        EventBox Shell,
        Button OpenButton,
        Label StatusLabel);

    public sealed record ManualDownloadTrackingResult(
        BlockiumLauncher.Domain.ValueObjects.InstanceId? InstanceId,
        bool IsCompleted,
        bool WasSkipped,
        int RemainingFileCount);
}
