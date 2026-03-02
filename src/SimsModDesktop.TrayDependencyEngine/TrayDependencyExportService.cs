using System.Buffers.Binary;
using System.Text.Json;
using Dapper;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.TrayDependencyEngine;

public sealed class TrayDependencyExportService : ITrayDependencyExportService
{
    private readonly IPackageIndexCache _packageIndexCache;
    private readonly TrayBundleLoader _bundleLoader = new();
    private readonly TraySearchExtractor _searchExtractor = new();
    private readonly DirectMatchEngine _directMatchEngine = new();
    private readonly DependencyExpandEngine _dependencyExpandEngine;
    private readonly ModFileExporter _fileExporter = new();

    public TrayDependencyExportService(
        IPackageIndexCache packageIndexCache,
        IDbpfResourceReader? resourceReader = null)
    {
        _packageIndexCache = packageIndexCache;
        _dependencyExpandEngine = new DependencyExpandEngine(resourceReader ?? new DbpfResourceReader());
    }

    public Task<TrayDependencyExportResult> ExportAsync(
        TrayDependencyExportRequest request,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Task.Run(async () =>
        {
            var issues = new List<TrayDependencyIssue>();
            Report(progress, TrayDependencyExportStage.Preparing, 0, "Copying tray files...");

            Directory.CreateDirectory(request.TrayExportRoot);
            Directory.CreateDirectory(request.ModsExportRoot);

            if (!TryCopyTrayFiles(request.TraySourceFiles, request.TrayExportRoot, issues, out var copiedTrayFileCount))
            {
                return BuildResult(copiedTrayFileCount, 0, issues);
            }

            var snapshot = await _packageIndexCache.GetSnapshotAsync(request.ModsRootPath, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.ParsingTray, 35, "Parsing tray files...");

            if (!_bundleLoader.TryLoad(request.TraySourceFiles, issues, out var bundle))
            {
                return BuildResult(copiedTrayFileCount, 0, issues);
            }

            var searchKeys = _searchExtractor.Extract(bundle, issues);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.MatchingDirectReferences, 45, "Matching direct references...");

            var directMatch = _directMatchEngine.Match(searchKeys, snapshot);
            issues.AddRange(directMatch.Issues);

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.ExpandingDependencies, 65, "Expanding second-level references...");

            var expansion = _dependencyExpandEngine.Expand(
                directMatch,
                snapshot,
                cancellationToken);
            issues.AddRange(expansion.Issues);

            var filePaths = directMatch.DirectMatches
                .Concat(expansion.ExpandedMatches)
                .Select(match => match.FilePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            cancellationToken.ThrowIfCancellationRequested();
            Report(progress, TrayDependencyExportStage.CopyingMods, 85, $"Copying referenced mods ({filePaths.Length})...");

            _fileExporter.CopyFiles(filePaths, request.ModsExportRoot, issues, out var copiedModFileCount);

            Report(progress, TrayDependencyExportStage.Completed, 100, "Completed.");
            return BuildResult(copiedTrayFileCount, copiedModFileCount, issues);
        }, cancellationToken);
    }

    private static bool TryCopyTrayFiles(
        IReadOnlyList<string> traySourceFiles,
        string trayExportRoot,
        List<TrayDependencyIssue> issues,
        out int copiedFileCount)
    {
        copiedFileCount = 0;
        foreach (var sourcePath in traySourceFiles
                     .Where(path => !string.IsNullOrWhiteSpace(path))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    issues.Add(new TrayDependencyIssue
                    {
                        Severity = TrayDependencyIssueSeverity.Error,
                        Kind = TrayDependencyIssueKind.CopyError,
                        FilePath = sourcePath,
                        Message = "Tray source file is missing."
                    });
                    return false;
                }

                var targetPath = Path.Combine(trayExportRoot, Path.GetFileName(sourcePath));
                targetPath = FileNameHelpers.GetUniquePath(targetPath);
                File.Copy(sourcePath, targetPath, overwrite: false);
                copiedFileCount++;
            }
            catch (Exception ex)
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Error,
                    Kind = TrayDependencyIssueKind.CopyError,
                    FilePath = sourcePath,
                    Message = $"Failed to copy tray file: {ex.Message}"
                });
                return false;
            }
        }

        return true;
    }

    private static TrayDependencyExportResult BuildResult(
        int copiedTrayFileCount,
        int copiedModFileCount,
        List<TrayDependencyIssue> issues)
    {
        var hasErrors = issues.Any(issue => issue.Severity == TrayDependencyIssueSeverity.Error);
        var hasMissingWarnings = issues.Any(issue =>
            issue.Severity == TrayDependencyIssueSeverity.Warning &&
            (issue.Kind == TrayDependencyIssueKind.MissingReference || issue.Kind == TrayDependencyIssueKind.MissingSourceFile));

        return new TrayDependencyExportResult
        {
            Success = !hasErrors,
            CopiedTrayFileCount = copiedTrayFileCount,
            CopiedModFileCount = copiedModFileCount,
            HasMissingReferenceWarnings = hasMissingWarnings,
            Issues = issues.ToArray()
        };
    }

    private static void Report(
        IProgress<TrayDependencyExportProgress>? progress,
        TrayDependencyExportStage stage,
        int percent,
        string detail)
    {
        progress?.Report(new TrayDependencyExportProgress
        {
            Stage = stage,
            Percent = Math.Clamp(percent, 0, 100),
            Detail = detail
        });
    }
}

