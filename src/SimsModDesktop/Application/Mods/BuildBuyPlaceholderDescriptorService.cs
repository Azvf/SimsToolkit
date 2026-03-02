using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public sealed class BuildBuyPlaceholderDescriptorService : IBuildBuyItemDescriptorService
{
    public IReadOnlyList<ModIndexedItemRecord> BuildItems(
        string packagePath,
        DbpfPackageIndex index,
        IReadOnlyList<ModPackageTextureCandidate> textureCandidates,
        FileInfo fileInfo,
        long nowUtcTicks)
    {
        var entries = index.Entries
            .Where(entry => !entry.IsDeleted && entry.Type == Sims4ResourceTypeRegistry.BuildBuyObject)
            .ToArray();
        var items = new List<ModIndexedItemRecord>(entries.Length);

        foreach (var entry in entries)
        {
            var sourceResourceKey = $"{entry.Type:X8}:{entry.Group:X8}:{entry.Instance:X16}";
            var groupText = $"{entry.Group:X8}";
            var textures = MatchTextures(textureCandidates, entry, index.Entries.Length, entries.Length);
            var primaryTexture = textures
                .OrderByDescending(candidate => candidate.Editable)
                .ThenByDescending(candidate => candidate.Width * candidate.Height)
                .ThenByDescending(candidate => candidate.SizeBytes)
                .FirstOrDefault();

            items.Add(new ModIndexedItemRecord
            {
                ItemKey = $"{packagePath}|{sourceResourceKey}",
                PackagePath = packagePath,
                PackageFingerprintLength = fileInfo.Length,
                PackageFingerprintLastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                EntityKind = "BuildBuy",
                EntitySubType = "Object",
                DisplayName = $"Build/Buy {entry.Instance:X8}",
                SortName = $"Build/Buy {entry.Instance:X8}",
                SearchText = $"Build/Buy {entry.Instance:X8} {Path.GetFileNameWithoutExtension(packagePath)} BuildBuy Object",
                ScopeText = "BuildBuy",
                ThumbnailStatus = primaryTexture is null ? "None" : "TextureLinked",
                PrimaryTextureResourceKey = primaryTexture?.ResourceKeyText,
                PrimaryTextureFormat = primaryTexture?.Format,
                PrimaryTextureWidth = primaryTexture?.Width,
                PrimaryTextureHeight = primaryTexture?.Height,
                TextureCount = textures.Count,
                EditableTextureCount = textures.Count(candidate => candidate.Editable),
                HasTextureData = textures.Count > 0,
                SourceResourceKey = sourceResourceKey,
                SourceGroupText = groupText,
                CreatedUtcTicks = nowUtcTicks,
                UpdatedUtcTicks = nowUtcTicks,
                TextureCandidates = textures
            });
        }

        return items;
    }

    private static IReadOnlyList<ModPackageTextureCandidate> MatchTextures(
        IReadOnlyList<ModPackageTextureCandidate> packageCandidates,
        DbpfIndexEntry entry,
        int packageEntryCount,
        int buildBuyItemCount)
    {
        var groupText = $"{entry.Group:X8}";
        var matches = packageCandidates
            .Where(candidate => TryGetKey(candidate.ResourceKeyText, out _, out var candidateGroup, out _) &&
                                string.Equals(candidateGroup, groupText, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (matches.Length == 0 && packageEntryCount == 1 && buildBuyItemCount == 1)
        {
            return packageCandidates;
        }

        return matches;
    }

    private static bool TryGetKey(string resourceKeyText, out string type, out string group, out string instance)
    {
        type = string.Empty;
        group = string.Empty;
        instance = string.Empty;
        var parts = resourceKeyText.Split(':');
        if (parts.Length != 3)
        {
            return false;
        }

        type = parts[0];
        group = parts[1];
        instance = parts[2];
        return true;
    }
}
