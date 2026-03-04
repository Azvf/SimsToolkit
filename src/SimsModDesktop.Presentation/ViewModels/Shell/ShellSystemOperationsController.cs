using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels.Shell;

public sealed class ShellSystemOperationsController : ObservableObject
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IAppCacheMaintenanceService _appCacheMaintenanceService;

    private string _launchGameStatus = string.Empty;
    private string _cacheMaintenanceStatus = string.Empty;

    public ShellSystemOperationsController(
        MainWindowViewModel workspaceVm,
        IGameLaunchService gameLaunchService,
        IAppCacheMaintenanceService appCacheMaintenanceService)
    {
        _workspaceVm = workspaceVm;
        _gameLaunchService = gameLaunchService;
        _appCacheMaintenanceService = appCacheMaintenanceService;
    }

    public string LaunchGameStatus
    {
        get => _launchGameStatus;
        private set => SetProperty(ref _launchGameStatus, value);
    }

    public string CacheMaintenanceStatus
    {
        get => _cacheMaintenanceStatus;
        private set => SetProperty(ref _cacheMaintenanceStatus, value);
    }

    public async Task LaunchGameAsync(string gameExecutablePath)
    {
        var request = new LaunchGameRequest
        {
            ExecutablePath = gameExecutablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(gameExecutablePath)
                ? null
                : Path.GetDirectoryName(gameExecutablePath)
        };

        var result = _gameLaunchService.Launch(request);
        LaunchGameStatus = result.Message;
        await Task.CompletedTask;
    }

    public async Task ClearCacheAsync(bool isTraySectionActive)
    {
        CacheMaintenanceStatus = "Clearing disk cache...";
        var result = await _appCacheMaintenanceService.ClearAsync();
        if (result.Success)
        {
            _workspaceVm.ModPreviewWorkspace.ResetAfterCacheClear();
            _workspaceVm.TrayPreviewWorkspace.ResetAfterCacheClear();
            if (isTraySectionActive)
            {
                await _workspaceVm.TrayPreviewWorkspace.EnsureLoadedAsync(forceReload: true);
            }
        }

        CacheMaintenanceStatus = result.Message;
    }
}
