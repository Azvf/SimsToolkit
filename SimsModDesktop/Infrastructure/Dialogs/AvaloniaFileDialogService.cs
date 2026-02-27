using Avalonia.Platform.Storage;
using SimsModDesktop.Infrastructure.Windowing;

namespace SimsModDesktop.Infrastructure.Dialogs;

public sealed class AvaloniaFileDialogService : IFileDialogService
{
    private readonly IWindowHostService _windowHostService;

    public AvaloniaFileDialogService(IWindowHostService windowHostService)
    {
        _windowHostService = windowHostService;
    }

    public async Task<string?> PickScriptPathAsync()
    {
        var storageProvider = GetStorageProviderOrThrow();
        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select sims-mod-cli.ps1",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("PowerShell script")
                {
                    Patterns = new[] { "*.ps1" }
                }
            }
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<IReadOnlyList<string>> PickFolderPathsAsync(string title, bool allowMultiple)
    {
        var storageProvider = GetStorageProviderOrThrow();
        var folders = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = allowMultiple
        });

        return folders
            .Select(folder => folder.TryGetLocalPath())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path!)
            .ToList();
    }

    public async Task<string?> PickCsvSavePathAsync(string title, string suggestedFileName)
    {
        var storageProvider = GetStorageProviderOrThrow();
        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "csv",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("CSV files")
                {
                    Patterns = new[] { "*.csv" }
                }
            }
        });

        return file?.TryGetLocalPath();
    }

    private IStorageProvider GetStorageProviderOrThrow()
    {
        var provider = _windowHostService.CurrentTopLevel?.StorageProvider;
        if (provider is null)
        {
            throw new InvalidOperationException("No active window is available for file dialogs.");
        }

        return provider;
    }
}
