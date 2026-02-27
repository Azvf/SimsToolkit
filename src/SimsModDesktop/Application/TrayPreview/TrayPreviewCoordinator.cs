using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;
using SimsModDesktop.Services;

namespace SimsModDesktop.Application.TrayPreview;

public sealed class TrayPreviewCoordinator : ITrayPreviewCoordinator
{
    private readonly ISimsTrayPreviewService _previewService;
    private readonly IActionInputValidator<TrayPreviewInput> _validator;
    private readonly Dictionary<int, SimsTrayPreviewPage> _pageCache = new();

    private TrayPreviewInput? _activeInput;
    private SimsTrayPreviewDashboard? _activeDashboard;
    private string _activeFingerprint = string.Empty;
    private int _activePageIndex = 1;

    public TrayPreviewCoordinator(
        ISimsTrayPreviewService previewService,
        IActionInputValidator<TrayPreviewInput> validator)
    {
        _previewService = previewService;
        _validator = validator;
    }

    public bool TryGetCached(TrayPreviewInput input, out TrayPreviewLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(input);

        result = null!;
        if (!_validator.TryValidate(input, out _))
        {
            return false;
        }

        var fingerprint = BuildFingerprint(input);
        if (!fingerprint.Equals(_activeFingerprint, StringComparison.Ordinal) ||
            _activeDashboard is null ||
            !_pageCache.TryGetValue(_activePageIndex, out var cachedPage))
        {
            return false;
        }

        result = new TrayPreviewLoadResult
        {
            Dashboard = _activeDashboard,
            Page = cachedPage,
            LoadedPageCount = _pageCache.Count
        };
        return true;
    }

    public async Task<TrayPreviewLoadResult> LoadAsync(
        TrayPreviewInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!_validator.TryValidate(input, out var validationError))
        {
            throw new InvalidOperationException(validationError);
        }

        var request = ToRequest(input);
        var dashboard = await _previewService.BuildDashboardAsync(request, cancellationToken);
        var firstPage = await _previewService.BuildPageAsync(request, pageIndex: 1, cancellationToken);

        _activeInput = input;
        _activeDashboard = dashboard;
        _activeFingerprint = BuildFingerprint(input);
        _activePageIndex = firstPage.PageIndex;
        _pageCache.Clear();
        _pageCache[firstPage.PageIndex] = firstPage;

        return new TrayPreviewLoadResult
        {
            Dashboard = dashboard,
            Page = firstPage,
            LoadedPageCount = _pageCache.Count
        };
    }

    public async Task<TrayPreviewPageResult> LoadPageAsync(int requestedPageIndex, CancellationToken cancellationToken = default)
    {
        if (_activeInput is null || _activeDashboard is null)
        {
            throw new InvalidOperationException("Tray preview is not loaded yet.");
        }

        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(_activeDashboard.TotalItems / (double)_activeInput.PageSize));
        var targetPageIndex = Math.Clamp(requestedPageIndex, 1, totalPages);

        if (_pageCache.TryGetValue(targetPageIndex, out var cachedPage))
        {
            _activePageIndex = cachedPage.PageIndex;
            return new TrayPreviewPageResult
            {
                Page = cachedPage,
                LoadedPageCount = _pageCache.Count,
                FromCache = true
            };
        }

        var request = ToRequest(_activeInput);
        var page = await _previewService.BuildPageAsync(request, targetPageIndex, cancellationToken);
        _activePageIndex = page.PageIndex;
        _pageCache[page.PageIndex] = page;

        return new TrayPreviewPageResult
        {
            Page = page,
            LoadedPageCount = _pageCache.Count,
            FromCache = false
        };
    }

    public void Reset()
    {
        _activeInput = null;
        _activeDashboard = null;
        _activeFingerprint = string.Empty;
        _activePageIndex = 1;
        _pageCache.Clear();
    }

    private static SimsTrayPreviewRequest ToRequest(TrayPreviewInput input)
    {
        return new SimsTrayPreviewRequest
        {
            TrayPath = Path.GetFullPath(input.TrayPath.Trim()),
            TrayItemKey = input.TrayItemKey.Trim(),
            TopN = input.TopN,
            MaxFilesPerItem = input.MaxFilesPerItem,
            PageSize = input.PageSize
        };
    }

    private static string BuildFingerprint(TrayPreviewInput input)
    {
        var trayPath = Path.GetFullPath(input.TrayPath.Trim())
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var trayItemKey = input.TrayItemKey.Trim().ToLowerInvariant();
        var topN = input.TopN?.ToString() ?? "all";

        return string.Join("|", trayPath, trayItemKey, topN, input.MaxFilesPerItem, input.PageSize);
    }
}
