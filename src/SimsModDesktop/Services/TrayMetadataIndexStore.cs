using System.Text.Json;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayMetadataIndexStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly object _gate = new();
    private readonly string _cacheRootPath;
    private readonly string _manifestPath;

    private bool _manifestLoaded;
    private Dictionary<string, StoredMetadataEntry> _entries = new(StringComparer.OrdinalIgnoreCase);

    public TrayMetadataIndexStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache",
                "TrayMetadataIndex"))
    {
    }

    internal TrayMetadataIndexStore(string cacheRootPath)
    {
        _cacheRootPath = cacheRootPath;
        _manifestPath = Path.Combine(_cacheRootPath, "manifest.json");
    }

    public IReadOnlyDictionary<string, TrayMetadataResult> GetMetadata(IReadOnlyCollection<string> trayItemPaths)
    {
        ArgumentNullException.ThrowIfNull(trayItemPaths);

        var normalizedPaths = trayItemPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPaths.Length == 0)
        {
            return new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        }

        var results = new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        var changed = false;

        lock (_gate)
        {
            EnsureManifestLoadedLocked();

            foreach (var path in normalizedPaths)
            {
                if (!_entries.TryGetValue(path, out var entry))
                {
                    continue;
                }

                if (!IsValidLocked(entry))
                {
                    _entries.Remove(path);
                    changed = true;
                    continue;
                }

                if (entry.Metadata is not null)
                {
                    results[path] = entry.Metadata;
                }
            }

            if (changed)
            {
                PersistManifestLocked();
            }
        }

        return results;
    }

    public void Store(IReadOnlyDictionary<string, TrayMetadataResult> metadataByTrayItemPath)
    {
        ArgumentNullException.ThrowIfNull(metadataByTrayItemPath);

        if (metadataByTrayItemPath.Count == 0)
        {
            return;
        }

        var changed = false;

        lock (_gate)
        {
            EnsureManifestLoadedLocked();

            foreach (var pair in metadataByTrayItemPath)
            {
                var normalizedPath = Path.GetFullPath(pair.Key);
                var file = new FileInfo(normalizedPath);
                if (!file.Exists)
                {
                    continue;
                }

                _entries[normalizedPath] = new StoredMetadataEntry
                {
                    TrayItemPath = normalizedPath,
                    Length = file.Length,
                    LastWriteTimeUtc = file.LastWriteTimeUtc,
                    Metadata = CloneMetadata(normalizedPath, pair.Value)
                };
                changed = true;
            }

            if (changed)
            {
                PersistManifestLocked();
            }
        }
    }

    private void EnsureManifestLoadedLocked()
    {
        if (_manifestLoaded)
        {
            return;
        }

        _manifestLoaded = true;
        _entries = new Dictionary<string, StoredMetadataEntry>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(_manifestPath))
        {
            return;
        }

        try
        {
            using var stream = File.OpenRead(_manifestPath);
            var payload = JsonSerializer.Deserialize<TrayMetadataIndexPayload>(stream, JsonOptions);
            if (payload?.Entries is null)
            {
                return;
            }

            foreach (var entry in payload.Entries)
            {
                if (string.IsNullOrWhiteSpace(entry.TrayItemPath))
                {
                    continue;
                }

                _entries[Path.GetFullPath(entry.TrayItemPath)] = entry;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (JsonException)
        {
        }
    }

    private void PersistManifestLocked()
    {
        Directory.CreateDirectory(_cacheRootPath);

        var payload = new TrayMetadataIndexPayload
        {
            Entries = _entries.Values
                .OrderBy(entry => entry.TrayItemPath, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };

        using var stream = new FileStream(_manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, payload, JsonOptions);
    }

    private static bool IsValidLocked(StoredMetadataEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.TrayItemPath) || !File.Exists(entry.TrayItemPath))
        {
            return false;
        }

        var file = new FileInfo(entry.TrayItemPath);
        return entry.Length == file.Length &&
               entry.LastWriteTimeUtc == file.LastWriteTimeUtc;
    }

    private static TrayMetadataResult CloneMetadata(string trayItemPath, TrayMetadataResult metadata)
    {
        return new TrayMetadataResult
        {
            TrayItemPath = trayItemPath,
            ItemType = metadata.ItemType,
            Name = metadata.Name,
            Description = metadata.Description,
            CreatorName = metadata.CreatorName,
            CreatorId = metadata.CreatorId,
            FamilySize = metadata.FamilySize,
            PendingBabies = metadata.PendingBabies,
            SizeX = metadata.SizeX,
            SizeZ = metadata.SizeZ,
            PriceValue = metadata.PriceValue,
            NumBedrooms = metadata.NumBedrooms,
            NumBathrooms = metadata.NumBathrooms,
            Height = metadata.Height,
            IsModdedContent = metadata.IsModdedContent,
            Members = metadata.Members
                .Select(member => new TrayMemberDisplayMetadata
                {
                    SlotIndex = member.SlotIndex,
                    FullName = member.FullName,
                    Subtitle = member.Subtitle,
                    Detail = member.Detail
                })
                .ToList()
        };
    }

    private sealed class TrayMetadataIndexPayload
    {
        public List<StoredMetadataEntry> Entries { get; init; } = new();
    }

    private sealed class StoredMetadataEntry
    {
        public string TrayItemPath { get; init; } = string.Empty;
        public long Length { get; init; }
        public DateTime LastWriteTimeUtc { get; init; }
        public TrayMetadataResult? Metadata { get; init; }
    }
}
