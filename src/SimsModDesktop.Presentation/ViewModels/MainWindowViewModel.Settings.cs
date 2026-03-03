using System.Collections.Specialized;
using System.ComponentModel;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    public Task InitializeAsync() =>
        _lifecycleController.InitializeAsync(CreateLifecycleHost());

    public Task PersistSettingsAsync() =>
        _lifecycleController.PersistSettingsAsync(CreateLifecycleHost());

    public Task ResumeRecoverableOperationAsync(RecoverableOperationRecord record, CancellationToken cancellationToken = default) =>
        _lifecycleController.ResumeRecoverableOperationAsync(CreateLifecycleHost(), record, cancellationToken);

    private void QueueSettingsPersist() =>
        _lifecycleController.QueueSettingsPersist(CreateLifecycleHost());

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

    private MainWindowLifecycleHost CreateLifecycleHost()
    {
        return new MainWindowLifecycleHost
        {
            SharedFileOps = SharedFileOps,
            ModPreview = ModPreview,
            GetIsInitialized = () => _isInitialized,
            SetIsInitialized = value => _isInitialized = value,
            GetSelectedLanguageCode = () => SelectedLanguageCode,
            SetSelectedLanguageCode = value => SelectedLanguageCode = value,
            GetScriptPath = () => ScriptPath,
            SetScriptPath = value => ScriptPath = value,
            GetWhatIf = () => WhatIf,
            SetWhatIf = value => WhatIf = value,
            GetSelectedAction = () => SelectedAction,
            SetSelectedAction = value => SelectedAction = value,
            GetWorkspace = () => Workspace,
            SetWorkspace = value => Workspace = value,
            GetIsToolkitLogDrawerOpen = () => IsToolkitLogDrawerOpen,
            SetIsToolkitLogDrawerOpen = value => IsToolkitLogDrawerOpen = value,
            GetIsTrayPreviewLogDrawerOpen = () => IsTrayPreviewLogDrawerOpen,
            SetIsTrayPreviewLogDrawerOpen = value => IsTrayPreviewLogDrawerOpen = value,
            GetIsToolkitAdvancedOpen = () => IsToolkitAdvancedOpen,
            SetIsToolkitAdvancedOpen = value => IsToolkitAdvancedOpen = value,
            AvailableToolkitActions = AvailableToolkitActions,
            ResolveFixedScriptPath = ResolveFixedScriptPath,
            ClearTrayPreview = ClearTrayPreview,
            CancelTrayPreviewThumbnailLoading = CancelTrayPreviewThumbnailLoading,
            RefreshValidation = RefreshValidationNow,
            CancelPendingValidation = _validationController.CancelPending,
            RunToolkitPlanAsync = RunToolkitPlanAsync,
            RunTrayDependenciesPlanAsync = RunTrayDependenciesPlanAsync,
            RunTrayPreviewCoreAsync = RunTrayPreviewCoreAsync,
            AppendLog = AppendLog,
            SetStatus = value => StatusMessage = value,
            ShowErrorPopupAsync = ShowErrorPopupAsync,
            Localize = L,
            LocalizeFormat = (key, args) => LF(key, args)
        };
    }
}