public sealed class PackageIndexCache : IPackageIndexCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly object _sync = new();
    private readonly Dictionary<string, CacheState> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly IDbpfPackageCatalog _packageCatalog;
    private readonly SqliteCacheDatabase _database;

    public PackageIndexCache(IDbpfPackageCatalog? packageCatalog = null)
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache",
                "TrayDependencyPackageIndex"),
            packageCatalog)
    {
    }

    public PackageIndexCache(string cacheRootPath, IDbpfPackageCatalog? packageCatalog = null)
    {
        _packageCatalog = packageCatalog ?? new DbpfPackageCatalog();
        _database = new SqliteCacheDatabase(Path.Combine(cacheRootPath, "cache.db"));
    }

    public async Task<PackageIndexSnapshot> GetSnapshotAsync(
        string modsRootPath,
        IProgress<TrayDependencyExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(modsRootPath))
        {
            throw new ArgumentException("Mods root path is required.", nameof(modsRootPath));
        }

        var normalizedRoot = Path.GetFullPath(modsRootPath.Trim());
        var currentPackages = Directory.Exists(normalizedRoot)
            ? Directory.EnumerateFiles(normalizedRoot, "*.package", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path =>
                {
                    var info = new FileInfo(path);
                    return new PackageStamp(path, info.Length, info.LastWriteTimeUtc);
                })
                .ToArray()
            : Array.Empty<PackageStamp>();

        CacheState? existingState;
        lock (_sync)
        {
            _states.TryGetValue(normalizedRoot, out existingState);
        }

        if (existingState is not null && HasSameStamps(existingState.PackageStamps, currentPackages))
        {
            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.IndexingPackages,
                Percent = 35,
                Detail = $"Indexing packages... {currentPackages.Length}/{currentPackages.Length}"
            });
            return existingState.Snapshot;
        }

        var persistedState = TryLoadPersistedState(normalizedRoot);
        if (persistedState is not null && HasSameStamps(persistedState.PackageStamps, currentPackages))
        {
            lock (_sync)
            {
                _states[normalizedRoot] = persistedState;
            }

            progress?.Report(new TrayDependencyExportProgress
            {
                Stage = TrayDependencyExportStage.IndexingPackages,
                Percent = 35,
                Detail = $"Indexing packages... {currentPackages.Length}/{currentPackages.Length}"
            });
            return persistedState.Snapshot;
        }

        progress?.Report(new TrayDependencyExportProgress
        {
            Stage = TrayDependencyExportStage.IndexingPackages,
            Percent = 5,
            Detail = "Indexing packages..."
        });

        var catalog = await _packageCatalog.BuildSnapshotAsync(
            normalizedRoot,
            new DbpfCatalogBuildOptions
            {
                SupportedInstanceTypes = KnownResourceTypes.Supported
            },
            cancellationToken);

        var packages = catalog.Packages
            .OrderBy(package => package.FilePath, StringComparer.OrdinalIgnoreCase)
            .Select(MapPackage)
            .ToArray();
        var packageMap = packages.ToDictionary(package => package.FilePath, StringComparer.OrdinalIgnoreCase);

        progress?.Report(new TrayDependencyExportProgress
        {
            Stage = TrayDependencyExportStage.IndexingPackages,
            Percent = 35,
            Detail = $"Indexing packages... {packages.Length}/{packages.Length}"
        });

        var snapshot = new PackageIndexSnapshot
        {
            ModsRootPath = normalizedRoot,
            Packages = packages,
            ExactIndex = MapIndex(catalog.ExactIndex, packageMap),
            TypeInstanceIndex = MapIndex(catalog.TypeInstanceIndex, packageMap),
            SupportedInstanceIndex = MapIndex(catalog.SupportedInstanceIndex, packageMap)
        };

        lock (_sync)
        {
            _states[normalizedRoot] = new CacheState(snapshot, currentPackages);
        }

        PersistState(normalizedRoot, snapshot, currentPackages);
        return snapshot;
    }

    private static IndexedPackageFile MapPackage(DbpfPackageIndex package)
    {
        var entries = new PackageIndexEntry[package.Entries.Length];
        for (var i = 0; i < package.Entries.Length; i++)
        {
            entries[i] = MapEntry(package.Entries[i]);
        }

        var typeIndexes = new Dictionary<uint, PackageTypeIndex>(package.TypeBuckets.Count);
        foreach (var pair in package.TypeBuckets)
        {
            typeIndexes[pair.Key] = new PackageTypeIndex
            {
                InstanceToEntryIndexes = pair.Value.InstanceToEntryIndexes
            };
        }

        return new IndexedPackageFile
        {
            FilePath = package.FilePath,
            Length = package.Fingerprint.Length,
            LastWriteTimeUtc = new DateTime(package.Fingerprint.LastWriteUtcTicks, DateTimeKind.Utc),
            Entries = entries,
            TypeIndexes = typeIndexes
        };
    }

    private static PackageIndexEntry MapEntry(DbpfIndexEntry entry)
    {
        return new PackageIndexEntry
        {
            Type = entry.Type,
            Group = entry.Group,
            Instance = entry.Instance,
            IsDeleted = entry.IsDeleted,
            DataOffset = entry.DataOffset,
            CompressedSize = entry.CompressedSize,
            UncompressedSize = entry.UncompressedSize,
            CompressionType = entry.CompressionType
        };
    }

    private static Dictionary<TrayResourceKey, ResolvedResourceRef[]> MapIndex(
        IReadOnlyDictionary<DbpfResourceKey, ResourceLocation[]> source,
        IReadOnlyDictionary<string, IndexedPackageFile> packageMap)
    {
        var result = new Dictionary<TrayResourceKey, ResolvedResourceRef[]>(source.Count);
        foreach (var pair in source)
        {
            result[new TrayResourceKey(pair.Key.Type, pair.Key.Group, pair.Key.Instance)] =
                pair.Value.Select(location => MapLocation(location, packageMap)).ToArray();
        }

        return result;
    }

    private static Dictionary<TypeInstanceKey, ResolvedResourceRef[]> MapIndex(
        IReadOnlyDictionary<TypeInstanceKey, ResourceLocation[]> source,
        IReadOnlyDictionary<string, IndexedPackageFile> packageMap)
    {
        var result = new Dictionary<TypeInstanceKey, ResolvedResourceRef[]>(source.Count);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value.Select(location => MapLocation(location, packageMap)).ToArray();
        }

        return result;
    }

    private static Dictionary<ulong, ResolvedResourceRef[]> MapIndex(
        IReadOnlyDictionary<ulong, ResourceLocation[]> source,
        IReadOnlyDictionary<string, IndexedPackageFile> packageMap)
    {
        var result = new Dictionary<ulong, ResolvedResourceRef[]>(source.Count);
        foreach (var pair in source)
        {
            result[pair.Key] = pair.Value.Select(location => MapLocation(location, packageMap)).ToArray();
        }

        return result;
    }

    private static ResolvedResourceRef MapLocation(
        ResourceLocation location,
        IReadOnlyDictionary<string, IndexedPackageFile> packageMap)
    {
        var package = packageMap[location.FilePath];
        return new ResolvedResourceRef
        {
            Key = new TrayResourceKey(location.Entry.Type, location.Entry.Group, location.Entry.Instance),
            FilePath = location.FilePath,
            Entry = package.Entries[location.EntryIndex]
        };
    }

    private static bool HasSameStamps(
        IReadOnlyList<PackageStamp> previous,
        IReadOnlyList<PackageStamp> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        for (var i = 0; i < current.Count; i++)
        {
            if (!string.Equals(previous[i].FilePath, current[i].FilePath, StringComparison.OrdinalIgnoreCase) ||
                previous[i].Length != current[i].Length ||
                previous[i].LastWriteTimeUtc != current[i].LastWriteTimeUtc)
            {
                return false;
            }
        }

        return true;
    }

    private CacheState? TryLoadPersistedState(string normalizedRoot)
    {
        try
        {
            using var connection = OpenConnection();
            var row = connection.QuerySingleOrDefault<PersistedCacheRow>(
                """
                SELECT
                    ModsRootPath,
                    PackageStampsJson,
                    SnapshotJson
                FROM PackageIndexCache
                WHERE ModsRootPath = @ModsRootPath;
                """,
                new { ModsRootPath = normalizedRoot });
            if (row is null)
            {
                return null;
            }

            var stamps = JsonSerializer.Deserialize<List<PackageStamp>>(row.PackageStampsJson, JsonOptions);
            var persisted = JsonSerializer.Deserialize<PersistedSnapshot>(row.SnapshotJson, JsonOptions);
            if (stamps is null || persisted is null)
            {
                return null;
            }

            return new CacheState(FromPersistedSnapshot(persisted), stamps);
        }
        catch
        {
            return null;
        }
    }

    private void PersistState(string normalizedRoot, PackageIndexSnapshot snapshot, IReadOnlyList<PackageStamp> stamps)
    {
        try
        {
            using var connection = OpenConnection();
            connection.Execute(
                """
                INSERT INTO PackageIndexCache (
                    ModsRootPath,
                    PackageStampsJson,
                    SnapshotJson,
                    UpdatedUtcTicks
                )
                VALUES (
                    @ModsRootPath,
                    @PackageStampsJson,
                    @SnapshotJson,
                    @UpdatedUtcTicks
                )
                ON CONFLICT(ModsRootPath) DO UPDATE SET
                    PackageStampsJson = excluded.PackageStampsJson,
                    SnapshotJson = excluded.SnapshotJson,
                    UpdatedUtcTicks = excluded.UpdatedUtcTicks;
                """,
                new PersistedCacheRow
                {
                    ModsRootPath = normalizedRoot,
                    PackageStampsJson = JsonSerializer.Serialize(stamps, JsonOptions),
                    SnapshotJson = JsonSerializer.Serialize(ToPersistedSnapshot(snapshot), JsonOptions),
                    UpdatedUtcTicks = DateTime.UtcNow.Ticks
                });
        }
        catch
        {
        }
    }

    private System.Data.IDbConnection OpenConnection()
    {
        var connection = _database.OpenConnection();
        connection.Execute(
            """
            CREATE TABLE IF NOT EXISTS PackageIndexCache (
                ModsRootPath TEXT PRIMARY KEY,
                PackageStampsJson TEXT NOT NULL,
                SnapshotJson TEXT NOT NULL,
                UpdatedUtcTicks INTEGER NOT NULL
            );
            """);
        return connection;
    }

    private static PersistedSnapshot ToPersistedSnapshot(PackageIndexSnapshot snapshot)
    {
        return new PersistedSnapshot
        {
            ModsRootPath = snapshot.ModsRootPath,
            Packages = snapshot.Packages.Select(package => new PersistedPackage
            {
                FilePath = package.FilePath,
                Length = package.Length,
                LastWriteUtcTicks = package.LastWriteTimeUtc.ToUniversalTime().Ticks,
                Entries = package.Entries.Select(entry => new PersistedPackageEntry
                {
                    Type = entry.Type,
                    Group = entry.Group,
                    Instance = entry.Instance,
                    IsDeleted = entry.IsDeleted,
                    DataOffset = entry.DataOffset,
                    CompressedSize = entry.CompressedSize,
                    UncompressedSize = entry.UncompressedSize,
                    CompressionType = entry.CompressionType
                }).ToList(),
                TypeIndexes = package.TypeIndexes.Select(pair => new PersistedPackageTypeIndex
                {
                    Type = pair.Key,
                    InstanceToEntryIndexes = pair.Value.InstanceToEntryIndexes
                        .Select(indexPair => new PersistedInstanceEntryIndexes
                        {
                            Instance = indexPair.Key,
                            EntryIndexes = indexPair.Value
                        })
                        .ToList()
                }).ToList()
            }).ToList(),
            ExactIndex = snapshot.ExactIndex.Select(pair => new PersistedExactIndex
            {
                Type = pair.Key.Type,
                Group = pair.Key.Group,
                Instance = pair.Key.Instance,
                Refs = pair.Value.Select(ToPersistedResolvedRef).ToList()
            }).ToList(),
            TypeInstanceIndex = snapshot.TypeInstanceIndex.Select(pair => new PersistedTypeInstanceIndex
            {
                Type = pair.Key.Type,
                Instance = pair.Key.Instance,
                Refs = pair.Value.Select(ToPersistedResolvedRef).ToList()
            }).ToList(),
            SupportedInstanceIndex = snapshot.SupportedInstanceIndex.Select(pair => new PersistedSupportedInstanceIndex
            {
                Instance = pair.Key,
                Refs = pair.Value.Select(ToPersistedResolvedRef).ToList()
            }).ToList()
        };
    }

    private static PackageIndexSnapshot FromPersistedSnapshot(PersistedSnapshot persisted)
    {
        var packages = persisted.Packages.Select(package => new IndexedPackageFile
        {
            FilePath = package.FilePath,
            Length = package.Length,
            LastWriteTimeUtc = new DateTime(package.LastWriteUtcTicks, DateTimeKind.Utc),
            Entries = package.Entries.Select(entry => new PackageIndexEntry
            {
                Type = entry.Type,
                Group = entry.Group,
                Instance = entry.Instance,
                IsDeleted = entry.IsDeleted,
                DataOffset = entry.DataOffset,
                CompressedSize = entry.CompressedSize,
                UncompressedSize = entry.UncompressedSize,
                CompressionType = entry.CompressionType
            }).ToArray(),
            TypeIndexes = package.TypeIndexes.ToDictionary(
                pair => pair.Type,
                pair => new PackageTypeIndex
                {
                    InstanceToEntryIndexes = pair.InstanceToEntryIndexes.ToDictionary(
                        indexPair => indexPair.Instance,
                        indexPair => indexPair.EntryIndexes)
                })
        }).ToArray();

        return new PackageIndexSnapshot
        {
            ModsRootPath = persisted.ModsRootPath,
            Packages = packages,
            ExactIndex = persisted.ExactIndex.ToDictionary(
                row => new TrayResourceKey(row.Type, row.Group, row.Instance),
                row => row.Refs.Select(FromPersistedResolvedRef).ToArray()),
            TypeInstanceIndex = persisted.TypeInstanceIndex.ToDictionary(
                row => new TypeInstanceKey(row.Type, row.Instance),
                row => row.Refs.Select(FromPersistedResolvedRef).ToArray()),
            SupportedInstanceIndex = persisted.SupportedInstanceIndex.ToDictionary(
                row => row.Instance,
                row => row.Refs.Select(FromPersistedResolvedRef).ToArray())
        };
    }

    private static PersistedResolvedRef ToPersistedResolvedRef(ResolvedResourceRef item)
    {
        return new PersistedResolvedRef
        {
            KeyType = item.Key.Type,
            KeyGroup = item.Key.Group,
            KeyInstance = item.Key.Instance,
            FilePath = item.FilePath,
            Entry = item.Entry is null
                ? null
                : new PersistedPackageEntry
                {
                    Type = item.Entry.Type,
                    Group = item.Entry.Group,
                    Instance = item.Entry.Instance,
                    IsDeleted = item.Entry.IsDeleted,
                    DataOffset = item.Entry.DataOffset,
                    CompressedSize = item.Entry.CompressedSize,
                    UncompressedSize = item.Entry.UncompressedSize,
                    CompressionType = item.Entry.CompressionType
                }
        };
    }

    private static ResolvedResourceRef FromPersistedResolvedRef(PersistedResolvedRef item)
    {
        return new ResolvedResourceRef
        {
            Key = new TrayResourceKey(item.KeyType, item.KeyGroup, item.KeyInstance),
            FilePath = item.FilePath,
            Entry = item.Entry is null
                ? null
                : new PackageIndexEntry
                {
                    Type = item.Entry.Type,
                    Group = item.Entry.Group,
                    Instance = item.Entry.Instance,
                    IsDeleted = item.Entry.IsDeleted,
                    DataOffset = item.Entry.DataOffset,
                    CompressedSize = item.Entry.CompressedSize,
                    UncompressedSize = item.Entry.UncompressedSize,
                    CompressionType = item.Entry.CompressionType
                }
        };
    }

    private sealed record CacheState(
        PackageIndexSnapshot Snapshot,
        IReadOnlyList<PackageStamp> PackageStamps);

    private sealed record PackageStamp(
        string FilePath,
        long Length,
        DateTime LastWriteTimeUtc);

    private sealed class PersistedCacheRow
    {
        public string ModsRootPath { get; set; } = string.Empty;
        public string PackageStampsJson { get; set; } = "[]";
        public string SnapshotJson { get; set; } = "{}";
        public long UpdatedUtcTicks { get; set; }
    }

    private sealed class PersistedSnapshot
    {
        public string ModsRootPath { get; set; } = string.Empty;
        public List<PersistedPackage> Packages { get; set; } = [];
        public List<PersistedExactIndex> ExactIndex { get; set; } = [];
        public List<PersistedTypeInstanceIndex> TypeInstanceIndex { get; set; } = [];
        public List<PersistedSupportedInstanceIndex> SupportedInstanceIndex { get; set; } = [];
    }

    private sealed class PersistedPackage
    {
        public string FilePath { get; set; } = string.Empty;
        public long Length { get; set; }
        public long LastWriteUtcTicks { get; set; }
        public List<PersistedPackageEntry> Entries { get; set; } = [];
        public List<PersistedPackageTypeIndex> TypeIndexes { get; set; } = [];
    }

    private sealed class PersistedPackageEntry
    {
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public bool IsDeleted { get; set; }
        public long DataOffset { get; set; }
        public int CompressedSize { get; set; }
        public int UncompressedSize { get; set; }
        public ushort CompressionType { get; set; }
    }

    private sealed class PersistedPackageTypeIndex
    {
        public uint Type { get; set; }
        public List<PersistedInstanceEntryIndexes> InstanceToEntryIndexes { get; set; } = [];
    }

    private sealed class PersistedInstanceEntryIndexes
    {
        public ulong Instance { get; set; }
        public int[] EntryIndexes { get; set; } = Array.Empty<int>();
    }

    private sealed class PersistedExactIndex
    {
        public uint Type { get; set; }
        public uint Group { get; set; }
        public ulong Instance { get; set; }
        public List<PersistedResolvedRef> Refs { get; set; } = [];
    }

    private sealed class PersistedTypeInstanceIndex
    {
        public uint Type { get; set; }
        public ulong Instance { get; set; }
        public List<PersistedResolvedRef> Refs { get; set; } = [];
    }

    private sealed class PersistedSupportedInstanceIndex
    {
        public ulong Instance { get; set; }
        public List<PersistedResolvedRef> Refs { get; set; } = [];
    }

    private sealed class PersistedResolvedRef
    {
        public uint KeyType { get; set; }
        public uint KeyGroup { get; set; }
        public ulong KeyInstance { get; set; }
        public string FilePath { get; set; } = string.Empty;
        public PersistedPackageEntry? Entry { get; set; }
    }
}

