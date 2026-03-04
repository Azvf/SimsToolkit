using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Text;

namespace SimsModDesktop.PackageCore;

public sealed class DbpfPackageCatalog : IDbpfPackageCatalog
{
    private static readonly byte[] CacheMagic = Encoding.ASCII.GetBytes("STDBPFC1");
    private const int CacheVersion = 1;

    public async Task<DbpfCatalogSnapshot> BuildSnapshotAsync(
        string rootPath,
        DbpfCatalogBuildOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new DbpfCatalogBuildOptions();
        var normalizedRoot = Path.GetFullPath(rootPath.Trim());
        var packagePaths = Directory.Exists(normalizedRoot)
            ? Directory.EnumerateFiles(normalizedRoot, "*.package", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

        var metadata = packagePaths
            .Select(path =>
            {
                var info = new FileInfo(path);
                return new PackageFileMetadata(path, info.Length, info.LastWriteTimeUtc.Ticks);
            })
            .ToArray();

        var cacheFilePath = options.CacheFilePath ?? GetDefaultCacheFilePath();
        var cachedPackages = options.EnablePersistentCache
            ? TryReadCache(cacheFilePath)
            : null;

        var indexes = new DbpfPackageIndex[metadata.Length];
        var issues = new ConcurrentBag<DbpfCatalogIssue>();
        var pending = new List<(int Index, PackageFileMetadata Metadata)>();
        var totalPackages = metadata.Length;
        var reportStep = Math.Max(1, totalPackages / 100);
        var cachedPackageCount = 0;
        var completedPackageCount = 0;

        Report(0);

        for (var i = 0; i < metadata.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = metadata[i];
            if (cachedPackages is not null &&
                cachedPackages.TryGetValue(item.CacheKey, out var cached))
            {
                indexes[i] = cached;
                cachedPackageCount++;
                Report(Interlocked.Increment(ref completedPackageCount));
                continue;
            }

            pending.Add((i, item));
        }

        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = options.MaxDegreeOfParallelism
            },
            (item, token) =>
            {
                try
                {
                    indexes[item.Index] = DbpfPackageIndexReader.ReadPackageIndex(item.Metadata.FilePath);
                }
                catch (Exception ex)
                {
                    issues.Add(new DbpfCatalogIssue
                    {
                        FilePath = item.Metadata.FilePath,
                        Message = ex.Message
                    });

                    indexes[item.Index] = new DbpfPackageIndex
                    {
                        FilePath = item.Metadata.FilePath,
                        Fingerprint = new DbpfPackageFingerprint(item.Metadata.Length, item.Metadata.LastWriteUtcTicks),
                        Entries = Array.Empty<DbpfIndexEntry>(),
                        TypeBuckets = new Dictionary<uint, DbpfTypeBucket>().ToFrozenDictionary()
                    };
                }
                finally
                {
                    Report(Interlocked.Increment(ref completedPackageCount));
                }

                return ValueTask.CompletedTask;
            });

        Report(totalPackages);

        if (options.EnablePersistentCache)
        {
            TryWriteCache(cacheFilePath, indexes);
        }

        var packageList = indexes
            .OrderBy(package => package.FilePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var exact = new Dictionary<DbpfResourceKey, List<ResourceLocation>>();
        var typeInstance = new Dictionary<TypeInstanceKey, List<ResourceLocation>>();
        var supported = new Dictionary<ulong, List<ResourceLocation>>();
        var supportedTypes = options.SupportedInstanceTypes?.ToHashSet() ?? [];

        foreach (var package in packageList)
        {
            for (var entryIndex = 0; entryIndex < package.Entries.Length; entryIndex++)
            {
                var entry = package.Entries[entryIndex];
                if (entry.IsDeleted)
                {
                    continue;
                }

                var location = new ResourceLocation(package.FilePath, entryIndex, entry);
                Add(exact, new DbpfResourceKey(entry.Type, entry.Group, entry.Instance), location);
                Add(typeInstance, new TypeInstanceKey(entry.Type, entry.Instance), location);

                if (supportedTypes.Count > 0 && supportedTypes.Contains(entry.Type))
                {
                    Add(supported, entry.Instance, location);
                }
            }
        }

        return new DbpfCatalogSnapshot
        {
            RootPath = normalizedRoot,
            Packages = packageList,
            ExactIndex = Freeze(exact),
            TypeInstanceIndex = Freeze(typeInstance),
            SupportedInstanceIndex = Freeze(supported),
            Issues = issues.OrderBy(issue => issue.FilePath, StringComparer.OrdinalIgnoreCase).ToArray()
        };

        void Report(int currentCompleted)
        {
            if (options.Progress is null)
            {
                return;
            }

            if (totalPackages > 0 &&
                currentCompleted != totalPackages &&
                currentCompleted != 0 &&
                currentCompleted % reportStep != 0)
            {
                return;
            }

            var completed = totalPackages <= 0
                ? 0
                : Math.Clamp(currentCompleted, 0, totalPackages);
            options.Progress.Report(new DbpfCatalogBuildProgress
            {
                TotalPackages = totalPackages,
                CompletedPackages = completed,
                CachedPackages = cachedPackageCount
            });
        }
    }

