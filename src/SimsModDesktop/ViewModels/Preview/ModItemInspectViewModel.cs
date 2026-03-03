using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Media.Imaging;
using SimsModDesktop.Application.Mods;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Infrastructure.Dialogs;
using SimsModDesktop.ViewModels.Infrastructure;
using SimsModDesktop.ViewModels.Preview.Models;

namespace SimsModDesktop.ViewModels.Preview;

public sealed class ModItemInspectViewModel : ObservableObject
{
    private readonly IModItemInspectService _inspectService;
    private readonly IModPackageTextureEditService _textureEditService;
    private readonly IFileDialogService _fileDialogService;

    private string _selectedTextureKey = string.Empty;
    private string _statusText = "Select an item to inspect.";
    private Bitmap? _previewBitmap;
    private string _previewStatusText = "No preview loaded.";
    private string _historyText = "No local texture edit records yet.";
    private ModItemInspectDetail? _detail;
    private string _currentItemKey = string.Empty;
    private bool _isBackgroundSyncActive;
    private bool _hasPendingRefresh;

    public ModItemInspectViewModel(
        IModItemInspectService inspectService,
        IModPackageTextureEditService textureEditService,
        IFileDialogService fileDialogService)
    {
        _inspectService = inspectService;
        _textureEditService = textureEditService;
        _fileDialogService = fileDialogService;

        TextureCandidates = new ObservableCollection<ModPreviewTextureCandidateModel>();
        SelectTextureCandidateCommand = new RelayCommand<string>(SelectTextureCandidate, key => FindCandidate(key) is not null);
        PreviewTextureCandidateCommand = new AsyncRelayCommand<string>(PreviewTextureCandidateAsync, key => FindCandidate(key) is not null);
        EditTextureCandidateCommand = new AsyncRelayCommand<string>(EditTextureCandidateAsync, key => CanEdit(FindCandidate(key)));
        ImportTextureCandidateCommand = new AsyncRelayCommand<string>(ImportTextureCandidateAsync, key => FindCandidate(key) is not null);
        RollbackSelectedTextureCommand = new AsyncRelayCommand(RollbackSelectedTextureAsync, () => !string.IsNullOrWhiteSpace(_selectedTextureKey));
        RefreshDetailsCommand = new AsyncRelayCommand(RefreshDetailsAsync, () => _hasPendingRefresh && !string.IsNullOrWhiteSpace(_currentItemKey));
    }

    public ObservableCollection<ModPreviewTextureCandidateModel> TextureCandidates { get; }
    public RelayCommand<string> SelectTextureCandidateCommand { get; }
    public AsyncRelayCommand<string> PreviewTextureCandidateCommand { get; }
    public AsyncRelayCommand<string> EditTextureCandidateCommand { get; }
    public AsyncRelayCommand<string> ImportTextureCandidateCommand { get; }
    public AsyncRelayCommand RollbackSelectedTextureCommand { get; }
    public AsyncRelayCommand RefreshDetailsCommand { get; }

