using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Caching;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.Presentation.Services;
using SimsModDesktop.Presentation.ViewModels.Infrastructure;
using SimsModDesktop.Presentation.ViewModels.Saves;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.ViewModels.Shell;

public sealed class ShellSystemOperationsController : ObservableObject
{
    private readonly MainWindowViewModel _workspaceVm;
    private readonly SaveWorkspaceViewModel? _savesVm;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IAppCacheMaintenanceService _appCacheMaintenanceService;
    private readonly ITrayBundleAnalysisCache? _trayBundleAnalysisCache;
    private readonly AppIdlePrewarmBootstrapper? _idlePrewarmBootstrapper;
    private readonly IListQueryCache? _listQueryCache;
    private readonly ILogger<ShellSystemOperationsController> _logger;

    private string _launchGameStatus = string.Empty;
    private string _cacheMaintenanceStatus = string.Empty;

    public ShellSystemOperationsController(
        MainWindowViewModel workspaceVm,
        SaveWorkspaceViewModel? savesVm,
        IGameLaunchService gameLaunchService,
        IAppCacheMaintenanceService appCacheMaintenanceService,
        ILogger<ShellSystemOperationsController>? logger = null,
        ITrayBundleAnalysisCache? trayBundleAnalysisCache = null,
        AppIdlePrewarmBootstrapper? idlePrewarmBootstrapper = null,
        IListQueryCache? listQueryCache = null)
    {
        _workspaceVm = workspaceVm;
        _savesVm = savesVm;
        _gameLaunchService = gameLaunchService;
        _appCacheMaintenanceService = appCacheMaintenanceService;
        _trayBundleAnalysisCache = trayBundleAnalysisCache;
        _idlePrewarmBootstrapper = idlePrewarmBootstrapper;
        _listQueryCache = listQueryCache;
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
                _idlePrewarmBootstrapper?.Reset();
                _trayBundleAnalysisCache?.Reset();
                _listQueryCache?.Clear();
                _workspaceVm.ModPreviewWorkspace.ResetAfterCacheClear();
                _workspaceVm.TrayPreviewWorkspace.ResetAfterCacheClear();
                _savesVm?.ResetAfterCacheClear();
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
