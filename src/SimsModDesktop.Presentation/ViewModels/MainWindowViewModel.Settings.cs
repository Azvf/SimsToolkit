using System.Collections.Specialized;
using System.ComponentModel;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Settings;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _recoveryController.InitializeAsync();

        var settings = await _settingsPersistenceController.LoadAsync();
        var resolved = _settingsProjection.Resolve(settings, AvailableToolkitActions);

        SelectedLanguageCode = resolved.UiLanguageCode;
        ScriptPath = resolved.ScriptPath;
        WhatIf = resolved.WhatIf;

        SharedFileOps.SkipPruneEmptyDirs = resolved.SharedFileOps.SkipPruneEmptyDirs;
        SharedFileOps.ModFilesOnly = resolved.SharedFileOps.ModFilesOnly;
        SharedFileOps.VerifyContentOnNameConflict = resolved.SharedFileOps.VerifyContentOnNameConflict;
        SharedFileOps.ModExtensionsText = resolved.SharedFileOps.ModExtensionsText;
        SharedFileOps.PrefixHashBytesText = resolved.SharedFileOps.PrefixHashBytesText;
        SharedFileOps.HashWorkerCountText = resolved.SharedFileOps.HashWorkerCountText;
        ModPreview.ModsRoot = resolved.ModPreview.ModsRoot;
        ModPreview.PackageTypeFilter = resolved.ModPreview.PackageTypeFilter;
        ModPreview.ScopeFilter = resolved.ModPreview.ScopeFilter;
        ModPreview.SortBy = resolved.ModPreview.SortBy;
        ModPreview.SearchQuery = resolved.ModPreview.SearchQuery;
        ModPreview.ShowOverridesOnly = resolved.ModPreview.ShowOverridesOnly;
        IsToolkitLogDrawerOpen = resolved.UiState.ToolkitLogDrawerOpen;
        IsTrayPreviewLogDrawerOpen = resolved.UiState.TrayPreviewLogDrawerOpen;
        IsToolkitAdvancedOpen = resolved.UiState.ToolkitAdvancedOpen;

        _toolkitActionPlanner.LoadModuleSettings(settings);
        SelectedAction = resolved.SelectedAction;
        Workspace = resolved.Workspace;

        ScriptPath = ResolveFixedScriptPath();
        if (!File.Exists(ScriptPath))
        {
            StatusMessage = LF("status.scriptNotFound", ScriptPath);
        }

        ClearTrayPreview();
        if (File.Exists(ScriptPath))
        {
            StatusMessage = L("status.ready");
        }

        _isInitialized = true;
        RefreshValidationNow();
    }

    public async Task PersistSettingsAsync()
    {
        _validationDebounceCts?.Cancel();
        _settingsPersistenceController.CancelPending();
        CancelTrayPreviewThumbnailLoading();
        await SaveCurrentSettingsAsync();
    }

    public async Task ResumeRecoverableOperationAsync(RecoverableOperationRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            switch (record.Payload.PayloadKind)
            {
                case "ToolkitCli":
                    await RunToolkitPlanAsync(_recoveryController.BuildToolkitCliPlan(record.Payload), record.OperationId);
                    break;
                case "TrayDependencies":
                    await RunTrayDependenciesPlanAsync(
                        new TrayDependenciesExecutionPlan(_recoveryController.BuildTrayDependenciesRequest(record.Payload)),
                        record.OperationId);
                    break;
                case "TrayPreview":
                    await RunTrayPreviewCoreAsync(_recoveryController.BuildTrayPreviewInput(record.Payload), record.OperationId);
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

            AppendLog("[recovery] " + ex.Message);
            StatusMessage = "Failed to resume the previous task.";
            await ShowErrorPopupAsync("Failed to resume the previous task.");
        }
    }

    private void QueueSettingsPersist()
    {
        _settingsPersistenceController.QueuePersist(ApplyCurrentSettings);
    }

    private async Task SaveCurrentSettingsAsync(CancellationToken cancellationToken = default)
    {
        await _settingsPersistenceController.SaveAsync(ApplyCurrentSettings, cancellationToken);
    }

    private void ApplyCurrentSettings(AppSettings settings)
    {
        settings.UiLanguageCode = string.IsNullOrWhiteSpace(SelectedLanguageCode)
            ? DefaultLanguageCode
            : SelectedLanguageCode.Trim();
        settings.ScriptPath = ScriptPath;
        settings.SelectedWorkspace = Workspace;
        settings.SelectedAction = SelectedAction;
        settings.WhatIf = WhatIf;
        settings.SharedFileOps = new AppSettings.SharedFileOpsSettings
        {
            SkipPruneEmptyDirs = SharedFileOps.SkipPruneEmptyDirs,
            ModFilesOnly = SharedFileOps.ModFilesOnly,
            VerifyContentOnNameConflict = SharedFileOps.VerifyContentOnNameConflict,
            ModExtensionsText = SharedFileOps.ModExtensionsText,
            PrefixHashBytesText = SharedFileOps.PrefixHashBytesText,
            HashWorkerCountText = SharedFileOps.HashWorkerCountText
        };
        settings.UiState = new AppSettings.UiStateSettings
        {
            ToolkitLogDrawerOpen = IsToolkitLogDrawerOpen,
            TrayPreviewLogDrawerOpen = IsTrayPreviewLogDrawerOpen,
            ToolkitAdvancedOpen = IsToolkitAdvancedOpen
        };
        settings.ModPreview = new AppSettings.ModPreviewSettings
        {
            ModsRoot = ModPreview.ModsRoot,
            PackageTypeFilter = ModPreview.PackageTypeFilter,
            ScopeFilter = ModPreview.ScopeFilter,
            SortBy = ModPreview.SortBy,
            SearchQuery = ModPreview.SearchQuery,
            ShowOverridesOnly = ModPreview.ShowOverridesOnly
        };

        _toolkitActionPlanner.SaveModuleSettings(settings);
    }

    private void OnMergeSourcePathsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (var removed in e.OldItems.OfType<MergeSourcePathEntryViewModel>())
            {
                removed.PropertyChanged -= OnMergeSourcePathPropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var added in e.NewItems.OfType<MergeSourcePathEntryViewModel>())
            {
                added.PropertyChanged += OnMergeSourcePathPropertyChanged;
            }
        }

        NotifyCommandStates();
        QueueValidationRefresh();
    }

    private void OnMergeSourcePathPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueValidationRefresh();
    }
}
