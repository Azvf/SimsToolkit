using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;

namespace SimsModDesktop.Presentation.ViewModels.Shell;

public sealed class ShellSystemOperationsController : ObservableObject
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IAppCacheMaintenanceService _appCacheMaintenanceService;
    private readonly ILogger<ShellSystemOperationsController> _logger;

    private string _launchGameStatus = string.Empty;
    private string _cacheMaintenanceStatus = string.Empty;

    public ShellSystemOperationsController(
        MainWindowViewModel workspaceVm,
        IGameLaunchService gameLaunchService,
        IAppCacheMaintenanceService appCacheMaintenanceService,
        ILogger<ShellSystemOperationsController>? logger = null)
    {
        _workspaceVm = workspaceVm;
        _gameLaunchService = gameLaunchService;
        _appCacheMaintenanceService = appCacheMaintenanceService;
        _logger = logger ?? NullLogger<ShellSystemOperationsController>.Instance;
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

    public async Task LaunchGameAsync(string? gameExecutablePath)
    {
        var executablePath = gameExecutablePath?.Trim() ?? string.Empty;
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} executablePath={ExecutablePath}",
            LogEvents.ShellOpsLaunchGameStart,
            "start",
            "shell",
            executablePath);
        var request = new LaunchGameRequest
        {
            ExecutablePath = executablePath,
            WorkingDirectory = string.IsNullOrWhiteSpace(executablePath)
                ? null
                : Path.GetDirectoryName(executablePath)
        };

        try
        {
            var result = _gameLaunchService.Launch(request);
            LaunchGameStatus = result.Message;
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} success={Success} message={Message}",
                LogEvents.ShellOpsLaunchGameDone,
                "done",
                "shell",
                result.Success,
                result.Message);
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{Event} status={Status} domain={Domain} executablePath={ExecutablePath}",
                LogEvents.ShellOpsLaunchGameFail,
                "fail",
                "shell",
                executablePath);
            throw;
        }
    }

    public async Task ClearCacheAsync(bool isTraySectionActive)
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} isTraySectionActive={IsTraySectionActive}",
            LogEvents.ShellOpsClearCacheStart,
            "start",
            "shell",
            isTraySectionActive);
        CacheMaintenanceStatus = "Clearing disk cache...";
        try
        {
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
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} success={Success} removedDirectories={RemovedDirectoryCount} message={Message}",
                LogEvents.ShellOpsClearCacheDone,
                "done",
                "shell",
                result.Success,
                result.RemovedDirectoryCount,
                result.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "{Event} status={Status} domain={Domain}",
                LogEvents.ShellOpsClearCacheFail,
                "fail",
                "shell");
            throw;
        }
    }
}
