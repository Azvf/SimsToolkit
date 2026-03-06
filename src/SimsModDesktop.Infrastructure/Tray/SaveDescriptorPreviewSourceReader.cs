namespace SimsModDesktop.Infrastructure.Tray;

internal sealed class SaveDescriptorPreviewSourceReader
{
    private readonly ISavePreviewDescriptorStore _savePreviewDescriptorStore;

    public SaveDescriptorPreviewSourceReader(ISavePreviewDescriptorStore savePreviewDescriptorStore)
    {
        _savePreviewDescriptorStore = savePreviewDescriptorStore;
    }

    public RootSnapshot Read(string normalizedSavePath, Func<string, long, IReadOnlyCollection<PreviewRowDescriptor>, string> buildRootFingerprint)
    {
        if (!_savePreviewDescriptorStore.TryLoadDescriptor(normalizedSavePath, out var manifest) ||
            !_savePreviewDescriptorStore.IsDescriptorCurrent(normalizedSavePath, manifest))
        {
            throw new InvalidOperationException($"Save preview descriptor is not ready: {normalizedSavePath}");
        }

        var rows = manifest.Entries
            .OrderBy(entry => entry.DisplayTitle, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.HouseholdId)
            .Select(entry => CreateRowDescriptor(manifest, entry))
            .ToList();

        return new RootSnapshot
        {
            SourceKind = PreviewSourceKind.SaveDescriptor,
            SourceKey = normalizedSavePath,
            NormalizedTrayRoot = normalizedSavePath,
            DirectoryWriteUtcTicks = manifest.SourceLastWriteTimeUtc.Ticks,
            RootFingerprint = buildRootFingerprint(
                normalizedSavePath,
                manifest.SourceLastWriteTimeUtc.Ticks,
                rows),
            RowDescriptors = rows,
            CachedAtUtc = DateTime.UtcNow
        };
    }

    private static PreviewRowDescriptor CreateRowDescriptor(
        SavePreviewDescriptorManifest manifest,
        SavePreviewDescriptorEntry entry)
    {
        var group = new GroupAccumulator(entry.TrayItemKey)
        {
            ItemName = entry.DisplayTitle,
            TrayInstanceId = entry.StableInstanceIdHex,
            TrayItemPath = string.Empty,
            HasHouseholdAnchorFile = true,
            FileCount = 1,
            TotalBytes = 0,
            LatestWriteTimeUtc = manifest.SourceLastWriteTimeUtc
        };
        group.Extensions.Add(".trayitem");
        group.FileNames.Add($"{entry.DisplayTitle}.trayitem");

        return new PreviewRowDescriptor
        {
            Group = group,
            ChildGroups = Array.Empty<GroupAccumulator>(),
            PresetType = "Household",
            ItemName = entry.DisplayTitle,
            FileListPreview = $"{Math.Max(0, entry.HouseholdSize)} sims",
            NormalizedFallbackSearchText = PreviewRowTextUtilities.NormalizeSearch(entry.SearchText),
            FileCount = 1,
            TotalBytes = 0,
            LatestWriteTimeLocal = manifest.SourceLastWriteTimeUtc == DateTime.MinValue
                ? DateTime.MinValue
                : manifest.SourceLastWriteTimeUtc.ToLocalTime(),
            SaveDescriptorEntry = entry,
            SaveDescriptorSourcePath = manifest.SourceSavePath,
            SaveDescriptorSourceLastWriteUtcTicks = manifest.SourceLastWriteTimeUtc.Ticks,
            SaveDescriptorSchemaVersion = manifest.DescriptorSchemaVersion
        };
    }
}
