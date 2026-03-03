using System.ComponentModel;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void HookValidationTracking() =>
        _validationController.HookValidationTracking(CreateValidationHost());

    private void QueueValidationRefresh() =>
        _validationController.QueueValidationRefresh(CreateValidationHost());

    private void RefreshValidationNow() =>
        _validationController.RefreshValidationNow(CreateValidationHost());

    private void OnMergeSourcePathPropertyChanged(object? sender, PropertyChangedEventArgs e) =>
        QueueValidationRefresh();

    private MainWindowValidationHost CreateValidationHost()
    {
        return new MainWindowValidationHost
        {
            Organize = Organize,
            Flatten = Flatten,
            Normalize = Normalize,
            FindDup = FindDup,
            TrayDependencies = TrayDependencies,
            ModPreview = ModPreview,
            TrayPreview = TrayPreview,
            SharedFileOps = SharedFileOps,
            Merge = Merge,
            GetSelectedAction = () => SelectedAction,
            GetWorkspace = () => Workspace,
            GetIsBusy = () => IsBusy,
            GetIsInitialized = () => _isInitialized,
            GetHasValidModPreviewPath = () => HasValidModPreviewPath,
            GetHasValidTrayPreviewPath = () => HasValidTrayPreviewPath,
            GetIsBuildSizeFilterVisible = () => IsBuildSizeFilterVisible,
            GetIsHouseholdSizeFilterVisible = () => IsHouseholdSizeFilterVisible,
            CreatePlanBuilderState = CreatePlanBuilderState,
            QueueSettingsPersist = QueueSettingsPersist,
            ClearTrayPreview = ClearTrayPreview,
            ApplyTrayPreviewDebugVisibility = ApplyTrayPreviewDebugVisibility,
            NotifyTrayPreviewFilterVisibilityChanged = NotifyTrayPreviewFilterVisibilityChanged,
            ExecuteOnUi = ExecuteOnUi,
            SetValidationSummary = value => ValidationSummaryText = value,
            SetHasValidationErrors = value => HasValidationErrors = value,
            RaisePropertyChanged = propertyName => OnPropertyChanged(propertyName),
            Localize = L,
            LocalizeFormat = (key, args) => LF(key, args)
        };
    }
}
