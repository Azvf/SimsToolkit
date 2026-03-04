using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public sealed class DeepModItemEnrichmentService : IDeepModItemEnrichmentService, IContextAwareDeepModItemEnrichmentService
{
    private readonly IModPackageTextureAnalysisService _textureAnalysisService;
    private readonly ICasItemDescriptorService _casDescriptorService;
    private readonly IBuildBuyItemDescriptorService _buildBuyDescriptorService;

    public DeepModItemEnrichmentService(
        IModPackageTextureAnalysisService textureAnalysisService,
        ICasItemDescriptorService? casDescriptorService = null,
        IBuildBuyItemDescriptorService? buildBuyDescriptorService = null)
    {
        _textureAnalysisService = textureAnalysisService;
        _casDescriptorService = casDescriptorService ?? new CasItemDescriptorService();
        _buildBuyDescriptorService = buildBuyDescriptorService ?? new BuildBuyPlaceholderDescriptorService();
    }

    public Task<ModItemEnrichmentBatch> EnrichPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        var parseContext = ModPackageParseContext.Create(packagePath);
        return ((IContextAwareDeepModItemEnrichmentService)this).EnrichPackageAsync(parseContext, cancellationToken);
    }

    async Task<ModItemEnrichmentBatch> IContextAwareDeepModItemEnrichmentService.EnrichPackageAsync(
        ModPackageParseContext parseContext,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entries = parseContext.PackageIndex.Entries
            .Where(entry => !entry.IsDeleted && Sims4ResourceTypeRegistry.IsSupportedGameItemType(entry.Type))
            .ToArray();
        var textureResult = await _textureAnalysisService.AnalyzeResultAsync(parseContext.PackagePath, cancellationToken).ConfigureAwait(false);
        var packageCandidates = textureResult.Candidates.Where(candidate => candidate.Editable).ToArray();
        var now = DateTime.UtcNow.Ticks;
        var items = new List<ModIndexedItemRecord>(entries.Length);
        items.AddRange(_casDescriptorService.BuildCasItems(parseContext.PackagePath, parseContext.PackageIndex, packageCandidates, new FileInfo(parseContext.PackagePath), now));
        items.AddRange(_buildBuyDescriptorService.BuildItems(parseContext.PackagePath, parseContext.PackageIndex, packageCandidates, new FileInfo(parseContext.PackagePath), now));

        var enrichedItems = items.Select(item => new ModIndexedItemRecord
        {
            ItemKey = item.ItemKey,
            PackagePath = item.PackagePath,
            PackageFingerprintLength = item.PackageFingerprintLength,
            PackageFingerprintLastWriteUtcTicks = item.PackageFingerprintLastWriteUtcTicks,
            EntityKind = item.EntityKind,
            EntitySubType = item.EntitySubType,
            DisplayName = item.DisplayName,
            SortName = item.SortName,
            SearchText = item.SearchText,
            ScopeText = item.ScopeText,
            ThumbnailStatus = item.ThumbnailStatus,
            PrimaryTextureResourceKey = item.PrimaryTextureResourceKey,
            PrimaryTextureFormat = item.PrimaryTextureFormat,
            PrimaryTextureWidth = item.PrimaryTextureWidth,
            PrimaryTextureHeight = item.PrimaryTextureHeight,
            TextureCount = item.TextureCount,
            EditableTextureCount = item.EditableTextureCount,
            HasTextureData = item.HasTextureData,
            SourceResourceKey = item.SourceResourceKey,
            SourceGroupText = item.SourceGroupText,
            CreatedUtcTicks = item.CreatedUtcTicks,
            UpdatedUtcTicks = item.UpdatedUtcTicks,
            SortKeyStable = string.IsNullOrWhiteSpace(item.SortKeyStable)
                ? $"{item.EntityKind}:{item.EntitySubType}:{ParseInstance(item.SourceResourceKey):X16}"
                : item.SortKeyStable,
            DisplayStage = item.TextureCount > 0 ? ModItemDisplayStage.Complete : ModItemDisplayStage.DeepTextureReady,
            ThumbnailStage = ModItemThumbnailStage.None,
            TextureStage = ModItemTextureStage.Ready,
            ThumbnailCacheKey = null,
            LastFastParsedUtcTicks = item.LastFastParsedUtcTicks == 0 ? now : item.LastFastParsedUtcTicks,
            LastDeepParsedUtcTicks = now,
            PendingDeepRefresh = false,
            PartNameRaw = item.PartNameRaw,
            DisplayNameSource = item.DisplayNameSource,
            TitleKey = item.TitleKey,
            PartDescriptionKey = item.PartDescriptionKey,
            BodyTypeNumeric = item.BodyTypeNumeric,
            BodyTypeText = item.BodyTypeText,
            BodySubTypeNumeric = item.BodySubTypeNumeric,
            AgeGenderFlags = item.AgeGenderFlags,
            AgeGenderText = item.AgeGenderText,
            SpeciesNumeric = item.SpeciesNumeric,
            SpeciesText = item.SpeciesText,
            OutfitId = item.OutfitId,
            TextureCandidates = item.TextureCandidates
        }).ToArray();

        return new ModItemEnrichmentBatch
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = parseContext.PackagePath,
                FileLength = parseContext.FileLength,
                LastWriteUtcTicks = parseContext.LastWriteUtcTicks,
                PackageType = parseContext.FileName.Contains("override", StringComparison.OrdinalIgnoreCase) ? "Override" : ".package",
                ScopeHint = ResolveScopeHint(enrichedItems),
                IndexedUtcTicks = now,
                ItemCount = enrichedItems.Length,
                CasItemCount = enrichedItems.Count(item => string.Equals(item.EntityKind, "Cas", StringComparison.OrdinalIgnoreCase)),
                BuildBuyItemCount = enrichedItems.Count(item => string.Equals(item.EntityKind, "BuildBuy", StringComparison.OrdinalIgnoreCase)),
                UnclassifiedEntityCount = Math.Max(0, entries.Length - enrichedItems.Length),
                TextureResourceCount = textureResult.Summary.TextureResourceCount,
                EditableTextureCount = textureResult.Summary.EditableTextureCount,
                Status = "Ready",
                FailureMessage = null
            },
            Items = enrichedItems,
            AffectedItemKeys = enrichedItems.Select(item => item.ItemKey).ToArray()
        };
    }

    private static ulong ParseInstance(string sourceResourceKey)
    {
        var parts = sourceResourceKey.Split(':');
        if (parts.Length != 3)
        {
            return 0;
        }

        return ulong.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out var instance)
            ? instance
            : 0;
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
}
