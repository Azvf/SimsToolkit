using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using SimsModDesktop.Models;

namespace SimsModDesktop.Services;

public sealed class TrayMetadataService : ITrayMetadataService
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyDictionary<string, TrayMetadataResult>> GetMetadataAsync(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(trayItemPaths);

        if (trayItemPaths.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<string, TrayMetadataResult>>(
                new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase));
        }

        return Task.Run(
            () => GetMetadataCore(trayItemPaths, cancellationToken),
            cancellationToken);
    }

    private IReadOnlyDictionary<string, TrayMetadataResult> GetMetadataCore(
        IReadOnlyCollection<string> trayItemPaths,
        CancellationToken cancellationToken)
    {
        var normalizedPaths = trayItemPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(File.Exists)
            .ToList();
        if (normalizedPaths.Count == 0)
        {
            return new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        }

        var results = new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        var misses = new List<string>();

        lock (_gate)
        {
            foreach (var path in normalizedPaths)
            {
                var file = new FileInfo(path);
                if (_cache.TryGetValue(path, out var cached) &&
                    cached.Length == file.Length &&
                    cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
                {
                    results[path] = cached.Value;
                    continue;
                }

                misses.Add(path);
            }
        }

        if (misses.Count == 0)
        {
            return results;
        }

        var loaded = new Dictionary<string, TrayMetadataResult>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in misses)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var metadata = InternalTrayMetadataReader.Read(path);
                loaded[path] = metadata;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // Skip invalid tray items. Exact parsing only: malformed files produce no metadata.
            }
        }

        if (loaded.Count == 0)
        {
            return results;
        }

        lock (_gate)
        {
            foreach (var pair in loaded)
            {
                var file = new FileInfo(pair.Key);
                _cache[pair.Key] = new CacheEntry
                {
                    Length = file.Exists ? file.Length : 0,
                    LastWriteTimeUtc = file.Exists ? file.LastWriteTimeUtc : DateTime.MinValue,
                    Value = pair.Value
                };
                results[pair.Key] = pair.Value;
            }
        }

        return results;
    }

    private sealed class CacheEntry
    {
        public long Length { get; init; }

        public DateTime LastWriteTimeUtc { get; init; }

        public required TrayMetadataResult Value { get; init; }
    }
}

internal static class InternalTrayMetadataReader
{
    private const int TrayItemHeaderSize = 8;
    private static readonly Encoding Utf8Strict = new UTF8Encoding(false, true);

