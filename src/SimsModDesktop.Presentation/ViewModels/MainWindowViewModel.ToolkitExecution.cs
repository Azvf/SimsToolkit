using System.Diagnostics;
using System.Text.Json;
using SimsModDesktop.Application.Execution;
using SimsModDesktop.Application.Modules;
using SimsModDesktop.Application.Recovery;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Results;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.Application.TextureProcessing;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed partial class MainWindowViewModel
{
    private async Task RunToolkitAsync()
    {
        if (SelectedAction == SimsAction.TrayDependencies)
        {
            await RunTrayDependenciesAsync();
            return;
        }

        if (SelectedAction == SimsAction.TextureCompress)
        {
            await RunTextureCompressionAsync();
            return;
        }

        if (!_toolkitActionPlanner.TryBuildToolkitCliPlan(CreatePlanBuilderState(), out var cliPlan, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            await ShowErrorPopupAsync(L("status.validationFailed"));
            return;
        }

        if (!await ConfirmDangerousFindDupCleanupAsync())
        {
            return;
        }

        await RunToolkitPlanAsync(cliPlan, existingOperationId: null);
    }

    private async Task RunToolkitPlanAsync(CliExecutionPlan cliPlan, string? existingOperationId)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        var input = cliPlan.Input;
        var operationId = existingOperationId ?? await RegisterRecoveryAsync(_recoveryController.BuildToolkitRecoveryPayload(cliPlan));
        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var recoveryCompleted = false;

        ClearLog();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: L("progress.preparing"));
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] " + input.Action.ToString().ToLowerInvariant());
        if (input.WhatIf)
        {
            AppendLog("[whatif] enabled");
        }

        StatusMessage = L("status.running");

        try
        {
            await MarkRecoveryStartedAsync(operationId);

            var result = await _executionCoordinator.ExecuteAsync(
                input,
                AppendLog,
                HandleProgress,
                _executionCts.Token);

            stopwatch.Stop();
            AppendLog($"[exit] code={result.ExitCode}");
            StatusMessage = result.ExitCode == 0
                ? LF("status.executionCompleted", stopwatch.Elapsed.ToString("mm\\:ss"))
                : LF("status.executionFailedExit", result.ExitCode, stopwatch.Elapsed.ToString("mm\\:ss"));
            SetProgress(
                isIndeterminate: false,
                percent: result.ExitCode == 0 ? 100 : 0,
                message: result.ExitCode == 0 ? L("progress.completed") : L("progress.failed"));

            if (result.ExitCode != 0)
            {
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = $"Process exited with code {result.ExitCode}.",
                        ResultSummaryJson = JsonSerializer.Serialize(new
                        {
                            result.ExitCode,
                            Elapsed = stopwatch.Elapsed.ToString("mm\\:ss")
                        })
                    });
                await SaveResultHistoryAsync(input.Action, "Toolkit", $"Exit code {result.ExitCode}", operationId);
                recoveryCompleted = true;
                await ShowErrorPopupAsync(L("status.executionFailed"));
            }
            else
            {
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Succeeded,
                        ResultSummaryJson = JsonSerializer.Serialize(new
                        {
                            result.ExitCode,
                            Elapsed = stopwatch.Elapsed.ToString("mm\\:ss")
                        })
                    });
                await SaveResultHistoryAsync(input.Action, "Toolkit", "Completed", operationId);
                recoveryCompleted = true;
            }
        }
        catch (Exception ex)
        {
            if (!recoveryCompleted)
            {
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = ex.Message
                    });
            }

            throw;
        }
        finally
        {
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            RefreshValidationNow();
        }
    }

    private async Task RunTextureCompressionAsync()
    {
        if (!_toolkitActionPlanner.TryBuildTextureCompressionPlan(CreatePlanBuilderState(), out var plan, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            await ShowErrorPopupAsync(L("status.validationFailed"));
            return;
        }

        await RunTextureCompressionAsync(plan);
    }

    private async Task RunTextureCompressionAsync(TextureCompressionExecutionPlan plan)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        _executionCts = new CancellationTokenSource();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: "Compressing texture...");
        StatusMessage = L("status.running");
        AppendLog("[action] texturecompress");

        try
        {
            var request = plan.Request;
            if (request.WhatIf)
            {
                TextureCompress.LastOutputPath = request.OutputPath;
                TextureCompress.LastRunSummary = $"WhatIf: would compress '{Path.GetFileName(request.SourcePath)}' to '{Path.GetFileName(request.OutputPath)}'.";
                AppendLog("[whatif] source=" + request.SourcePath);
                AppendLog("[whatif] output=" + request.OutputPath);
                SetProgress(isIndeterminate: false, percent: 100, message: "Texture compression preview completed.");
                StatusMessage = "Texture compression preview completed.";
                return;
            }

            var sourceBytes = await File.ReadAllBytesAsync(request.SourcePath, _executionCts.Token);
            if (!TryResolveTextureContainerKind(request.SourcePath, out var containerKind, out var containerError))
            {
                throw new InvalidOperationException(containerError);
            }

            if (!_textureDimensionProbe.TryGetDimensions(containerKind, sourceBytes, out var sourceWidth, out var sourceHeight, out var probeError))
            {
                throw new InvalidOperationException(probeError);
            }

            var result = _textureCompressionService.Compress(new TextureCompressionRequest
            {
                Source = new TextureSourceDescriptor
                {
                    ResourceKey = default,
                    ContainerKind = containerKind,
                    SourcePixelFormat = TexturePixelFormatKind.Rgba32,
                    Width = sourceWidth,
                    Height = sourceHeight,
                    HasAlpha = request.HasAlphaHint,
                    MipMapCount = 1
                },
                SourceBytes = sourceBytes,
                TargetWidth = request.TargetWidth,
                TargetHeight = request.TargetHeight,
                GenerateMipMaps = request.GenerateMipMaps,
                PreferredFormat = request.PreferredFormat
            });

            if (!result.Success || result.TranscodeResult is null || !result.TranscodeResult.Success)
            {
                throw new InvalidOperationException(result.Error ?? result.TranscodeResult?.Error ?? "Texture compression failed.");
            }

            var outputDirectory = Path.GetDirectoryName(request.OutputPath);
            if (!string.IsNullOrWhiteSpace(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            await File.WriteAllBytesAsync(request.OutputPath, result.TranscodeResult.EncodedBytes, _executionCts.Token);

            TextureCompress.LastOutputPath = request.OutputPath;
            TextureCompress.LastRunSummary =
                $"Compressed {Path.GetFileName(request.SourcePath)} to {result.SelectedFormat} {result.TranscodeResult.OutputWidth}x{result.TranscodeResult.OutputHeight}.";

            AppendLog("[output] " + request.OutputPath);
            SetProgress(isIndeterminate: false, percent: 100, message: "Texture compression completed.");
            StatusMessage = "Texture compression completed.";
        }
        catch (OperationCanceledException)
        {
            AppendLog("[cancelled]");
            StatusMessage = L("status.executionCancelled");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
        }
        catch (Exception ex)
        {
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Texture compression failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Texture compression failed.");
            await ShowErrorPopupAsync("Texture compression failed.");
        }
        finally
        {
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            RefreshValidationNow();
        }
    }

    private async Task RunTrayDependenciesAsync()
    {
        if (!_toolkitActionPlanner.TryBuildTrayDependenciesPlan(CreatePlanBuilderState(), out var plan, out var error))
        {
            StatusMessage = error;
            AppendLog("[validation] " + error);
            await ShowErrorPopupAsync(L("status.validationFailed"));
            return;
        }

        await RunTrayDependenciesPlanAsync(plan, existingOperationId: null);
    }

    private async Task RunTrayDependenciesPlanAsync(TrayDependenciesExecutionPlan plan, string? existingOperationId)
    {
        if (_executionCts is not null)
        {
            StatusMessage = L("status.executionAlreadyRunning");
            return;
        }

        var operationId = existingOperationId ?? await RegisterRecoveryAsync(_recoveryController.BuildTrayDependenciesRecoveryPayload(plan));

        _executionCts = new CancellationTokenSource();
        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var recoveryCompleted = false;

        ClearLog();
        IsBusy = true;
        SetProgress(isIndeterminate: true, percent: 0, message: "Preparing tray dependency analysis...");
        AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        AppendLog("[action] traydependencies");
        StatusMessage = L("status.running");

        try
        {
            await MarkRecoveryStartedAsync(operationId);
            var result = await _trayDependencyAnalysisService.AnalyzeAsync(
                plan.Request,
                new Progress<TrayDependencyAnalysisProgress>(HandleTrayDependencyAnalysisProgress),
                _executionCts.Token);
            stopwatch.Stop();

            AppendLog($"[traydependencies] matched={result.MatchedPackageCount}");
            AppendLog($"[traydependencies] unused={result.UnusedPackageCount}");

            if (!string.IsNullOrWhiteSpace(result.OutputCsvPath))
            {
                AppendLog("CSV: " + result.OutputCsvPath);
            }

            if (!string.IsNullOrWhiteSpace(result.UnusedOutputCsvPath))
            {
                AppendLog("UNUSED CSV: " + result.UnusedOutputCsvPath);
            }

            if (!string.IsNullOrWhiteSpace(result.MatchedExportPath))
            {
                AppendLog("[export] matched=" + result.MatchedExportPath);
            }

            if (!string.IsNullOrWhiteSpace(result.UnusedExportPath))
            {
                AppendLog("[export] unused=" + result.UnusedExportPath);
            }

            foreach (var row in result.MatchedPackages.Take(10))
            {
                AppendLog(
                    $"[match] {Path.GetFileName(row.PackagePath)} confidence={row.Confidence} count={row.MatchInstanceCount} rate={row.MatchRatePct:0.##}%");
            }

            foreach (var issue in result.Issues)
            {
                var prefix = issue.Severity == TrayDependencyIssueSeverity.Error ? "[error]" : "[warn]";
                AppendLog(prefix + " " + issue.Message);
            }

            if (result.Success)
            {
                var hasWarnings = result.Issues.Any(issue => issue.Severity == TrayDependencyIssueSeverity.Warning);
                StatusMessage = hasWarnings
                    ? $"Tray dependencies completed with warnings ({stopwatch.Elapsed:mm\\:ss})"
                    : $"Tray dependencies completed ({stopwatch.Elapsed:mm\\:ss})";
                SetProgress(
                    isIndeterminate: false,
                    percent: 100,
                    message: hasWarnings ? "Tray dependency analysis completed with warnings." : "Tray dependency analysis completed.");
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Succeeded,
                        ResultSummaryJson = JsonSerializer.Serialize(new
                        {
                            result.MatchedPackageCount,
                            result.UnusedPackageCount,
                            HasWarnings = hasWarnings
                        })
                    });
                await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Completed", operationId);
                recoveryCompleted = true;
                return;
            }

            StatusMessage = "Tray dependency analysis failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Tray dependency analysis failed.");
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = "Tray dependency analysis failed."
                });
            await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Failed", operationId);
            recoveryCompleted = true;
            await ShowErrorPopupAsync("Tray dependency analysis failed.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            AppendLog("[cancelled]");
            StatusMessage = L("status.executionCancelled");
            SetProgress(isIndeterminate: false, percent: 0, message: L("progress.cancelled"));
            await MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Cancelled
                });
            await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Cancelled", operationId);
            recoveryCompleted = true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            AppendLog("[error] " + ex.Message);
            StatusMessage = "Tray dependency analysis failed.";
            SetProgress(isIndeterminate: false, percent: 0, message: "Tray dependency analysis failed.");
            if (!recoveryCompleted)
            {
                await MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = ex.Message
                    });
                await SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", ex.Message, operationId);
            }

            await ShowErrorPopupAsync("Tray dependency analysis failed.");
        }
        finally
        {
            _executionCts.Dispose();
            _executionCts = null;
            IsBusy = false;
            RefreshValidationNow();
        }
    }

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
        await RunTrayDependenciesAsync();
    }

    private Task<string?> RegisterRecoveryAsync(RecoverableOperationPayload payload) =>
        _recoveryController.RegisterRecoveryAsync(payload);

    private Task MarkRecoveryStartedAsync(string? operationId) =>
        _recoveryController.MarkRecoveryStartedAsync(operationId);

    private Task MarkRecoveryCompletedAsync(string? operationId, RecoverableOperationCompletion completion) =>
        _recoveryController.MarkRecoveryCompletedAsync(operationId, completion);

    private Task SaveResultHistoryAsync(SimsAction action, string source, string summary, string? relatedOperationId) =>
        _recoveryController.SaveResultHistoryAsync(action, source, summary, relatedOperationId);

    private void HandleTrayDependencyAnalysisProgress(TrayDependencyAnalysisProgress progress)
    {
        var message = string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Stage.ToString()
            : progress.Detail;
        SetProgress(
            isIndeterminate: progress.Percent <= 0,
            percent: Math.Clamp(progress.Percent, 0, 100),
            message: message);
    }

    private void CancelExecution()
    {
        var cts = _executionCts;
        if (cts is null)
        {
            StatusMessage = L("status.noRunningExecution");
            return;
        }

        AppendLog("[cancel] requested");
        StatusMessage = L("status.cancelling");
        SetProgress(isIndeterminate: true, percent: 0, message: L("status.cancelling"));

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore cancellation race when operation has already completed.
        }
    }

    private ToolkitPlanningState CreatePlanBuilderState()
    {
        return new ToolkitPlanningState
        {
            ScriptPath = ScriptPath,
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

    private void HandleProgress(SimsProgressUpdate progress)
    {
        var normalizedPercent = Math.Clamp(progress.Percent, 0, 100);
        var hasTotal = progress.Total > 0;
        var text = hasTotal
            ? $"{progress.Stage}: {progress.Current}/{progress.Total} ({normalizedPercent}%)"
            : progress.Stage;

        if (!string.IsNullOrWhiteSpace(progress.Detail))
        {
            text = $"{text} - {progress.Detail}";
        }

        SetProgress(
            isIndeterminate: !hasTotal || progress.Percent < 0,
            percent: normalizedPercent,
            message: text);
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

    private static bool TryResolveTextureContainerKind(string sourcePath, out TextureContainerKind containerKind, out string error)
    {
        error = string.Empty;
        switch (Path.GetExtension(sourcePath).Trim().ToLowerInvariant())
        {
            case ".png":
                containerKind = TextureContainerKind.Png;
                return true;
            case ".dds":
                containerKind = TextureContainerKind.Dds;
                return true;
            case ".tga":
                containerKind = TextureContainerKind.Tga;
                return true;
            default:
                containerKind = default;
                error = "Source file must be a .png, .dds, or .tga file.";
                return false;
        }
    }
}
