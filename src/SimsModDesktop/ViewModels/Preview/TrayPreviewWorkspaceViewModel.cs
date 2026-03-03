using System.Diagnostics;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.Services;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.ViewModels.Preview;

public sealed class TrayPreviewWorkspaceViewModel : ObservableObject
{
    private readonly IFileDialogService _fileDialogService;
    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private readonly TrayDependenciesPanelViewModel _trayDependencies;
    private bool _isActive;
    private bool _hasPendingRefresh = true;
    private string _logText = "Tray preview ready.";

    public TrayPreviewWorkspaceViewModel(
        TrayPreviewPanelViewModel filter,
        ITrayPreviewRunner trayPreviewRunner,
        ITrayThumbnailService trayThumbnailService,
        IFileDialogService fileDialogService,
        ITrayDependencyExportService trayDependencyExportService,
        TrayDependenciesPanelViewModel trayDependencies)
    {
        Filter = filter;
        Surface = new TrayLikePreviewSurfaceViewModel(trayPreviewRunner, trayThumbnailService);
        _fileDialogService = fileDialogService;
        _trayDependencyExportService = trayDependencyExportService;
        _trayDependencies = trayDependencies;

        OpenSelectedCommand = new RelayCommand(OpenSelected, () => Surface.HasSelection);
        ExportSelectedCommand = new AsyncRelayCommand(ExportSelectedAsync, () => Surface.HasSelection);

        Surface.Configure(Filter, () => Filter.TrayRoot, PreviewSurfaceSelectionMode.Multiple, autoLoad: false);
        Surface.SetActionButtons(
        [
            new PreviewSurfaceActionButtonViewModel { Label = "Refresh", Command = Surface.RefreshCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Open Selected", Command = OpenSelectedCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Select Page", Command = Surface.SelectAllPageCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Export Selected", Command = ExportSelectedCommand },
            new PreviewSurfaceActionButtonViewModel { Label = "Clear", Command = Surface.ClearSelectionCommand }
        ]);
        Surface.SetFooter("Tray Preview Log", LogText);

        Filter.PropertyChanged += OnFilterPropertyChanged;
        Surface.PropertyChanged += OnSurfacePropertyChanged;
    }

    public TrayPreviewPanelViewModel Filter { get; }
    public TrayLikePreviewSurfaceViewModel Surface { get; }
    public RelayCommand OpenSelectedCommand { get; }
    public AsyncRelayCommand ExportSelectedCommand { get; }

    public void ResetAfterCacheClear()
    {
        Surface.ResetAfterCacheClear();
    }

    public Task EnsureLoadedAsync(bool forceReload = false)
    {
        return Surface.EnsureLoadedAsync(forceReload);
    }

    public void SetIsActive(bool isActive)
    {
        if (_isActive == isActive)
        {
            return;
        }

        _isActive = isActive;
        if (!_isActive)
        {
            Surface.PauseBackgroundLoading();
            return;
        }

        if (!string.IsNullOrWhiteSpace(Filter.TrayRoot) &&
            Directory.Exists(Filter.TrayRoot))
        {
            var forceReload = _hasPendingRefresh;
            _hasPendingRefresh = false;
            _ = Surface.EnsureLoadedAsync(forceReload);
        }
    }

    public string LogText
    {
        get => _logText;
        private set
        {
            if (!SetProperty(ref _logText, value))
            {
                return;
            }

            Surface.SetFooter("Tray Preview Log", value);
        }
    }

    private void OnFilterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.TrayRoot), StringComparison.Ordinal))
        {
            _hasPendingRefresh = true;
            if (_isActive)
            {
                _hasPendingRefresh = false;
                Surface.NotifyTrayPathChanged();
            }
        }
    }

    private void OnSurfacePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(TrayLikePreviewSurfaceViewModel.HasSelection), StringComparison.Ordinal))
        {
            return;
        }

        OpenSelectedCommand.NotifyCanExecuteChanged();
        ExportSelectedCommand.NotifyCanExecuteChanged();
    }

    private void OpenSelected()
    {
        var selectedItems = Surface.GetSelectedItems();
        var sourcePaths = selectedItems
            .SelectMany(item => item.Item.SourceFilePaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (sourcePaths.Length == 0)
        {
            return;
        }

        try
        {
            if (sourcePaths.Length == 1)
            {
                LaunchExplorer(sourcePaths[0], selectFile: true);
                LogText = $"Opened selected tray file location.{Environment.NewLine}{sourcePaths[0]}";
                return;
            }

            foreach (var directory in sourcePaths
                         .Select(Path.GetDirectoryName)
                         .Where(path => !string.IsNullOrWhiteSpace(path))
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                LaunchExplorer(directory!, selectFile: false);
            }

            LogText = $"Opened {sourcePaths.Length} selected tray source files.";
        }
        catch (Exception ex)
        {
            LogText = "Failed to open selected tray path." + Environment.NewLine + ex.Message;
        }
    }

    private async Task ExportSelectedAsync()
    {
        var selectedItems = Surface.GetSelectedItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var modsPath = _trayDependencies.ModsPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
        {
            LogText = "Mods Path is missing. Set a valid Mods Path before exporting referenced mods.";
            return;
        }

        var pickedFolders = await _fileDialogService.PickFolderPathsAsync("Select export folder", allowMultiple: false);
        var exportRoot = pickedFolders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exportRoot))
        {
            return;
        }

        var messages = new List<string>();
        foreach (var selectedItem in selectedItems)
        {
            var trayKey = selectedItem.Item.TrayItemKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trayKey) || selectedItem.Item.SourceFilePaths.Count == 0)
            {
                messages.Add($"Skipped {selectedItem.Item.DisplayTitle}: invalid tray metadata.");
                continue;
            }

            var trayPath = selectedItem.Item.TrayRootPath?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trayPath) || !Directory.Exists(trayPath))
            {
                messages.Add($"Skipped {selectedItem.Item.DisplayTitle}: tray path missing.");
                continue;
            }

            var itemRoot = Path.Combine(exportRoot, BuildExportDirectoryName(selectedItem.Item));
            var request = new TrayDependencyExportRequest
            {
                ItemTitle = selectedItem.Item.DisplayTitle,
                TrayItemKey = trayKey,
                TrayRootPath = trayPath,
                TraySourceFiles = selectedItem.Item.SourceFilePaths.ToArray(),
                ModsRootPath = modsPath,
                TrayExportRoot = Path.Combine(itemRoot, "Tray"),
                ModsExportRoot = Path.Combine(itemRoot, "Mods")
            };

            try
            {
                var result = await _trayDependencyExportService.ExportAsync(request);
                if (result.Success)
                {
                    messages.Add($"{selectedItem.Item.DisplayTitle}: exported {result.CopiedTrayFileCount} tray files, {result.CopiedModFileCount} mods.");
                }
                else
                {
                    var issue = result.Issues.FirstOrDefault()?.Message ?? "Unknown export failure.";
                    messages.Add($"{selectedItem.Item.DisplayTitle}: export failed - {issue}");
                }
            }
            catch (Exception ex)
            {
                messages.Add($"{selectedItem.Item.DisplayTitle}: export failed - {ex.Message}");
            }
        }

        LogText = string.Join(Environment.NewLine, messages);
    }

    private static string BuildExportDirectoryName(SimsModDesktop.Application.Models.SimsTrayPreviewItem item)
    {
        var baseName = string.IsNullOrWhiteSpace(item.DisplayTitle) ? "TrayItem" : item.DisplayTitle.Trim();
        var sanitized = string.Concat(baseName.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        var keySuffix = string.IsNullOrWhiteSpace(item.TrayItemKey) ? "item" : item.TrayItemKey.Trim();
        return $"{sanitized}_{keySuffix}";
    }

    private static void LaunchExplorer(string path, bool selectFile)
    {
        var target = selectFile
            ? $"/select,\"{path}\""
            : $"\"{path}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = target,
            UseShellExecute = true
        });
    }
}