internal static class ProgressScale
{
    public static int Scale(int start, int end, int current, int total)
    {
        if (total <= 0)
        {
            return end;
        }

        var clamped = Math.Clamp(current, 0, total);
        var value = start + ((end - start) * (double)clamped / total);
        return (int)Math.Round(value, MidpointRounding.AwayFromZero);
    }
}

internal sealed class TrayBundleLoader
{
    public bool TryLoad(
        IReadOnlyList<string> sourceFilePaths,
        List<TrayDependencyIssue> issues,
        out TrayFileBundle bundle)
    {
        var trayItems = sourceFilePaths
            .Where(path => string.Equals(Path.GetExtension(path), ".trayitem", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (trayItems.Length != 1)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                Message = ".trayitem file is missing or duplicated."
            });
            bundle = new TrayFileBundle();
            return false;
        }

        bundle = new TrayFileBundle
        {
            TrayItemPath = trayItems[0],
            HhiPath = sourceFilePaths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".hhi", StringComparison.OrdinalIgnoreCase)),
            SgiPaths = sourceFilePaths
                .Where(path => string.Equals(Path.GetExtension(path), ".sgi", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            HouseholdBinaryPath = sourceFilePaths.FirstOrDefault(path => string.Equals(Path.GetExtension(path), ".householdbinary", StringComparison.OrdinalIgnoreCase)),
            BlueprintPath = sourceFilePaths.FirstOrDefault(path =>
                string.Equals(Path.GetExtension(path), ".blueprint", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".bpi", StringComparison.OrdinalIgnoreCase)),
            RoomPath = sourceFilePaths.FirstOrDefault(path =>
                string.Equals(Path.GetExtension(path), ".room", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".rmi", StringComparison.OrdinalIgnoreCase))
        };
        return true;
    }
}

