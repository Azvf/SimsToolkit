using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SimsModDesktop.PackageCore;
using SimsModDesktop.Presentation.Diagnostics;
using SimsModDesktop.TrayDependencyEngine;
using SimsModDesktop.Presentation.ViewModels.Panels;
using SimsModDesktop.Presentation.ViewModels.Preview;

namespace SimsModDesktop.Presentation.ViewModels;

public sealed class MainWindowTrayExportController
{
    private const int MaxConcurrentItemExports = 8;

    private readonly ITrayDependencyExportService _trayDependencyExportService;
    private readonly MainWindowCacheWarmupController _cacheWarmupController;
    private readonly ILogger<MainWindowTrayExportController> _logger;
    private readonly IPathIdentityResolver _pathIdentityResolver;
    private MainWindowTrayExportHost? _hookedHost;

    public MainWindowTrayExportController(
        ITrayDependencyExportService trayDependencyExportService,
        MainWindowCacheWarmupController cacheWarmupController,
        ILogger<MainWindowTrayExportController> logger,
        IPathIdentityResolver? pathIdentityResolver = null)
    {
        _trayDependencyExportService = trayDependencyExportService;
        _cacheWarmupController = cacheWarmupController;
        _logger = logger;
        _pathIdentityResolver = pathIdentityResolver ?? new SystemPathIdentityResolver();
    }

