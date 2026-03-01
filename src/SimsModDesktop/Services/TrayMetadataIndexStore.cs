using System.Text.Json;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayMetadataIndexStore
{
    private const string CacheFormatVersion = "metadata-v3";

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
            if (payload?.Entries is null ||
                !string.Equals(payload.FormatVersion, CacheFormatVersion, StringComparison.Ordinal))
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
            FormatVersion = CacheFormatVersion,
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
            TrayMetadataId = metadata.TrayMetadataId,
            ItemType = metadata.ItemType,
            Name = metadata.Name,
            Description = metadata.Description,
            DescriptionHashtags = metadata.DescriptionHashtags,
            CreatorName = metadata.CreatorName,
            CreatorId = metadata.CreatorId,
            ModifierName = metadata.ModifierName,
            ModifierId = metadata.ModifierId,
            Favorites = metadata.Favorites,
            Downloads = metadata.Downloads,
            ItemTimestamp = metadata.ItemTimestamp,
            MtxIds = metadata.MtxIds.ToArray(),
            MetaInfo = metadata.MetaInfo.ToArray(),
            VerifyCode = metadata.VerifyCode,
            CustomImageCount = metadata.CustomImageCount,
            MannequinCount = metadata.MannequinCount,
            IndexedCounter = metadata.IndexedCounter,
            CreatorPlatform = metadata.CreatorPlatform,
            ModifierPlatform = metadata.ModifierPlatform,
            CreatorPlatformId = metadata.CreatorPlatformId,
            CreatorPlatformName = metadata.CreatorPlatformName,
            ModifierPlatformId = metadata.ModifierPlatformId,
            ModifierPlatformName = metadata.ModifierPlatformName,
            ImageUriType = metadata.ImageUriType,
            SharedTimestamp = metadata.SharedTimestamp,
            Liked = metadata.Liked,
            FamilySize = metadata.FamilySize,
            PendingBabies = metadata.PendingBabies,
            SizeX = metadata.SizeX,
            SizeZ = metadata.SizeZ,
            PriceValue = metadata.PriceValue,
            NumBedrooms = metadata.NumBedrooms,
            NumBathrooms = metadata.NumBathrooms,
            Height = metadata.Height,
            IsModdedContent = metadata.IsModdedContent,
            IsHidden = metadata.IsHidden,
            IsDownloadTemp = metadata.IsDownloadTemp,
            LanguageId = metadata.LanguageId,
            SkuId = metadata.SkuId,
            IsMaxisContent = metadata.IsMaxisContent,
            PayloadSize = metadata.PayloadSize,
            WasReported = metadata.WasReported,
            WasReviewedAndCleared = metadata.WasReviewedAndCleared,
            IsImageModdedContent = metadata.IsImageModdedContent,
            SpecificCreatorPlatform = metadata.SpecificCreatorPlatform,
            SpecificModifierPlatform = metadata.SpecificModifierPlatform,
            SpecificCreatorPlatformPersonaId = metadata.SpecificCreatorPlatformPersonaId,
            SpecificModifierPlatformPersonaId = metadata.SpecificModifierPlatformPersonaId,
            IsCgItem = metadata.IsCgItem,
            IsCgInterested = metadata.IsCgInterested,
            CgName = metadata.CgName,
            Sku2Id = metadata.Sku2Id,
            CdsPatchBaseChangelists = metadata.CdsPatchBaseChangelists.ToArray(),
            CdsContentPatchMounted = metadata.CdsContentPatchMounted,
            SpecificDataVersion = metadata.SpecificDataVersion,
            VenueType = metadata.VenueType,
            PriceLevel = metadata.PriceLevel,
            ArchitectureValue = metadata.ArchitectureValue,
            NumThumbnails = metadata.NumThumbnails,
            FrontSide = metadata.FrontSide,
            VenueTypeStringKey = metadata.VenueTypeStringKey,
            GroundFloorIndex = metadata.GroundFloorIndex,
            OptionalRuleSatisfiedStringKeys = metadata.OptionalRuleSatisfiedStringKeys.ToArray(),
            LotTraits = metadata.LotTraits.ToArray(),
            BuildingType = metadata.BuildingType,
            LotTemplateId = metadata.LotTemplateId,
            HasUniversityHousingConfiguration = metadata.HasUniversityHousingConfiguration,
            TileCount = metadata.TileCount,
            UnitCount = metadata.UnitCount,
            UnitTraitCount = metadata.UnitTraitCount,
            DynamicAreas = metadata.DynamicAreas.ToArray(),
            RoomType = metadata.RoomType,
            RoomTypeStringKey = metadata.RoomTypeStringKey,
            PartBodyType = metadata.PartBodyType,
            Members = metadata.Members
                .Select(member => new TrayMemberDisplayMetadata
                {
                    SlotIndex = member.SlotIndex,
                    FullName = member.FullName,
                    Subtitle = member.Subtitle,
                    Detail = member.Detail,
                    SimId = member.SimId,
                    Gender = member.Gender,
                    AspirationId = member.AspirationId,
                    Age = member.Age,
                    Species = member.Species,
                    IsCustomGender = member.IsCustomGender,
                    OccultTypes = member.OccultTypes,
                    BreedNameKey = member.BreedNameKey,
                    FameRankedStatId = member.FameRankedStatId,
                    FameValue = member.FameValue,
                    DeathTrait = member.DeathTrait
                })
                .ToList()
        };
    }

    private sealed class TrayMetadataIndexPayload
    {
        public string FormatVersion { get; init; } = string.Empty;

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
