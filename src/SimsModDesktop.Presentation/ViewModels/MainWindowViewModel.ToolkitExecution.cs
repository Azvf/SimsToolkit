using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private Task RunToolkitAsync() =>
        _executionController.RunToolkitAsync(CreateExecutionHost());

    private Task RunToolkitPlanAsync(CliExecutionPlan cliPlan, string? existingOperationId) =>
        _executionController.RunToolkitPlanAsync(CreateExecutionHost(), cliPlan, existingOperationId);

    private Task RunTextureCompressionAsync() =>
        _executionController.RunTextureCompressionAsync(CreateExecutionHost());

    private Task RunTextureCompressionAsync(TextureCompressionExecutionPlan plan) =>
        _executionController.RunTextureCompressionAsync(CreateExecutionHost(), plan);

    private Task RunTrayDependenciesAsync() =>
        _executionController.RunTrayDependenciesAsync(CreateExecutionHost());

    private Task RunTrayDependenciesPlanAsync(TrayDependenciesExecutionPlan plan, string? existingOperationId) =>
        _executionController.RunTrayDependenciesPlanAsync(CreateExecutionHost(), plan, existingOperationId);

    public async Task RunTrayDependenciesForTrayItemAsync(string trayPath, string trayItemKey)
    {
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            throw new ArgumentException("Tray path is required.", nameof(trayPath));
        }

        if (string.IsNullOrWhiteSpace(trayItemKey))
        {
            throw new ArgumentException("Tray item key is required.", nameof(trayItemKey));
        }

        TrayDependencies.TrayPath = Path.GetFullPath(trayPath.Trim());
        TrayDependencies.TrayItemKey = trayItemKey.Trim();
        Workspace = AppWorkspace.Toolkit;
        SelectedAction = SimsAction.TrayDependencies;
        await _executionController.RunTrayDependenciesAsync(CreateExecutionHost());
    }

    private Task<string?> RegisterRecoveryAsync(RecoverableOperationPayload payload) =>
        _recoveryController.RegisterRecoveryAsync(payload);

    private Task MarkRecoveryStartedAsync(string? operationId) =>
        _recoveryController.MarkRecoveryStartedAsync(operationId);

    private Task MarkRecoveryCompletedAsync(string? operationId, RecoverableOperationCompletion completion) =>
        _recoveryController.MarkRecoveryCompletedAsync(operationId, completion);

    private Task SaveResultHistoryAsync(SimsAction action, string source, string summary, string? relatedOperationId) =>
        _recoveryController.SaveResultHistoryAsync(action, source, summary, relatedOperationId);

    private void CancelExecution() =>
        _executionController.CancelExecution(CreateExecutionHost());

    private MainWindowExecutionHost CreateExecutionHost()
    {
        return new MainWindowExecutionHost
        {
            SelectedAction = SelectedAction,
            TextureCompress = TextureCompress,
            TrayDependencies = TrayDependencies,
            CreatePlanBuilderState = CreatePlanBuilderState,
            ConfirmDangerousFindDupCleanupAsync = ConfirmDangerousFindDupCleanupAsync,
            GetExecutionCts = () => _executionCts,
            SetExecutionCts = cts => _executionCts = cts,
            SetBusy = value => IsBusy = value,
            SetStatus = value => StatusMessage = value,
            AppendLog = AppendLog,
            ClearLog = ClearLog,
            SetProgress = SetProgress,
            RefreshValidation = RefreshValidationNow,
            ShowErrorPopupAsync = ShowErrorPopupAsync,
            Localize = L,
            LocalizeFormat = (key, args) => LF(key, args)
        };
    }

    private ToolkitPlanningState CreatePlanBuilderState()
    {
        return new ToolkitPlanningState
        {
            WhatIf = WhatIf,
            SelectedAction = SelectedAction,
            SharedFileOps = new SharedFileOpsPlanState
            {
                SkipPruneEmptyDirs = SharedFileOps.SkipPruneEmptyDirs,
                ModFilesOnly = SharedFileOps.ModFilesOnly,
                VerifyContentOnNameConflict = SharedFileOps.VerifyContentOnNameConflict,
                ModExtensionsText = SharedFileOps.ModExtensionsText,
                PrefixHashBytesText = SharedFileOps.PrefixHashBytesText,
                HashWorkerCountText = SharedFileOps.HashWorkerCountText
            }
        };
    }

    private void SetProgress(bool isIndeterminate, int percent, string message)
    {
        ExecuteOnUi(() => _statusController.SetProgress(isIndeterminate, percent, message));
    }

    private void ClearLog()
    {
        ExecuteOnUi(_statusController.ClearLog);
    }

    private void AppendLog(string message)
    {
        ExecuteOnUi(() => _statusController.AppendLog(message));
    }

    public void AppendSystemLog(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        AppendLog(message);
    }
}
