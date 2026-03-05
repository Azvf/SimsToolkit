using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;
using SimsModDesktop.PackageCore.Performance;

namespace SimsModDesktop.Application.Saves;

public sealed class SavePreviewCacheBuilder : ISavePreviewCacheBuilder
{
    private const string CacheSchemaVersion = "save-preview-v2";
    private static readonly HashSet<string> CachedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".trayitem",
        ".householdbinary",
        ".hhi",
        ".sgi"
    };

    private readonly ISaveHouseholdReader _saveHouseholdReader;
    private readonly IHouseholdTrayExporter _householdTrayExporter;
    private readonly ISavePreviewCacheStore _cacheStore;

    public SavePreviewCacheBuilder(
        ISaveHouseholdReader saveHouseholdReader,
        IHouseholdTrayExporter householdTrayExporter,
        ISavePreviewCacheStore cacheStore)
    {
        _saveHouseholdReader = saveHouseholdReader;
        _householdTrayExporter = householdTrayExporter;
        _cacheStore = cacheStore;
    }

    public Task<SavePreviewCacheBuildResult> BuildAsync(
        string saveFilePath,
        IProgress<SavePreviewCacheBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return BuildAsync(saveFilePath, options: null, progress, cancellationToken);
    }

    public Task<SavePreviewCacheBuildResult> BuildAsync(
        string saveFilePath,
        SavePreviewBuildOptions? options,
        IProgress<SavePreviewCacheBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var normalizedSavePath = Path.GetFullPath(saveFilePath);
            var cacheRoot = _cacheStore.GetCacheRootPath(normalizedSavePath);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var snapshot = _saveHouseholdReader.Load(normalizedSavePath);
                Directory.CreateDirectory(cacheRoot);
                ClearTrayFiles(cacheRoot);

                var buildStartedUtc = DateTime.UtcNow;
                var entries = new SavePreviewCacheHouseholdEntry[snapshot.Households.Count];
                var readyCount = 0;
                var failedCount = 0;
                var blockedCount = 0;
                var exportableCount = snapshot.Households.Count(item => item.CanExport);
                var previewNames = BuildPreviewNameLookup(snapshot.Households);
                var workerCount = PerformanceWorkerSizer.ResolveSavePreviewWorkers(options?.WorkerCount);
                var continueOnItemFailure = options?.ContinueOnItemFailure ?? true;
                var processedCount = 0;
                var indexQueue = new ConcurrentQueue<int>(Enumerable.Range(0, snapshot.Households.Count));
                var baselineWorkingSet = Process.GetCurrentProcess().WorkingSet64;
                var throttle = new PerformanceAdaptiveThrottle(
                    targetWorkers: workerCount,
                    minWorkers: 4,
                    startedAtUtc: DateTime.UtcNow);
                var allowedWorkers = workerCount;

                using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var monitorTask = Task.Run(async () =>
                {
                    while (!workerCts.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), workerCts.Token).ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }

                        var decision = throttle.Update(
                            totalCompletedCount: Interlocked.CompareExchange(ref processedCount, 0, 0),
                            nowUtc: DateTime.UtcNow,
                            workingSetBytes: Process.GetCurrentProcess().WorkingSet64,
                            baselineWorkingSetBytes: baselineWorkingSet);
                        if (!decision.Changed)
                        {
                            continue;
                        }

                        Interlocked.Exchange(ref allowedWorkers, decision.RecommendedWorkers);
                    }
                }, workerCts.Token);

                var workers = Enumerable.Range(0, workerCount)
                    .Select(workerId => Task.Run(() =>
                    {
                        while (!workerCts.IsCancellationRequested)
                        {
                            if (workerId >= Volatile.Read(ref allowedWorkers))
                            {
                                try
                                {
                                    Task.Delay(50, workerCts.Token).GetAwaiter().GetResult();
                                }
                                catch (OperationCanceledException)
                                {
                                    break;
                                }

                                continue;
                            }

                            if (!indexQueue.TryDequeue(out var index))
                            {
                                break;
                            }

                            if (workerCts.IsCancellationRequested)
                            {
                                break;
                            }
                            var household = snapshot.Households[index];
                            var previewName = previewNames.TryGetValue(household.HouseholdId, out var resolvedPreviewName)
                                ? resolvedPreviewName
                                : household.Name;

                            if (!household.CanExport)
                            {
                                Interlocked.Increment(ref blockedCount);
                                entries[index] = new SavePreviewCacheHouseholdEntry
                                {
                                    HouseholdId = household.HouseholdId,
                                    HouseholdName = previewName,
                                    HomeZoneName = household.HomeZoneName,
                                    HouseholdSize = household.HouseholdSize,
                                    BuildState = "Blocked",
                                    LastError = household.ExportBlockReason
                                };
                            }
                            else
                            {
                                var stableInstanceId = ComputeStableInstanceId(normalizedSavePath, household.HouseholdId);
                                var exportResult = _householdTrayExporter.Export(new SaveHouseholdExportRequest
                                {
                                    SourceSavePath = normalizedSavePath,
                                    HouseholdId = household.HouseholdId,
                                    ExportRootPath = cacheRoot,
                                    OutputDirectoryOverride = cacheRoot,
                                    InstanceIdOverride = stableInstanceId,
                                    MetadataNameOverride = previewName,
                                    CreatorName = "SimsModDesktop Save Preview",
                                    CreatorId = 0x53494D5350524556,
                                    GenerateThumbnails = true
                                });

                                var trayItemKey = $"0x{stableInstanceId:X16}";
                                if (!exportResult.Succeeded)
                                {
                                    Interlocked.Increment(ref failedCount);
                                    entries[index] = new SavePreviewCacheHouseholdEntry
                                    {
                                        HouseholdId = household.HouseholdId,
                                        HouseholdName = previewName,
                                        HomeZoneName = household.HomeZoneName,
                                        HouseholdSize = household.HouseholdSize,
                                        BuildState = "Failed",
                                        TrayInstanceId = exportResult.InstanceIdHex,
                                        TrayItemKey = trayItemKey,
                                        LastError = exportResult.Error ?? "Failed to build save preview cache."
                                    };

                                    if (!continueOnItemFailure)
                                    {
                                        workerCts.Cancel();
                                    }
                                }
                                else
                                {
                                    Interlocked.Increment(ref readyCount);
                                    entries[index] = new SavePreviewCacheHouseholdEntry
                                    {
                                        HouseholdId = household.HouseholdId,
                                        HouseholdName = previewName,
                                        HomeZoneName = household.HomeZoneName,
                                        HouseholdSize = household.HouseholdSize,
                                        BuildState = "Ready",
                                        TrayInstanceId = exportResult.InstanceIdHex,
                                        TrayItemKey = trayItemKey,
                                        GeneratedFileNames = exportResult.WrittenFiles
                                            .Select(Path.GetFileName)
                                            .Where(name => !string.IsNullOrWhiteSpace(name))
                                            .Cast<string>()
                                            .ToArray()
                                    };
                                }
                            }

                            var done = Interlocked.Increment(ref processedCount);
                            progress?.Report(new SavePreviewCacheBuildProgress
                            {
                                Percent = snapshot.Households.Count == 0
                                    ? 100
                                    : (int)Math.Round((done / (double)snapshot.Households.Count) * 100d),
                                Detail = $"Caching {previewName}"
                            });
                        }
                    }, CancellationToken.None))
                    .ToArray();

                Task.WhenAll(workers).GetAwaiter().GetResult();
                workerCts.Cancel();
                try
                {
                    monitorTask.GetAwaiter().GetResult();
                }
                catch (OperationCanceledException)
                {
                }

                var materializedEntries = entries
                    .Select((entry, index) => entry ?? new SavePreviewCacheHouseholdEntry
                    {
                        HouseholdId = snapshot.Households[index].HouseholdId,
                        HouseholdName = previewNames.TryGetValue(snapshot.Households[index].HouseholdId, out var fallbackName)
                            ? fallbackName
                            : snapshot.Households[index].Name,
                        HomeZoneName = snapshot.Households[index].HomeZoneName,
                        HouseholdSize = snapshot.Households[index].HouseholdSize,
                        BuildState = "Failed",
                        LastError = "Build interrupted before household was processed."
                    })
                    .ToList();

                var sourceInfo = new FileInfo(normalizedSavePath);
                var manifest = new SavePreviewCacheManifest
                {
                    SourceSavePath = normalizedSavePath,
                    SourceLength = sourceInfo.Length,
                    SourceLastWriteTimeUtc = sourceInfo.LastWriteTimeUtc,
                    CacheSchemaVersion = CacheSchemaVersion,
                    BuildStartedUtc = buildStartedUtc,
                    BuildCompletedUtc = DateTime.UtcNow,
                    TotalHouseholdCount = snapshot.Households.Count,
                    ExportableHouseholdCount = exportableCount,
                    ReadyHouseholdCount = readyCount,
                    FailedHouseholdCount = failedCount,
                    BlockedHouseholdCount = blockedCount,
                    Entries = materializedEntries
                };
                _cacheStore.Save(normalizedSavePath, manifest);
                progress?.Report(new SavePreviewCacheBuildProgress
                {
                    Percent = 100,
                    Detail = "Save preview cache ready."
                });

                return new SavePreviewCacheBuildResult
                {
                    Succeeded = readyCount > 0 || (readyCount == 0 && failedCount == 0),
                    CacheRootPath = cacheRoot,
                    Snapshot = snapshot,
                    Manifest = manifest
                };
            }
            catch (OperationCanceledException)
            {
                _cacheStore.Clear(normalizedSavePath);
                return new SavePreviewCacheBuildResult
                {
                    Succeeded = false,
                    CacheRootPath = cacheRoot
                };
            }
            catch (Exception ex)
            {
                return new SavePreviewCacheBuildResult
                {
                    Succeeded = false,
                    CacheRootPath = cacheRoot,
                    Error = ex.Message
                };
            }
        });
    }

    private static IReadOnlyDictionary<ulong, string> BuildPreviewNameLookup(IReadOnlyList<SaveHouseholdItem> households)
    {
        var preferredNames = households.ToDictionary(
            household => household.HouseholdId,
            ResolvePreferredPreviewName);
        var duplicateNames = preferredNames.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (duplicateNames.Count == 0)
        {
            return preferredNames;
        }

        var resolved = new Dictionary<ulong, string>(preferredNames.Count);
        foreach (var household in households)
        {
            var preferredName = preferredNames[household.HouseholdId];
            if (!duplicateNames.Contains(preferredName))
            {
                resolved[household.HouseholdId] = preferredName;
                continue;
            }

            var uniqueName = preferredName;
            if (!string.IsNullOrWhiteSpace(household.HomeZoneName))
            {
                uniqueName = $"{preferredName} - {household.HomeZoneName}";
            }

            resolved[household.HouseholdId] = uniqueName;
        }

        var finalDuplicates = resolved
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(entry => entry.Key))
            .ToHashSet();
        if (finalDuplicates.Count == 0)
        {
            return resolved;
        }

        foreach (var household in households.Where(item => finalDuplicates.Contains(item.HouseholdId)))
        {
            resolved[household.HouseholdId] = $"{resolved[household.HouseholdId]} [0x{household.HouseholdId:X}]";
        }

        return resolved;
    }

    private static string ResolvePreferredPreviewName(SaveHouseholdItem household)
    {
        if (household.Members.Count == 1)
        {
            var memberName = household.Members[0].FullName;
            if (!string.IsNullOrWhiteSpace(memberName))
            {
                return memberName;
            }
        }

        return string.IsNullOrWhiteSpace(household.Name)
            ? $"Household {household.HouseholdId:X}"
            : household.Name;
    }

    private static void ClearTrayFiles(string cacheRoot)
    {
        if (!Directory.Exists(cacheRoot))
        {
            return;
        }

        foreach (var path in Directory.EnumerateFiles(cacheRoot, "*", SearchOption.TopDirectoryOnly))
        {
            if (!CachedExtensions.Contains(Path.GetExtension(path)))
            {
                continue;
            }

            try
            {
                File.Delete(path);
            }
            catch
            {
            }
        }
    }

    private static ulong ComputeStableInstanceId(string savePath, ulong householdId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{savePath}|{householdId:X16}"));
        var value = BitConverter.ToUInt64(bytes, 0);
        return value == 0 ? 1UL : value;
    }
}
