using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Settings;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowLifecycleController
{
    private readonly MainWindowSettingsPersistenceController _settingsPersistenceController;
    private readonly IMainWindowSettingsProjection _settingsProjection;
    private readonly MainWindowRecoveryController _recoveryController;
    private readonly IToolkitActionPlanner _toolkitActionPlanner;

    public MainWindowLifecycleController(
        MainWindowSettingsPersistenceController settingsPersistenceController,
        IMainWindowSettingsProjection settingsProjection,
        MainWindowRecoveryController recoveryController,
        IToolkitActionPlanner toolkitActionPlanner)
    {
        _settingsPersistenceController = settingsPersistenceController;
        _settingsProjection = settingsProjection;
        _recoveryController = recoveryController;
        _toolkitActionPlanner = toolkitActionPlanner;
    }

    internal async Task InitializeAsync(MainWindowLifecycleHost host)
    {
        if (host.GetIsInitialized())
        {
            return;
        }

        await _recoveryController.InitializeAsync();

        var settings = await _settingsPersistenceController.LoadAsync();
        var resolved = _settingsProjection.Resolve(settings, host.AvailableToolkitActions);

        host.SetSelectedLanguageCode(resolved.UiLanguageCode);
        host.SetScriptPath(resolved.ScriptPath);
        host.SetWhatIf(resolved.WhatIf);

        host.SharedFileOps.SkipPruneEmptyDirs = resolved.SharedFileOps.SkipPruneEmptyDirs;
        host.SharedFileOps.ModFilesOnly = resolved.SharedFileOps.ModFilesOnly;
        host.SharedFileOps.VerifyContentOnNameConflict = resolved.SharedFileOps.VerifyContentOnNameConflict;
        host.SharedFileOps.ModExtensionsText = resolved.SharedFileOps.ModExtensionsText;
        host.SharedFileOps.PrefixHashBytesText = resolved.SharedFileOps.PrefixHashBytesText;
        host.SharedFileOps.HashWorkerCountText = resolved.SharedFileOps.HashWorkerCountText;
        host.ModPreview.ModsRoot = resolved.ModPreview.ModsRoot;
        host.ModPreview.PackageTypeFilter = resolved.ModPreview.PackageTypeFilter;
        host.ModPreview.ScopeFilter = resolved.ModPreview.ScopeFilter;
        host.ModPreview.SortBy = resolved.ModPreview.SortBy;
        host.ModPreview.SearchQuery = resolved.ModPreview.SearchQuery;
        host.ModPreview.ShowOverridesOnly = resolved.ModPreview.ShowOverridesOnly;
        host.SetIsToolkitLogDrawerOpen(resolved.UiState.ToolkitLogDrawerOpen);
        host.SetIsTrayPreviewLogDrawerOpen(resolved.UiState.TrayPreviewLogDrawerOpen);
        host.SetIsToolkitAdvancedOpen(resolved.UiState.ToolkitAdvancedOpen);

        _toolkitActionPlanner.LoadModuleSettings(settings);
        host.SetSelectedAction(resolved.SelectedAction);
        host.SetWorkspace(resolved.Workspace);

        host.SetScriptPath(host.ResolveFixedScriptPath());
        if (!File.Exists(host.GetScriptPath()))
        {
            host.SetStatus(host.LocalizeFormat("status.scriptNotFound", [host.GetScriptPath()]));
        }

        host.ClearTrayPreview();
        if (File.Exists(host.GetScriptPath()))
        {
            host.SetStatus(host.Localize("status.ready"));
        }

        host.SetIsInitialized(true);
        host.RefreshValidation();
    }

    internal async Task PersistSettingsAsync(MainWindowLifecycleHost host)
    {
        host.CancelPendingValidation();
        _settingsPersistenceController.CancelPending();
        host.CancelTrayPreviewThumbnailLoading();
        await SaveCurrentSettingsAsync(host);
    }

    internal async Task ResumeRecoverableOperationAsync(
        MainWindowLifecycleHost host,
        RecoverableOperationRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            switch (record.Payload.PayloadKind)
            {
                case "ToolkitCli":
                    await host.RunToolkitPlanAsync(_recoveryController.BuildToolkitCliPlan(record.Payload), record.OperationId);
                    break;
                case "TrayDependencies":
                    await host.RunTrayDependenciesPlanAsync(
                        new TrayDependenciesExecutionPlan(_recoveryController.BuildTrayDependenciesRequest(record.Payload)),
                        record.OperationId);
                    break;
                case "TrayPreview":
                    await host.RunTrayPreviewCoreAsync(_recoveryController.BuildTrayPreviewInput(record.Payload), record.OperationId);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported recovery payload kind: {record.Payload.PayloadKind}");
            }
        }
        catch (Exception ex)
        {
            await _recoveryController.MarkRecoveryCompletedAsync(
                record.OperationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = ex.Message
                });

            host.AppendLog("[recovery] " + ex.Message);
            host.SetStatus("Failed to resume the previous task.");
            await host.ShowErrorPopupAsync("Failed to resume the previous task.");
        }
    }

    internal void QueueSettingsPersist(MainWindowLifecycleHost host)
    {
        _settingsPersistenceController.QueuePersist(settings => ApplyCurrentSettings(host, settings));
    }

    private Task SaveCurrentSettingsAsync(MainWindowLifecycleHost host, CancellationToken cancellationToken = default)
    {
        return _settingsPersistenceController.SaveAsync(settings => ApplyCurrentSettings(host, settings), cancellationToken);
    }

    private void ApplyCurrentSettings(MainWindowLifecycleHost host, AppSettings settings)
    {
        settings.UiLanguageCode = string.IsNullOrWhiteSpace(host.GetSelectedLanguageCode())
            ? "en-US"
            : host.GetSelectedLanguageCode().Trim();
        settings.ScriptPath = host.GetScriptPath();
        settings.SelectedWorkspace = host.GetWorkspace();
        settings.SelectedAction = host.GetSelectedAction();
        settings.WhatIf = host.GetWhatIf();
        settings.SharedFileOps = new AppSettings.SharedFileOpsSettings
        {
            SkipPruneEmptyDirs = host.SharedFileOps.SkipPruneEmptyDirs,
            ModFilesOnly = host.SharedFileOps.ModFilesOnly,
            VerifyContentOnNameConflict = host.SharedFileOps.VerifyContentOnNameConflict,
            ModExtensionsText = host.SharedFileOps.ModExtensionsText,
            PrefixHashBytesText = host.SharedFileOps.PrefixHashBytesText,
            HashWorkerCountText = host.SharedFileOps.HashWorkerCountText
        };
        settings.UiState = new AppSettings.UiStateSettings
        {
            ToolkitLogDrawerOpen = host.GetIsToolkitLogDrawerOpen(),
            TrayPreviewLogDrawerOpen = host.GetIsTrayPreviewLogDrawerOpen(),
            ToolkitAdvancedOpen = host.GetIsToolkitAdvancedOpen()
        };
        settings.ModPreview = new AppSettings.ModPreviewSettings
        {
            ModsRoot = host.ModPreview.ModsRoot,
            PackageTypeFilter = host.ModPreview.PackageTypeFilter,
            ScopeFilter = host.ModPreview.ScopeFilter,
            SortBy = host.ModPreview.SortBy,
            SearchQuery = host.ModPreview.SearchQuery,
            ShowOverridesOnly = host.ModPreview.ShowOverridesOnly
        };

        _toolkitActionPlanner.SaveModuleSettings(settings);
    }
}