internal sealed class TraySearchExtractor
{
    public TraySearchKeys Extract(TrayFileBundle bundle, List<TrayDependencyIssue> issues)
    {
        var householdIds = new HashSet<ulong>();
        var buildIds = new HashSet<ulong>();
        var resourceKeys = new HashSet<TrayResourceKey>();

        ScanFile(bundle.TrayItemPath, issues, householdIds, resourceKeys);
        ScanFile(bundle.HhiPath, issues, householdIds, resourceKeys);

        foreach (var sgiPath in bundle.SgiPaths)
        {
            ScanFile(sgiPath, issues, householdIds, resourceKeys);
        }

        ScanFile(bundle.HouseholdBinaryPath, issues, householdIds, resourceKeys);
        ScanFile(bundle.BlueprintPath, issues, buildIds, resourceKeys);
        ScanFile(bundle.RoomPath, issues, buildIds, resourceKeys);

        var householdArray = householdIds.OrderBy(value => value).ToArray();
        var buildArray = buildIds.OrderBy(value => value).ToArray();
        var resourceKeyArray = resourceKeys
            .OrderBy(key => key.Type)
            .ThenBy(key => key.Group)
            .ThenBy(key => key.Instance)
            .ToArray();

        if (householdArray.Length == 0 && buildArray.Length == 0 && resourceKeyArray.Length == 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = "No candidate references were detected in tray files."
            });
        }

        return new TraySearchKeys
        {
            CasPartIds = householdArray,
            SkinToneIds = householdArray,
            SimAspirationIds = householdArray,
            SimTraitIds = householdArray,
            CasPresetIds = householdArray,
            FaceSliderIds = householdArray,
            BodySliderIds = householdArray,
            ObjectDefinitionIds = buildArray.Length == 0 ? householdArray : buildArray,
            ResourceKeys = resourceKeyArray,
            LotTraitIds = buildArray
        };
    }

    private static void ScanFile(
        string? path,
        List<TrayDependencyIssue> issues,
        HashSet<ulong> ids,
        HashSet<TrayResourceKey> resourceKeys)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (!File.Exists(path))
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Warning,
                    Kind = TrayDependencyIssueKind.MissingSourceFile,
                    FilePath = path,
                    Message = "Tray file disappeared before parsing."
                });
                return;
            }

            var data = File.ReadAllBytes(path);
            BinaryReferenceScanner.Scan(data, ids, resourceKeys);
        }
        catch (Exception ex)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Error,
                Kind = TrayDependencyIssueKind.TrayParseError,
                FilePath = path,
                Message = $"Failed to parse tray file: {ex.Message}"
            });
        }
    }
}

