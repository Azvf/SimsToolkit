using System.ComponentModel;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private void HookValidationTracking()
    {
        SubscribeForValidation(Organize);
        SubscribeForValidation(Flatten);
        SubscribeForValidation(Normalize);
        SubscribeForValidation(FindDup);
        SubscribeForValidation(TrayDependencies);
        SubscribeForValidation(ModPreview);
        SubscribeForValidation(TrayPreview);
        SubscribeForValidation(SharedFileOps);
        SubscribeForValidation(Merge);

        foreach (var sourcePath in Merge.SourcePaths)
        {
            sourcePath.PropertyChanged += OnMergeSourcePathPropertyChanged;
        }
    }

    private void SubscribeForValidation(INotifyPropertyChanged source)
    {
        source.PropertyChanged += OnPanelPropertyChanged;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        QueueValidationRefresh();

        if (ReferenceEquals(sender, TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.PresetTypeFilter), StringComparison.Ordinal))
        {
            if (IsBuildSizeFilterVisible && !string.Equals(TrayPreview.HouseholdSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                TrayPreview.HouseholdSizeFilter = "All";
            }
            else if (IsHouseholdSizeFilterVisible && !string.Equals(TrayPreview.BuildSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                TrayPreview.BuildSizeFilter = "All";
            }
            else if (!IsBuildSizeFilterVisible &&
                     !IsHouseholdSizeFilterVisible &&
                     (!string.Equals(TrayPreview.BuildSizeFilter, "All", StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(TrayPreview.HouseholdSizeFilter, "All", StringComparison.OrdinalIgnoreCase)))
            {
                TrayPreview.BuildSizeFilter = "All";
                TrayPreview.HouseholdSizeFilter = "All";
            }

            NotifyTrayPreviewFilterVisibilityChanged();
        }

        if (ReferenceEquals(sender, TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.EnableDebugPreview), StringComparison.Ordinal))
        {
            ApplyTrayPreviewDebugVisibility();

            if (_isInitialized)
            {
                QueueSettingsPersist();
            }
        }

        if (ReferenceEquals(sender, TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.LayoutMode), StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(IsTrayPreviewEntryMode));
            OnPropertyChanged(nameof(IsTrayPreviewGridMode));

            if (_isInitialized)
            {
                QueueSettingsPersist();
            }
        }

        if (ReferenceEquals(sender, ModPreview))
        {
            OnPropertyChanged(nameof(HasValidModPreviewPath));
            OnPropertyChanged(nameof(ModPreviewPathHintText));
        }

        if (!ReferenceEquals(sender, TrayPreview) || !IsTrayPreviewAutoReloadProperty(e.PropertyName))
        {
            return;
        }

        if (!HasValidTrayPreviewPath)
        {
            ClearTrayPreview();
            return;
        }

        if (IsTrayPreviewWorkspace)
        {
            QueueTrayPreviewAutoLoad();
        }
    }

    private static bool IsTrayPreviewAutoReloadProperty(string? propertyName)
    {
        return string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.TrayRoot), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.PresetTypeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.BuildSizeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.HouseholdSizeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.AuthorFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.TimeFilter), StringComparison.Ordinal) ||
               string.Equals(propertyName, nameof(TrayPreviewPanelViewModel.SearchQuery), StringComparison.Ordinal);
    }

    private void QueueTrayPreviewAutoLoad()
    {
        // Tray preview auto-load now lives in TrayPreviewWorkspace.Surface.
    }

    private void QueueValidationRefresh()
    {
        if (!_isInitialized)
        {
            return;
        }

        _validationDebounceCts?.Cancel();
        _validationDebounceCts?.Dispose();
        _validationDebounceCts = new CancellationTokenSource();
        var cancellationToken = _validationDebounceCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            RefreshValidationNow();
        }, cancellationToken);
    }

    private void RefreshValidationNow()
    {
        ExecuteOnUi(() =>
        {
            if (IsBusy)
            {
                return;
            }

            if (IsToolkitWorkspace)
            {
                if (SelectedAction == SimsAction.TrayDependencies)
                {
                    if (!_toolkitActionPlanner.TryBuildTrayDependenciesPlan(CreatePlanBuilderState(), out _, out var trayDependencyError))
                    {
                        HasValidationErrors = true;
                        ValidationSummaryText = LF("validation.failed", trayDependencyError);
                        return;
                    }
                }
                else if (SelectedAction == SimsAction.TextureCompress)
                {
                    if (!_toolkitActionPlanner.TryBuildTextureCompressionPlan(CreatePlanBuilderState(), out _, out var textureCompressError))
                    {
                        HasValidationErrors = true;
                        ValidationSummaryText = LF("validation.failed", textureCompressError);
                        return;
                    }
                }
                else if (!_toolkitActionPlanner.TryBuildToolkitCliPlan(CreatePlanBuilderState(), out _, out var error))
                {
                    HasValidationErrors = true;
                    ValidationSummaryText = LF("validation.failed", error);
                    return;
                }

                HasValidationErrors = false;
                ValidationSummaryText = LF("validation.okToolkit", _toolkitActionPlanner.GetDisplayName(SelectedAction));
                return;
            }

            if (IsModPreviewWorkspace)
            {
                HasValidationErrors = false;
                ValidationSummaryText = HasValidModPreviewPath
                    ? "Mod preview scaffold is ready."
                    : "Set a valid Mods Path in Settings to prepare the mod preview scaffold.";
                return;
            }

            if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(CreatePlanBuilderState(), out _, out var trayPreviewError))
            {
                HasValidationErrors = true;
                ValidationSummaryText = LF("validation.failed", trayPreviewError);
                return;
            }

            HasValidationErrors = false;
            ValidationSummaryText = L("validation.okTrayPreview");
        });
    }
}
