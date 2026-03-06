using System.Globalization;
using System.Text.RegularExpressions;

namespace SimsModDesktop.Infrastructure.Tray;

internal sealed class TrayRootPreviewSourceReader
{
    private static readonly Regex TrayIdentityRegex = new(
        "^0x([0-9a-fA-F]{1,8})!0x([0-9a-fA-F]{1,16})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".trayitem",
        ".blueprint",
        ".bpi",
        ".room",
        ".rmi",
        ".householdbinary",
        ".hhi",
        ".sgi"
    };

    public RootSnapshot Read(
        string trayPath,
        string normalizedTrayRoot,
        long directoryWriteUtcTicks,
        CancellationToken cancellationToken,
        Action<string, IReadOnlyCollection<string>> scheduleThumbnailCleanup,
        Func<string, long, IReadOnlyCollection<PreviewRowDescriptor>, string> buildRootFingerprint)
    {
        var dir = new DirectoryInfo(trayPath);
        if (!dir.Exists)
        {
            throw new DirectoryNotFoundException($"Tray path does not exist: {trayPath}");
        }

        var fileEntries = dir
            .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
            .Where(file => SupportedExtensions.Contains(file.Extension))
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => new TrayFileEntry(file, ParseIdentity(file.Name)))
            .ToList();

