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

public sealed class MainWindowExecutionController
{
    private readonly IExecutionCoordinator _executionCoordinator;
    private readonly ITrayDependencyAnalysisService _trayDependencyAnalysisService;
    private readonly IToolkitActionPlanner _toolkitActionPlanner;
    private readonly MainWindowRecoveryController _recoveryController;
    private readonly ITextureCompressionService _textureCompressionService;
    private readonly ITextureDimensionProbe _textureDimensionProbe;

    public MainWindowExecutionController(
        IExecutionCoordinator executionCoordinator,
        ITrayDependencyAnalysisService trayDependencyAnalysisService,
        IToolkitActionPlanner toolkitActionPlanner,
        MainWindowRecoveryController recoveryController,
        ITextureCompressionService textureCompressionService,
        ITextureDimensionProbe textureDimensionProbe)
    {
        _executionCoordinator = executionCoordinator;
        _trayDependencyAnalysisService = trayDependencyAnalysisService;
        _toolkitActionPlanner = toolkitActionPlanner;
        _recoveryController = recoveryController;
        _textureCompressionService = textureCompressionService;
        _textureDimensionProbe = textureDimensionProbe;
    }

    internal async Task RunToolkitAsync(MainWindowExecutionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (host.SelectedAction == SimsAction.TrayDependencies)
        {
            await RunTrayDependenciesAsync(host);
            return;
        }

        if (host.SelectedAction == SimsAction.TextureCompress)
        {
            await RunTextureCompressionAsync(host);
            return;
        }

        if (!_toolkitActionPlanner.TryBuildToolkitCliPlan(host.CreatePlanBuilderState(), out var cliPlan, out var error))
        {
            host.SetStatus(error);
            host.AppendLog("[validation] " + error);
            await host.ShowErrorPopupAsync(host.Localize("status.validationFailed"));
            return;
        }

        if (!await host.ConfirmDangerousFindDupCleanupAsync())
        {
            return;
        }

        await RunToolkitPlanAsync(host, cliPlan, existingOperationId: null);
    }