internal static class BinaryReferenceScanner
{
    public static void Scan(
        ReadOnlySpan<byte> data,
        HashSet<ulong> ids,
        HashSet<TrayResourceKey> resourceKeys)
    {
        if (data.IsEmpty)
        {
            return;
        }

        ScanResourceKeys(data, resourceKeys);
        ParseMessage(data, 0, ids, resourceKeys);

        if (ids.Count == 0)
        {
            for (var i = 0; i <= data.Length - 8; i += 4)
            {
                var value = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(i, 8));
                if (LooksLikeResourceId(value))
                {
                    ids.Add(value);
                }
            }
        }
    }

    private static void ParseMessage(
        ReadOnlySpan<byte> data,
        int depth,
        HashSet<ulong> ids,
        HashSet<TrayResourceKey> resourceKeys)
    {
        if (depth > 4 || data.Length == 0)
        {
            return;
        }

        var position = 0;
        while (position < data.Length)
        {
            var start = position;
            if (!TryReadVarint(data, ref position, out var key))
            {
                break;
            }

            var wireType = (int)(key & 0x7);
            switch (wireType)
            {
                case 0:
                    if (!TryReadVarint(data, ref position, out var varintValue))
                    {
                        position = start + 1;
                        continue;
                    }

                    if (LooksLikeResourceId(varintValue))
                    {
                        ids.Add(varintValue);
                    }
                    break;
                case 1:
                    if (position + 8 > data.Length)
                    {
                        position = data.Length;
                        break;
                    }

                    var fixed64 = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(position, 8));
                    position += 8;
                    if (LooksLikeResourceId(fixed64))
                    {
                        ids.Add(fixed64);
                    }
                    break;
                case 2:
                    if (!TryReadVarint(data, ref position, out var lengthValue))
                    {
                        position = start + 1;
                        continue;
                    }

                    if (lengthValue > int.MaxValue || position + (int)lengthValue > data.Length)
                    {
                        position = start + 1;
                        continue;
                    }

                    var slice = data.Slice(position, (int)lengthValue);
                    position += (int)lengthValue;
                    ScanResourceKeys(slice, resourceKeys);
                    ParseMessage(slice, depth + 1, ids, resourceKeys);
                    break;
                case 5:
                    if (position + 4 > data.Length)
                    {
                        position = data.Length;
                        break;
                    }

                    var fixed32 = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(position, 4));
                    position += 4;
                    if (LooksLikeResourceId(fixed32))
                    {
                        ids.Add(fixed32);
                    }
                    break;
                default:
                    position = start + 1;
                    break;
            }
        }
    }

    private static void ScanResourceKeys(ReadOnlySpan<byte> data, HashSet<TrayResourceKey> resourceKeys)
    {
        for (var offset = 0; offset <= data.Length - 16; offset += 4)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            if (!KnownResourceTypes.IsSupported(type))
            {
                continue;
            }

            var group = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var instance = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8, 8));
            if (!LooksLikeResourceId(instance))
            {
                continue;
            }

            resourceKeys.Add(new TrayResourceKey(type, group, instance));
        }
    }

    private static bool TryReadVarint(ReadOnlySpan<byte> data, ref int position, out ulong value)
    {
        value = 0;
        var shift = 0;
        while (position < data.Length && shift < 64)
        {
            var b = data[position++];
            value |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return true;
            }

            shift += 7;
        }

        value = 0;
        return false;
    }

    internal static bool LooksLikeResourceId(ulong value)
    {
        if (value == 0 || value == ulong.MaxValue)
        {
            return false;
        }

        return value > 1024;
    }
}

