using System.Globalization;

namespace SimsModDesktop.Infrastructure.Tray;

internal static class TrayPreviewItemUtilities
{
    private const int MaxSupportedHouseholdMembers = 8;

    public static bool TryGetAuxiliaryHouseholdMemberSlot(GroupAccumulator group, out int slot)
    {
        slot = 0;

        if (group.RepresentativeIdentity is null)
        {
            return false;
        }

        return TryParseAuxiliaryHouseholdMemberSlot(group.RepresentativeIdentity, out slot);
    }

    public static string InferPresetType(IReadOnlyCollection<string> extensions)
    {
        var hasLot = extensions.Contains(".blueprint", StringComparer.OrdinalIgnoreCase) ||
                     extensions.Contains(".bpi", StringComparer.OrdinalIgnoreCase);
        var hasRoom = extensions.Contains(".room", StringComparer.OrdinalIgnoreCase) ||
                      extensions.Contains(".rmi", StringComparer.OrdinalIgnoreCase);
        var hasHousehold = extensions.Contains(".householdbinary", StringComparer.OrdinalIgnoreCase) ||
                           extensions.Contains(".hhi", StringComparer.OrdinalIgnoreCase) ||
                           extensions.Contains(".sgi", StringComparer.OrdinalIgnoreCase);

        var buckets = (hasLot ? 1 : 0) + (hasRoom ? 1 : 0) + (hasHousehold ? 1 : 0);
        if (buckets > 1)
        {
            return "Mixed";
        }

        if (hasLot)
        {
            return "Lot";
        }

        if (hasRoom)
        {
            return "Room";
        }

        if (hasHousehold)
        {
            return "Household";
        }

        return "Unknown";
    }

    private static bool TryParseAuxiliaryHouseholdMemberSlot(
        TrayIdentity identity,
        out int slot)
    {
        slot = 0;

        if (!identity.ParseSuccess ||
            string.IsNullOrWhiteSpace(identity.TypeHex))
        {
            return false;
        }

        var typeValue = uint.Parse(
            identity.TypeHex.AsSpan(2),
            NumberStyles.HexNumber,
            CultureInfo.InvariantCulture);
        if ((typeValue & 0xF) != 0x3)
        {
            return false;
        }

        var candidateSlot = (int)((typeValue >> 4) & 0xF);
        if (candidateSlot is < 1 or > MaxSupportedHouseholdMembers)
        {
            return false;
        }

        slot = candidateSlot;
        return true;
    }
}
