using System.Globalization;

namespace SimsModDesktop.Infrastructure.Tray;

internal static class TrayPreviewSnapshotPersistence
{
    public static string BuildRootFingerprint(
        string normalizedTrayRoot,
        long directoryWriteUtcTicks,
        IReadOnlyCollection<PreviewRowDescriptor> rows)
    {
        unchecked
        {
            long hash = 17;
            hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(normalizedTrayRoot);
            hash = (hash * 31) + directoryWriteUtcTicks.GetHashCode();
            hash = (hash * 31) + rows.Count.GetHashCode();

            foreach (var row in rows)
            {
                hash = (hash * 31) + StringComparer.OrdinalIgnoreCase.GetHashCode(row.Group.Key);
                hash = (hash * 31) + row.FileCount.GetHashCode();
                hash = (hash * 31) + row.TotalBytes.GetHashCode();
                hash = (hash * 31) + row.LatestWriteTimeLocal.Ticks.GetHashCode();
            }

            return string.Join(
                "-",
                directoryWriteUtcTicks.ToString("x16", CultureInfo.InvariantCulture),
                rows.Count.ToString("x8", CultureInfo.InvariantCulture),
                hash.ToString("x16", CultureInfo.InvariantCulture));
        }
    }

    public static TrayPreviewRootSnapshotRecord ToRootSnapshotRecord(RootSnapshot snapshot)
    {
        return new TrayPreviewRootSnapshotRecord
        {
            TrayRootPath = snapshot.NormalizedTrayRoot,
            DirectoryWriteUtcTicks = snapshot.DirectoryWriteUtcTicks,
            RootFingerprint = snapshot.RootFingerprint,
            CachedAtUtc = snapshot.CachedAtUtc,
            Items = snapshot.RowDescriptors
                .Select(row => new TrayPreviewRootItemRecord
                {
                    Group = ToGroupRecord(row.Group),
                    ChildGroups = row.ChildGroups.Select(ToGroupRecord).ToArray(),
                    PresetType = row.PresetType,
                    ItemName = row.ItemName,
                    FileListPreview = row.FileListPreview,
                    NormalizedFallbackSearchText = row.NormalizedFallbackSearchText,
                    FileCount = row.FileCount,
                    TotalBytes = row.TotalBytes,
                    LatestWriteUtcTicks = row.LatestWriteTimeLocal == DateTime.MinValue
                        ? 0
                        : row.LatestWriteTimeLocal.ToUniversalTime().Ticks
                })
                .ToArray()
        };
    }

    public static RootSnapshot CreateRootSnapshot(TrayPreviewRootSnapshotRecord record)
    {
        return new RootSnapshot
        {
            SourceKind = PreviewSourceKind.TrayRoot,
            SourceKey = record.TrayRootPath,
            NormalizedTrayRoot = record.TrayRootPath,
            DirectoryWriteUtcTicks = record.DirectoryWriteUtcTicks,
            RootFingerprint = record.RootFingerprint,
            CachedAtUtc = record.CachedAtUtc,
            RowDescriptors = record.Items
                .Select(item => new PreviewRowDescriptor
                {
                    Group = ToGroupAccumulator(item.Group),
                    ChildGroups = item.ChildGroups.Select(ToGroupAccumulator).ToArray(),
                    PresetType = item.PresetType,
                    ItemName = item.ItemName,
                    FileListPreview = item.FileListPreview,
                    NormalizedFallbackSearchText = item.NormalizedFallbackSearchText,
                    FileCount = item.FileCount,
                    TotalBytes = item.TotalBytes,
                    LatestWriteTimeLocal = item.LatestWriteUtcTicks <= 0
                        ? DateTime.MinValue
                        : new DateTime(item.LatestWriteUtcTicks, DateTimeKind.Utc).ToLocalTime()
                })
                .ToArray()
        };
    }

    private static TrayPreviewGroupRecord ToGroupRecord(GroupAccumulator group)
    {
        return new TrayPreviewGroupRecord
        {
            Key = group.Key,
            ItemName = group.ItemName,
            TrayInstanceId = group.TrayInstanceId,
            TrayItemPath = group.TrayItemPath,
            HasHouseholdAnchorFile = group.HasHouseholdAnchorFile,
            FileCount = group.FileCount,
            TotalBytes = group.TotalBytes,
            LatestWriteUtcTicks = group.LatestWriteTimeUtc == DateTime.MinValue
                ? 0
                : group.LatestWriteTimeUtc.ToUniversalTime().Ticks,
            ResourceTypes = group.ResourceTypes
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Extensions = group.Extensions
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            FileNames = group.FileNames.ToArray(),
            SourceFiles = group.SourceFiles.ToArray()
        };
    }

    private static GroupAccumulator ToGroupAccumulator(TrayPreviewGroupRecord record)
    {
        var group = new GroupAccumulator(record.Key)
        {
            ItemName = record.ItemName,
            TrayInstanceId = record.TrayInstanceId,
            TrayItemPath = record.TrayItemPath,
            HasHouseholdAnchorFile = record.HasHouseholdAnchorFile,
            FileCount = record.FileCount,
            TotalBytes = record.TotalBytes,
            LatestWriteTimeUtc = record.LatestWriteUtcTicks <= 0
                ? DateTime.MinValue
                : new DateTime(record.LatestWriteUtcTicks, DateTimeKind.Utc)
        };

        foreach (var value in record.ResourceTypes)
        {
            group.ResourceTypes.Add(value);
        }

        foreach (var value in record.Extensions)
        {
            group.Extensions.Add(value);
        }

        group.FileNames.AddRange(record.FileNames);
        group.SourceFiles.AddRange(record.SourceFiles);
        return group;
    }
}