internal static class KnownResourceTypes
{
    public const uint CasPart = 55242443u;
    public const uint SkinTone = 55867754u;
    public const uint Data = 1415235194u;
    public const uint CasPreset = 2635774068u;
    public const uint SliderModifier = 3321263678u;
    public const uint ObjectCatalog = 832458525u;
    public const uint ObjectDefinition = 3235601127u;
    public const uint TextureCompositor = 3066607264u;
    public const uint ImageResource = 877907861u;
    public const uint MaterialDefinition = 734023391u;

    public const uint AspirationGroup = 2161773u;
    public const uint SimTraitGroup = 6282508u;
    public const uint LotTraitGroup = 1935269u;

    public static readonly uint[] Supported =
    [
        CasPart,
        SkinTone,
        Data,
        CasPreset,
        SliderModifier,
        ObjectCatalog,
        ObjectDefinition
    ];

    public static readonly uint[] SkinToneOverlayTypes =
    [
        TextureCompositor,
        ImageResource
    ];

    public static readonly uint[] SkinToneMaterialTypes =
    [
        ImageResource,
        MaterialDefinition
    ];

    public static readonly uint[] SkinToneBumpMapTypes =
    [
        ImageResource
    ];

    public static readonly uint[] StructuredReferenceTypes =
    [
        CasPart,
        SkinTone,
        Data,
        CasPreset,
        SliderModifier,
        ObjectCatalog,
        ObjectDefinition,
        TextureCompositor,
        ImageResource,
        MaterialDefinition
    ];

    public static bool IsSupported(uint type)
    {
        for (var i = 0; i < Supported.Length; i++)
        {
            if (Supported[i] == type)
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsStructuredReferenceType(uint type)
    {
        for (var i = 0; i < StructuredReferenceTypes.Length; i++)
        {
            if (StructuredReferenceTypes[i] == type)
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class ResourceDependencyExtraction
{
    public HashSet<TrayResourceKey> ExactKeys { get; } = [];

    public List<TypedInstanceDependency> TypedInstances { get; } = [];

    public HashSet<ulong> FallbackIds { get; } = [];

    public bool HasAny =>
        ExactKeys.Count > 0 ||
        TypedInstances.Count > 0 ||
        FallbackIds.Count > 0;
}

internal readonly record struct TypedInstanceDependency(ulong Instance, uint[] AllowedTypes);

internal static class StructuredDependencyReaders
{
    public static ResourceDependencyExtraction Read(TrayResourceKey key, ReadOnlySpan<byte> data)
    {
        var extraction = new ResourceDependencyExtraction();
        switch (key.Type)
        {
            case KnownResourceTypes.CasPart:
                CasPartDependencyReader.Read(data, extraction);
                break;
            case KnownResourceTypes.SkinTone:
                SkinToneDependencyReader.Read(data, extraction);
                break;
            case KnownResourceTypes.ObjectDefinition:
            case KnownResourceTypes.ObjectCatalog:
                ObjectDefinitionDependencyReader.Read(data, extraction);
                break;
            default:
                break;
        }

        return extraction;
    }
}

internal static class CasPartDependencyReader
{
    public static void Read(ReadOnlySpan<byte> data, ResourceDependencyExtraction extraction)
    {
        StructuredDependencyReaderUtilities.ExtractStructuredKeys(data, extraction.ExactKeys);
    }
}

internal static class SkinToneDependencyReader
{
    public static void Read(ReadOnlySpan<byte> data, ResourceDependencyExtraction extraction)
    {
        var seen = new HashSet<ulong>();
        StructuredDependencyReaderUtilities.CollectAlignedIds(data, seen);
        foreach (var id in seen)
        {
            extraction.TypedInstances.Add(new TypedInstanceDependency(id, KnownResourceTypes.SkinToneOverlayTypes));
            extraction.TypedInstances.Add(new TypedInstanceDependency(id, KnownResourceTypes.SkinToneMaterialTypes));
            extraction.TypedInstances.Add(new TypedInstanceDependency(id, KnownResourceTypes.SkinToneBumpMapTypes));
        }
    }
}

internal static class ObjectDefinitionDependencyReader
{
    public static void Read(ReadOnlySpan<byte> data, ResourceDependencyExtraction extraction)
    {
        StructuredDependencyReaderUtilities.ExtractStructuredKeys(data, extraction.ExactKeys);
    }
}

internal static class StructuredDependencyReaderUtilities
{
    public static void CollectAlignedIds(ReadOnlySpan<byte> data, HashSet<ulong> ids)
    {
        for (var offset = 0; offset <= data.Length - 8; offset += 8)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8));
            if (!BinaryReferenceScanner.LooksLikeResourceId(value))
            {
                continue;
            }

            ids.Add(value);
        }
    }

    public static void ExtractStructuredKeys(ReadOnlySpan<byte> data, HashSet<TrayResourceKey> keys)
    {
        for (var offset = 0; offset <= data.Length - 16; offset += 4)
        {
            var type = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4));
            if (!KnownResourceTypes.IsStructuredReferenceType(type))
            {
                continue;
            }

            var group = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4));
            var instance = BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset + 8, 8));
            if (!BinaryReferenceScanner.LooksLikeResourceId(instance))
            {
                continue;
            }

            keys.Add(new TrayResourceKey(type, group, instance));
        }
    }
}