        var groups = new Dictionary<string, GroupAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in fileEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Accumulate(entry, groups);
        }

        var householdAnchorInstances = groups
            .Values
            .Where(group => group.HasHouseholdAnchorFile &&
                            group.Key.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var childParentKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var resolvedRootKeys = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        var childGroupsByParent = new Dictionary<string, List<GroupAccumulator>>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups.Values)
        {
            if (!TryResolveAuxiliaryHouseholdRootKey(
                    group,
                    groups,
                    householdAnchorInstances,
                    resolvedRootKeys,
                    out var parentKey))
            {
                continue;
            }

            childParentKeys[group.Key] = parentKey;
            if (!childGroupsByParent.TryGetValue(parentKey, out var childGroups))
            {
                childGroups = new List<GroupAccumulator>();
                childGroupsByParent[parentKey] = childGroups;
            }

            childGroups.Add(group);
        }

        var orderedRows = groups
            .Values
            .Where(group => !childParentKeys.ContainsKey(group.Key))
            .Select(group => PreviewPageBuilder.CreatePreviewRowDescriptor(
                group,
                childGroupsByParent.TryGetValue(group.Key, out var childGroups)
                    ? childGroups
                    : Array.Empty<GroupAccumulator>()))
            .OrderByDescending(item => item.LatestWriteTimeLocal)
            .ThenByDescending(item => item.FileCount)
            .ThenByDescending(item => item.TotalBytes)
            .ThenBy(item => item.Group.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        scheduleThumbnailCleanup(trayPath, groups.Keys.ToArray());

        return new RootSnapshot
        {
            SourceKind = PreviewSourceKind.TrayRoot,
            SourceKey = normalizedTrayRoot,
            NormalizedTrayRoot = normalizedTrayRoot,
            DirectoryWriteUtcTicks = directoryWriteUtcTicks,
            RootFingerprint = buildRootFingerprint(normalizedTrayRoot, directoryWriteUtcTicks, orderedRows),
            RowDescriptors = orderedRows,
            CachedAtUtc = DateTime.UtcNow
        };
    }

    private static void Accumulate(TrayFileEntry entry, IDictionary<string, GroupAccumulator> groups)
    {
        var file = entry.File;
        var identity = entry.Identity;
        var key = identity.ParseSuccess
            ? identity.InstanceHex
            : Path.GetFileNameWithoutExtension(file.Name);
        if (!groups.TryGetValue(key, out var group))
        {
            group = new GroupAccumulator(key);
            groups[key] = group;
        }

        group.FileCount++;
        group.TotalBytes += file.Length;
        if (file.LastWriteTimeUtc > group.LatestWriteTimeUtc)
        {
            group.LatestWriteTimeUtc = file.LastWriteTimeUtc;
        }

        if (identity.ParseSuccess && !string.IsNullOrWhiteSpace(identity.TypeHex))
        {
            group.ResourceTypes.Add(identity.TypeHex);
            group.RepresentativeIdentity ??= identity;
            if (string.IsNullOrWhiteSpace(group.TrayInstanceId))
            {
                group.TrayInstanceId = identity.InstanceHex;
            }
        }

        if (IsHouseholdAnchorExtension(file.Extension))
        {
            group.HasHouseholdAnchorFile = true;
        }

        group.Extensions.Add(file.Extension);
        group.SourceFiles.Add(file.FullName);
        if (group.FileNames.Count < 12)
        {
            group.FileNames.Add(file.Name);
        }

        if (string.IsNullOrWhiteSpace(group.ItemName) &&
            string.Equals(file.Extension, ".trayitem", StringComparison.OrdinalIgnoreCase))
        {
            group.ItemName = Path.GetFileNameWithoutExtension(file.Name);
        }

        if (string.IsNullOrWhiteSpace(group.TrayItemPath) &&
            string.Equals(file.Extension, ".trayitem", StringComparison.OrdinalIgnoreCase))
        {
            group.TrayItemPath = file.FullName;
        }
    }

    private static bool TryResolveAuxiliaryHouseholdRootKey(
        GroupAccumulator group,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        IReadOnlySet<string> householdAnchorInstances,
        IDictionary<string, string?> resolvedRootKeys,
        out string parentKey)
    {
        parentKey = string.Empty;

        if (resolvedRootKeys.TryGetValue(group.Key, out var cachedRootKey))
        {
            if (string.IsNullOrWhiteSpace(cachedRootKey))
            {
                return false;
            }

            parentKey = cachedRootKey;
            return true;
        }

        var visitedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!TryResolveAuxiliaryHouseholdRootKeyCore(
                group,
                groups,
                householdAnchorInstances,
                resolvedRootKeys,
                visitedKeys,
                out parentKey))
        {
            resolvedRootKeys[group.Key] = null;
            return false;
        }

        resolvedRootKeys[group.Key] = parentKey;
        return true;
    }

    private static bool TryResolveAuxiliaryHouseholdRootKeyCore(
        GroupAccumulator group,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        IReadOnlySet<string> householdAnchorInstances,
        IDictionary<string, string?> resolvedRootKeys,
        ISet<string> visitedKeys,
        out string parentKey)
    {
        parentKey = string.Empty;

        if (group.Extensions.Count != 1 ||
            !group.Extensions.Contains(".sgi", StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!visitedKeys.Add(group.Key) ||
            !TrayPreviewItemUtilities.TryGetAuxiliaryHouseholdMemberSlot(group, out var slot) ||
            group.RepresentativeIdentity is null)
        {
            return false;
        }

        if (!TryResolveAuxiliaryHouseholdDirectParentKey(
                group,
                groups,
                slot,
                out var directParentKey,
                out var directParentGroup))
        {
            return false;
        }

        if (householdAnchorInstances.Contains(directParentKey))
        {
            parentKey = directParentKey;
            resolvedRootKeys[group.Key] = parentKey;
            return true;
        }

        if (!TryResolveAuxiliaryHouseholdRootKeyCore(
                directParentGroup,
                groups,
                householdAnchorInstances,
                resolvedRootKeys,
                visitedKeys,
                out parentKey))
        {
            return false;
        }

        resolvedRootKeys[group.Key] = parentKey;
        return !string.IsNullOrWhiteSpace(parentKey);
    }

    private static bool TryResolveAuxiliaryHouseholdDirectParentKey(
        GroupAccumulator group,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        int slot,
        out string parentKey,
        out GroupAccumulator parentGroup)
    {
        parentKey = string.Empty;
        parentGroup = null!;

        if (group.RepresentativeIdentity is null || slot < 1)
        {
            return false;
        }

        var currentInstanceValue = ulong.Parse(
            group.RepresentativeIdentity.InstanceHex.AsSpan(2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        if (currentInstanceValue <= (ulong)slot)
        {
            return false;
        }

        var reducedValue = currentInstanceValue - (ulong)slot;
        var memberSpaceParentKey = $"0x{reducedValue:x16}";
        if (!memberSpaceParentKey.Equals(group.Key, StringComparison.OrdinalIgnoreCase) &&
            groups.TryGetValue(memberSpaceParentKey, out var resolvedMemberSpaceParent) &&
            resolvedMemberSpaceParent is not null)
        {
            parentGroup = resolvedMemberSpaceParent;
            parentKey = memberSpaceParentKey;
            return true;
        }

        var highByteMask = currentInstanceValue & 0xFF00000000000000UL;
        if (highByteMask == 0 || reducedValue <= highByteMask)
        {
            return false;
        }

        var anchorSpaceValue = reducedValue - highByteMask;
        var anchorSpaceParentKey = $"0x{anchorSpaceValue:x16}";
        if (anchorSpaceParentKey.Equals(group.Key, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (groups.TryGetValue(anchorSpaceParentKey, out var resolvedAnchorSpaceParent) &&
            resolvedAnchorSpaceParent is not null)
        {
            parentGroup = resolvedAnchorSpaceParent;
            parentKey = anchorSpaceParentKey;
            return true;
        }

        return TryResolveAuxiliaryHouseholdAnchorFallbackKey(
            anchorSpaceValue,
            group.Key,
            groups,
            out parentKey,
            out parentGroup);
    }

    private static bool TryResolveAuxiliaryHouseholdAnchorFallbackKey(
        ulong anchorSpaceValue,
        string currentKey,
        IReadOnlyDictionary<string, GroupAccumulator> groups,
        out string parentKey,
        out GroupAccumulator parentGroup)
    {
        parentKey = string.Empty;
        parentGroup = null!;

        for (var offset = 1; offset <= 8; offset++)
        {
            if (anchorSpaceValue < (ulong)offset)
            {
                break;
            }

            var candidateKey = $"0x{anchorSpaceValue - (ulong)offset:x16}";
            if (candidateKey.Equals(currentKey, StringComparison.OrdinalIgnoreCase) ||
                !groups.TryGetValue(candidateKey, out var candidateGroup) ||
                candidateGroup is null ||
                !candidateGroup.HasHouseholdAnchorFile)
            {
                continue;
            }

            parentKey = candidateKey;
            parentGroup = candidateGroup;
            return true;
        }

        return false;
    }

    private static TrayIdentity ParseIdentity(string fileName)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var match = TrayIdentityRegex.Match(baseName);
        if (!match.Success)
        {
            return new TrayIdentity
            {
                ParseSuccess = false,
                TypeHex = string.Empty,
                InstanceHex = string.Empty
            };
        }

        var typeValue = uint.Parse(match.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var instanceValue = ulong.Parse(match.Groups[2].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new TrayIdentity
        {
            ParseSuccess = true,
            TypeHex = $"0x{typeValue:x8}",
            InstanceHex = $"0x{instanceValue:x16}"
        };
    }

    private static bool IsHouseholdAnchorExtension(string extension)
    {
        return string.Equals(extension, ".trayitem", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".householdbinary", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(extension, ".hhi", StringComparison.OrdinalIgnoreCase);
    }
}
