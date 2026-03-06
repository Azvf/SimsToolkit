using System.Security.Cryptography;
using System.Text;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Application.Saves;

public static class SavePreviewIdentity
{
    public static ulong ComputeStableInstanceId(string saveFilePath, ulong householdId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);

        var normalized = Path.GetFullPath(saveFilePath.Trim());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{normalized}|{householdId:X16}"));
        var value = BitConverter.ToUInt64(bytes, 0);
        return value == 0 ? 1UL : value;
    }

    public static string ComputeTrayItemKey(string saveFilePath, ulong householdId)
    {
        return $"0x{ComputeStableInstanceId(saveFilePath, householdId):X16}";
    }

    public static string ComputeSaveHash(string saveFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);

        var normalized = Path.GetFullPath(saveFilePath.Trim());
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Convert.ToHexString(bytes)[..16];
    }

    public static IReadOnlyDictionary<ulong, string> BuildPreviewNameLookup(IReadOnlyList<SaveHouseholdItem> households)
    {
        ArgumentNullException.ThrowIfNull(households);

        var preferredNames = households.ToDictionary(
            household => household.HouseholdId,
            ResolvePreferredPreviewName);
        var duplicateNames = preferredNames.Values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (duplicateNames.Count == 0)
        {
            return preferredNames;
        }

        var resolved = new Dictionary<ulong, string>(preferredNames.Count);
        foreach (var household in households)
        {
            var preferredName = preferredNames[household.HouseholdId];
            if (!duplicateNames.Contains(preferredName))
            {
                resolved[household.HouseholdId] = preferredName;
                continue;
            }

            var uniqueName = preferredName;
            if (!string.IsNullOrWhiteSpace(household.HomeZoneName))
            {
                uniqueName = $"{preferredName} - {household.HomeZoneName}";
            }

            resolved[household.HouseholdId] = uniqueName;
        }

        var finalDuplicates = resolved
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group.Select(entry => entry.Key))
            .ToHashSet();
        if (finalDuplicates.Count == 0)
        {
            return resolved;
        }

        foreach (var household in households.Where(item => finalDuplicates.Contains(item.HouseholdId)))
        {
            resolved[household.HouseholdId] = $"{resolved[household.HouseholdId]} [0x{household.HouseholdId:X}]";
        }

        return resolved;
    }

    private static string ResolvePreferredPreviewName(SaveHouseholdItem household)
    {
        if (household.Members.Count == 1)
        {
            var memberName = household.Members[0].FullName;
            if (!string.IsNullOrWhiteSpace(memberName))
            {
                return memberName;
            }
        }

        return string.IsNullOrWhiteSpace(household.Name)
            ? $"Household {household.HouseholdId:X}"
            : household.Name;
    }
}