    public bool HasSelection => _detail is not null;
    public bool IsPlaceholderVisible => !HasSelection;
    public bool HasPreview => PreviewBitmap is not null;
    public string DisplayName => _detail?.DisplayName ?? "Item Inspect";
    public string Subtitle => _detail is null ? "No item selected" : $"{_detail.EntityKind} | {_detail.EntitySubType}";
    public string PackagePath => _detail?.PackagePath ?? string.Empty;
    public string SourceResourceKey => _detail?.SourceResourceKey ?? string.Empty;
    public string IndexedText => _detail is null ? "-" : new DateTime(_detail.UpdatedUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    public string DisplayNameSourceText => _detail?.DisplayNameSource ?? "-";
    public string BodyTypeText => _detail?.BodyTypeText ?? "-";
    public string AgeGenderText => _detail?.AgeGenderText ?? "-";
    public string SpeciesText => _detail?.SpeciesText ?? "-";
    public string OutfitIdText => _detail?.OutfitId is uint outfitId ? $"0x{outfitId:X8}" : "-";
    public string TitleKeyText => _detail?.TitleKey is uint titleKey ? $"0x{titleKey:X8}" : "-";
    public string PartDescriptionKeyText => _detail?.PartDescriptionKey is uint descriptionKey ? $"0x{descriptionKey:X8}" : "-";
    public string PartNameRawText => string.IsNullOrWhiteSpace(_detail?.PartNameRaw) ? "-" : _detail!.PartNameRaw!;
    public bool IsBackgroundSyncVisible => _isBackgroundSyncActive || _hasPendingRefresh;
    public string BackgroundSyncText => _hasPendingRefresh
        ? "Background sync finished. Refresh details to apply the latest snapshot."
        : "Background sync in progress. The current detail pane stays fixed.";

    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public Bitmap? PreviewBitmap
    {
        get => _previewBitmap;
        private set
        {
            if (ReferenceEquals(_previewBitmap, value))
            {
                return;
            }

            _previewBitmap?.Dispose();
            _previewBitmap = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasPreview));
        }
    }

    public string PreviewStatusText
    {
        get => _previewStatusText;
        private set => SetProperty(ref _previewStatusText, value);
    }

    public string HistoryText
    {
        get => _historyText;
        private set => SetProperty(ref _historyText, value);
    }

    public async Task LoadAsync(string itemKey, CancellationToken cancellationToken = default)
    {
        _currentItemKey = itemKey.Trim();
        _hasPendingRefresh = false;
        OnPropertyChanged(nameof(IsBackgroundSyncVisible));
        OnPropertyChanged(nameof(BackgroundSyncText));
        RefreshDetailsCommand.NotifyCanExecuteChanged();

        _detail = await _inspectService.TryGetAsync(itemKey, cancellationToken).ConfigureAwait(false);
        TextureCandidates.Clear();
        _selectedTextureKey = string.Empty;
        PreviewBitmap = null;
        PreviewStatusText = "No preview loaded.";
        HistoryText = "No local texture edit records yet.";

        if (_detail is null)
        {
            StatusText = "Item data was not found.";
            NotifyCoreChanged();
            return;
        }

        foreach (var candidate in _detail.TextureRows)
        {
            TextureCandidates.Add(new ModPreviewTextureCandidateModel
            {
                ResourceKey = candidate.ResourceKeyText,
                Format = candidate.Format,
                Resolution = candidate.Width > 0 && candidate.Height > 0 ? $"{candidate.Width} x {candidate.Height}" : "-",
                LinkRole = candidate.LinkRole,
                SuggestedAction = candidate.SuggestedAction,
                Notes = candidate.Notes,
                CanEdit = CanEdit(candidate),
                IsSelected = false
            });
        }

        StatusText = _detail.HasTextureData
            ? $"Textures {_detail.TextureCount} | Editable {_detail.EditableTextureCount}"
            : "This item has no linked editable texture resources.";
        NotifyCoreChanged();
    }

    public void SetBackgroundSyncActive(bool isActive)
    {
        if (_isBackgroundSyncActive == isActive)
        {
            return;
        }

        _isBackgroundSyncActive = isActive;
        OnPropertyChanged(nameof(IsBackgroundSyncVisible));
        OnPropertyChanged(nameof(BackgroundSyncText));
    }

    public void MarkPendingRefresh()
    {
        if (string.IsNullOrWhiteSpace(_currentItemKey))
        {
            return;
        }

        _hasPendingRefresh = true;
        OnPropertyChanged(nameof(IsBackgroundSyncVisible));
        OnPropertyChanged(nameof(BackgroundSyncText));
        RefreshDetailsCommand.NotifyCanExecuteChanged();
    }

    public Task RefreshCurrentAsync(CancellationToken cancellationToken = default)
    {
        return string.IsNullOrWhiteSpace(_currentItemKey)
            ? Task.CompletedTask
            : LoadAsync(_currentItemKey, cancellationToken);
    }

    private void NotifyCoreChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsPlaceholderVisible));
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(PackagePath));
        OnPropertyChanged(nameof(SourceResourceKey));
        OnPropertyChanged(nameof(IndexedText));
        OnPropertyChanged(nameof(DisplayNameSourceText));
        OnPropertyChanged(nameof(BodyTypeText));
        OnPropertyChanged(nameof(AgeGenderText));
        OnPropertyChanged(nameof(SpeciesText));
        OnPropertyChanged(nameof(OutfitIdText));
        OnPropertyChanged(nameof(TitleKeyText));
        OnPropertyChanged(nameof(PartDescriptionKeyText));
        OnPropertyChanged(nameof(PartNameRawText));
        RollbackSelectedTextureCommand.NotifyCanExecuteChanged();
        RefreshDetailsCommand.NotifyCanExecuteChanged();
    }

    private Task RefreshDetailsAsync()
    {
        return RefreshCurrentAsync();
    }

    private void SelectTextureCandidate(string? resourceKey)
    {
        var candidate = FindCandidate(resourceKey);
        if (candidate is null)
        {
            return;
        }

        _selectedTextureKey = candidate.ResourceKeyText;
        for (var index = 0; index < TextureCandidates.Count; index++)
        {
            var row = TextureCandidates[index];
            TextureCandidates[index] = row with
            {
                IsSelected = string.Equals(row.ResourceKey, _selectedTextureKey, StringComparison.OrdinalIgnoreCase)
            };
        }

        RollbackSelectedTextureCommand.NotifyCanExecuteChanged();
    }

    private async Task PreviewTextureCandidateAsync(string? resourceKey)
    {
        var candidate = FindCandidate(resourceKey);
        if (candidate is null || _detail is null)
        {
            return;
        }

        SelectTextureCandidate(candidate.ResourceKeyText);
        var result = await _textureEditService.PreviewAsync(_detail.PackagePath, candidate).ConfigureAwait(false);
        if (!result.Success || result.PngBytes.Length == 0)
        {
            PreviewBitmap = null;
            PreviewStatusText = result.Error ?? "Preview could not be generated.";
            return;
        }

        using var stream = new MemoryStream(result.PngBytes, writable: false);
        PreviewBitmap = new Bitmap(stream);
        PreviewStatusText = $"{result.Width} x {result.Height} | {result.Format}";
        await LoadHistoryAsync(candidate.ResourceKeyText).ConfigureAwait(false);
    }

    private async Task EditTextureCandidateAsync(string? resourceKey)
    {
        var candidate = FindCandidate(resourceKey);
        if (candidate is null || _detail is null)
        {
            return;
        }

        SelectTextureCandidate(candidate.ResourceKeyText);
        var result = await _textureEditService.ApplySuggestedEditAsync(_detail.PackagePath, candidate).ConfigureAwait(false);
        StatusText = result.StatusText;
        await LoadHistoryAsync(candidate.ResourceKeyText).ConfigureAwait(false);
    }

    private async Task ImportTextureCandidateAsync(string? resourceKey)
    {
        var candidate = FindCandidate(resourceKey);
        if (candidate is null || _detail is null)
        {
            return;
        }

        var filePath = await _fileDialogService.PickFilePathAsync("Import Texture Source", "Image Files", ["*.png", "*.dds", "*.tga"]).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        SelectTextureCandidate(candidate.ResourceKeyText);
        var result = await _textureEditService.ApplyImportedTextureAsync(_detail.PackagePath, candidate, filePath).ConfigureAwait(false);
        StatusText = result.StatusText;
        await LoadHistoryAsync(candidate.ResourceKeyText).ConfigureAwait(false);
    }

    private async Task RollbackSelectedTextureAsync()
    {
        if (_detail is null || string.IsNullOrWhiteSpace(_selectedTextureKey))
        {
            return;
        }

        var result = await _textureEditService.RollbackLatestAsync(_detail.PackagePath, _selectedTextureKey).ConfigureAwait(false);
        StatusText = result.StatusText;
        await LoadHistoryAsync(_selectedTextureKey).ConfigureAwait(false);
    }

    private async Task LoadHistoryAsync(string resourceKey)
    {
        if (_detail is null)
        {
            return;
        }

        var history = await _textureEditService.GetHistoryAsync(_detail.PackagePath, resourceKey, 3).ConfigureAwait(false);
        HistoryText = history.Count == 0
            ? "No local texture edit records yet."
            : string.Join(Environment.NewLine, history.Select(record =>
            {
                var timestamp = new DateTime(record.AppliedUtcTicks, DateTimeKind.Utc).ToLocalTime();
                return $"{timestamp:yyyy-MM-dd HH:mm:ss} | {record.RecordKind} | {record.AppliedAction}";
            }));
    }

    private ModPackageTextureCandidate? FindCandidate(string? resourceKey)
    {
        if (_detail is null || string.IsNullOrWhiteSpace(resourceKey))
        {
            return null;
        }

        return _detail.TextureRows.FirstOrDefault(candidate =>
            string.Equals(candidate.ResourceKeyText, resourceKey.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool CanEdit(ModPackageTextureCandidate? candidate)
    {
        return candidate is not null &&
               candidate.Editable &&
               !string.Equals(candidate.SuggestedAction, "Keep", StringComparison.OrdinalIgnoreCase) &&
               !string.Equals(candidate.SuggestedAction, "Skip", StringComparison.OrdinalIgnoreCase);
    }
}
