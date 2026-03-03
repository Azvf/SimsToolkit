using System.ComponentModel;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowValidationController
{
    private readonly IToolkitActionPlanner _toolkitActionPlanner;
    private CancellationTokenSource? _validationDebounceCts;
    private MainWindowValidationHost? _hookedHost;

    public MainWindowValidationController(IToolkitActionPlanner toolkitActionPlanner)
    {
        _toolkitActionPlanner = toolkitActionPlanner;
    }

    internal void HookValidationTracking(MainWindowValidationHost host)
    {
        _hookedHost = host;

        SubscribeForValidation(host.Organize);
        SubscribeForValidation(host.Flatten);
        SubscribeForValidation(host.Normalize);
        SubscribeForValidation(host.FindDup);
        SubscribeForValidation(host.TrayDependencies);
        SubscribeForValidation(host.ModPreview);
        SubscribeForValidation(host.TrayPreview);
        SubscribeForValidation(host.SharedFileOps);
        SubscribeForValidation(host.Merge);

        foreach (var sourcePath in host.Merge.SourcePaths)
        {
            sourcePath.PropertyChanged += OnMergeSourcePathPropertyChanged;
        }
    }

    internal void QueueValidationRefresh(MainWindowValidationHost host)
    {
        if (!host.GetIsInitialized())
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

            RefreshValidationNow(host);
        }, cancellationToken);
    }

    internal void RefreshValidationNow(MainWindowValidationHost host)
    {
        host.ExecuteOnUi(() =>
        {
            if (host.GetIsBusy())
            {
                return;
            }

            var workspace = host.GetWorkspace();
            if (workspace == AppWorkspace.Toolkit)
            {
                var selectedAction = host.GetSelectedAction();
                if (selectedAction == SimsAction.TrayDependencies)
                {
                    if (!_toolkitActionPlanner.TryBuildTrayDependenciesPlan(host.CreatePlanBuilderState(), out _, out var trayDependencyError))
                    {
                        host.SetHasValidationErrors(true);
                        host.SetValidationSummary(host.LocalizeFormat("validation.failed", [trayDependencyError]));
                        return;
                    }
                }
                else if (selectedAction == SimsAction.TextureCompress)
                {
                    if (!_toolkitActionPlanner.TryBuildTextureCompressionPlan(host.CreatePlanBuilderState(), out _, out var textureCompressError))
                    {
                        host.SetHasValidationErrors(true);
                        host.SetValidationSummary(host.LocalizeFormat("validation.failed", [textureCompressError]));
                        return;
                    }
                }
                else if (!_toolkitActionPlanner.TryBuildToolkitCliPlan(host.CreatePlanBuilderState(), out _, out var error))
                {
                    host.SetHasValidationErrors(true);
                    host.SetValidationSummary(host.LocalizeFormat("validation.failed", [error]));
                    return;
                }

                host.SetHasValidationErrors(false);
                host.SetValidationSummary(host.LocalizeFormat("validation.okToolkit", [_toolkitActionPlanner.GetDisplayName(selectedAction)]));
                return;
            }

            if (workspace == AppWorkspace.ModPreview)
            {
                host.SetHasValidationErrors(false);
                host.SetValidationSummary(host.GetHasValidModPreviewPath()
                    ? "Mod preview scaffold is ready."
                    : "Set a valid Mods Path in Settings to prepare the mod preview scaffold.");
                return;
            }

            if (!_toolkitActionPlanner.TryBuildTrayPreviewInput(host.CreatePlanBuilderState(), out _, out var trayPreviewError))
            {
                host.SetHasValidationErrors(true);
                host.SetValidationSummary(host.LocalizeFormat("validation.failed", [trayPreviewError]));
                return;
            }

            host.SetHasValidationErrors(false);
            host.SetValidationSummary(host.Localize("validation.okTrayPreview"));
        });
    }

    internal void CancelPending()
    {
        _validationDebounceCts?.Cancel();
        _validationDebounceCts?.Dispose();
        _validationDebounceCts = null;
    }

    private void SubscribeForValidation(INotifyPropertyChanged source)
    {
        source.PropertyChanged += OnPanelPropertyChanged;
    }

    private void OnPanelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var host = _hookedHost;
        if (host is null)
        {
            return;
        }

        QueueValidationRefresh(host);

        if (ReferenceEquals(sender, host.TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.PresetTypeFilter), StringComparison.Ordinal))
        {
            if (host.GetIsBuildSizeFilterVisible() &&
                !string.Equals(host.TrayPreview.HouseholdSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                host.TrayPreview.HouseholdSizeFilter = "All";
            }
            else if (host.GetIsHouseholdSizeFilterVisible() &&
                     !string.Equals(host.TrayPreview.BuildSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
            {
                host.TrayPreview.BuildSizeFilter = "All";
            }
            else if (!host.GetIsBuildSizeFilterVisible() &&
                     !host.GetIsHouseholdSizeFilterVisible() &&
                     (!string.Equals(host.TrayPreview.BuildSizeFilter, "All", StringComparison.OrdinalIgnoreCase) ||
                      !string.Equals(host.TrayPreview.HouseholdSizeFilter, "All", StringComparison.OrdinalIgnoreCase)))
            {
                host.TrayPreview.BuildSizeFilter = "All";
                host.TrayPreview.HouseholdSizeFilter = "All";
            }

            host.NotifyTrayPreviewFilterVisibilityChanged();
        }

        if (ReferenceEquals(sender, host.TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.EnableDebugPreview), StringComparison.Ordinal))
        {
            host.ApplyTrayPreviewDebugVisibility();

            if (host.GetIsInitialized())
            {
                host.QueueSettingsPersist();
            }
        }

        if (ReferenceEquals(sender, host.TrayPreview) &&
            string.Equals(e.PropertyName, nameof(TrayPreviewPanelViewModel.LayoutMode), StringComparison.Ordinal))
        {
            host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayPreviewEntryMode));
            host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayPreviewGridMode));

            if (host.GetIsInitialized())
            {
                host.QueueSettingsPersist();
            }
        }

        if (ReferenceEquals(sender, host.ModPreview))
        {
            host.RaisePropertyChanged(nameof(MainWindowViewModel.HasValidModPreviewPath));
            host.RaisePropertyChanged(nameof(MainWindowViewModel.ModPreviewPathHintText));
        }

        if (!ReferenceEquals(sender, host.TrayPreview) || !IsTrayPreviewAutoReloadProperty(e.PropertyName))
        {
            return;
        }

        if (!host.GetHasValidTrayPreviewPath())
        {
            host.ClearTrayPreview();
            return;
        }

        if (host.GetWorkspace() == AppWorkspace.TrayPreview)
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
    }

    private void OnMergeSourcePathPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_hookedHost is not null)
        {
            QueueValidationRefresh(_hookedHost);
        }
    }
}
