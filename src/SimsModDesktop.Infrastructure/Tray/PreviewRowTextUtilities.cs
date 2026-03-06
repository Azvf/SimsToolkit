using System.Globalization;

namespace SimsModDesktop.Infrastructure.Tray;

internal static class PreviewRowTextUtilities
{
    public static IReadOnlyDictionary<int, TrayMemberDisplayMetadata> CreateMemberMetadataLookup(TrayMetadataResult? metadata)
    {
        if (metadata is null || metadata.Members.Count == 0)
        {
            return new Dictionary<int, TrayMemberDisplayMetadata>();
        }

        return metadata.Members
            .Where(member => member.SlotIndex > 0)
            .GroupBy(member => member.SlotIndex)
            .ToDictionary(
                group => group.Key,
                group => group.First());
    }

    public static string ResolveDefaultTitle(GroupAccumulator group, TrayMetadataResult? metadata)
    {
        if (!string.IsNullOrWhiteSpace(metadata?.Name))
        {
            return metadata.Name;
        }

        return string.IsNullOrWhiteSpace(group.ItemName)
            ? group.Key
            : group.ItemName;
    }

    public static string ResolveFallbackStandaloneTitle(GroupAccumulator group, bool isChildItem)
    {
        if (isChildItem &&
            TrayPreviewItemUtilities.TryGetAuxiliaryHouseholdMemberSlot(group, out var slot))
        {
            return string.Create(CultureInfo.InvariantCulture, $"Member {slot}");
        }

        return string.IsNullOrWhiteSpace(group.ItemName)
            ? group.Key
            : group.ItemName;
    }

    public static string BuildAuthorSearchText(string creatorName, string creatorId)
    {
        if (string.IsNullOrWhiteSpace(creatorName))
        {
            return creatorId;
        }

        if (string.IsNullOrWhiteSpace(creatorId))
        {
            return creatorName;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{creatorName} {creatorId}");
    }

    public static string BuildNormalizedSearchText(
        GroupAccumulator group,
        IReadOnlyList<GroupAccumulator> childGroups,
        TrayMetadataResult? metadata)
    {
        var normalizedParts = new List<string>
        {
            NormalizeSearch(ResolveDefaultTitle(group, metadata)),
            NormalizeSearch(BuildAuthorSearchText(
                metadata?.CreatorName?.Trim() ?? string.Empty,
                metadata?.CreatorId?.Trim() ?? string.Empty)),
            NormalizeSearch(group.Key)
        };

        var memberMetadataBySlot = CreateMemberMetadataLookup(metadata);
        foreach (var childGroup in childGroups
                     .OrderBy(entry => TrayPreviewItemUtilities.TryGetAuxiliaryHouseholdMemberSlot(entry, out var slot) ? slot : int.MaxValue)
                     .ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            normalizedParts.Add(NormalizeSearch(childGroup.Key));

            TrayMemberDisplayMetadata? memberMetadata = null;
            if (TrayPreviewItemUtilities.TryGetAuxiliaryHouseholdMemberSlot(childGroup, out var slot) &&
                memberMetadataBySlot.TryGetValue(slot, out var resolvedMemberMetadata))
            {
                memberMetadata = resolvedMemberMetadata;
            }

            var childTitle = string.IsNullOrWhiteSpace(memberMetadata?.FullName)
                ? ResolveFallbackStandaloneTitle(childGroup, isChildItem: true)
                : memberMetadata!.FullName;
            normalizedParts.Add(NormalizeSearch(childTitle));
        }

        return string.Join("|", normalizedParts.Where(value => value.Length != 0));
    }

    public static string NormalizeSearch(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().ToLowerInvariant();
    }
}
