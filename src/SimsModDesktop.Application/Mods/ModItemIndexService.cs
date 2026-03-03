using SimsModDesktop.Application.TextureCompression;
using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Mods;

public sealed class ModItemIndexService : IModItemIndexService
{
    private readonly IModItemIndexStore _store;
    private readonly IModPackageTextureAnalysisService _textureAnalysisService;
    private readonly ICasItemDescriptorService _casDescriptorService;
    private readonly IBuildBuyItemDescriptorService _buildBuyDescriptorService;

    public ModItemIndexService(
        IModItemIndexStore store,
        IModPackageTextureAnalysisService textureAnalysisService,
        ICasItemDescriptorService? casDescriptorService = null,
        IBuildBuyItemDescriptorService? buildBuyDescriptorService = null)
    {
        _store = store;
        _textureAnalysisService = textureAnalysisService;
        _casDescriptorService = casDescriptorService ?? new CasItemDescriptorService();
        _buildBuyDescriptorService = buildBuyDescriptorService ?? new BuildBuyPlaceholderDescriptorService();
    }

    public async Task<ModItemIndexBuildResult> RebuildPackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = Path.GetFullPath(packagePath.Trim());
        var fileInfo = new FileInfo(fullPath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException("Package file was not found.", fullPath);
        }

        var index = DbpfPackageIndexReader.ReadPackageIndex(fullPath);
        var entries = index.Entries
            .Where(entry => !entry.IsDeleted && Sims4ResourceTypeRegistry.IsSupportedGameItemType(entry.Type))
            .ToArray();
        var textureResult = await _textureAnalysisService.AnalyzeResultAsync(fullPath, cancellationToken).ConfigureAwait(false);
        var packageCandidates = textureResult.Candidates.Where(candidate => candidate.Editable).ToArray();
        var now = DateTime.UtcNow.Ticks;
        var items = new List<ModIndexedItemRecord>(entries.Length);
        items.AddRange(_casDescriptorService.BuildCasItems(fullPath, index, packageCandidates, fileInfo, now));
        items.AddRange(_buildBuyDescriptorService.BuildItems(fullPath, index, packageCandidates, fileInfo, now));

        var buildResult = new ModItemIndexBuildResult
        {
            PackageState = new ModPackageIndexState
            {
                PackagePath = fullPath,
                FileLength = fileInfo.Length,
                LastWriteUtcTicks = fileInfo.LastWriteTimeUtc.Ticks,
                PackageType = Path.GetFileName(fullPath).Contains("override", StringComparison.OrdinalIgnoreCase) ? "Override" : ".package",
                ScopeHint = ResolveScopeHint(items),
                IndexedUtcTicks = now,
                ItemCount = items.Count,
                CasItemCount = items.Count(item => string.Equals(item.EntityKind, "Cas", StringComparison.OrdinalIgnoreCase)),
                BuildBuyItemCount = items.Count(item => string.Equals(item.EntityKind, "BuildBuy", StringComparison.OrdinalIgnoreCase)),
                UnclassifiedEntityCount = Math.Max(0, entries.Length - items.Count),
                TextureResourceCount = textureResult.Summary.TextureResourceCount,
                EditableTextureCount = textureResult.Summary.EditableTextureCount,
                Status = "Ready",
                FailureMessage = null
            },
            Items = items
        };

        await _store.ReplacePackageAsync(buildResult, cancellationToken).ConfigureAwait(false);
        return buildResult;
    }

    public Task InvalidatePackageAsync(
        string packagePath,
        CancellationToken cancellationToken = default)
    {
        return _store.DeletePackageAsync(packagePath, cancellationToken);
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
