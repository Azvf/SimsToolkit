using SimsModDesktop.Application.Localization;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Presentation.ViewModels.Panels;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    public string SelectedLanguageCode
    {
        get => _selectedLanguageCode;
        set
        {
            _localization.SetLanguage(value);
            var normalized = _localization.CurrentLanguageCode;
            if (!SetProperty(ref _selectedLanguageCode, normalized))
            {
                return;
            }

            NotifyLocalizationDependentProperties();

            if (!_isInitialized)
            {
                return;
            }

            StatusMessage = L("status.ready");
            if (!IsBusy)
            {
                ProgressMessage = L("progress.idle");
            }

            if (PreviewItems.Count == 0)
            {
                ClearTrayPreview();
            }

            QueueValidationRefresh();
        }
    }

    public bool IsChineseTranslationTodo =>
        SelectedLanguage?.DisplayName.Contains("(TODO)", StringComparison.OrdinalIgnoreCase) == true;

    public AppWorkspace Workspace
    {
        get => _workspace;
        set
        {
            if (!SetProperty(ref _workspace, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsToolkitWorkspace));
            OnPropertyChanged(nameof(IsModPreviewWorkspace));
            OnPropertyChanged(nameof(IsTrayPreviewWorkspace));
            OnPropertyChanged(nameof(IsSharedFileOpsVisible));
            StatusMessage = L("status.ready");
            NotifyCommandStates();
            QueueValidationRefresh();

            if (value != AppWorkspace.TrayPreview)
            {
                CloseTrayPreviewDetails();
                CancelTrayPreviewThumbnailLoading();
            }
        }
    }

    public SimsAction SelectedAction
    {
        get => _selectedAction;
        set
        {
            if (!SetProperty(ref _selectedAction, value))
            {
                return;
            }

            OnPropertyChanged(nameof(IsOrganizeVisible));
            OnPropertyChanged(nameof(IsTextureCompressVisible));
            OnPropertyChanged(nameof(IsFlattenVisible));
            OnPropertyChanged(nameof(IsNormalizeVisible));
            OnPropertyChanged(nameof(IsMergeVisible));
            OnPropertyChanged(nameof(IsFindDupVisible));
            OnPropertyChanged(nameof(IsTrayDependenciesVisible));
            OnPropertyChanged(nameof(IsSharedFileOpsVisible));
            StatusMessage = L("status.ready");
            QueueValidationRefresh();
        }
    }

    public string ScriptPath
    {
        get => _scriptPath;
        set
        {
            if (!SetProperty(ref _scriptPath, value))
            {
                return;
            }

            QueueValidationRefresh();
        }
    }

    public bool WhatIf
    {
        get => _whatIf;
        set
        {
            if (!SetProperty(ref _whatIf, value))
            {
                return;
            }

            QueueValidationRefresh();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsProgressIndeterminate
    {
        get => _statusController.IsProgressIndeterminate;
        private set => _statusController.IsProgressIndeterminate = value;
    }

    public int ProgressValue
    {
        get => _statusController.ProgressValue;
        private set => _statusController.ProgressValue = value;
    }

    public string ProgressMessage
    {
        get => _statusController.ProgressMessage;
        private set => _statusController.ProgressMessage = value;
    }

    public string StatusMessage
    {
        get => _statusController.StatusMessage;
        private set => _statusController.StatusMessage = value;
    }

    public string LogText
    {
        get => _statusController.LogText;
    }

    public string PreviewSummaryText
    {
        get => _trayPreviewStateController.SummaryText;
    }

    public string PreviewTotalItems
    {
        get => _trayPreviewStateController.TotalItems;
    }

    public string PreviewTotalFiles
    {
        get => _trayPreviewStateController.TotalFiles;
    }

    public string PreviewTotalSize
    {
        get => _trayPreviewStateController.TotalSize;
    }

    public string PreviewLatestWrite
    {
        get => _trayPreviewStateController.LatestWrite;
    }

    public string PreviewPageText
    {
        get => _trayPreviewStateController.PageText;
    }

    public string PreviewLazyLoadText
    {
        get => _trayPreviewStateController.LazyLoadText;
    }

    public string PreviewJumpPageText
    {
        get => _trayPreviewStateController.JumpPageText;
        set
        {
            if (string.Equals(_trayPreviewStateController.JumpPageText, value, StringComparison.Ordinal))
            {
                return;
            }

            _trayPreviewStateController.JumpPageText = value;
        }
    }

    public bool IsToolkitLogDrawerOpen
    {
        get => _isToolkitLogDrawerOpen;
        set
        {
            if (!SetProperty(ref _isToolkitLogDrawerOpen, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsTrayPreviewLogDrawerOpen
    {
        get => _isTrayPreviewLogDrawerOpen;
        set
        {
            if (!SetProperty(ref _isTrayPreviewLogDrawerOpen, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsToolkitAdvancedOpen
    {
        get => _isToolkitAdvancedOpen;
        set => SetProperty(ref _isToolkitAdvancedOpen, value);
    }

    public string ValidationSummaryText
    {
        get => _validationSummaryText;
        private set => SetProperty(ref _validationSummaryText, value);
    }

    public bool HasValidationErrors
    {
        get => _hasValidationErrors;
        private set
        {
            if (!SetProperty(ref _hasValidationErrors, value))
            {
                return;
            }

            NotifyCommandStates();
        }
    }

    public bool IsOrganizeVisible => SelectedAction == SimsAction.Organize;
    public bool IsTextureCompressVisible => SelectedAction == SimsAction.TextureCompress;
    public bool IsFlattenVisible => SelectedAction == SimsAction.Flatten;
    public bool IsNormalizeVisible => SelectedAction == SimsAction.Normalize;
    public bool IsMergeVisible => SelectedAction == SimsAction.Merge;
    public bool IsFindDupVisible => SelectedAction == SimsAction.FindDuplicates;
    public bool IsTrayDependenciesVisible => SelectedAction == SimsAction.TrayDependencies;
    public bool IsToolkitWorkspace => Workspace == AppWorkspace.Toolkit;
    public bool IsModPreviewWorkspace => Workspace == AppWorkspace.ModPreview;
    public bool IsTrayPreviewWorkspace => Workspace == AppWorkspace.TrayPreview;
    public bool IsSharedFileOpsVisible =>
        IsToolkitWorkspace &&
        _toolkitActionPlanner.UsesSharedFileOps(SelectedAction);

    public bool HasValidModPreviewPath =>
        !string.IsNullOrWhiteSpace(ModPreview.ModsRoot) &&
        Directory.Exists(ModPreview.ModsRoot);

    public string ModPreviewPathHintText => HasValidModPreviewPath
        ? "Mods Path comes from Settings."
        : "Set a valid Mods Path in Settings before building preview.";

    public bool HasValidTrayPreviewPath =>
        !string.IsNullOrWhiteSpace(TrayPreview.TrayRoot) &&
        Directory.Exists(TrayPreview.TrayRoot);

    public string TrayPreviewPathHintText => HasValidTrayPreviewPath
        ? "Tray Path comes from Settings."
        : "Set a valid Tray Path in Settings before loading preview.";

    public bool CanGoPrevPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewStateController.CurrentPage > 1;
    public bool CanGoNextPage => !IsBusy && !_isTrayPreviewPageLoading && _trayPreviewStateController.CurrentPage < _trayPreviewStateController.TotalPages;
    public bool CanJumpToPage => !IsBusy && !_isTrayPreviewPageLoading && TryParsePreviewJumpPage(PreviewJumpPageText, out var page) && page >= 1 && page <= _trayPreviewStateController.TotalPages;
    public bool IsBuildSizeFilterVisible =>
        string.Equals(TrayPreview.PresetTypeFilter, "Lot", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(TrayPreview.PresetTypeFilter, "Room", StringComparison.OrdinalIgnoreCase);
    public bool IsHouseholdSizeFilterVisible =>
        string.Equals(TrayPreview.PresetTypeFilter, "Household", StringComparison.OrdinalIgnoreCase);
    public bool HasTrayPreviewItems => PreviewItems.Count > 0;
    public bool HasSelectedTrayPreviewItems => _trayPreviewSelectionController.HasSelectedItems;
    public int SelectedTrayPreviewCount => _trayPreviewSelectionController.SelectedCount;
    public string TrayPreviewSelectionSummaryText => $"{SelectedTrayPreviewCount} selected / {PreviewItems.Count} on page / {PreviewTotalItems} total";
    public bool HasTrayExportTasks => TrayExportTasks.Count > 0;
    public bool HasCompletedTrayExportTasks => TrayExportTasks.Any(item => item.IsCompleted);
    public bool HasRunningTrayExportTasks => TrayExportTasks.Any(item => item.IsRunning);
    public bool IsTrayExportQueueDockVisible => IsTrayPreviewWorkspace && HasTrayExportTasks;
    public bool IsTrayExportQueueVisible => IsTrayExportQueueDockVisible && _isTrayExportQueueExpanded;
    public string TrayExportQueueSummaryText =>
        HasTrayExportTasks
            ? $"{TrayExportTasks.Count(item => item.IsRunning)} running / {TrayExportTasks.Count(item => item.IsCompleted)} finished"
            : "No export tasks";
    public string TrayExportQueueToggleText => IsTrayExportQueueVisible
        ? "Hide Tasks"
        : $"Show Tasks ({TrayExportTasks.Count})";
    public bool IsTrayPreviewLoadingStateVisible => IsBusy && !HasTrayPreviewItems;
    public bool IsTrayPreviewEmptyStateVisible => !IsBusy && !HasTrayPreviewItems;
    public bool IsTrayPreviewPagerVisible => HasTrayPreviewItems;
    public bool IsTrayPreviewEntryMode => !string.Equals(TrayPreview.LayoutMode, "Grid", StringComparison.OrdinalIgnoreCase);
    public bool IsTrayPreviewGridMode => string.Equals(TrayPreview.LayoutMode, "Grid", StringComparison.OrdinalIgnoreCase);
    public TrayPreviewListItemViewModel? TrayPreviewDetailItem
    {
        get => _trayPreviewSelectionController.DetailItem;
    }
    public bool IsTrayPreviewDetailVisible => TrayPreviewDetailItem is not null;
    public bool IsTrayPreviewDetailDescriptionEmpty => TrayPreviewDetailItem is null || !TrayPreviewDetailItem.Item.HasDisplayDescription;
    public bool IsTrayPreviewDetailOverviewEmpty =>
        TrayPreviewDetailItem is null ||
        (!TrayPreviewDetailItem.Item.HasDisplayPrimaryMeta &&
         !TrayPreviewDetailItem.Item.HasDisplaySecondaryMeta &&
         !TrayPreviewDetailItem.Item.HasDisplayTertiaryMeta);
    public bool CanGoBackTrayPreviewDetail => _trayPreviewSelectionController.CanGoBackDetail;
    public bool IsTrayPreviewEmptyStatusOk => HasValidTrayPreviewPath && !_trayPreviewStateController.HasLoadedOnce;
    public bool IsTrayPreviewEmptyStatusWarning => HasValidTrayPreviewPath && _trayPreviewStateController.HasLoadedOnce;
    public bool IsTrayPreviewEmptyStatusMissing => !HasValidTrayPreviewPath;
    public bool IsTrayPreviewPathMissing => !HasValidTrayPreviewPath;

    public string TrayPreviewEmptyTitleText
    {
        get
        {
            if (!HasValidTrayPreviewPath)
            {
                return L("preview.empty.pathMissing.title");
            }

            if (_trayPreviewStateController.HasLoadedOnce)
            {
                return L("preview.empty.noResults.title");
            }

            return L("preview.empty.initial.title");
        }
    }

    public string TrayPreviewEmptyDescriptionText
    {
        get
        {
            if (!HasValidTrayPreviewPath)
            {
                return L("preview.empty.pathMissing.description");
            }

            if (_trayPreviewStateController.HasLoadedOnce)
            {
                return L("preview.empty.noResults.description");
            }

            return L("preview.empty.initial.description");
        }
    }

    public string TrayPreviewEmptyStatusText
    {
        get
        {
            if (!HasValidTrayPreviewPath)
            {
                return L("preview.empty.status.pathMissing");
            }

            if (_trayPreviewStateController.HasLoadedOnce)
            {
                return L("preview.empty.status.noResults");
            }

            return L("preview.empty.status.ready");
        }
    }

    public string TrayPreviewLoadingText => L("status.trayPreviewLoading");
}