    public static TrayMetadataResult Read(string trayItemPath)
    {
        using var stream = new FileStream(
            trayItemPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);

        var header = ReadHeader(stream);
        var payload = ReadPayload(stream, header.DataSize);
        var metadata = TrayMetadataPayloadParser.Parse(payload);
        var effectiveType = ResolveEffectiveType(metadata, header.HeaderType);
        var specific = metadata.SpecificData;
        var household = specific?.Household;
        var blueprint = specific?.Blueprint;
        var room = specific?.Room;
        var part = specific?.Part;
        var members = household is null
            ? Array.Empty<TrayMemberDisplayMetadata>()
            : BuildMembers(household.Sims);
        int? familySize = household is null
            ? null
            : household.FamilySize > 0
                ? household.FamilySize
                : household.Sims.Count > 0
                    ? household.Sims.Count
                    : null;

        return new TrayMetadataResult
        {
            TrayItemPath = Path.GetFullPath(trayItemPath),
            TrayMetadataId = ToStringOrEmpty(metadata.Id),
            ItemType = effectiveType,
            Name = metadata.Name,
            Description = metadata.Description,
            DescriptionHashtags = specific?.DescriptionHashtags ?? string.Empty,
            CreatorName = metadata.CreatorName,
            CreatorId = ToStringOrEmpty(metadata.CreatorId),
            ModifierName = metadata.ModifierName,
            ModifierId = ToStringOrEmpty(metadata.ModifierId),
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
            FamilySize = familySize,
            PendingBabies = household?.PendingBabies > 0 ? household.PendingBabies : null,
            SizeX = GetPositiveOrNull((blueprint?.SizeX ?? room?.SizeX) ?? 0),
            SizeZ = GetPositiveOrNull((blueprint?.SizeZ ?? room?.SizeZ) ?? 0),
            PriceValue = GetPositiveOrNull((blueprint?.PriceValue ?? room?.PriceValue) ?? 0),
            NumBedrooms = GetPositiveOrNull(blueprint?.NumBedrooms ?? 0),
            NumBathrooms = GetPositiveOrNull(blueprint?.NumBathrooms ?? 0),
            Height = GetPositiveOrNull(room?.Height ?? 0),
            IsModdedContent = specific?.IsModdedContent ?? false,
            IsHidden = specific?.IsHidden,
            IsDownloadTemp = specific?.IsDownloadTemp,
            LanguageId = specific?.LanguageId,
            SkuId = specific?.SkuId,
            IsMaxisContent = specific?.IsMaxisContent,
            PayloadSize = specific?.PayloadSize,
            WasReported = specific?.WasReported,
            WasReviewedAndCleared = specific?.WasReviewedAndCleared,
            IsImageModdedContent = specific?.IsImageModdedContent,
            SpecificCreatorPlatform = specific?.SpecificCreatorPlatform,
            SpecificModifierPlatform = specific?.SpecificModifierPlatform,
            SpecificCreatorPlatformPersonaId = specific?.SpecificCreatorPlatformPersonaId,
            SpecificModifierPlatformPersonaId = specific?.SpecificModifierPlatformPersonaId,
            IsCgItem = specific?.IsCgItem,
            IsCgInterested = specific?.IsCgInterested,
            CgName = specific?.CgName ?? string.Empty,
            Sku2Id = specific?.Sku2Id,
            CdsPatchBaseChangelists = specific?.CdsPatchBaseChangelists.ToArray() ?? Array.Empty<uint>(),
            CdsContentPatchMounted = specific?.CdsContentPatchMounted,
            SpecificDataVersion = specific?.Version,
            VenueType = blueprint?.VenueType,
            PriceLevel = blueprint?.PriceLevel ?? room?.PriceLevel,
            ArchitectureValue = blueprint?.ArchitectureValue,
            NumThumbnails = blueprint?.NumThumbnails ?? part?.NumThumbnails,
            FrontSide = blueprint?.FrontSide,
            VenueTypeStringKey = blueprint?.VenueTypeStringKey,
            GroundFloorIndex = blueprint?.GroundFloorIndex,
            OptionalRuleSatisfiedStringKeys = blueprint?.OptionalRuleSatisfiedStringKeys.ToArray() ?? Array.Empty<uint>(),
            LotTraits = blueprint?.LotTraits.ToArray() ?? Array.Empty<ulong>(),
            BuildingType = blueprint?.BuildingType,
            LotTemplateId = blueprint?.LotTemplateId,
            HasUniversityHousingConfiguration = blueprint?.HasUniversityHousingConfiguration ?? false,
            TileCount = blueprint?.TileCount,
            UnitCount = blueprint?.UnitCount,
            UnitTraitCount = blueprint?.UnitTraitCount,
            DynamicAreas = blueprint?.DynamicAreas.ToArray() ?? Array.Empty<uint>(),
            RoomType = room?.RoomType,
            RoomTypeStringKey = room?.RoomTypeStringKey,
            PartBodyType = part?.BodyType,
            Members = members
        };
    }

    private static TrayItemHeader ReadHeader(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[TrayItemHeaderSize];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer[totalRead..]);
            if (read <= 0)
            {
                throw new InvalidDataException("Tray item header is incomplete.");
            }