    internal async Task RunToolkitPlanAsync(MainWindowExecutionHost host, CliExecutionPlan cliPlan, string? existingOperationId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(cliPlan);

        if (host.GetExecutionCts() is not null)
        {
            host.SetStatus(host.Localize("status.executionAlreadyRunning"));
            return;
        }

        var input = cliPlan.Input;
        var operationId = existingOperationId ?? await _recoveryController.RegisterRecoveryAsync(_recoveryController.BuildToolkitRecoveryPayload(cliPlan));
        using var executionCts = new CancellationTokenSource();
        host.SetExecutionCts(executionCts);

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var recoveryCompleted = false;

        host.ClearLog();
        host.SetBusy(true);
        host.SetProgress(true, 0, host.Localize("progress.preparing"));
        host.AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        host.AppendLog("[action] " + input.Action.ToString().ToLowerInvariant());
        if (input.WhatIf)
        {
            host.AppendLog("[whatif] enabled");
        }

        host.SetStatus(host.Localize("status.running"));

        try
        {
            await _recoveryController.MarkRecoveryStartedAsync(operationId);

            var result = await _executionCoordinator.ExecuteAsync(
                input,
                host.AppendLog,
                progress => HandleProgress(host, progress),
                executionCts.Token);

            stopwatch.Stop();
            host.AppendLog($"[exit] code={result.ExitCode}");
            host.SetStatus(result.ExitCode == 0
                ? host.LocalizeFormat("status.executionCompleted", [stopwatch.Elapsed.ToString("mm\\:ss")])
                : host.LocalizeFormat("status.executionFailedExit", [result.ExitCode, stopwatch.Elapsed.ToString("mm\\:ss")]));
            host.SetProgress(
                false,
                result.ExitCode == 0 ? 100 : 0,
                result.ExitCode == 0 ? host.Localize("progress.completed") : host.Localize("progress.failed"));

            if (result.ExitCode != 0)
            {
                await _recoveryController.MarkRecoveryCompletedAsync(
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
                await _recoveryController.SaveResultHistoryAsync(input.Action, "Toolkit", $"Exit code {result.ExitCode}", operationId);
                recoveryCompleted = true;
                await host.ShowErrorPopupAsync(host.Localize("status.executionFailed"));
            }
            else
            {
                await _recoveryController.MarkRecoveryCompletedAsync(
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
                await _recoveryController.SaveResultHistoryAsync(input.Action, "Toolkit", "Completed", operationId);
                recoveryCompleted = true;
            }
        }
        catch (Exception ex)
        {
            if (!recoveryCompleted)
            {
                await _recoveryController.MarkRecoveryCompletedAsync(
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
            host.SetExecutionCts(null);
            host.SetBusy(false);
            host.RefreshValidation();
        }
    }

    internal async Task RunTextureCompressionAsync(MainWindowExecutionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!_toolkitActionPlanner.TryBuildTextureCompressionPlan(host.CreatePlanBuilderState(), out var plan, out var error))
        {
            host.SetStatus(error);
            host.AppendLog("[validation] " + error);
            await host.ShowErrorPopupAsync(host.Localize("status.validationFailed"));
            return;
        }

        await RunTextureCompressionAsync(host, plan);
    }

    internal async Task RunTextureCompressionAsync(MainWindowExecutionHost host, TextureCompressionExecutionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(plan);

        if (host.GetExecutionCts() is not null)
        {
            host.SetStatus(host.Localize("status.executionAlreadyRunning"));
            return;
        }

        using var executionCts = new CancellationTokenSource();
        host.SetExecutionCts(executionCts);
        host.SetBusy(true);
        host.SetProgress(true, 0, "Compressing texture...");
        host.SetStatus(host.Localize("status.running"));
        host.AppendLog("[action] texturecompress");

        try
        {
            var request = plan.Request;
            if (request.WhatIf)
            {
                host.TextureCompress.LastOutputPath = request.OutputPath;
                host.TextureCompress.LastRunSummary = $"WhatIf: would compress '{Path.GetFileName(request.SourcePath)}' to '{Path.GetFileName(request.OutputPath)}'.";
                host.AppendLog("[whatif] source=" + request.SourcePath);
                host.AppendLog("[whatif] output=" + request.OutputPath);
                host.SetProgress(false, 100, "Texture compression preview completed.");
                host.SetStatus("Texture compression preview completed.");
                return;
            }

            var sourceBytes = await File.ReadAllBytesAsync(request.SourcePath, executionCts.Token);
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

            await File.WriteAllBytesAsync(request.OutputPath, result.TranscodeResult.EncodedBytes, executionCts.Token);

            host.TextureCompress.LastOutputPath = request.OutputPath;
            host.TextureCompress.LastRunSummary =
                $"Compressed {Path.GetFileName(request.SourcePath)} to {result.SelectedFormat} {result.TranscodeResult.OutputWidth}x{result.TranscodeResult.OutputHeight}.";

            host.AppendLog("[output] " + request.OutputPath);
            host.SetProgress(false, 100, "Texture compression completed.");
            host.SetStatus("Texture compression completed.");
        }
        catch (OperationCanceledException)
        {
            host.AppendLog("[cancelled]");
            host.SetStatus(host.Localize("status.executionCancelled"));
            host.SetProgress(false, 0, host.Localize("progress.cancelled"));
        }
        catch (Exception ex)
        {
            host.AppendLog("[error] " + ex.Message);
            host.SetStatus("Texture compression failed.");
            host.SetProgress(false, 0, "Texture compression failed.");
            await host.ShowErrorPopupAsync("Texture compression failed.");
        }
        finally
        {
            host.SetExecutionCts(null);
            host.SetBusy(false);
            host.RefreshValidation();
        }
    }

    internal async Task RunTrayDependenciesAsync(MainWindowExecutionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!_toolkitActionPlanner.TryBuildTrayDependenciesPlan(host.CreatePlanBuilderState(), out var plan, out var error))
        {
            host.SetStatus(error);
            host.AppendLog("[validation] " + error);
            await host.ShowErrorPopupAsync(host.Localize("status.validationFailed"));
            return;
        }

        await RunTrayDependenciesPlanAsync(host, plan, existingOperationId: null);
    }

    internal async Task RunTrayDependenciesPlanAsync(MainWindowExecutionHost host, TrayDependenciesExecutionPlan plan, string? existingOperationId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(plan);

        if (host.GetExecutionCts() is not null)
        {
            host.SetStatus(host.Localize("status.executionAlreadyRunning"));
            return;
        }

        var operationId = existingOperationId ?? await _recoveryController.RegisterRecoveryAsync(_recoveryController.BuildTrayDependenciesRecoveryPayload(plan));
        using var executionCts = new CancellationTokenSource();
        host.SetExecutionCts(executionCts);

        var startedAt = DateTimeOffset.Now;
        var stopwatch = Stopwatch.StartNew();
        var recoveryCompleted = false;

        host.ClearLog();
        host.SetBusy(true);
        host.SetProgress(true, 0, "Preparing tray dependency analysis...");
        host.AppendLog("[start] " + startedAt.ToString("yyyy-MM-dd HH:mm:ss"));
        host.AppendLog("[action] traydependencies");
        host.SetStatus(host.Localize("status.running"));

        try
        {
            await _recoveryController.MarkRecoveryStartedAsync(operationId);
            var result = await _trayDependencyAnalysisService.AnalyzeAsync(
                plan.Request,
                new Progress<TrayDependencyAnalysisProgress>(progress => HandleTrayDependencyAnalysisProgress(host, progress)),
                executionCts.Token);
            stopwatch.Stop();

            host.AppendLog($"[traydependencies] matched={result.MatchedPackageCount}");
            host.AppendLog($"[traydependencies] unused={result.UnusedPackageCount}");

            if (!string.IsNullOrWhiteSpace(result.OutputCsvPath))
            {
                host.AppendLog("CSV: " + result.OutputCsvPath);
            }

            if (!string.IsNullOrWhiteSpace(result.UnusedOutputCsvPath))
            {
                host.AppendLog("UNUSED CSV: " + result.UnusedOutputCsvPath);
            }

            if (!string.IsNullOrWhiteSpace(result.MatchedExportPath))
            {
                host.AppendLog("[export] matched=" + result.MatchedExportPath);
            }

            if (!string.IsNullOrWhiteSpace(result.UnusedExportPath))
            {
                host.AppendLog("[export] unused=" + result.UnusedExportPath);
            }

            foreach (var row in result.MatchedPackages.Take(10))
            {
                host.AppendLog(
                    $"[match] {Path.GetFileName(row.PackagePath)} confidence={row.Confidence} count={row.MatchInstanceCount} rate={row.MatchRatePct:0.##}%");
            }

            foreach (var issue in result.Issues)
            {
                var prefix = issue.Severity == TrayDependencyIssueSeverity.Error ? "[error]" : "[warn]";
                host.AppendLog(prefix + " " + issue.Message);
            }

            if (result.Success)
            {
                var hasWarnings = result.Issues.Any(issue => issue.Severity == TrayDependencyIssueSeverity.Warning);
                host.SetStatus(hasWarnings
                    ? $"Tray dependencies completed with warnings ({stopwatch.Elapsed:mm\\:ss})"
                    : $"Tray dependencies completed ({stopwatch.Elapsed:mm\\:ss})");
                host.SetProgress(false, 100, hasWarnings
                    ? "Tray dependency analysis completed with warnings."
                    : "Tray dependency analysis completed.");
                await _recoveryController.MarkRecoveryCompletedAsync(
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
                await _recoveryController.SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Completed", operationId);
                recoveryCompleted = true;
                return;
            }

            host.SetStatus("Tray dependency analysis failed.");
            host.SetProgress(false, 0, "Tray dependency analysis failed.");
            await _recoveryController.MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Failed,
                    FailureMessage = "Tray dependency analysis failed."
                });
            await _recoveryController.SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Failed", operationId);
            recoveryCompleted = true;
            await host.ShowErrorPopupAsync("Tray dependency analysis failed.");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            host.AppendLog("[cancelled]");
            host.SetStatus(host.Localize("status.executionCancelled"));
            host.SetProgress(false, 0, host.Localize("progress.cancelled"));
            await _recoveryController.MarkRecoveryCompletedAsync(
                operationId,
                new RecoverableOperationCompletion
                {
                    Status = OperationRecoveryStatus.Cancelled
                });
            await _recoveryController.SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", "Cancelled", operationId);
            recoveryCompleted = true;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            host.AppendLog("[error] " + ex.Message);
            host.SetStatus("Tray dependency analysis failed.");
            host.SetProgress(false, 0, "Tray dependency analysis failed.");
            if (!recoveryCompleted)
            {
                await _recoveryController.MarkRecoveryCompletedAsync(
                    operationId,
                    new RecoverableOperationCompletion
                    {
                        Status = OperationRecoveryStatus.Failed,
                        FailureMessage = ex.Message
                    });
                await _recoveryController.SaveResultHistoryAsync(SimsAction.TrayDependencies, "TrayDependencies", ex.Message, operationId);
            }

            await host.ShowErrorPopupAsync("Tray dependency analysis failed.");
        }
        finally
        {
            host.SetExecutionCts(null);
            host.SetBusy(false);
            host.RefreshValidation();
        }
    }

    internal void CancelExecution(MainWindowExecutionHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        var cts = host.GetExecutionCts();
        if (cts is null)
        {
            host.SetStatus(host.Localize("status.noRunningExecution"));
            return;
        }

        host.AppendLog("[cancel] requested");
        host.SetStatus(host.Localize("status.cancelling"));
        host.SetProgress(true, 0, host.Localize("status.cancelling"));

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Ignore cancellation race when operation has already completed.
        }
    }

    private static void HandleTrayDependencyAnalysisProgress(MainWindowExecutionHost host, TrayDependencyAnalysisProgress progress)
    {
        var message = string.IsNullOrWhiteSpace(progress.Detail)
            ? progress.Stage.ToString()
            : progress.Detail;
        host.SetProgress(progress.Percent <= 0, Math.Clamp(progress.Percent, 0, 100), message);
    }

    private static void HandleProgress(MainWindowExecutionHost host, SimsProgressUpdate progress)
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

        host.SetProgress(!hasTotal || progress.Percent < 0, normalizedPercent, text);
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