    private static string GetDefaultCacheFilePath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, "SimsToolkit", "Cache", "dbpf-catalog-v1.bin");
    }

    private static FrozenDictionary<TKey, ResourceLocation[]> Freeze<TKey>(Dictionary<TKey, List<ResourceLocation>> source)
        where TKey : notnull
    {
        var materialized = new Dictionary<TKey, ResourceLocation[]>(source.Count);
        foreach (var pair in source)
        {
            materialized[pair.Key] = pair.Value.ToArray();
        }

        return materialized.ToFrozenDictionary();
    }

    private static void Add<TKey>(Dictionary<TKey, List<ResourceLocation>> source, TKey key, ResourceLocation value)
        where TKey : notnull
    {
        if (!source.TryGetValue(key, out var items))
        {
            items = new List<ResourceLocation>();
            source[key] = items;
        }

        items.Add(value);
    }

    private static Dictionary<PackageCacheKey, DbpfPackageIndex>? TryReadCache(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);
            var magic = reader.ReadBytes(CacheMagic.Length);
            if (!magic.SequenceEqual(CacheMagic))
            {
                return null;
            }

            if (reader.ReadInt32() != CacheVersion)
            {
                return null;
            }

            var count = reader.ReadInt32();
            var result = new Dictionary<PackageCacheKey, DbpfPackageIndex>(count);
            for (var i = 0; i < count; i++)
            {
                var filePath = reader.ReadString();
                var length = reader.ReadInt64();
                var lastWriteUtcTicks = reader.ReadInt64();
                var entryCount = reader.ReadInt32();
                var entries = new DbpfIndexEntry[entryCount];
                for (var entryIndex = 0; entryIndex < entryCount; entryIndex++)
                {
                    entries[entryIndex] = new DbpfIndexEntry(
                        reader.ReadUInt32(),
                        reader.ReadUInt32(),
                        reader.ReadUInt64(),
                        reader.ReadInt64(),
                        reader.ReadInt32(),
                        reader.ReadInt32(),
                        reader.ReadUInt16(),
                        reader.ReadBoolean());
                }

                result[new PackageCacheKey(filePath, length, lastWriteUtcTicks)] = new DbpfPackageIndex
                {
                    FilePath = filePath,
                    Fingerprint = new DbpfPackageFingerprint(length, lastWriteUtcTicks),
                    Entries = entries,
                    TypeBuckets = BuildTypeBuckets(entries)
                };
            }

            return result;
        }
        catch
        {
            return null;
        }
    }

    private static void TryWriteCache(string path, IReadOnlyList<DbpfPackageIndex> packages)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = path + ".tmp";
            using (var stream = File.Open(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(CacheMagic);
                writer.Write(CacheVersion);
                writer.Write(packages.Count);
                foreach (var package in packages.OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase))
                {
                    writer.Write(package.FilePath);
                    writer.Write(package.Fingerprint.Length);
                    writer.Write(package.Fingerprint.LastWriteUtcTicks);
                    writer.Write(package.Entries.Length);
                    foreach (var entry in package.Entries)
                    {
                        writer.Write(entry.Type);
                        writer.Write(entry.Group);
                        writer.Write(entry.Instance);
                        writer.Write(entry.DataOffset);
                        writer.Write(entry.CompressedSize);
                        writer.Write(entry.UncompressedSize);
                        writer.Write(entry.CompressionType);
                        writer.Write(entry.IsDeleted);
                    }
                }
            }

            if (File.Exists(path))
            {
                File.Replace(tempPath, path, null, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(tempPath, path, overwrite: true);
            }
        }
        catch
        {
        }
    }

    private static FrozenDictionary<uint, DbpfTypeBucket> BuildTypeBuckets(DbpfIndexEntry[] entries)
    {
        var map = new Dictionary<uint, Dictionary<ulong, List<int>>>();
        for (var index = 0; index < entries.Length; index++)
        {
            var entry = entries[index];
            if (entry.IsDeleted)
            {
                continue;
            }

            if (!map.TryGetValue(entry.Type, out var instances))
            {
                instances = new Dictionary<ulong, List<int>>();
                map[entry.Type] = instances;
            }

            if (!instances.TryGetValue(entry.Instance, out var indexes))
            {
                indexes = new List<int>();
                instances[entry.Instance] = indexes;
            }

            indexes.Add(index);
        }

        var buckets = new Dictionary<uint, DbpfTypeBucket>(map.Count);
        foreach (var pair in map)
        {
            var instanceIndexes = new Dictionary<ulong, int[]>(pair.Value.Count);
            foreach (var instancePair in pair.Value)
            {
                instanceIndexes[instancePair.Key] = instancePair.Value.ToArray();
            }

            buckets[pair.Key] = new DbpfTypeBucket
            {
                InstanceToEntryIndexes = instanceIndexes.ToFrozenDictionary()
            };
        }

        return buckets.ToFrozenDictionary();
    }

    private readonly record struct PackageCacheKey(string FilePath, long Length, long LastWriteUtcTicks);

    private readonly record struct PackageFileMetadata(string FilePath, long Length, long LastWriteUtcTicks)
    {
        public PackageCacheKey CacheKey => new(FilePath, Length, LastWriteUtcTicks);
    }
}
