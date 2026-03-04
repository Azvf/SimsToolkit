using System.Collections.Specialized;
using System.ComponentModel;
using SimsModDesktop.Application.Localization;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void OnLocalizationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(ILocalizationService.AvailableLanguages), StringComparison.Ordinal) &&
            !string.Equals(e.PropertyName, nameof(ILocalizationService.CurrentLanguageCode), StringComparison.Ordinal) &&
            !string.Equals(e.PropertyName, "Item[]", StringComparison.Ordinal))
        {
            return;
        }

        ExecuteOnUi(() =>
        {
            var normalized = _localization.CurrentLanguageCode;
            if (!string.Equals(_selectedLanguageCode, normalized, StringComparison.OrdinalIgnoreCase))
            {
                _selectedLanguageCode = normalized;
                OnPropertyChanged(nameof(SelectedLanguageCode));
            }

            NotifyLocalizationDependentProperties();
        });
    }

    private void OnStatusControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowStatusController.StatusMessage):
                OnPropertyChanged(nameof(StatusMessage));
                return;
            case nameof(MainWindowStatusController.IsProgressIndeterminate):
                OnPropertyChanged(nameof(IsProgressIndeterminate));
                return;
            case nameof(MainWindowStatusController.ProgressValue):
                OnPropertyChanged(nameof(ProgressValue));
                return;
            case nameof(MainWindowStatusController.ProgressMessage):
                OnPropertyChanged(nameof(ProgressMessage));
                return;
            case nameof(MainWindowStatusController.LogText):
                OnPropertyChanged(nameof(LogText));
                ClearLogCommand.NotifyCanExecuteChanged();
                return;
        }
    }

    private void OnTrayPreviewStateControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowTrayPreviewStateController.SummaryText):
                OnPropertyChanged(nameof(PreviewSummaryText));
                return;
            case nameof(MainWindowTrayPreviewStateController.TotalItems):
                OnPropertyChanged(nameof(PreviewTotalItems));
                OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
                return;
            case nameof(MainWindowTrayPreviewStateController.TotalFiles):
                OnPropertyChanged(nameof(PreviewTotalFiles));
                return;
            case nameof(MainWindowTrayPreviewStateController.TotalSize):
                OnPropertyChanged(nameof(PreviewTotalSize));
                return;
            case nameof(MainWindowTrayPreviewStateController.LatestWrite):
                OnPropertyChanged(nameof(PreviewLatestWrite));
                return;
            case nameof(MainWindowTrayPreviewStateController.PageText):
                OnPropertyChanged(nameof(PreviewPageText));
                return;
            case nameof(MainWindowTrayPreviewStateController.LazyLoadText):
                OnPropertyChanged(nameof(PreviewLazyLoadText));
                return;
            case nameof(MainWindowTrayPreviewStateController.JumpPageText):
                OnPropertyChanged(nameof(PreviewJumpPageText));
                OnPropertyChanged(nameof(CanJumpToPage));
                PreviewJumpPageCommand.NotifyCanExecuteChanged();
                return;
            case nameof(MainWindowTrayPreviewStateController.CurrentPage):
            case nameof(MainWindowTrayPreviewStateController.TotalPages):
                OnPropertyChanged(nameof(CanGoPrevPage));
                OnPropertyChanged(nameof(CanGoNextPage));
                OnPropertyChanged(nameof(CanJumpToPage));
                PreviewPrevPageCommand.NotifyCanExecuteChanged();
                PreviewNextPageCommand.NotifyCanExecuteChanged();
                PreviewJumpPageCommand.NotifyCanExecuteChanged();
                return;
            case nameof(MainWindowTrayPreviewStateController.HasLoadedOnce):
                OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusOk));
                OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusWarning));
                OnPropertyChanged(nameof(TrayPreviewEmptyTitleText));
                OnPropertyChanged(nameof(TrayPreviewEmptyDescriptionText));
                OnPropertyChanged(nameof(TrayPreviewEmptyStatusText));
                return;
        }
    }

    private void OnTrayPreviewSelectionControllerPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainWindowTrayPreviewSelectionController.HasSelectedItems):
                OnPropertyChanged(nameof(HasSelectedTrayPreviewItems));
                OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
                NotifyCommandStates();
                return;
            case nameof(MainWindowTrayPreviewSelectionController.SelectedCount):
                OnPropertyChanged(nameof(SelectedTrayPreviewCount));
                OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
                NotifyCommandStates();
                return;
            case nameof(MainWindowTrayPreviewSelectionController.DetailItem):
                OnPropertyChanged(nameof(TrayPreviewDetailItem));
                OnPropertyChanged(nameof(TrayPreviewDetailItemSafe));
                OnPropertyChanged(nameof(IsTrayPreviewDetailVisible));
                OnPropertyChanged(nameof(IsTrayPreviewDetailDescriptionEmpty));
                OnPropertyChanged(nameof(IsTrayPreviewDetailOverviewEmpty));
                GoBackTrayPreviewDetailCommand.NotifyCanExecuteChanged();
                CloseTrayPreviewDetailCommand.NotifyCanExecuteChanged();
                return;
            case nameof(MainWindowTrayPreviewSelectionController.CanGoBackDetail):
                OnPropertyChanged(nameof(CanGoBackTrayPreviewDetail));
                GoBackTrayPreviewDetailCommand.NotifyCanExecuteChanged();
                return;
        }
    }

    private void NotifyLocalizationDependentProperties()
    {
        OnPropertyChanged(nameof(AvailableLanguages));
        OnPropertyChanged(nameof(SelectedLanguage));
        OnPropertyChanged(nameof(IsChineseTranslationTodo));
        OnPropertyChanged(nameof(SettingsTitleText));
        OnPropertyChanged(nameof(SettingsLowFrequencySectionTitleText));
        OnPropertyChanged(nameof(SettingsLowFrequencySectionHintText));
        OnPropertyChanged(nameof(SettingsLanguageLabelText));
        OnPropertyChanged(nameof(SettingsLanguageTodoHintText));
        OnPropertyChanged(nameof(SettingsPrefixHashBytesLabelText));
        OnPropertyChanged(nameof(SettingsHashWorkerCountLabelText));
        OnPropertyChanged(nameof(SettingsPerformanceHintText));
        OnPropertyChanged(nameof(TrayPreviewBuildSizeFilterOptions));
        OnPropertyChanged(nameof(TrayPreviewHouseholdSizeFilterOptions));
        OnPropertyChanged(nameof(TrayPreviewEmptyTitleText));
        OnPropertyChanged(nameof(TrayPreviewEmptyDescriptionText));
        OnPropertyChanged(nameof(TrayPreviewEmptyStatusText));
        OnPropertyChanged(nameof(TrayPreviewLoadingText));
    }

    private void OnPreviewItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifyTrayPreviewViewStateChanged();
    }

    private void OnTrayExportTasksChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => _trayExportController.OnTrayExportTasksChanged(CreateTrayExportHost(), sender, e);

    private void OnTrayPreviewWorkspacePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(TrayPreviewWorkspaceViewModel.IsTrayDependencyCacheReady), StringComparison.Ordinal) &&
            !string.Equals(e.PropertyName, nameof(TrayPreviewWorkspaceViewModel.IsDependencyCacheWarmupBlocking), StringComparison.Ordinal))
        {
            return;
        }

        ExportSelectedTrayPreviewFilesCommand.NotifyCanExecuteChanged();
    }

    private void NotifyTrayPreviewFilterVisibilityChanged()
    {
        OnPropertyChanged(nameof(IsBuildSizeFilterVisible));
        OnPropertyChanged(nameof(IsHouseholdSizeFilterVisible));
    }

    private void NotifyTrayPreviewViewStateChanged()
    {
        OnPropertyChanged(nameof(HasTrayPreviewItems));
        OnPropertyChanged(nameof(TrayPreviewSelectionSummaryText));
        OnPropertyChanged(nameof(IsTrayExportQueueDockVisible));
        OnPropertyChanged(nameof(IsTrayExportQueueVisible));
        OnPropertyChanged(nameof(TrayExportQueueToggleText));
        OnPropertyChanged(nameof(IsTrayPreviewLoadingStateVisible));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStateVisible));
        OnPropertyChanged(nameof(IsTrayPreviewPagerVisible));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusOk));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusWarning));
        OnPropertyChanged(nameof(IsTrayPreviewEmptyStatusMissing));
        OnPropertyChanged(nameof(IsTrayPreviewPathMissing));
        OnPropertyChanged(nameof(TrayPreviewEmptyTitleText));
        OnPropertyChanged(nameof(TrayPreviewEmptyDescriptionText));
        OnPropertyChanged(nameof(TrayPreviewEmptyStatusText));
    }

}