            totalRead += read;
        }

        return new TrayItemHeader(
            BinaryPrimitives.ReadUInt32LittleEndian(buffer[..4]),
            checked((int)BinaryPrimitives.ReadUInt32LittleEndian(buffer[4..])));
    }

    private static byte[] ReadPayload(Stream stream, int dataSize)
    {
        if (dataSize <= 0)
        {
            throw new InvalidDataException("Tray item payload is empty.");
        }

        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if (dataSize > remaining)
            {
                throw new InvalidDataException("Tray item payload length exceeds file size.");
            }
        }

        var buffer = new byte[dataSize];
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read <= 0)
            {
                throw new InvalidDataException("Tray item payload is incomplete.");
            }

            totalRead += read;
        }

        return buffer;
    }

    private static string ResolveEffectiveType(TrayMetadataPayload metadata, uint headerType)
    {
        return metadata.Type switch
        {
            1 => "Household",
            2 => "Lot",
            3 => "Room",
            5 => "Part",
            _ => ResolveTypeFromStructuredData(metadata, headerType)
        };
    }

    private static string ResolveTypeFromStructuredData(TrayMetadataPayload metadata, uint headerType)
    {
        if (metadata.SpecificData?.Household is not null)
        {
            return "Household";
        }

        if (metadata.SpecificData?.Blueprint is not null)
        {
            return "Lot";
        }

        if (metadata.SpecificData?.Room is not null)
        {
            return "Room";
        }

        return headerType switch
        {
            1 => "Household",
            2 => "Lot",
            3 => "Room",
            5 => "Part",
            _ => string.Empty
        };
    }

    private static IReadOnlyList<TrayMemberDisplayMetadata> BuildMembers(IReadOnlyList<TraySimPayload> sims)
    {
        if (sims.Count == 0)
        {
            return Array.Empty<TrayMemberDisplayMetadata>();
        }

        var members = new List<TrayMemberDisplayMetadata>(sims.Count);
        foreach (var sim in sims)
        {
            var fullName = string.Join(
                " ",
                new[] { sim.FirstName, sim.LastName }
                    .Where(value => !string.IsNullOrWhiteSpace(value)))
                .Trim();

            if (string.IsNullOrWhiteSpace(fullName))
            {
                fullName = sim.BreedName;
            }

            if (string.IsNullOrWhiteSpace(fullName))
            {
                continue;
            }

            members.Add(new TrayMemberDisplayMetadata
            {
                SlotIndex = members.Count + 1,
                FullName = fullName,
                Subtitle = string.Empty,
                Detail = string.Empty,
                SimId = ToStringOrEmpty(sim.Id),
                Gender = sim.Gender,
                AspirationId = sim.AspirationId,
                Age = sim.Age,
                Species = sim.Species,
                IsCustomGender = sim.IsCustomGender,
                OccultTypes = sim.OccultTypes,
                BreedNameKey = sim.BreedNameKey,
                FameRankedStatId = sim.Fame?.Id,
                FameValue = sim.Fame?.Value,
                DeathTrait = sim.DeathTrait
            });
        }

        return members;
    }

    private static int? GetPositiveOrNull(int value)
    {
        return value > 0 ? value : null;
    }

    private static string ToStringOrEmpty(ulong? value)
    {
        return value.HasValue
            ? value.Value.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
    }

    private readonly record struct TrayItemHeader(uint HeaderType, int DataSize);

    private sealed class TrayMetadataPayload
    {
        public ulong? Id { get; set; }
        public int Type { get; set; }
        public ulong? CreatorId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string CreatorName { get; set; } = string.Empty;
        public ulong? Favorites { get; set; }
        public ulong? Downloads { get; set; }
        public ulong? ItemTimestamp { get; set; }
        public List<ulong> MtxIds { get; } = [];
        public ulong? ModifierId { get; set; }
        public string ModifierName { get; set; } = string.Empty;
        public List<uint> MetaInfo { get; } = [];
        public int? VerifyCode { get; set; }
        public uint? CustomImageCount { get; set; }
        public uint? MannequinCount { get; set; }
        public ulong? IndexedCounter { get; set; }
        public uint? CreatorPlatform { get; set; }
        public uint? ModifierPlatform { get; set; }
        public ulong? CreatorPlatformId { get; set; }
        public string CreatorPlatformName { get; set; } = string.Empty;
        public ulong? ModifierPlatformId { get; set; }
        public string ModifierPlatformName { get; set; } = string.Empty;
        public uint? ImageUriType { get; set; }
        public ulong? SharedTimestamp { get; set; }
        public bool? Liked { get; set; }
        public TraySpecificDataPayload? SpecificData { get; set; }
    }

    private sealed class TraySpecificDataPayload
    {
        public TrayBlueprintPayload? Blueprint { get; set; }
        public TrayHouseholdPayload? Household { get; set; }
        public TrayRoomPayload? Room { get; set; }
        public TrayPartPayload? Part { get; set; }
        public bool IsModdedContent { get; set; }
        public bool? IsHidden { get; set; }
        public bool? IsDownloadTemp { get; set; }
        public string DescriptionHashtags { get; set; } = string.Empty;
        public ulong? LanguageId { get; set; }
        public ulong? SkuId { get; set; }
        public bool? IsMaxisContent { get; set; }
        public uint? PayloadSize { get; set; }
        public bool? WasReported { get; set; }
        public bool? WasReviewedAndCleared { get; set; }
        public bool? IsImageModdedContent { get; set; }
        public uint? SpecificCreatorPlatform { get; set; }
        public uint? SpecificModifierPlatform { get; set; }
        public ulong? SpecificCreatorPlatformPersonaId { get; set; }
        public ulong? SpecificModifierPlatformPersonaId { get; set; }
        public bool? IsCgItem { get; set; }
        public bool? IsCgInterested { get; set; }
        public string CgName { get; set; } = string.Empty;
        public ulong? Sku2Id { get; set; }
        public List<uint> CdsPatchBaseChangelists { get; } = [];
        public bool? CdsContentPatchMounted { get; set; }
        public uint? Version { get; set; }
    }

    private sealed class TrayBlueprintPayload
    {
        public ulong? VenueType { get; set; }
        public int SizeX { get; set; }
        public int SizeZ { get; set; }
        public int PriceValue { get; set; }
        public int NumBedrooms { get; set; }
        public int NumBathrooms { get; set; }
        public uint? PriceLevel { get; set; }
        public uint? ArchitectureValue { get; set; }
        public uint? NumThumbnails { get; set; }
        public uint? FrontSide { get; set; }
        public uint? VenueTypeStringKey { get; set; }
        public uint? GroundFloorIndex { get; set; }
        public List<uint> OptionalRuleSatisfiedStringKeys { get; } = [];
        public List<ulong> LotTraits { get; } = [];
        public uint? BuildingType { get; set; }
        public ulong? LotTemplateId { get; set; }
        public bool? HasUniversityHousingConfiguration { get; set; }
        public uint? TileCount { get; set; }
        public uint? UnitCount { get; set; }
        public int? UnitTraitCount { get; set; }
        public List<uint> DynamicAreas { get; } = [];
    }

    private sealed class TrayRoomPayload
    {
        public uint? RoomType { get; set; }
        public int SizeX { get; set; }
        public int SizeZ { get; set; }
        public int PriceValue { get; set; }
        public int Height { get; set; }
        public uint? PriceLevel { get; set; }
        public uint? RoomTypeStringKey { get; set; }
    }

    private sealed class TrayPartPayload
    {
        public uint? BodyType { get; set; }
        public uint? NumThumbnails { get; set; }
    }

    private sealed class TrayHouseholdPayload
    {
        public int FamilySize { get; set; }
        public int PendingBabies { get; set; }
        public List<TraySimPayload> Sims { get; } = [];
    }

    private sealed class TraySimPayload
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string BreedName { get; set; } = string.Empty;
        public ulong? Id { get; set; }
        public uint? Gender { get; set; }
        public ulong? AspirationId { get; set; }
        public uint? Age { get; set; }
        public uint? Species { get; set; }
        public bool? IsCustomGender { get; set; }
        public uint? OccultTypes { get; set; }
        public uint? BreedNameKey { get; set; }
        public TrayRankedStatPayload? Fame { get; set; }
        public ulong? DeathTrait { get; set; }
    }

    private sealed class TrayRankedStatPayload
    {
        public ulong? Id { get; set; }
        public float? Value { get; set; }
    }

    private static class TrayMetadataPayloadParser
    {
        public static TrayMetadataPayload Parse(ReadOnlySpan<byte> data)
        {
            var payload = new TrayMetadataPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == ProtoWireType.Varint:
                        payload.Id = reader.ReadUInt64();
                        break;
                    case 2 when wireType == ProtoWireType.Varint:
                        payload.Type = reader.ReadInt32();
                        break;
                    case 4 when wireType == ProtoWireType.LengthDelimited:
                        payload.Name = reader.ReadString(Utf8Strict);
                        break;
                    case 5 when wireType == ProtoWireType.LengthDelimited:
                        payload.Description = reader.ReadString(Utf8Strict);
                        break;
                    case 6 when wireType == ProtoWireType.Varint:
                        payload.CreatorId = reader.ReadUInt64();
                        break;
                    case 7 when wireType == ProtoWireType.LengthDelimited:
                        payload.CreatorName = reader.ReadString(Utf8Strict);
                        break;
                    case 8 when wireType == ProtoWireType.Varint:
                        payload.Favorites = reader.ReadUInt64();
                        break;
                    case 9 when wireType == ProtoWireType.Varint:
                        payload.Downloads = reader.ReadUInt64();
                        break;
                    case 10 when wireType == ProtoWireType.LengthDelimited:
                        payload.SpecificData = ParseSpecificData(reader.ReadBytes());
                        break;
                    case 11 when wireType == ProtoWireType.Varint:
                        payload.ItemTimestamp = reader.ReadUInt64();
                        break;
                    case 12 when wireType == ProtoWireType.Varint:
                        payload.MtxIds.Add(reader.ReadUInt64());
                        break;
                    case 12 when wireType == ProtoWireType.LengthDelimited:
                        AppendPackedUInt64Values(payload.MtxIds, reader.ReadBytes());
                        break;
                    case 14 when wireType == ProtoWireType.Varint:
                        payload.ModifierId = reader.ReadUInt64();
                        break;
                    case 15 when wireType == ProtoWireType.LengthDelimited:
                        payload.ModifierName = reader.ReadString(Utf8Strict);
                        break;
                    case 16 when wireType == ProtoWireType.Varint:
                        payload.MetaInfo.Add(reader.ReadUInt32());
                        break;
                    case 16 when wireType == ProtoWireType.LengthDelimited:
                        AppendPackedUInt32Values(payload.MetaInfo, reader.ReadBytes());
                        break;
                    case 17 when wireType == ProtoWireType.Varint:
                        payload.VerifyCode = reader.ReadInt32();
                        break;
                    case 20 when wireType == ProtoWireType.Varint:
                        payload.CustomImageCount = reader.ReadUInt32();
                        break;
                    case 21 when wireType == ProtoWireType.Varint:
                        payload.MannequinCount = reader.ReadUInt32();
                        break;
                    case 25 when wireType == ProtoWireType.Varint:
                        payload.IndexedCounter = reader.ReadUInt64();
                        break;
                    case 26 when wireType == ProtoWireType.Varint:
                        payload.CreatorPlatform = reader.ReadUInt32();
                        break;
                    case 27 when wireType == ProtoWireType.Varint:
                        payload.ModifierPlatform = reader.ReadUInt32();
                        break;
                    case 28 when wireType == ProtoWireType.Varint:
                        payload.CreatorPlatformId = reader.ReadUInt64();
                        break;
                    case 29 when wireType == ProtoWireType.LengthDelimited:
                        payload.CreatorPlatformName = reader.ReadString(Utf8Strict);
                        break;
                    case 30 when wireType == ProtoWireType.Varint:
                        payload.ModifierPlatformId = reader.ReadUInt64();
                        break;
                    case 31 when wireType == ProtoWireType.LengthDelimited:
                        payload.ModifierPlatformName = reader.ReadString(Utf8Strict);
                        break;
                    case 32 when wireType == ProtoWireType.Varint:
                        payload.ImageUriType = reader.ReadUInt32();
                        break;
                    case 33 when wireType == ProtoWireType.Varint:
                        payload.SharedTimestamp = reader.ReadUInt64();
                        break;
                    case 34 when wireType == ProtoWireType.Varint:
                        payload.Liked = reader.ReadBoolean();
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static TraySpecificDataPayload ParseSpecificData(ReadOnlySpan<byte> data)
        {
            var payload = new TraySpecificDataPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == ProtoWireType.LengthDelimited:
                        payload.Blueprint = ParseBlueprint(reader.ReadBytes());
                        break;
                    case 2 when wireType == ProtoWireType.LengthDelimited:
                        payload.Household = ParseHousehold(reader.ReadBytes());
                        break;
                    case 3 when wireType == ProtoWireType.Varint:
                        payload.IsHidden = reader.ReadBoolean();
                        break;
                    case 4 when wireType == ProtoWireType.Varint:
                        payload.IsDownloadTemp = reader.ReadBoolean();
                        break;
                    case 5 when wireType == ProtoWireType.Varint:
                        payload.IsModdedContent = reader.ReadBoolean();
                        break;
                    case 7 when wireType == ProtoWireType.LengthDelimited:
                        payload.Room = ParseRoom(reader.ReadBytes());
                        break;
                    case 8 when wireType == ProtoWireType.LengthDelimited:
                        payload.DescriptionHashtags = reader.ReadString(Utf8Strict);
                        break;
                    case 9 when wireType == ProtoWireType.Varint:
                        payload.LanguageId = reader.ReadUInt64();
                        break;
                    case 10 when wireType == ProtoWireType.Varint:
                        payload.SkuId = reader.ReadUInt64();
                        break;
                    case 11 when wireType == ProtoWireType.Varint:
                        payload.IsMaxisContent = reader.ReadBoolean();
                        break;
                    case 12 when wireType == ProtoWireType.Varint:
                        payload.PayloadSize = reader.ReadUInt32();
                        break;
                    case 13 when wireType == ProtoWireType.Varint:
                        payload.WasReported = reader.ReadBoolean();
                        break;
                    case 14 when wireType == ProtoWireType.Varint:
                        payload.WasReviewedAndCleared = reader.ReadBoolean();
                        break;
                    case 15 when wireType == ProtoWireType.Varint:
                        payload.IsImageModdedContent = reader.ReadBoolean();
                        break;
                    case 16 when wireType == ProtoWireType.Varint:
                        payload.SpecificCreatorPlatform = reader.ReadUInt32();
                        break;
                    case 17 when wireType == ProtoWireType.Varint:
                        payload.SpecificModifierPlatform = reader.ReadUInt32();
                        break;
                    case 18 when wireType == ProtoWireType.Varint:
                        payload.SpecificCreatorPlatformPersonaId = reader.ReadUInt64();
                        break;
                    case 19 when wireType == ProtoWireType.Varint:
                        payload.SpecificModifierPlatformPersonaId = reader.ReadUInt64();
                        break;
                    case 20 when wireType == ProtoWireType.Varint:
                        payload.IsCgItem = reader.ReadBoolean();
                        break;
                    case 21 when wireType == ProtoWireType.Varint:
                        payload.IsCgInterested = reader.ReadBoolean();
                        break;
                    case 22 when wireType == ProtoWireType.LengthDelimited:
                        payload.CgName = reader.ReadString(Utf8Strict);
                        break;
                    case 23 when wireType == ProtoWireType.Varint:
                        payload.Sku2Id = reader.ReadUInt64();
                        break;
                    case 24 when wireType == ProtoWireType.Varint:
                        payload.CdsPatchBaseChangelists.Add(reader.ReadUInt32());
                        break;
                    case 24 when wireType == ProtoWireType.LengthDelimited:
                        AppendPackedUInt32Values(payload.CdsPatchBaseChangelists, reader.ReadBytes());
                        break;
                    case 25 when wireType == ProtoWireType.Varint:
                        payload.CdsContentPatchMounted = reader.ReadBoolean();
                        break;
                    case 26 when wireType == ProtoWireType.LengthDelimited:
                        payload.Part = ParsePart(reader.ReadBytes());
                        break;
                    case 1001 when wireType == ProtoWireType.Varint:
                        payload.Version = reader.ReadUInt32();
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static TrayBlueprintPayload ParseBlueprint(ReadOnlySpan<byte> data)
        {
            var payload = new TrayBlueprintPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == ProtoWireType.Varint:
                        payload.VenueType = reader.ReadUInt64();
                        break;
                    case 2 when wireType == ProtoWireType.Varint:
                        payload.SizeX = reader.ReadInt32();
                        break;
                    case 3 when wireType == ProtoWireType.Varint:
                        payload.SizeZ = reader.ReadInt32();
                        break;
                    case 4 when wireType == ProtoWireType.Varint:
                        payload.PriceLevel = reader.ReadUInt32();
                        break;
                    case 5 when wireType == ProtoWireType.Varint:
                        payload.PriceValue = reader.ReadInt32();
                        break;
                    case 6 when wireType == ProtoWireType.Varint:
                        payload.NumBedrooms = reader.ReadInt32();
                        break;
                    case 7 when wireType == ProtoWireType.Varint:
                        payload.NumBathrooms = reader.ReadInt32();
                        break;
                    case 8 when wireType == ProtoWireType.Varint:
                        payload.ArchitectureValue = reader.ReadUInt32();
                        break;
                    case 9 when wireType == ProtoWireType.Varint:
                        payload.NumThumbnails = reader.ReadUInt32();
                        break;
                    case 10 when wireType == ProtoWireType.Varint:
                        payload.FrontSide = reader.ReadUInt32();
                        break;
                    case 11 when wireType == ProtoWireType.Varint:
                        payload.VenueTypeStringKey = reader.ReadUInt32();
                        break;
                    case 12 when wireType == ProtoWireType.Varint:
                        payload.GroundFloorIndex = reader.ReadUInt32();
                        break;
                    case 13 when wireType == ProtoWireType.Varint:
                        payload.OptionalRuleSatisfiedStringKeys.Add(reader.ReadUInt32());
                        break;
                    case 13 when wireType == ProtoWireType.LengthDelimited:
                        AppendPackedUInt32Values(payload.OptionalRuleSatisfiedStringKeys, reader.ReadBytes());
                        break;
                    case 14 when wireType == ProtoWireType.Fixed64:
                        payload.LotTraits.Add(reader.ReadFixed64());
                        break;
                    case 14 when wireType == ProtoWireType.LengthDelimited:
                        AppendPackedFixed64Values(payload.LotTraits, reader.ReadBytes());
                        break;
                    case 15 when wireType == ProtoWireType.Varint:
                        payload.BuildingType = reader.ReadUInt32();
                        break;
                    case 16 when wireType == ProtoWireType.Varint:
                        payload.LotTemplateId = reader.ReadUInt64();
                        break;
                    case 17 when wireType == ProtoWireType.LengthDelimited:
                        payload.HasUniversityHousingConfiguration = true;
                        _ = reader.ReadBytes();
                        break;
                    case 18 when wireType == ProtoWireType.Varint:
                        payload.TileCount = reader.ReadUInt32();
                        break;
                    case 19 when wireType == ProtoWireType.Varint:
                        payload.UnitCount = reader.ReadUInt32();
                        break;
                    case 20 when wireType == ProtoWireType.LengthDelimited:
                        payload.UnitTraitCount = (payload.UnitTraitCount ?? 0) + 1;
                        _ = reader.ReadBytes();
                        break;
                    case 21 when wireType == ProtoWireType.Varint:
                        payload.DynamicAreas.Add(reader.ReadUInt32());
                        break;
                    case 21 when wireType == ProtoWireType.LengthDelimited:
                        AppendPackedUInt32Values(payload.DynamicAreas, reader.ReadBytes());
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static TrayRoomPayload ParseRoom(ReadOnlySpan<byte> data)
        {
            var payload = new TrayRoomPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == ProtoWireType.Varint:
                        payload.RoomType = reader.ReadUInt32();
                        break;
                    case 2 when wireType == ProtoWireType.Varint:
                        payload.SizeX = reader.ReadInt32();
                        break;
                    case 3 when wireType == ProtoWireType.Varint:
                        payload.SizeZ = reader.ReadInt32();
                        break;
                    case 4 when wireType == ProtoWireType.Varint:
                        payload.PriceValue = reader.ReadInt32();
                        break;
                    case 5 when wireType == ProtoWireType.Varint:
                        payload.Height = reader.ReadInt32();
                        break;
                    case 6 when wireType == ProtoWireType.Varint:
                        payload.PriceLevel = reader.ReadUInt32();
                        break;
                    case 7 when wireType == ProtoWireType.Varint:
                        payload.RoomTypeStringKey = reader.ReadUInt32();
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static TrayPartPayload ParsePart(ReadOnlySpan<byte> data)
        {
            var payload = new TrayPartPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == ProtoWireType.Varint:
                        payload.BodyType = reader.ReadUInt32();
                        break;
                    case 2 when wireType == ProtoWireType.Varint:
                        payload.NumThumbnails = reader.ReadUInt32();
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static TrayHouseholdPayload ParseHousehold(ReadOnlySpan<byte> data)
        {
            var payload = new TrayHouseholdPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == ProtoWireType.Varint:
                        payload.FamilySize = reader.ReadInt32();
                        break;
                    case 2 when wireType == ProtoWireType.LengthDelimited:
                        payload.Sims.Add(ParseSim(reader.ReadBytes()));
                        break;
                    case 3 when wireType == ProtoWireType.Varint:
                        payload.PendingBabies = reader.ReadInt32();
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static TraySimPayload ParseSim(ReadOnlySpan<byte> data)
        {
            var payload = new TraySimPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 3 when wireType == ProtoWireType.LengthDelimited:
                        payload.FirstName = reader.ReadString(Utf8Strict);
                        break;
                    case 4 when wireType == ProtoWireType.LengthDelimited:
                        payload.LastName = reader.ReadString(Utf8Strict);
                        break;
                    case 5 when wireType == ProtoWireType.Varint:
                        payload.Id = reader.ReadUInt64();
                        break;
                    case 6 when wireType == ProtoWireType.Varint:
                        payload.Gender = reader.ReadUInt32();
                        break;
                    case 7 when wireType == ProtoWireType.Varint:
                        payload.AspirationId = reader.ReadUInt64();
                        break;
                    case 9 when wireType == ProtoWireType.Varint:
                        payload.Age = reader.ReadUInt32();
                        break;
                    case 12 when wireType == ProtoWireType.Varint:
                        payload.Species = reader.ReadUInt32();
                        break;
                    case 13 when wireType == ProtoWireType.Varint:
                        payload.IsCustomGender = reader.ReadBoolean();
                        break;
                    case 14 when wireType == ProtoWireType.Varint:
                        payload.OccultTypes = reader.ReadUInt32();
                        break;
                    case 15 when wireType == ProtoWireType.LengthDelimited:
                        payload.BreedName = reader.ReadString(Utf8Strict);
                        break;
                    case 16 when wireType == ProtoWireType.Varint:
                        payload.BreedNameKey = reader.ReadUInt32();
                        break;
                    case 17 when wireType == ProtoWireType.LengthDelimited:
                        payload.Fame = ParseRankedStat(reader.ReadBytes());
                        break;
                    case 24 when wireType == ProtoWireType.Varint:
                        payload.DeathTrait = reader.ReadUInt64();
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static TrayRankedStatPayload ParseRankedStat(ReadOnlySpan<byte> data)
        {
            var payload = new TrayRankedStatPayload();
            var reader = new ProtoReader(data);

            while (reader.TryReadFieldHeader(out var fieldNumber, out var wireType))
            {
                switch (fieldNumber)
                {
                    case 1 when wireType == ProtoWireType.Varint:
                        payload.Id = reader.ReadUInt64();
                        break;
                    case 2 when wireType == ProtoWireType.Fixed32:
                        payload.Value = reader.ReadSingle();
                        break;
                    default:
                        reader.SkipField(wireType);
                        break;
                }
            }

            return payload;
        }

        private static void AppendPackedUInt32Values(List<uint> target, ReadOnlySpan<byte> data)
        {
            var reader = new ProtoReader(data);
            while (reader.HasRemaining)
            {
                target.Add(reader.ReadUInt32());
            }
        }

        private static void AppendPackedUInt64Values(List<ulong> target, ReadOnlySpan<byte> data)
        {
            var reader = new ProtoReader(data);
            while (reader.HasRemaining)
            {
                target.Add(reader.ReadUInt64());
            }
        }

        private static void AppendPackedFixed64Values(List<ulong> target, ReadOnlySpan<byte> data)
        {
            if (data.Length % 8 != 0)
            {
                throw new InvalidDataException("Malformed packed fixed64 field.");
            }

            for (var offset = 0; offset < data.Length; offset += 8)
            {
                target.Add(BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset, 8)));
            }
        }
    }

    private static class ProtoWireType
    {
        public const int Varint = 0;
        public const int Fixed64 = 1;
        public const int LengthDelimited = 2;
        public const int Fixed32 = 5;
    }

    private ref struct ProtoReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _position;

        public ProtoReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _position = 0;
        }

        public bool TryReadFieldHeader(out int fieldNumber, out int wireType)
        {
            if (_position >= _data.Length)
            {
                fieldNumber = 0;
                wireType = 0;
                return false;
            }

            var tag = ReadUInt64();
            if (tag == 0)
            {
                throw new InvalidDataException("Encountered invalid protobuf tag.");
            }

            fieldNumber = checked((int)(tag >> 3));
            wireType = checked((int)(tag & 0x07));
            return true;
        }

        public bool HasRemaining => _position < _data.Length;

        public ulong ReadUInt64()
        {
            ulong value = 0;
            var shift = 0;

            while (_position < _data.Length && shift < 64)
            {
                var current = _data[_position++];
                value |= (ulong)(current & 0x7Fu) << shift;
                if ((current & 0x80) == 0)
                {
                    return value;
                }

                shift += 7;
            }

            throw new InvalidDataException("Malformed protobuf varint.");
        }

        public int ReadInt32()
        {
            return unchecked((int)ReadUInt64());
        }

        public uint ReadUInt32()
        {
            return checked((uint)ReadUInt64());
        }

        public bool ReadBoolean()
        {
            return ReadUInt64() != 0;
        }

        public ulong ReadFixed64()
        {
            if (_position + 8 > _data.Length)
            {
                throw new InvalidDataException("Malformed protobuf fixed64 field.");
            }

            var value = BinaryPrimitives.ReadUInt64LittleEndian(_data.Slice(_position, 8));
            _position += 8;
            return value;
        }

        public uint ReadFixed32()
        {
            if (_position + 4 > _data.Length)
            {
                throw new InvalidDataException("Malformed protobuf fixed32 field.");
            }

            var value = BinaryPrimitives.ReadUInt32LittleEndian(_data.Slice(_position, 4));
            _position += 4;
            return value;
        }

        public float ReadSingle()
        {
            return BitConverter.UInt32BitsToSingle(ReadFixed32());
        }

        public ReadOnlySpan<byte> ReadBytes()
        {
            var length = checked((int)ReadUInt64());
            if (_position + length > _data.Length)
            {
                throw new InvalidDataException("Malformed protobuf length-delimited field.");
            }

            var slice = _data.Slice(_position, length);
            _position += length;
            return slice;
        }

        public string ReadString(Encoding encoding)
        {
            var bytes = ReadBytes();
            return bytes.IsEmpty ? string.Empty : encoding.GetString(bytes);
        }

        public void SkipField(int wireType)
        {
            switch (wireType)
            {
                case ProtoWireType.Varint:
                    ReadUInt64();
                    break;
                case ProtoWireType.Fixed64:
                    Advance(8);
                    break;
                case ProtoWireType.LengthDelimited:
                    _ = ReadBytes();
                    break;
                case ProtoWireType.Fixed32:
                    Advance(4);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported protobuf wire type: {wireType}.");
            }
        }

        private void Advance(int count)
        {
            if (_position + count > _data.Length)
            {
                throw new InvalidDataException("Malformed protobuf field length.");
            }

            _position += count;
        }
    }
}
