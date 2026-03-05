using System.Buffers;
using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public sealed class CasItemDescriptorService : ICasItemDescriptorService
{
    private readonly IDbpfResourceReader _resourceReader;
    private readonly ISims4StblLookup _stblLookup;

    public CasItemDescriptorService(
        IDbpfResourceReader? resourceReader = null,
        ISims4StblLookup? stblLookup = null)
    {
        _resourceReader = resourceReader ?? new DbpfResourceReader();
        _stblLookup = stblLookup ?? new Sims4StblLookup();
    }

    public IReadOnlyList<ModIndexedItemRecord> BuildCasItems(
        string packagePath,
        DbpfPackageIndex index,
        IReadOnlyList<ModPackageTextureCandidate> textureCandidates,
        FileInfo fileInfo,
        long nowUtcTicks)
    {
        var entries = index.Entries
            .Where(entry => !entry.IsDeleted && entry.Type == Sims4ResourceTypeRegistry.CasPart)
            .ToArray();
        var textureMap = textureCandidates
            .GroupBy(candidate => candidate.ResourceKeyText, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var totalGameItems = index.Entries.Count(entry => !entry.IsDeleted && Sims4ResourceTypeRegistry.IsSupportedGameItemType(entry.Type));
        var items = new List<ModIndexedItemRecord>(entries.Length);

        using var session = _resourceReader.OpenSession(packagePath);
        var payload = new ArrayBufferWriter<byte>();

        foreach (var entry in entries)
        {
            payload.Clear();
            if (!session.TryReadInto(entry, payload, out _) ||
                !Sims4CasPartExtendedParser.TryParse(new DbpfResourceKey(entry.Type, entry.Group, entry.Instance), payload.WrittenSpan, out var casPartExtended, out _))
            {
                continue;
            }

            var casPart = casPartExtended.BaseInfo;
            var linkedTextures = ResolveTextures(casPart, textureMap, textureCandidates, entry, index.Entries.Length, totalGameItems);
            var primaryTexture = SelectPrimaryTexture(casPart, linkedTextures);
            var displayName = ResolveDisplayName(session, index, casPart, entry.Instance, out var displayNameSource);
            var bodyTypeText = Sims4BodyTypeCatalog.ResolveDisplayName(casPart.BodyTypeNumeric);
            var entitySubType = Sims4BodyTypeCatalog.ResolveSubTypeCode(casPart.BodyTypeNumeric);
            var ageGenderText = Sims4AgeGenderCatalog.Describe(casPart.AgeGenderFlags);
            var speciesText = ResolveSpeciesText(casPart.SpeciesNumeric);
            var sourceResourceKey = $"{entry.Type:X8}:{entry.Group:X8}:{entry.Instance:X16}";

            items.Add(new ModIndexedItemRecord
            {
                ItemKey = $"{packagePath}|{sourceResourceKey}",
                PackagePath = packagePath,
                PackageFingerprintLength = fileInfo.Length,
                PackageFingerprintLastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                EntityKind = "Cas",
                EntitySubType = entitySubType,
                DisplayName = displayName,
                SortName = displayName,
                SearchText = BuildSearchText(displayName, bodyTypeText, ageGenderText, speciesText, packagePath, entitySubType),
                ScopeText = "Cas",
                ThumbnailStatus = primaryTexture is null ? "None" : "TextureLinked",
                PrimaryTextureResourceKey = primaryTexture?.ResourceKeyText,
                PrimaryTextureFormat = primaryTexture?.Format,
                PrimaryTextureWidth = primaryTexture?.Width,
                PrimaryTextureHeight = primaryTexture?.Height,
                TextureCount = linkedTextures.Count,
                EditableTextureCount = linkedTextures.Count(candidate => candidate.Editable),
                HasTextureData = linkedTextures.Count > 0,
                SourceResourceKey = sourceResourceKey,
                SourceGroupText = $"{entry.Group:X8}",
                CreatedUtcTicks = nowUtcTicks,
                UpdatedUtcTicks = nowUtcTicks,
                PartNameRaw = casPart.PartNameRaw,
                DisplayNameSource = displayNameSource,
                TitleKey = casPart.TitleKey == 0 ? null : casPart.TitleKey,
                PartDescriptionKey = casPart.PartDescriptionKey == 0 ? null : casPart.PartDescriptionKey,
                BodyTypeNumeric = casPart.BodyTypeNumeric,
                BodyTypeText = bodyTypeText,
                BodySubTypeNumeric = casPart.BodySubTypeNumeric,
                AgeGenderFlags = casPart.AgeGenderFlags,
                AgeGenderText = ageGenderText,
                SpeciesNumeric = casPart.SpeciesNumeric,
                SpeciesText = speciesText,
                OutfitId = casPart.OutfitId,
                TextureCandidates = linkedTextures
            });
        }

        return items;
    }

    private string ResolveDisplayName(
        DbpfPackageReadSession session,
        DbpfPackageIndex index,
        Sims4CasPartInfo casPart,
        ulong instance,
        out string source)
    {
        var localized = _stblLookup.TryResolveString(session, index, casPart.TitleKey);
        if (!string.IsNullOrWhiteSpace(localized))
        {
            source = "Stbl";
            return localized.Trim();
        }

        if (!string.IsNullOrWhiteSpace(casPart.PartNameRaw))
        {
            source = "PartName";
            return casPart.PartNameRaw.Trim();
        }

        source = "Fallback";
        return $"{Sims4BodyTypeCatalog.ResolveDisplayName(casPart.BodyTypeNumeric)} 0x{instance:X8}";
    }

    private static IReadOnlyList<ModPackageTextureCandidate> ResolveTextures(
        Sims4CasPartInfo casPart,
        IReadOnlyDictionary<string, ModPackageTextureCandidate> textureMap,
        IReadOnlyList<ModPackageTextureCandidate> allCandidates,
        DbpfIndexEntry sourceEntry,
        int packageEntryCount,
        int totalGameItems)
    {
        var strongMatches = new List<ModPackageTextureCandidate>();

        AddStrongMatch(strongMatches, textureMap, casPart.TextureRefs.Diffuse, "Diffuse");
        AddStrongMatch(strongMatches, textureMap, casPart.TextureRefs.Normal, "Normal");
        AddStrongMatch(strongMatches, textureMap, casPart.TextureRefs.Specular, "Specular");
        AddStrongMatch(strongMatches, textureMap, casPart.TextureRefs.Emission, "Emission");
        AddStrongMatch(strongMatches, textureMap, casPart.TextureRefs.Shadow, "Shadow");

        if (strongMatches.Count > 0)
        {
            return strongMatches
                .DistinctBy(candidate => candidate.ResourceKeyText, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var fallbackMatches = allCandidates
            .Where(candidate => LooksLikeFallbackTexture(candidate, sourceEntry))
            .OrderByDescending(candidate => candidate.Width * candidate.Height)
            .ThenByDescending(candidate => candidate.SizeBytes)
            .Select(candidate => CloneWithLinkRole(candidate, "Fallback"))
            .ToArray();

        if (fallbackMatches.Length > 0)
        {
            return fallbackMatches;
        }

        if (packageEntryCount == 1 && totalGameItems == 1)
        {
            return allCandidates
                .Select(candidate => CloneWithLinkRole(candidate, "Fallback"))
                .ToArray();
        }

        return Array.Empty<ModPackageTextureCandidate>();
    }

    private static void AddStrongMatch(
        ICollection<ModPackageTextureCandidate> destination,
        IReadOnlyDictionary<string, ModPackageTextureCandidate> textureMap,
        DbpfResourceKey? key,
        string linkRole)
    {
        if (key is null)
        {
            return;
        }

        var keyText = $"{key.Value.Type:X8}:{key.Value.Group:X8}:{key.Value.Instance:X16}";
        if (textureMap.TryGetValue(keyText, out var candidate))
        {
            destination.Add(CloneWithLinkRole(candidate, linkRole));
        }
    }

    private static bool LooksLikeFallbackTexture(ModPackageTextureCandidate candidate, DbpfIndexEntry sourceEntry)
    {
        if (!TryParseResourceKey(candidate.ResourceKeyText, out _, out var group, out var instance))
        {
            return false;
        }

        if (group != sourceEntry.Group)
        {
            return false;
        }

        var sourceHigh = (uint)(sourceEntry.Instance >> 32);
        var sourceLow = (uint)(sourceEntry.Instance & 0xFFFFFFFF);
        var candidateHigh = (uint)(instance >> 32);
        var candidateLow = (uint)(instance & 0xFFFFFFFF);

        return sourceHigh == candidateHigh ||
               (sourceLow & 0xFFFF) == (candidateLow & 0xFFFF);
    }

    private static ModPackageTextureCandidate? SelectPrimaryTexture(
        Sims4CasPartInfo casPart,
        IReadOnlyList<ModPackageTextureCandidate> linkedTextures)
    {
        var diffuseKey = casPart.TextureRefs.Diffuse is null
            ? null
            : $"{casPart.TextureRefs.Diffuse.Value.Type:X8}:{casPart.TextureRefs.Diffuse.Value.Group:X8}:{casPart.TextureRefs.Diffuse.Value.Instance:X16}";
        var diffuse = diffuseKey is null
            ? null
            : linkedTextures.FirstOrDefault(candidate => string.Equals(candidate.ResourceKeyText, diffuseKey, StringComparison.OrdinalIgnoreCase));

        if (diffuse is not null)
        {
            return diffuse;
        }

        return linkedTextures
            .OrderByDescending(candidate => candidate.Editable)
            .ThenByDescending(candidate => candidate.Width * candidate.Height)
            .ThenByDescending(candidate => candidate.SizeBytes)
            .FirstOrDefault();
    }

    private static string BuildSearchText(
        string displayName,
        string bodyTypeText,
        string ageGenderText,
        string speciesText,
        string packagePath,
        string entitySubType)
    {
        var parts = new[]
        {
            displayName,
            bodyTypeText,
            ageGenderText,
            speciesText,
            Path.GetFileNameWithoutExtension(packagePath),
            "Cas",
            entitySubType
        };

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string ResolveSpeciesText(uint species)
    {
        return species switch
        {
            1 => "Human",
            2 => "Dog",
            3 => "Cat",
            4 => "LittleDog",
            5 => "Fox",
            6 => "Horse",
            _ => "Unknown"
        };
    }

    private static bool TryParseResourceKey(string resourceKeyText, out uint type, out uint group, out ulong instance)
    {
        type = 0;
        group = 0;
        instance = 0;
        var parts = resourceKeyText.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        return uint.TryParse(parts[0], System.Globalization.NumberStyles.HexNumber, null, out type) &&
               uint.TryParse(parts[1], System.Globalization.NumberStyles.HexNumber, null, out group) &&
               ulong.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out instance);
    }

    private static ModPackageTextureCandidate CloneWithLinkRole(ModPackageTextureCandidate candidate, string linkRole)
    {
        return new ModPackageTextureCandidate
        {
            ResourceKeyText = candidate.ResourceKeyText,
            ContainerKind = candidate.ContainerKind,
            Format = candidate.Format,
            Width = candidate.Width,
            Height = candidate.Height,
            MipMapCount = candidate.MipMapCount,
            Editable = candidate.Editable,
            SuggestedAction = candidate.SuggestedAction,
            Notes = candidate.Notes,
            SizeBytes = candidate.SizeBytes,
            LinkRole = linkRole
        };
    }
}
