using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public sealed class FastModItemIndexService : IFastModItemIndexService, IContextAwareFastModItemIndexService
{
    private readonly IDbpfResourceReader _resourceReader;

    public FastModItemIndexService(IDbpfResourceReader? resourceReader = null)
    {
        _resourceReader = resourceReader ?? new DbpfResourceReader();
    }

    public Task<ModItemFastIndexBuildResult> BuildFastPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var parseContext = ModPackageParseContext.Create(packagePath);
        return ((IContextAwareFastModItemIndexService)this).BuildFastPackageAsync(parseContext, cancellationToken);
    }

    Task<ModItemFastIndexBuildResult> IContextAwareFastModItemIndexService.BuildFastPackageAsync(
        ModPackageParseContext parseContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = parseContext.PackageIndex.Entries
            .Where(entry => !entry.IsDeleted && Sims4ResourceTypeRegistry.IsSupportedGameItemType(entry.Type))
            .ToArray();
        var now = DateTime.UtcNow.Ticks;
        var items = new List<ModIndexedItemRecord>(entries.Length);

        using var session = _resourceReader.OpenSession(parseContext.PackagePath);

        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceResourceKey = $"{entry.Type:X8}:{entry.Group:X8}:{entry.Instance:X16}";
            if (entry.Type == Sims4ResourceTypeRegistry.CasPart &&
                session.TryReadBytes(entry, out var bytes, out _) &&
                Sims4CasPartParser.TryParse(new DbpfResourceKey(entry.Type, entry.Group, entry.Instance), bytes, out var casPart, out _))
            {
                var bodyTypeText = Sims4BodyTypeCatalog.ResolveDisplayName(casPart.BodyTypeNumeric);
                var entitySubType = Sims4BodyTypeCatalog.ResolveSubTypeCode(casPart.BodyTypeNumeric);
                var displayName = $"{bodyTypeText} 0x{entry.Instance:X8}";
                items.Add(new ModIndexedItemRecord
                {
                    ItemKey = $"{parseContext.PackagePath}|{sourceResourceKey}",
                    PackagePath = parseContext.PackagePath,
                    PackageFingerprintLength = parseContext.FileLength,
                    PackageFingerprintLastWriteUtcTicks = parseContext.LastWriteUtcTicks,
                    EntityKind = "Cas",
                    EntitySubType = entitySubType,
                    DisplayName = displayName,
                    SortName = displayName,
                    SearchText = BuildSearchText(displayName, bodyTypeText, parseContext.PackagePath),
                    ScopeText = "Cas",
                    ThumbnailStatus = "Placeholder",
                    PrimaryTextureResourceKey = null,
                    PrimaryTextureFormat = null,
                    PrimaryTextureWidth = null,
                    PrimaryTextureHeight = null,
                    TextureCount = 0,
                    EditableTextureCount = 0,
                    HasTextureData = false,
                    SourceResourceKey = sourceResourceKey,
                    SourceGroupText = $"{entry.Group:X8}",
                    CreatedUtcTicks = now,
                    UpdatedUtcTicks = now,
                    SortKeyStable = BuildStableSortKey("Cas", entitySubType, entry.Instance),
                    DisplayStage = ModItemDisplayStage.Fast,
                    ThumbnailStage = ModItemThumbnailStage.None,
                    TextureStage = ModItemTextureStage.Pending,
                    ThumbnailCacheKey = null,
                    LastFastParsedUtcTicks = now,
                    LastDeepParsedUtcTicks = null,
                    PendingDeepRefresh = true,
                    PartNameRaw = casPart.PartNameRaw,
                    DisplayNameSource = "Fallback",
                    TitleKey = casPart.TitleKey == 0 ? null : casPart.TitleKey,
                    PartDescriptionKey = casPart.PartDescriptionKey == 0 ? null : casPart.PartDescriptionKey,
                    BodyTypeNumeric = casPart.BodyTypeNumeric,
                    BodyTypeText = bodyTypeText,
                    BodySubTypeNumeric = casPart.BodySubTypeNumeric,
                    AgeGenderFlags = casPart.AgeGenderFlags,
                    AgeGenderText = Sims4AgeGenderCatalog.Describe(casPart.AgeGenderFlags),
                    SpeciesNumeric = casPart.SpeciesNumeric,
                    SpeciesText = ResolveSpeciesText(casPart.SpeciesNumeric),
                    OutfitId = casPart.OutfitId,
                    TextureCandidates = Array.Empty<SimsModDesktop.Application.TextureCompression.ModPackageTextureCandidate>()
                });
                continue;
            }

            var buildBuyDisplay = $"Build/Buy 0x{entry.Instance:X8}";
            items.Add(new ModIndexedItemRecord
            {
                ItemKey = $"{parseContext.PackagePath}|{sourceResourceKey}",
                PackagePath = parseContext.PackagePath,
                PackageFingerprintLength = parseContext.FileLength,
                PackageFingerprintLastWriteUtcTicks = parseContext.LastWriteUtcTicks,
                EntityKind = "BuildBuy",
                EntitySubType = "Object",
                DisplayName = buildBuyDisplay,
                SortName = buildBuyDisplay,
                SearchText = $"{buildBuyDisplay} {Path.GetFileNameWithoutExtension(parseContext.PackagePath)} BuildBuy Object",
                ScopeText = "BuildBuy",
                ThumbnailStatus = "Placeholder",
                PrimaryTextureResourceKey = null,
                PrimaryTextureFormat = null,
                PrimaryTextureWidth = null,
                PrimaryTextureHeight = null,
                TextureCount = 0,
                EditableTextureCount = 0,
                HasTextureData = false,
                SourceResourceKey = sourceResourceKey,
                SourceGroupText = $"{entry.Group:X8}",
                CreatedUtcTicks = now,
                UpdatedUtcTicks = now,
                SortKeyStable = BuildStableSortKey("BuildBuy", "Object", entry.Instance),
                DisplayStage = ModItemDisplayStage.Fast,
                ThumbnailStage = ModItemThumbnailStage.None,
                TextureStage = ModItemTextureStage.Pending,
                ThumbnailCacheKey = null,
                LastFastParsedUtcTicks = now,
                LastDeepParsedUtcTicks = null,
                PendingDeepRefresh = true,
                DisplayNameSource = "Fallback",
                TextureCandidates = Array.Empty<SimsModDesktop.Application.TextureCompression.ModPackageTextureCandidate>()
            });
        }

        return Task.FromResult(new ModItemFastIndexBuildResult
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = parseContext.PackagePath,
                FileLength = parseContext.FileLength,
                LastWriteUtcTicks = parseContext.LastWriteUtcTicks,
                PackageType = parseContext.FileName.Contains("override", StringComparison.OrdinalIgnoreCase) ? "Override" : ".package",
                ScopeHint = ResolveScopeHint(items),
                IndexedUtcTicks = now,
                ItemCount = items.Count,
                CasItemCount = items.Count(item => string.Equals(item.EntityKind, "Cas", StringComparison.OrdinalIgnoreCase)),
                BuildBuyItemCount = items.Count(item => string.Equals(item.EntityKind, "BuildBuy", StringComparison.OrdinalIgnoreCase)),
                UnclassifiedEntityCount = Math.Max(0, entries.Length - items.Count),
                TextureResourceCount = 0,
                EditableTextureCount = 0,
                Status = "FastReady",
                FailureMessage = null
            },
            Items = items
        });
    }

    private static string BuildSearchText(string displayName, string bodyTypeText, string packagePath)
    {
        return string.Join(' ',
            new[]
            {
                displayName,
                bodyTypeText,
                Path.GetFileNameWithoutExtension(packagePath),
                "Cas"
            }.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string BuildStableSortKey(string entityKind, string entitySubType, ulong instance)
    {
        return $"{entityKind}:{entitySubType}:{instance:X16}";
    }

    private static string ResolveScopeHint(IReadOnlyList<ModIndexedItemRecord> items)
    {
        if (items.Count == 0)
        {
            return "All";
        }

        if (items.All(item => string.Equals(item.EntityKind, "Cas", StringComparison.OrdinalIgnoreCase)))
        {
            return "CAS";
        }

        if (items.All(item => string.Equals(item.EntityKind, "BuildBuy", StringComparison.OrdinalIgnoreCase)))
        {
            return "BuildBuy";
        }

        return "Mixed";
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
}
