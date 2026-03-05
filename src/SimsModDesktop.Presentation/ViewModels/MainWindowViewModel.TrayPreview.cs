using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Presentation.Diagnostics;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task RunTrayPreviewAsync(TrayPreviewInput? explicitInput = null)
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} workspace={Workspace} hasExplicitInput={HasExplicitInput}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "RunTrayPreview",
            Workspace,
            explicitInput is not null);
        return _trayPreviewController.RunTrayPreviewAsync(CreateTrayPreviewHost(), explicitInput);
    }

    private Task RunTrayPreviewCoreAsync(TrayPreviewInput? explicitInput = null, string? existingOperationId = null) =>
        _trayPreviewController.RunTrayPreviewCoreAsync(CreateTrayPreviewHost(), explicitInput, existingOperationId);

    private Task LoadPreviousTrayPreviewPageAsync()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "LoadPreviousTrayPreviewPage");
        return _trayPreviewController.LoadPreviousTrayPreviewPageAsync(CreateTrayPreviewHost());
    }

    private Task LoadNextTrayPreviewPageAsync()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "LoadNextTrayPreviewPage");
        return _trayPreviewController.LoadNextTrayPreviewPageAsync(CreateTrayPreviewHost());
    }

    private Task JumpToTrayPreviewPageAsync()
    {
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} command={Command} jumpText={JumpText}",
            LogEvents.UiCommandInvoke,
            "invoke",
            "main-window",
            "JumpTrayPreviewPage",
            PreviewJumpPageText);
        return _trayPreviewController.JumpToTrayPreviewPageAsync(CreateTrayPreviewHost());
    }

    private static bool TryParsePreviewJumpPage(string? rawValue, out int page)
    {
        page = 0;
        return int.TryParse(rawValue?.Trim(), out page);
    }

    private Task TryAutoLoadTrayPreviewAsync() =>
        _trayPreviewController.TryAutoLoadTrayPreviewAsync(CreateTrayPreviewHost());

    private void ClearTrayPreview() =>
        _trayPreviewController.ClearTrayPreview(CreateTrayPreviewHost());

    private void SetTrayPreviewSummary(SimsTrayPreviewSummary summary) =>
        _trayPreviewController.SetTrayPreviewSummary(CreateTrayPreviewHost(), summary);

    private void SetTrayPreviewPage(SimsTrayPreviewPage page, int loadedPageCount) =>
        _trayPreviewController.SetTrayPreviewPage(CreateTrayPreviewHost(), page, loadedPageCount);

    private void ApplyTrayPreviewDebugVisibility() =>
        _trayPreviewController.ApplyTrayPreviewDebugVisibility(CreateTrayPreviewHost());

    private void OnTrayPreviewItemExpanded(TrayPreviewListItemViewModel expandedItem) =>
        _trayPreviewController.OnTrayPreviewItemExpanded(CreateTrayPreviewHost(), expandedItem);

    public void ApplyTrayPreviewSelection(
        TrayPreviewListItemViewModel selectedItem,
        bool controlPressed,
        bool shiftPressed)
    {
        _trayPreviewController.ApplyTrayPreviewSelection(CreateTrayPreviewHost(), selectedItem, controlPressed, shiftPressed);
    }

    private void OpenTrayPreviewDetails(TrayPreviewListItemViewModel selectedItem) =>
        _trayPreviewController.OpenTrayPreviewDetails(CreateTrayPreviewHost(), selectedItem);

    private void GoBackTrayPreviewDetails() =>
        _trayPreviewController.GoBackTrayPreviewDetails(CreateTrayPreviewHost());

    private void CloseTrayPreviewDetails() =>
        _trayPreviewController.CloseTrayPreviewDetails();

    private void ClearTrayPreviewSelection() =>
        _trayPreviewController.ClearTrayPreviewSelection(CreateTrayPreviewHost());

    private void CancelTrayPreviewThumbnailLoading() =>
        _trayPreviewController.CancelTrayPreviewThumbnailLoading();

    private bool IsTrayPreviewItemSelected(SimsTrayPreviewItem item)
    {
        return _trayPreviewSelectionController.IsItemSelected(item);
    }

    private void SetTrayPreviewPageLoading(bool loading)
    {
        ExecuteOnUi(() =>
        {
            _isTrayPreviewPageLoading = loading;
            NotifyCommandStates();
        });
    }

    private MainWindowTrayPreviewHost CreateTrayPreviewHost()
    {
        return new MainWindowTrayPreviewHost
        {
            TrayPreview = TrayPreview,
            PreviewItems = PreviewItems,
            IsTrayPreviewWorkspace = IsTrayPreviewWorkspace,
            GetIsBusy = () => IsBusy,
            CreatePlanBuilderState = CreatePlanBuilderState,
            GetExecutionCts = () => _executionCts,
            SetExecutionCts = cts => _executionCts = cts,
            GetIsTrayPreviewPageLoading = () => _isTrayPreviewPageLoading,
            SetTrayPreviewPageLoading = SetTrayPreviewPageLoading,
            SetBusy = value => IsBusy = value,
            SetStatus = value => StatusMessage = value,
            AppendLog = AppendLog,
            ClearLog = ClearLog,
            SetProgress = SetProgress,
            RefreshValidation = RefreshValidationNow,
            NotifyCommandStates = NotifyCommandStates,
            NotifyTrayPreviewViewStateChanged = NotifyTrayPreviewViewStateChanged,
            ShowErrorPopupAsync = ShowErrorPopupAsync,
            Localize = L,
            LocalizeFormat = (key, args) => LF(key, args),
            ExecuteOnUi = ExecuteOnUi,
            ExecuteOnUiAsync = ExecuteOnUiAsync
        };
    }
}
