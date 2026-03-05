using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SimsModDesktop.PackageCore.Performance;

namespace SimsModDesktop.TrayDependencyEngine;

internal sealed class ModFileExporter
{
    private readonly ILogger _logger;
    private readonly ModFileCopyPlanner _planner = new();
    private readonly ParallelModFileCopier _copier = new();

    public ModFileExporter(ILogger logger)
    {
        _logger = logger;
    }

    public async Task<int> CopyFilesAsync(
        IReadOnlyList<string> sourceFiles,
        string targetRoot,
        List<TrayDependencyIssue> issues,
        IProgress<TrayDependencyExportProgress>? progress,
        string trayItemKey,
        int? requestedWorkerCount,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceFiles);
        ArgumentNullException.ThrowIfNull(targetRoot);
        ArgumentNullException.ThrowIfNull(issues);

        Directory.CreateDirectory(targetRoot);

        var copyPlan = _planner.Plan(sourceFiles, targetRoot);
        if (copyPlan.Issues.Count != 0)
        {
            issues.AddRange(copyPlan.Issues);
        }

        var workerCount = PerformanceWorkerSizer.ResolveTrayExportCopyWorkers(requestedWorkerCount);
        var timer = Stopwatch.StartNew();
        _logger.LogInformation(
            "trayexport.copy.batch.start trayItemKey={TrayItemKey} fileCount={FileCount} workerCount={WorkerCount}",
            trayItemKey,
            copyPlan.Items.Count,
            workerCount);

        ParallelModFileCopyResult copyResult;
        try
        {
            copyResult = await _copier.CopyAsync(
                copyPlan.Items,
                workerCount,
                progress,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "trayexport.copy.batch.fail trayItemKey={TrayItemKey} fileCount={FileCount} copiedCount={CopiedCount} workerCount={WorkerCount} elapsedMs={ElapsedMs} reason={Reason}",
                trayItemKey,
                copyPlan.Items.Count,
                0,
                workerCount,
                timer.ElapsedMilliseconds,
                "cancelled");
            throw;
        }

        if (copyResult.ErrorIssue is not null)
        {
            issues.Add(copyResult.ErrorIssue);
            _logger.LogWarning(
                "trayexport.copy.batch.fail trayItemKey={TrayItemKey} fileCount={FileCount} copiedCount={CopiedCount} workerCount={WorkerCount} elapsedMs={ElapsedMs} filePath={FilePath}",
                trayItemKey,
                copyPlan.Items.Count,
                copyResult.CopiedFileCount,
                workerCount,
                timer.ElapsedMilliseconds,
                copyResult.ErrorIssue.FilePath ?? string.Empty);
            return copyResult.CopiedFileCount;
        }

        _logger.LogInformation(
            "trayexport.copy.batch.done trayItemKey={TrayItemKey} fileCount={FileCount} copiedCount={CopiedCount} workerCount={WorkerCount} elapsedMs={ElapsedMs}",
            trayItemKey,
            copyPlan.Items.Count,
            copyResult.CopiedFileCount,
            workerCount,
            timer.ElapsedMilliseconds);
        return copyResult.CopiedFileCount;
    }
}