    internal void OpenSelectedTrayPreviewPaths(MainWindowTrayExportHost host)
    {
        var sourcePaths = GetSelectedTrayPreviewSourceFilePaths(host);
        if (sourcePaths.Count == 0)
        {
            return;
        }

        try
        {
            if (sourcePaths.Count == 1)
            {
                LaunchExplorer(sourcePaths[0], selectFile: true);
                host.SetStatus("Opened selected tray file location.");
                host.AppendLog($"[tray-selection] opened path={sourcePaths[0]}");
                return;
            }

            var directories = sourcePaths
                .Select(path => Path.GetDirectoryName(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (directories.Count == 0)
            {
                return;
            }

            foreach (var directory in directories)
            {
                LaunchExplorer(directory!, selectFile: false);
            }

            host.SetStatus(directories.Count == 1
                ? "Opened selected tray directory."
                : $"Opened {directories.Count} directories for selected tray files.");
            host.AppendLog($"[tray-selection] opened-directories count={directories.Count}");
        }
        catch (Exception ex)
        {
            host.SetStatus("Failed to open selected tray path.");
            host.AppendLog("[tray-selection] open failed: " + ex.Message);
        }
    }

    internal async Task ExportSelectedTrayPreviewFilesAsync(MainWindowTrayExportHost host)
    {
        var selectedItems = host.GetSelectedTrayPreviewItems();
        if (selectedItems.Count == 0)
        {
            return;
        }

        var selectedTrayKeys = selectedItems
            .Select(item => item.Item.TrayItemKey?.Trim())
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .ToArray();
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} batchSize={BatchSize} trayKeys={TrayKeys}",
            LogEvents.TrayExportBatchStart,
            "start",
            "trayexport",
            selectedItems.Count,
            string.Join(",", selectedTrayKeys));
        host.AppendLog(
            $"[trayexport.click] selectedItems={selectedItems.Count} selectedSources={GetSelectedTrayPreviewSourceFilePaths(host, selectedItems).Count} trayKeys={string.Join(",", selectedTrayKeys)}");

        var pickedFolders = await host.PickFolderPathsAsync("Select export folder", false);
        var exportRoot = pickedFolders.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(exportRoot))
        {
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} reason={Reason}",
                LogEvents.UiCommandBlocked,
                "blocked",
                "trayexport",
                "export-root-not-selected");
            return;
        }

        if (!TryBuildTrayDependencyExportRequests(host, selectedItems, exportRoot, out var dependencyRequests, out var error))
        {
            _logger.LogWarning(
                "{Event} status={Status} domain={Domain} reason={Reason}",
                LogEvents.TrayExportSnapshotBlocked,
                "blocked",
                "trayexport",
                error);
            var setupTask = EnqueueTrayExportTask(host, "Export setup");
            setupTask.SetExportRoot(exportRoot);
            setupTask.MarkFailed(error);
            host.SetStatus(error);
            host.AppendLog("[tray-selection] export blocked: " + error);
            return;
        }

        var queueEntries = dependencyRequests
            .Select((request, index) => new QueueEntry(
                index,
                request.ItemExportRoot,
                request.Request,
                EnqueueTrayExportTask(host, request.Item.Item.DisplayTitle)))
            .ToArray();
        using var batchTiming = PerformanceLogScope.Begin(
            _logger,
            "trayexport.batch",
            ("items", queueEntries.Length),
            ("concurrency", MaxConcurrentItemExports),
            ("target", exportRoot),
            ("modsPath", queueEntries[0].Request.ModsRootPath));
        var batchStopwatch = Stopwatch.StartNew();
        host.SetStatus($"Preparing tray export for {queueEntries.Length} selected item(s)...");
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} batchSize={BatchSize} concurrency={Concurrency} modsPath={ModsPath} target={Target}",
            LogEvents.TrayExportBatchStart,
            "start",
            "trayexport",
            queueEntries.Length,
            MaxConcurrentItemExports,
            queueEntries[0].Request.ModsRootPath,
            exportRoot);
        host.AppendLog(
            $"[trayexport.batch.start] items={queueEntries.Length} concurrency={MaxConcurrentItemExports} target={exportRoot} modsPath={queueEntries[0].Request.ModsRootPath}");
        host.AppendLog(
            $"[tray-selection] export-start items={queueEntries.Length} target={exportRoot} modsPath={queueEntries[0].Request.ModsRootPath}");
        foreach (var queueEntry in queueEntries)
        {
            queueEntry.Task.UpdatePendingProgress(1, "Preparing tray export...");
        }

        foreach (var dependencyRequest in dependencyRequests)
        {
            LogTraySelectionItemContext(host, dependencyRequest.Request);
        }

        if (!_cacheWarmupController.TryGetReadyTraySnapshot(queueEntries[0].Request.ModsRootPath, out var preloadedSnapshot))
        {
            _logger.LogWarning(
                "{Event} status={Status} domain={Domain} modsPath={ModsPath} reason={Reason}",
                LogEvents.TrayExportSnapshotBlocked,
                "blocked",
                "trayexport",
                queueEntries[0].Request.ModsRootPath,
                "cache-not-ready");
            for (var index = 0; index < queueEntries.Length; index++)
            {
                var exportTask = queueEntries[index].Task;
                exportTask.SetExportRoot(queueEntries[index].ItemExportRoot);
                exportTask.MarkFailed(index == 0
                    ? "Tray dependency cache is not ready. Open the Tray page and wait for cache warmup to finish."
                    : "Cancelled after batch failure.");
            }

            host.SetStatus("Export blocked: tray dependency cache is not ready.");
            host.AppendLog("[trayexport.blocked.cache-not-ready]");
            batchTiming.Cancel("cache-not-ready");
            return;
        }
        if (preloadedSnapshot is null || preloadedSnapshot.Packages.Count == 0)
        {
            _logger.LogWarning(
                "{Event} status={Status} domain={Domain} modsPath={ModsPath} inventoryVersion={InventoryVersion} snapshotPackages={SnapshotPackages} reason={Reason}",
                LogEvents.TrayExportSnapshotBlocked,
                "blocked",
                "trayexport",
                queueEntries[0].Request.ModsRootPath,
                preloadedSnapshot?.InventoryVersion ?? 0,
                preloadedSnapshot?.Packages.Count ?? 0,
                "snapshot-empty");
            const string blockedMessage = "Export blocked: tray dependency cache is empty (0 packages). Verify Mods Path points to your real The Sims 4 Mods folder and wait for warmup to finish.";
            for (var index = 0; index < queueEntries.Length; index++)
            {
                var exportTask = queueEntries[index].Task;
                exportTask.SetExportRoot(queueEntries[index].ItemExportRoot);
                exportTask.MarkFailed(index == 0
                    ? blockedMessage
                    : "Cancelled after batch failure.");
            }

            host.SetStatus(blockedMessage);
            host.AppendLog(
                $"[trayexport.blocked.snapshot-empty] modsPath={queueEntries[0].Request.ModsRootPath} inventoryVersion={preloadedSnapshot?.InventoryVersion ?? 0} snapshotPackages=0");
            batchTiming.Cancel("snapshot-empty");
            return;
        }
        host.AppendLog(
            $"[tray-selection] using-ready-snapshot modsPath={queueEntries[0].Request.ModsRootPath} packages={preloadedSnapshot.Packages.Count}");

        using var batchCts = new CancellationTokenSource();
        using var runGate = new SemaphoreSlim(MaxConcurrentItemExports, MaxConcurrentItemExports);
        var itemResults = new ItemRunResult?[queueEntries.Length];
        var startedEntries = new bool[queueEntries.Length];
        var runningTasks = new List<Task>(queueEntries.Length);
        var failureSignaled = 0;
        var firstFailureReason = string.Empty;

        void SignalFailure(string reason)
        {
            if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) != 0)
            {
                return;
            }

            firstFailureReason = string.IsNullOrWhiteSpace(reason) ? "Unknown export failure." : reason.Trim();
            batchCts.Cancel();
        }

        async Task RunQueueEntryAsync(QueueEntry queueEntry)
        {
            try
            {
                itemResults[queueEntry.ItemIndex] = await RunSingleItemExportAsync(
                    host,
                    queueEntry,
                    preloadedSnapshot,
                    batchCts.Token,
                    SignalFailure).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var failureStatus = "Mods export failed: " + ex.Message;
                await host.RunOnUiAsync(() =>
                {
                    queueEntry.Task.MarkFailed(failureStatus);
                    queueEntry.Task.AppendDetailLine(ex.ToString());
                }).ConfigureAwait(false);
                host.AppendLog(
                    $"[trayexport.item.fail] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} title={queueEntry.Request.ItemTitle} reason={ex.Message}");
                _logger.LogError(
                    ex,
                    "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey}",
                    LogEvents.TrayExportItemFail,
                    "fail",
                    "trayexport",
                    queueEntry.ItemIndex,
                    queueEntry.Request.TrayItemKey);
                itemResults[queueEntry.ItemIndex] = new ItemRunResult(
                    ItemRunResultKind.Failed,
                    0,
                    0,
                    false,
                    failureStatus);
                SignalFailure(ex.Message);
            }
            finally
            {
                runGate.Release();
            }
        }

        foreach (var queueEntry in queueEntries)
        {
            if (batchCts.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await runGate.WaitAsync(batchCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (batchCts.IsCancellationRequested)
            {
                break;
            }

            startedEntries[queueEntry.ItemIndex] = true;
            runningTasks.Add(RunQueueEntryAsync(queueEntry));
        }

        if (runningTasks.Count > 0)
        {
            await Task.WhenAll(runningTasks).ConfigureAwait(false);
        }

        for (var index = 0; index < itemResults.Length; index++)
        {
            itemResults[index] ??= new ItemRunResult(
                ItemRunResultKind.Cancelled,
                0,
                0,
                false,
                "Cancelled after batch failure.");
        }

        var completedResults = itemResults.Select(result => result!).ToArray();
        var copiedTrayFileCount = completedResults
            .Where(result => result.Kind is ItemRunResultKind.Success or ItemRunResultKind.SuccessWithWarnings)
            .Sum(result => result.CopiedTrayFileCount);
        var copiedModFileCount = completedResults
            .Where(result => result.Kind is ItemRunResultKind.Success or ItemRunResultKind.SuccessWithWarnings)
            .Sum(result => result.CopiedModFileCount);
        var warningCount = completedResults.Count(result => result.Kind == ItemRunResultKind.SuccessWithWarnings);
        var successCount = completedResults.Count(result => result.Kind is ItemRunResultKind.Success or ItemRunResultKind.SuccessWithWarnings);
        var failedCount = completedResults.Count(result => result.Kind == ItemRunResultKind.Failed);
        var cancelledCount = completedResults.Count(result => result.Kind == ItemRunResultKind.Cancelled);

        if (failureSignaled == 1)
        {
            var reason = string.IsNullOrWhiteSpace(firstFailureReason)
                ? "Unknown export failure."
                : firstFailureReason;
            var rollbackRoots = queueEntries
                .Where(entry => startedEntries[entry.ItemIndex])
                .Select(entry => entry.ItemExportRoot)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var rollbackStopwatch = Stopwatch.StartNew();
            _logger.LogWarning(
                "{Event} status={Status} domain={Domain} reason={Reason} createdRoots={CreatedRoots}",
                LogEvents.TrayExportRollbackStart,
                "start",
                "trayexport",
                reason,
                rollbackRoots.Length);
            host.AppendLog(
                $"[trayexport.batch.rollback.start] createdRoots={rollbackRoots.Length} reason={reason}");
            await Task.Run(() => RollbackTraySelectionExports(rollbackRoots)).ConfigureAwait(false);
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} reason={Reason} createdRoots={CreatedRoots} elapsedMs={ElapsedMs}",
                LogEvents.TrayExportRollbackDone,
                "done",
                "trayexport",
                reason,
                rollbackRoots.Length,
                rollbackStopwatch.ElapsedMilliseconds);
            host.AppendLog(
                $"[trayexport.batch.rollback.done] createdRoots={rollbackRoots.Length} elapsedMs={rollbackStopwatch.ElapsedMilliseconds} reason={reason}");

            var rolledBackCount = 0;
            await host.RunOnUiAsync(() =>
            {
                foreach (var queueEntry in queueEntries)
                {
                    var result = completedResults[queueEntry.ItemIndex];
                    switch (result.Kind)
                    {
                        case ItemRunResultKind.Success:
                        case ItemRunResultKind.SuccessWithWarnings:
                            rolledBackCount++;
                            queueEntry.Task.MarkFailed("Rolled back after batch failure.");
                            break;
                        case ItemRunResultKind.Cancelled:
                            if (queueEntry.Task.IsRunning)
                            {
                                queueEntry.Task.MarkFailed("Cancelled after batch failure.");
                            }

                            break;
                        case ItemRunResultKind.Failed:
                            if (queueEntry.Task.IsRunning)
                            {
                                queueEntry.Task.MarkFailed(result.FailureStatus ?? "Mods export failed.");
                            }

                            break;
                    }
                }

                host.SetStatus("Export failed: " + reason);
            }).ConfigureAwait(false);

            host.AppendLog(
                $"[trayexport.batch.done] successCount=0 failedCount={queueEntries.Length} rolledBackCount={rolledBackCount} elapsedMs={batchStopwatch.ElapsedMilliseconds}");
            _logger.LogError(
                "{Event} status={Status} domain={Domain} batchSize={BatchSize} successCount={SuccessCount} failedCount={FailedCount} cancelledCount={CancelledCount} rolledBackCount={RolledBackCount} elapsedMs={ElapsedMs} reason={Reason}",
                LogEvents.TrayExportBatchFail,
                "fail",
                "trayexport",
                queueEntries.Length,
                successCount,
                failedCount,
                cancelledCount,
                rolledBackCount,
                batchStopwatch.ElapsedMilliseconds,
                reason);
            batchTiming.Fail(
                new InvalidOperationException(reason),
                "batch aborted after item failure",
                ("successCount", successCount),
                ("failedCount", failedCount),
                ("cancelledCount", cancelledCount),
                ("rolledBackCount", rolledBackCount),
                ("concurrency", MaxConcurrentItemExports),
                ("elapsedMs", batchStopwatch.ElapsedMilliseconds));
            return;
        }

        await host.RunOnUiAsync(() =>
        {
            host.SetStatus(warningCount == 0
                ? $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files."
                : $"Exported {copiedTrayFileCount} tray files and {copiedModFileCount} referenced mod files ({warningCount} warning item(s) ignored).");
        }).ConfigureAwait(false);

        host.AppendLog(
            $"[tray-selection] export tray={copiedTrayFileCount} mods={copiedModFileCount} items={dependencyRequests.Count} warnings={warningCount} target={exportRoot}");
        host.AppendLog(
            $"[trayexport.batch.done] successCount={successCount} failedCount={failedCount} rolledBackCount=0 elapsedMs={batchStopwatch.ElapsedMilliseconds}");
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} batchSize={BatchSize} concurrency={Concurrency} copiedTrayFiles={CopiedTrayFiles} copiedModFiles={CopiedModFiles} warnings={Warnings} failures={Failures} elapsedMs={ElapsedMs}",
            LogEvents.TrayExportBatchDone,
            "done",
            "trayexport",
            queueEntries.Length,
            MaxConcurrentItemExports,
            copiedTrayFileCount,
            copiedModFileCount,
            warningCount,
            failedCount,
            batchStopwatch.ElapsedMilliseconds);
        batchTiming.Success(
            "batch completed",
            ("trayFiles", copiedTrayFileCount),
            ("modFiles", copiedModFileCount),
            ("warnings", warningCount),
            ("successCount", successCount),
            ("failedCount", failedCount),
            ("cancelledCount", cancelledCount),
            ("concurrency", MaxConcurrentItemExports),
            ("elapsedMs", batchStopwatch.ElapsedMilliseconds));
    }

    internal void SelectAllTrayPreviewPage(MainWindowTrayExportHost host)
    {
        if (host.PreviewItems.Count == 0)
        {
            return;
        }

        host.SelectAllTrayPreviewPage();
    }

    internal void ClearCompletedTrayExportTasks(MainWindowTrayExportHost host)
    {
        for (var index = host.TrayExportTasks.Count - 1; index >= 0; index--)
        {
            if (host.TrayExportTasks[index].IsCompleted)
            {
                host.TrayExportTasks.RemoveAt(index);
            }
        }
    }

    internal void ToggleTrayExportQueue(MainWindowTrayExportHost host)
    {
        if (!host.GetHasTrayExportTasks())
        {
            return;
        }

        SetTrayExportQueueExpanded(host, !host.GetIsTrayExportQueueExpanded());
    }

    internal void OpenTrayExportTaskPath(MainWindowTrayExportHost host, TrayExportTaskItemViewModel? task)
    {
        if (task is null || !task.HasExportRoot)
        {
            return;
        }

        try
        {
            LaunchExplorer(task.ExportRootPath, selectFile: false);
        }
        catch (Exception ex)
        {
            task.AppendDetailLine("Failed to open export folder: " + ex.Message);
            host.SetStatus("Failed to open export folder.");
        }
    }

    internal void ToggleTrayExportTaskDetails(TrayExportTaskItemViewModel? task)
    {
        task?.ToggleDetails();
    }

    internal void OnTrayExportTasksChanged(MainWindowTrayExportHost host, object? sender, NotifyCollectionChangedEventArgs e)
    {
        _hookedHost = host;

        if (e.NewItems is { Count: > 0 })
        {
            SetTrayExportQueueExpanded(host, true);
        }

        if (e.OldItems is not null)
        {
            foreach (var item in e.OldItems.OfType<TrayExportTaskItemViewModel>())
            {
                host.UnsubscribeTaskPropertyChanged(item, OnTrayExportTaskPropertyChanged);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (var item in e.NewItems.OfType<TrayExportTaskItemViewModel>())
            {
                host.SubscribeTaskPropertyChanged(item, OnTrayExportTaskPropertyChanged);
            }
        }

        NotifyTrayExportQueueChanged(host);
    }

    private IReadOnlyList<string> GetSelectedTrayPreviewSourceFilePaths(
        MainWindowTrayExportHost host,
        IReadOnlyCollection<TrayPreviewListItemViewModel>? selectedItems = null)
    {
        var source = selectedItems ?? host.GetSelectedTrayPreviewItems();
        return source
            .SelectMany(item => item.Item.SourceFilePaths)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private TrayExportTaskItemViewModel EnqueueTrayExportTask(MainWindowTrayExportHost host, string? title)
    {
        var task = new TrayExportTaskItemViewModel(
            string.IsNullOrWhiteSpace(title)
                ? "Tray Export"
                : title.Trim());
        host.TrayExportTasks.Add(task);
        return task;
    }

    private async Task<ItemRunResult> RunSingleItemExportAsync(
        MainWindowTrayExportHost host,
        QueueEntry queueEntry,
        PackageIndexSnapshot preloadedSnapshot,
        CancellationToken cancellationToken,
        Action<string> signalFailure)
    {
        await host.RunOnUiAsync(() =>
        {
            queueEntry.Task.SetExportRoot(queueEntry.ItemExportRoot);
        }).ConfigureAwait(false);

        var exportRequest = queueEntry.Request with { PreloadedSnapshot = preloadedSnapshot };
        var lastStage = (TrayDependencyExportStage?)null;
        using var itemTiming = PerformanceLogScope.Begin(
            _logger,
            "trayexport.item",
            ("itemIndex", queueEntry.ItemIndex),
            ("trayKey", queueEntry.Request.TrayItemKey),
            ("title", queueEntry.Request.ItemTitle));

        host.AppendLog(
            $"[trayexport.item.start] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} title={queueEntry.Request.ItemTitle}");
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey} itemTitle={ItemTitle} snapshotPackages={SnapshotPackages} sourceFiles={SourceFiles}",
            LogEvents.TrayExportItemStart,
            "start",
            "trayexport",
            queueEntry.ItemIndex,
            queueEntry.Request.TrayItemKey,
            queueEntry.Request.ItemTitle,
            preloadedSnapshot.Packages.Count,
            queueEntry.Request.TraySourceFiles.Count);
        host.AppendLog(
            $"[tray-selection][item] trayKey={queueEntry.Request.TrayItemKey} export-begin title={queueEntry.Request.ItemTitle}");
        host.AppendLog(
            $"[trayexport.item.context] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} snapshotInventory={preloadedSnapshot.InventoryVersion} snapshotPackages={preloadedSnapshot.Packages.Count} sourceFiles={queueEntry.Request.TraySourceFiles.Count} trayOut={queueEntry.Request.TrayExportRoot} modsOut={queueEntry.Request.ModsExportRoot}");

        TrayDependencyExportResult result;
        try
        {
            result = await _trayDependencyExportService.ExportAsync(
                exportRequest,
                new Progress<TrayDependencyExportProgress>(progress =>
                {
                    var detail = string.IsNullOrWhiteSpace(progress.Detail)
                        ? progress.Stage.ToString()
                        : progress.Detail.Trim();
                    if (lastStage != progress.Stage)
                    {
                        lastStage = progress.Stage;
                        host.AppendLog(
                            $"[tray-selection][stage] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} stage={progress.Stage} percent={progress.Percent} detail={detail}");
                        _logger.LogInformation(
                            "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey} stage={Stage} percent={Percent} detail={Detail}",
                            LogEvents.TrayExportItemStage,
                            "mark",
                            "trayexport",
                            queueEntry.ItemIndex,
                            queueEntry.Request.TrayItemKey,
                            progress.Stage,
                            progress.Percent,
                            detail);
                        itemTiming.Mark(
                            "stage",
                            ("itemIndex", queueEntry.ItemIndex),
                            ("trayKey", queueEntry.Request.TrayItemKey),
                            ("stage", progress.Stage.ToString()),
                            ("percent", progress.Percent));
                    }

                    _ = host.RunOnUiAsync(() =>
                    {
                        // Progress callbacks can be delivered after completion on background threads.
                        // Ignore late updates so final status text is not overwritten.
                        if (!queueEntry.Task.IsRunning)
                        {
                            return;
                        }

                        if (progress.Stage == TrayDependencyExportStage.Preparing)
                        {
                            queueEntry.Task.MarkTrayRunning(detail);
                            return;
                        }

                        if (progress.Stage == TrayDependencyExportStage.Completed)
                        {
                            queueEntry.Task.UpdateModsProgress(99, detail);
                            return;
                        }

                        queueEntry.Task.UpdateModsProgress(progress.Percent, detail);
                    });
                }),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            const string cancelledStatus = "Cancelled after batch failure.";
            await host.RunOnUiAsync(() =>
            {
                if (queueEntry.Task.IsRunning)
                {
                    queueEntry.Task.MarkFailed(cancelledStatus);
                }
            }).ConfigureAwait(false);
            host.AppendLog(
                $"[trayexport.item.fail] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} title={queueEntry.Request.ItemTitle} reason=cancelled");
            _logger.LogWarning(
                "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey} reason={Reason}",
                LogEvents.TrayExportItemCancel,
                "cancel",
                "trayexport",
                queueEntry.ItemIndex,
                queueEntry.Request.TrayItemKey,
                "cancelled-after-batch-failure");
            itemTiming.Cancel("export-cancelled");
            return new ItemRunResult(
                ItemRunResultKind.Cancelled,
                0,
                0,
                false,
                cancelledStatus);
        }
        catch (Exception ex)
        {
            var failureStatus = "Mods export failed: " + ex.Message;
            await host.RunOnUiAsync(() =>
            {
                queueEntry.Task.MarkFailed(failureStatus);
                queueEntry.Task.AppendDetailLine(ex.ToString());
            }).ConfigureAwait(false);
            host.AppendLog($"[tray-selection][internal] export failed for trayKey={queueEntry.Request.TrayItemKey}: {ex.Message}");
            host.AppendLog(
                $"[trayexport.item.fail] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} title={queueEntry.Request.ItemTitle} reason={ex.Message}");
            _logger.LogError(
                ex,
                "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey}",
                LogEvents.TrayExportItemFail,
                "fail",
                "trayexport",
                queueEntry.ItemIndex,
                queueEntry.Request.TrayItemKey);
            signalFailure(ex.Message);
            itemTiming.Fail(ex, "export crashed");
            return new ItemRunResult(
                ItemRunResultKind.Failed,
                0,
                0,
                false,
                failureStatus);
        }

        if (result.Issues.Count > 0)
        {
            await host.RunOnUiAsync(() =>
            {
                foreach (var issue in result.Issues)
                {
                    queueEntry.Task.AppendDetailLine(issue.Message);
                }
            }).ConfigureAwait(false);
            foreach (var issue in result.Issues)
            {
                host.AppendLog($"[tray-selection][internal] {issue.Severity}: {issue.Message}");
            }

            var issueKinds = string.Join(
                ",",
                result.Issues
                    .Select(issue => $"{issue.Severity}:{issue.Kind}")
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            host.AppendLog(
                $"[trayexport.item.issues] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} count={result.Issues.Count} kinds={issueKinds}");
        }

        host.AppendLog(
            $"[tray-selection][item] trayKey={queueEntry.Request.TrayItemKey} success={result.Success} tray={result.CopiedTrayFileCount} mods={result.CopiedModFileCount} issues={result.Issues.Count}");
        if (result.Diagnostics is not null)
        {
            host.AppendLog(
                $"[tray-selection][diag] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} inputFiles={result.Diagnostics.InputSourceFileCount} bundleTray={result.Diagnostics.BundleTrayItemFileCount} bundleAux={result.Diagnostics.BundleAuxiliaryFileCount} candidateIds={result.Diagnostics.CandidateIdCount} resourceKeys={result.Diagnostics.CandidateResourceKeyCount} packages={result.Diagnostics.SnapshotPackageCount} direct={result.Diagnostics.DirectMatchCount} expanded={result.Diagnostics.ExpandedMatchCount}");
        }
        if (result.CopiedModFileCount == 0)
        {
            host.AppendLog($"[tray-selection][item] trayKey={queueEntry.Request.TrayItemKey} no matched mod files exported");
            LogNoModExportRootCause(host, queueEntry, result);
        }
        else
        {
            var modFilesOnDisk = CountFilesSafely(queueEntry.Request.ModsExportRoot);
            host.AppendLog(
                $"[trayexport.item.mods.disk] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} reported={result.CopiedModFileCount} disk={modFilesOnDisk} modsOut={queueEntry.Request.ModsExportRoot}");
        }

        if (!result.Success)
        {
            var failure = result.Issues.FirstOrDefault(issue => issue.Severity == TrayDependencyIssueSeverity.Error)?.Message
                          ?? "Unknown error.";
            var failureStatus = "Mods export failed: " + failure;
            await host.RunOnUiAsync(() =>
            {
                queueEntry.Task.MarkFailed(failureStatus);
            }).ConfigureAwait(false);
            host.AppendLog(
                $"[trayexport.item.fail] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} title={queueEntry.Request.ItemTitle} reason={failure}");
            _logger.LogError(
                "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey} failure={Failure} warnings={Warnings} copiedTrayFiles={CopiedTrayFiles} copiedModFiles={CopiedModFiles}",
                LogEvents.TrayExportItemFail,
                "fail",
                "trayexport",
                queueEntry.ItemIndex,
                queueEntry.Request.TrayItemKey,
                failure,
                result.Issues.Count,
                result.CopiedTrayFileCount,
                result.CopiedModFileCount);
            signalFailure(failure);
            itemTiming.Fail(new InvalidOperationException(failure), "export completed with errors");
            return new ItemRunResult(
                ItemRunResultKind.Failed,
                result.CopiedTrayFileCount,
                result.CopiedModFileCount,
                result.HasMissingReferenceWarnings,
                failureStatus);
        }

        if (result.HasMissingReferenceWarnings)
        {
            await host.RunOnUiAsync(() =>
            {
                queueEntry.Task.MarkCompleted("Completed (missing references ignored).", failed: false);
            }).ConfigureAwait(false);
            host.AppendLog(
                $"[trayexport.item.done] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} title={queueEntry.Request.ItemTitle} success=true warnings=true tray={result.CopiedTrayFileCount} mods={result.CopiedModFileCount}");
            _logger.LogInformation(
                "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey} warnings={Warnings} copiedTrayFiles={CopiedTrayFiles} copiedModFiles={CopiedModFiles}",
                LogEvents.TrayExportItemDone,
                "done",
                "trayexport",
                queueEntry.ItemIndex,
                queueEntry.Request.TrayItemKey,
                result.Issues.Count,
                result.CopiedTrayFileCount,
                result.CopiedModFileCount);
            itemTiming.Success(
                "completed with warnings",
                ("itemIndex", queueEntry.ItemIndex),
                ("trayFiles", result.CopiedTrayFileCount),
                ("modFiles", result.CopiedModFileCount),
                ("issues", result.Issues.Count));
            return new ItemRunResult(
                ItemRunResultKind.SuccessWithWarnings,
                result.CopiedTrayFileCount,
                result.CopiedModFileCount,
                true,
                null);
        }

        await host.RunOnUiAsync(() =>
        {
            queueEntry.Task.MarkCompleted("Completed.", failed: false);
        }).ConfigureAwait(false);
        host.AppendLog(
            $"[trayexport.item.done] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} title={queueEntry.Request.ItemTitle} success=true warnings=false tray={result.CopiedTrayFileCount} mods={result.CopiedModFileCount}");
        _logger.LogInformation(
            "{Event} status={Status} domain={Domain} itemIndex={ItemIndex} trayItemKey={TrayItemKey} copiedTrayFiles={CopiedTrayFiles} copiedModFiles={CopiedModFiles} warnings={Warnings}",
            LogEvents.TrayExportItemDone,
            "done",
            "trayexport",
            queueEntry.ItemIndex,
            queueEntry.Request.TrayItemKey,
            result.CopiedTrayFileCount,
            result.CopiedModFileCount,
            0);
        itemTiming.Success(
            "completed",
            ("itemIndex", queueEntry.ItemIndex),
            ("trayFiles", result.CopiedTrayFileCount),
            ("modFiles", result.CopiedModFileCount),
            ("issues", result.Issues.Count));
        return new ItemRunResult(
            ItemRunResultKind.Success,
            result.CopiedTrayFileCount,
            result.CopiedModFileCount,
            false,
            null);
    }

    private static void RollbackTraySelectionExports(IEnumerable<string> exportRoots)
    {
        foreach (var exportRoot in exportRoots
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (Directory.Exists(exportRoot))
                {
                    Directory.Delete(exportRoot, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private bool TryBuildTrayDependencyExportRequests(
        MainWindowTrayExportHost host,
        IReadOnlyList<TrayPreviewListItemViewModel> selectedItems,
        string exportRoot,
        out List<(TrayPreviewListItemViewModel Item, string ItemExportRoot, TrayDependencyExportRequest Request)> requests,
        out string error)
    {
        requests = new List<(TrayPreviewListItemViewModel Item, string ItemExportRoot, TrayDependencyExportRequest Request)>();
        error = string.Empty;

        var modsPath = ResolveDirectoryPath(host.TrayDependencies.ModsPath ?? string.Empty);
        var modsPathResolved = ResolveDirectory(host.TrayDependencies.ModsPath ?? string.Empty);
        host.AppendLog(
            $"[path.resolve] component=trayexport.batch rawPath={modsPathResolved.FullPath} canonicalPath={modsPathResolved.CanonicalPath} exists={modsPathResolved.Exists} isReparse={modsPathResolved.IsReparsePoint} linkTarget={modsPathResolved.LinkTarget ?? string.Empty}");
        if (string.IsNullOrWhiteSpace(modsPath) || !Directory.Exists(modsPath))
        {
            error = "Mods Path is missing. Set a valid Mods Path before exporting referenced mods.";
            return false;
        }

        var usedDirectoryNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var selectedItem in selectedItems)
        {
            var trayKey = selectedItem.Item.TrayItemKey?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trayKey))
            {
                error = "A selected tray item is missing TrayItemKey, cannot export referenced mods.";
                requests.Clear();
                return false;
            }

            var trayPath = ResolveDirectoryPath(selectedItem.Item.TrayRootPath ?? string.Empty);
            if (string.IsNullOrWhiteSpace(trayPath) || !Directory.Exists(trayPath))
            {
                error = $"Tray path is missing for selected tray item {trayKey}.";
                requests.Clear();
                return false;
            }

            var traySourceFiles = ResolveTraySourceFiles(selectedItem.Item);
            if (traySourceFiles.Count == 0)
            {
                error = $"Selected tray item {trayKey} has no source files to export.";
                requests.Clear();
                return false;
            }

            var exportDirectoryName = BuildTraySelectionExportDirectoryName(selectedItem.Item, usedDirectoryNames);
            var itemExportRoot = Path.Combine(exportRoot, exportDirectoryName);
            var trayExportRoot = Path.Combine(itemExportRoot, "Tray");
            var modsExportRoot = Path.Combine(itemExportRoot, "Mods");

            requests.Add((selectedItem, itemExportRoot, new TrayDependencyExportRequest
            {
                ItemTitle = selectedItem.Item.DisplayTitle,
                TrayItemKey = trayKey,
                TrayRootPath = trayPath,
                TraySourceFiles = traySourceFiles,
                ModsRootPath = modsPath,
                TrayExportRoot = trayExportRoot,
                ModsExportRoot = modsExportRoot
            }));
        }

        return true;
    }

    private IReadOnlyList<string> ResolveTraySourceFiles(SimsTrayPreviewItem item)
    {
        var resolved = item.SourceFilePaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Where(File.Exists)
            .Select(ResolveFilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var trayPath = ResolveDirectoryPath(item.TrayRootPath ?? string.Empty);
        var trayKey = item.TrayItemKey?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trayPath) || string.IsNullOrWhiteSpace(trayKey) || !Directory.Exists(trayPath))
        {
            return resolved;
        }

        foreach (var extension in SupportedTrayExportExtensions)
        {
            var candidate = Path.Combine(trayPath, trayKey + extension);
            if (File.Exists(candidate))
            {
                resolved.Add(candidate);
            }
        }

        foreach (var candidate in Directory.EnumerateFiles(trayPath, trayKey + ".*", SearchOption.TopDirectoryOnly))
        {
            if (!IsSupportedTrayExportFile(candidate))
            {
                continue;
            }

            resolved.Add(ResolveFilePath(candidate));
        }

        return resolved
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsSupportedTrayExportFile(string path)
    {
        var extension = Path.GetExtension(path);
        return SupportedTrayExportExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private ResolvedPathInfo ResolveDirectory(string path)
    {
        var resolved = _pathIdentityResolver.ResolveDirectory(path);
        var fullPath = !string.IsNullOrWhiteSpace(resolved.FullPath)
            ? resolved.FullPath
            : path.Trim().Trim('"');
        var canonicalPath = !string.IsNullOrWhiteSpace(resolved.CanonicalPath)
            ? resolved.CanonicalPath
            : fullPath;
        return resolved with
        {
            FullPath = fullPath,
            CanonicalPath = canonicalPath
        };
    }

    private string ResolveDirectoryPath(string path)
    {
        return ResolveDirectory(path).CanonicalPath;
    }

    private string ResolveFilePath(string path)
    {
        var resolved = _pathIdentityResolver.ResolveFile(path);
        if (!string.IsNullOrWhiteSpace(resolved.CanonicalPath))
        {
            return resolved.CanonicalPath;
        }

        if (!string.IsNullOrWhiteSpace(resolved.FullPath))
        {
            return resolved.FullPath;
        }

        return path.Trim().Trim('"');
    }

    private static void LogTraySelectionItemContext(MainWindowTrayExportHost host, TrayDependencyExportRequest request)
    {
        var sourceFiles = request.TraySourceFiles
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sourceExtensions = sourceFiles
            .Select(path => Path.GetExtension(path))
            .Where(ext => !string.IsNullOrWhiteSpace(ext))
            .GroupBy(ext => ext, StringComparer.OrdinalIgnoreCase)
            .Select(group => $"{group.Key}:{group.Count()}")
            .OrderBy(text => text, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        host.AppendLog(
            $"[tray-selection][item] trayKey={request.TrayItemKey} trayPath={request.TrayRootPath} sourceFiles={sourceFiles.Length} trayOut={request.TrayExportRoot} modsOut={request.ModsExportRoot}");
        host.AppendLog(
            $"[trayexport.item.sources] trayKey={request.TrayItemKey} extensions={string.Join(",", sourceExtensions)}");
        foreach (var sourceFile in sourceFiles)
        {
            host.AppendLog($"[tray-selection][item-source] trayKey={request.TrayItemKey} path={sourceFile}");
        }
    }

    private static void LogNoModExportRootCause(
        MainWindowTrayExportHost host,
        QueueEntry queueEntry,
        TrayDependencyExportResult result)
    {
        var diagnostics = result.Diagnostics;
        var reason = "unknown";
        if (diagnostics is null)
        {
            reason = "no-diagnostics";
        }
        else if (diagnostics.CandidateIdCount == 0 && diagnostics.CandidateResourceKeyCount == 0)
        {
            reason = "no-candidates-extracted-from-tray";
        }
        else if (diagnostics.DirectMatchCount == 0 && diagnostics.ExpandedMatchCount == 0)
        {
            reason = "no-match-found-in-mod-cache";
        }
        else if (!result.Success)
        {
            reason = "export-failed-before-copy";
        }
        else
        {
            reason = "copy-step-produced-zero-mod-files";
        }

        host.AppendLog(
            $"[trayexport.item.nomods] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} reason={reason} success={result.Success} issues={result.Issues.Count}");
        if (diagnostics is not null)
        {
            host.AppendLog(
                $"[trayexport.item.nomods.diag] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} candidates={diagnostics.CandidateIdCount} resourceKeys={diagnostics.CandidateResourceKeyCount} direct={diagnostics.DirectMatchCount} expanded={diagnostics.ExpandedMatchCount}");
        }

        var modFilesOnDisk = CountFilesSafely(queueEntry.Request.ModsExportRoot);
        host.AppendLog(
            $"[trayexport.item.nomods.disk] itemIndex={queueEntry.ItemIndex} trayKey={queueEntry.Request.TrayItemKey} modsOut={queueEntry.Request.ModsExportRoot} diskFiles={modFilesOnDisk}");
    }

    private static int CountFilesSafely(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Count()
                : 0;
        }
        catch
        {
            return -1;
        }
    }

    private static void LaunchExplorer(string path, bool selectFile)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var arguments = selectFile
            ? $"/select,\"{path}\""
            : $"\"{path}\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = arguments,
            UseShellExecute = true
        });
    }

    private static string BuildTraySelectionExportDirectoryName(SimsTrayPreviewItem item, ISet<string> usedDirectoryNames)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(usedDirectoryNames);

        var rawLabel = !string.IsNullOrWhiteSpace(item.DisplayTitle)
            ? item.DisplayTitle
            : !string.IsNullOrWhiteSpace(item.ItemName)
                ? item.ItemName
                : item.PresetType;
        var safeLabel = SanitizePathSegment(rawLabel);
        var safeKey = SanitizePathSegment(item.TrayItemKey);
        var baseName = string.IsNullOrWhiteSpace(safeLabel)
            ? safeKey
            : $"{safeLabel}_{safeKey}";
        if (string.IsNullOrWhiteSpace(baseName))
        {
            baseName = "TrayItem";
        }

        var candidate = baseName;
        var suffix = 2;
        while (!usedDirectoryNames.Add(candidate))
        {
            candidate = $"{baseName}_{suffix}";
            suffix++;
        }

        return candidate;
    }

    private static string SanitizePathSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var invalidChars = Path.GetInvalidFileNameChars();
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (var character in value.Trim())
        {
            if (invalidChars.Contains(character))
            {
                builder.Append('_');
            }
            else if (char.IsWhiteSpace(character))
            {
                builder.Append(' ');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder
            .ToString()
            .Trim()
            .TrimEnd('.');
    }

    private void SetTrayExportQueueExpanded(MainWindowTrayExportHost host, bool expanded)
    {
        if (host.GetIsTrayExportQueueExpanded() == expanded)
        {
            return;
        }

        host.SetIsTrayExportQueueExpanded(expanded);
        host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayExportQueueVisible));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.TrayExportQueueToggleText));
    }

    private void NotifyTrayExportQueueChanged(MainWindowTrayExportHost host)
    {
        host.RaisePropertyChanged(nameof(MainWindowViewModel.HasTrayExportTasks));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.HasCompletedTrayExportTasks));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.HasRunningTrayExportTasks));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayExportQueueDockVisible));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.IsTrayExportQueueVisible));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.TrayExportQueueSummaryText));
        host.RaisePropertyChanged(nameof(MainWindowViewModel.TrayExportQueueToggleText));
        host.NotifyTrayExportCommandsChanged();
    }

    private void OnTrayExportTaskPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_hookedHost is not null)
        {
            NotifyTrayExportQueueChanged(_hookedHost);
        }
    }

    private sealed record QueueEntry(
        int ItemIndex,
        string ItemExportRoot,
        TrayDependencyExportRequest Request,
        TrayExportTaskItemViewModel Task);

    private enum ItemRunResultKind
    {
        Success,
        SuccessWithWarnings,
        Failed,
        Cancelled
    }

    private sealed record ItemRunResult(
        ItemRunResultKind Kind,
        int CopiedTrayFileCount,
        int CopiedModFileCount,
        bool HasMissingReferenceWarnings,
        string? FailureStatus);

    private static readonly string[] SupportedTrayExportExtensions =
    [
        ".trayitem",
        ".hhi",
        ".sgi",
        ".householdbinary",
        ".blueprint",
        ".bpi",
        ".room",
        ".rmi"
    ];
}
