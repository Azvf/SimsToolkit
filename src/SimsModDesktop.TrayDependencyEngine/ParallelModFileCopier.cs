using System.Collections.Concurrent;

namespace SimsModDesktop.TrayDependencyEngine;

internal sealed class ParallelModFileCopier
{
    private const int GlobalMaxConcurrentCopies = 8;
    private static readonly SemaphoreSlim GlobalCopyGate = new(GlobalMaxConcurrentCopies, GlobalMaxConcurrentCopies);

    public async Task<ParallelModFileCopyResult> CopyAsync(
        IReadOnlyList<ModFileCopyPlanItem> items,
        int workerCount,
        IProgress<TrayDependencyExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (items.Count == 0)
        {
            return new ParallelModFileCopyResult(0, null);
        }

        var copiedFileCount = 0;
        var failureSignaled = 0;
        TrayDependencyIssue? failureIssue = null;

        if (workerCount <= 1 || items.Count == 1)
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await GlobalCopyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    File.Copy(item.SourcePath, item.TargetPath, overwrite: false);
                }
                catch (Exception ex)
                {
                    failureIssue = new TrayDependencyIssue
                    {
                        Severity = TrayDependencyIssueSeverity.Error,
                        Kind = TrayDependencyIssueKind.CopyError,
                        FilePath = item.SourcePath,
                        Message = $"Failed to copy mod file: {ex.Message}"
                    };
                    break;
                }
                finally
                {
                    GlobalCopyGate.Release();
                }

                var copied = ++copiedFileCount;
                progress?.Report(new TrayDependencyExportProgress
                {
                    Stage = TrayDependencyExportStage.CopyingMods,
                    Percent = ProgressScale.Scale(85, 99, copied, items.Count),
                    Detail = $"Copying referenced mods... {copied}/{items.Count}"
                });
            }

            return new ParallelModFileCopyResult(copiedFileCount, failureIssue);
        }

        var queue = new ConcurrentQueue<ModFileCopyPlanItem>(items);
        var workers = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Run(async () =>
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    if (Volatile.Read(ref failureSignaled) != 0)
                    {
                        break;
                    }

                    if (!queue.TryDequeue(out var item))
                    {
                        break;
                    }

                    await GlobalCopyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        if (Volatile.Read(ref failureSignaled) != 0)
                        {
                            break;
                        }

                        File.Copy(item.SourcePath, item.TargetPath, overwrite: false);
                        var copied = Interlocked.Increment(ref copiedFileCount);
                        progress?.Report(new TrayDependencyExportProgress
                        {
                            Stage = TrayDependencyExportStage.CopyingMods,
                            Percent = ProgressScale.Scale(85, 99, copied, items.Count),
                            Detail = $"Copying referenced mods... {copied}/{items.Count}"
                        });
                    }
                    catch (Exception ex)
                    {
                        if (Interlocked.CompareExchange(ref failureSignaled, 1, 0) == 0)
                        {
                            failureIssue = new TrayDependencyIssue
                            {
                                Severity = TrayDependencyIssueSeverity.Error,
                                Kind = TrayDependencyIssueKind.CopyError,
                                FilePath = item.SourcePath,
                                Message = $"Failed to copy mod file: {ex.Message}"
                            };
                        }
                    }
                    finally
                    {
                        GlobalCopyGate.Release();
                    }
                }
            }, CancellationToken.None))
            .ToArray();

        await Task.WhenAll(workers).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        return new ParallelModFileCopyResult(copiedFileCount, failureIssue);
    }
}

internal sealed record ParallelModFileCopyResult(
    int CopiedFileCount,
    TrayDependencyIssue? ErrorIssue);
