using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowTrayPreviewStateController : ObservableObject
{
    private int _currentPage = 1;
    private int _totalPages = 1;
    private string _summaryText = "No preview data loaded.";
    private string _totalItems = "0";
    private string _totalFiles = "0";
    private string _totalSize = "0 MB";
    private string _latestWrite = "-";
    private string _pageText = "Page 0/0";
    private string _lazyLoadText = "Lazy cache 0/0 pages";
    private string _jumpPageText = string.Empty;
    private bool _hasLoadedOnce;

    public int CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public int TotalPages
    {
        get => _totalPages;
        private set => SetProperty(ref _totalPages, value);
    }

    public string SummaryText
    {
        get => _summaryText;
        private set => SetProperty(ref _summaryText, value);
    }

    public string TotalItems
    {
        get => _totalItems;
        private set => SetProperty(ref _totalItems, value);
    }

    public string TotalFiles
    {
        get => _totalFiles;
        private set => SetProperty(ref _totalFiles, value);
    }

    public string TotalSize
    {
        get => _totalSize;
        private set => SetProperty(ref _totalSize, value);
    }

    public string LatestWrite
    {
        get => _latestWrite;
        private set => SetProperty(ref _latestWrite, value);
    }

    public string PageText
    {
        get => _pageText;
        private set => SetProperty(ref _pageText, value);
    }

    public string LazyLoadText
    {
        get => _lazyLoadText;
        private set => SetProperty(ref _lazyLoadText, value);
    }

    public string JumpPageText
    {
        get => _jumpPageText;
        set => SetProperty(ref _jumpPageText, value);
    }

    public bool HasLoadedOnce
    {
        get => _hasLoadedOnce;
        private set => SetProperty(ref _hasLoadedOnce, value);
    }

    public void Reset(string summaryText, string pageText, string lazyLoadText)
    {
        SummaryText = summaryText;
        TotalItems = "0";
        TotalFiles = "0";
        TotalSize = "0 MB";
        LatestWrite = "-";
        PageText = pageText;
        LazyLoadText = lazyLoadText;
        JumpPageText = string.Empty;
        HasLoadedOnce = false;
        CurrentPage = 1;
        TotalPages = 1;
    }

    public void ApplySummary(
        string totalItems,
        string totalFiles,
        string totalSize,
        string latestWrite,
        string summaryText)
    {
        TotalItems = totalItems;
        TotalFiles = totalFiles;
        TotalSize = totalSize;
        LatestWrite = latestWrite;
        SummaryText = summaryText;
    }

    public void ApplyPage(
        int currentPage,
        int totalPages,
        string totalItems,
        string summaryText,
        string pageText,
        string lazyLoadText,
        string jumpPageText)
    {
        HasLoadedOnce = true;
        CurrentPage = currentPage;
        TotalPages = Math.Max(totalPages, 1);
        TotalItems = totalItems;
        SummaryText = summaryText;
        PageText = pageText;
        LazyLoadText = lazyLoadText;
        JumpPageText = jumpPageText;
    }
}