internal sealed class DirectMatchEngine
{
    public DirectMatchResult Match(TraySearchKeys keys, PackageIndexSnapshot snapshot)
    {
        var issues = new List<TrayDependencyIssue>();
        var results = new Dictionary<TrayResourceKey, ResolvedResourceRef>();
        var candidateInstances = GetCandidateInstances(keys);
        var matchedInstances = new HashSet<ulong>();
        var unmatchedExactKeys = new HashSet<TrayResourceKey>(keys.ResourceKeys);

        foreach (var resourceKey in keys.ResourceKeys)
        {
            if (!snapshot.ExactIndex.TryGetValue(resourceKey, out var matches) || matches.Length == 0)
            {
                continue;
            }

            Register(results, matches[0]);
            matchedInstances.Add(resourceKey.Instance);
            unmatchedExactKeys.Remove(resourceKey);
        }

        foreach (var instance in candidateInstances)
        {
            foreach (var supportedType in KnownResourceTypes.Supported)
            {
                if (!snapshot.TypeInstanceIndex.TryGetValue(new TypeInstanceKey(supportedType, instance), out var candidates))
                {
                    continue;
                }

                var chosen = ChooseEntry(candidates, supportedType);
                if (chosen is null)
                {
                    continue;
                }

                Register(results, chosen);
                matchedInstances.Add(instance);
                break;
            }
        }

        if (candidateInstances.Count > 0 && matchedInstances.Count == 0 && keys.ResourceKeys.Length == 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = "No matching mod files were found for extracted tray references."
            });
        }
        else if (candidateInstances.Count > matchedInstances.Count)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = $"Some extracted references were not matched ({candidateInstances.Count - matchedInstances.Count})."
            });
        }

        if (unmatchedExactKeys.Count > 0)
        {
            issues.Add(new TrayDependencyIssue
            {
                Severity = TrayDependencyIssueSeverity.Warning,
                Kind = TrayDependencyIssueKind.MissingReference,
                Message = $"Some exact resource keys were not matched ({unmatchedExactKeys.Count})."
            });
        }

        return new DirectMatchResult(results.Values.OrderBy(match => match.FilePath, StringComparer.OrdinalIgnoreCase).ToArray(), issues.ToArray());
    }

    private static HashSet<ulong> GetCandidateInstances(TraySearchKeys keys)
    {
        return keys.CasPartIds
            .Concat(keys.SkinToneIds)
            .Concat(keys.SimAspirationIds)
            .Concat(keys.SimTraitIds)
            .Concat(keys.CasPresetIds)
            .Concat(keys.FaceSliderIds)
            .Concat(keys.BodySliderIds)
            .Concat(keys.ObjectDefinitionIds)
            .Concat(keys.LotTraitIds)
            .ToHashSet();
    }

    private static ResolvedResourceRef? ChooseEntry(ResolvedResourceRef[] candidates, uint supportedType)
    {
        ResolvedResourceRef? chosen = null;
        string? chosenPath = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Entry is null || candidate.Entry.IsDeleted)
            {
                continue;
            }

            var entry = candidate.Entry;
            if (supportedType == KnownResourceTypes.Data &&
                entry.Group != KnownResourceTypes.AspirationGroup &&
                entry.Group != KnownResourceTypes.SimTraitGroup &&
                entry.Group != KnownResourceTypes.LotTraitGroup)
            {
                continue;
            }

            if (chosenPath is null)
            {
                chosenPath = candidate.FilePath;
                chosen = candidate;
                continue;
            }

            if (!string.Equals(chosenPath, candidate.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            if (chosen is null || entry.Group < chosen.Entry!.Group)
            {
                chosen = candidate;
            }
        }

        return chosen;
    }

    private static void Register(Dictionary<TrayResourceKey, ResolvedResourceRef> results, ResolvedResourceRef item)
    {
        if (results.TryGetValue(item.Key, out var existing) &&
            string.Compare(item.FilePath, existing.FilePath, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        results[item.Key] = item;
    }
}

internal sealed record DirectMatchResult(
    IReadOnlyList<ResolvedResourceRef> DirectMatches,
    IReadOnlyList<TrayDependencyIssue> Issues);

internal sealed class DependencyExpandEngine
{
    private readonly IDbpfResourceReader _resourceReader;

    public DependencyExpandEngine(IDbpfResourceReader resourceReader)
    {
        _resourceReader = resourceReader;
    }

    public DependencyExpandResult Expand(
        DirectMatchResult directMatch,
        PackageIndexSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var issues = new List<TrayDependencyIssue>();
        var results = new Dictionary<TrayResourceKey, ResolvedResourceRef>();
        var sessions = new Dictionary<string, DbpfPackageReadSession>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var match in directMatch.DirectMatches)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (match.Entry is null)
                {
                    continue;
                }

                if (!TryReadBytes(match, sessions, out var bytes, out var readError))
                {
                    if (!string.IsNullOrWhiteSpace(readError))
                    {
                        issues.Add(new TrayDependencyIssue
                        {
                            Severity = TrayDependencyIssueSeverity.Warning,
                            Kind = TrayDependencyIssueKind.PackageParseError,
                            FilePath = match.FilePath,
                            ResourceKey = FormatKey(match.Key),
                            Message = readError
                        });
                    }

                    continue;
                }

                var extraction = StructuredDependencyReaders.Read(match.Key, bytes);

                if (match.Key.Type == KnownResourceTypes.ObjectCatalog &&
                    TryResolveExact(
                        snapshot,
                        new TrayResourceKey(KnownResourceTypes.ObjectDefinition, match.Key.Group, match.Key.Instance),
                        out var siblingObjectDefinition))
                {
                    Register(results, siblingObjectDefinition with { Parent = match });

                    if (TryReadBytes(siblingObjectDefinition, sessions, out var objectDefinitionBytes, out _))
                    {
                        ObjectDefinitionDependencyReader.Read(objectDefinitionBytes, extraction);
                    }
                }

                if (!extraction.HasAny)
                {
                    BinaryReferenceScanner.Scan(bytes, extraction.FallbackIds, extraction.ExactKeys);
                }

                foreach (var exactKey in extraction.ExactKeys)
                {
                    if (TryResolveExact(snapshot, exactKey, out var resolved))
                    {
                        Register(results, resolved with { Parent = match });
                    }
                }

                foreach (var typedInstance in extraction.TypedInstances)
                {
                    if (TryResolveByTypes(snapshot, typedInstance.Instance, typedInstance.AllowedTypes, out var resolved))
                    {
                        Register(results, resolved with { Parent = match });
                    }
                }

                foreach (var id in extraction.FallbackIds)
                {
                    if (!TryResolveAny(snapshot, id, out var resolved))
                    {
                        continue;
                    }

                    Register(results, resolved with { Parent = match });
                }
            }
        }
        finally
        {
            foreach (var session in sessions.Values)
            {
                session.Dispose();
            }
        }

        return new DependencyExpandResult(results.Values.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ToArray(), issues.ToArray());
    }

    private static bool TryResolveExact(PackageIndexSnapshot snapshot, TrayResourceKey key, out ResolvedResourceRef resolved)
    {
        if (snapshot.ExactIndex.TryGetValue(key, out var matches) && matches.Length > 0)
        {
            resolved = matches[0];
            return true;
        }

        resolved = null!;
        return false;
    }

    private static bool TryResolveByTypes(
        PackageIndexSnapshot snapshot,
        ulong instance,
        IReadOnlyList<uint> allowedTypes,
        out ResolvedResourceRef resolved)
    {
        for (var typeIndex = 0; typeIndex < allowedTypes.Count; typeIndex++)
        {
            if (!snapshot.TypeInstanceIndex.TryGetValue(new TypeInstanceKey(allowedTypes[typeIndex], instance), out var matches))
            {
                continue;
            }

            if (TryChooseFirst(matches, out resolved))
            {
                return true;
            }
        }

        resolved = null!;
        return false;
    }

    private static bool TryResolveAny(PackageIndexSnapshot snapshot, ulong instance, out ResolvedResourceRef resolved)
    {
        if (!snapshot.SupportedInstanceIndex.ContainsKey(instance))
        {
            resolved = null!;
            return false;
        }

        foreach (var supportedType in KnownResourceTypes.Supported)
        {
            if (!snapshot.TypeInstanceIndex.TryGetValue(new TypeInstanceKey(supportedType, instance), out var matches))
            {
                continue;
            }

            if (TryChooseFirst(matches, out resolved))
            {
                return true;
            }
        }

        resolved = null!;
        return false;
    }

    private bool TryReadBytes(
        ResolvedResourceRef resource,
        IDictionary<string, DbpfPackageReadSession> sessions,
        out byte[] bytes,
        out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;

        if (resource.Entry is null)
        {
            error = "Missing package entry.";
            return false;
        }

        if (!sessions.TryGetValue(resource.FilePath, out var session))
        {
            session = _resourceReader.OpenSession(resource.FilePath);
            sessions[resource.FilePath] = session;
        }

        return session.TryReadBytes(
            new DbpfIndexEntry(
                resource.Entry.Type,
                resource.Entry.Group,
                resource.Entry.Instance,
                resource.Entry.DataOffset,
                resource.Entry.CompressedSize,
                resource.Entry.UncompressedSize,
                resource.Entry.CompressionType,
                resource.Entry.IsDeleted),
            out bytes,
            out error);
    }

    private static bool TryChooseFirst(ResolvedResourceRef[] matches, out ResolvedResourceRef resolved)
    {
        for (var i = 0; i < matches.Length; i++)
        {
            var candidate = matches[i];
            if (candidate.Entry is null || candidate.Entry.IsDeleted)
            {
                continue;
            }

            resolved = candidate;
            return true;
        }

        resolved = null!;
        return false;
    }

    private static void Register(Dictionary<TrayResourceKey, ResolvedResourceRef> results, ResolvedResourceRef item)
    {
        if (results.TryGetValue(item.Key, out var existing) &&
            string.Compare(existing.FilePath, item.FilePath, StringComparison.OrdinalIgnoreCase) <= 0)
        {
            return;
        }

        results[item.Key] = item;
    }

    private static string FormatKey(TrayResourceKey key)
    {
        return $"0x{key.Type:x8}:0x{key.Group:x8}:0x{key.Instance:x16}";
    }
}

