using System.Security.Cryptography;
using System.Text;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;

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
                var entries = new List<SavePreviewCacheHouseholdEntry>(snapshot.Households.Count);
                var readyCount = 0;
                var failedCount = 0;
                var blockedCount = 0;
                var exportableCount = snapshot.Households.Count(item => item.CanExport);
                var previewNames = BuildPreviewNameLookup(snapshot.Households);

                for (var index = 0; index < snapshot.Households.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var household = snapshot.Households[index];
                    var previewName = previewNames.TryGetValue(household.HouseholdId, out var resolvedPreviewName)
                        ? resolvedPreviewName
                        : household.Name;
                    progress?.Report(new SavePreviewCacheBuildProgress
                    {
                        Percent = snapshot.Households.Count == 0 ? 100 : (int)Math.Round(((index + 1d) / snapshot.Households.Count) * 100d),
                        Detail = $"Caching {previewName}"
                    });

                    if (!household.CanExport)
                    {
                        blockedCount++;
                        entries.Add(new SavePreviewCacheHouseholdEntry
                        {
                            HouseholdId = household.HouseholdId,
                            HouseholdName = previewName,
                            HomeZoneName = household.HomeZoneName,
                            HouseholdSize = household.HouseholdSize,
                            BuildState = "Blocked",
                            LastError = household.ExportBlockReason
                        });
                        continue;
                    }

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
                        failedCount++;
                        entries.Add(new SavePreviewCacheHouseholdEntry
                        {
                            HouseholdId = household.HouseholdId,
                            HouseholdName = previewName,
                            HomeZoneName = household.HomeZoneName,
                            HouseholdSize = household.HouseholdSize,
                            BuildState = "Failed",
                            TrayInstanceId = exportResult.InstanceIdHex,
                            TrayItemKey = trayItemKey,
                            LastError = exportResult.Error ?? "Failed to build save preview cache."
                        });
                        continue;
                    }

                    readyCount++;
                    entries.Add(new SavePreviewCacheHouseholdEntry
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
                    });
                }

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
                    Entries = entries
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