internal sealed record DependencyExpandResult(
    IReadOnlyList<ResolvedResourceRef> ExpandedMatches,
    IReadOnlyList<TrayDependencyIssue> Issues);

internal sealed class ModFileExporter
{
    public void CopyFiles(
        IReadOnlyList<string> sourceFiles,
        string targetRoot,
        List<TrayDependencyIssue> issues,
        out int copiedFileCount)
    {
        copiedFileCount = 0;
        Directory.CreateDirectory(targetRoot);

        foreach (var sourcePath in sourceFiles)
        {
            try
            {
                if (!File.Exists(sourcePath))
                {
                    issues.Add(new TrayDependencyIssue
                    {
                        Severity = TrayDependencyIssueSeverity.Warning,
                        Kind = TrayDependencyIssueKind.MissingSourceFile,
                        FilePath = sourcePath,
                        Message = "Source mod file no longer exists."
                    });
                    continue;
                }

                var targetPath = Path.Combine(targetRoot, Path.GetFileName(sourcePath));
                targetPath = FileNameHelpers.GetUniquePath(targetPath);
                File.Copy(sourcePath, targetPath, overwrite: false);
                copiedFileCount++;
            }
            catch (Exception ex)
            {
                issues.Add(new TrayDependencyIssue
                {
                    Severity = TrayDependencyIssueSeverity.Error,
                    Kind = TrayDependencyIssueKind.CopyError,
                    FilePath = sourcePath,
                    Message = $"Failed to copy mod file: {ex.Message}"
                });
                return;
            }
        }
    }
}

internal static class FileNameHelpers
{
    public static string GetUniquePath(string targetPath)
    {
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
        {
            return targetPath;
        }

        var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);

        for (var suffix = 2; suffix < int.MaxValue; suffix++)
        {
            var candidate = Path.Combine(directory, $"{fileNameWithoutExtension} ({suffix}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException("Unable to generate a unique export filename.");
    }
}
