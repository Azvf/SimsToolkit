using EA.Sims4.Persistence;
using ProtoBuf;
using SimsModDesktop.SaveData.Formats;
using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.SaveData.Services;

public sealed class SaveHouseholdReader : ISaveHouseholdReader
{
    private const uint SaveDataResourceType = 0x0000000D;

    public SaveHouseholdSnapshot Load(string saveFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        if (!File.Exists(saveFilePath))
        {
            throw new FileNotFoundException("Save file was not found.", saveFilePath);
        }

        var package = DbpfPackageReader.ReadPackage(saveFilePath);
        var saveDataEntry = package.Entries.FirstOrDefault(entry => !entry.IsDeleted && entry.Type == SaveDataResourceType);
        if (saveDataEntry is null)
        {
            throw new InvalidDataException("SaveGameData resource (0x0000000D) was not found.");
        }

        if (!DbpfPackageReader.TryReadResourceBytes(saveFilePath, saveDataEntry, out var bytes, out var error))
        {
            throw new InvalidDataException(error ?? "Failed to read save resource.");
        }

        SaveGameData save;
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            save = Serializer.Deserialize<SaveGameData>(stream);
        }

        var simIndex = save.sims
            .Where(sim => sim is not null && sim.sim_id != 0)
            .GroupBy(sim => sim.sim_id)
            .ToDictionary(group => group.Key, group => group.First());
        var zoneIndex = save.zones
            .Where(zone => zone is not null && zone.zone_id != 0)
            .GroupBy(zone => zone.zone_id)
            .ToDictionary(group => group.Key, group => group.First());
        var householdIndex = save.households
            .Where(household => household is not null && household.household_id != 0)
            .GroupBy(household => household.household_id)
            .ToDictionary(group => group.Key, group => group.First());

        var households = new List<SaveHouseholdItem>(householdIndex.Count);
        foreach (var household in householdIndex.Values.OrderBy(ResolveHouseholdSortKey, StringComparer.OrdinalIgnoreCase))
        {
            var memberIds = household.sims?.ids ?? Array.Empty<ulong>();
            var members = new List<SaveMemberItem>(memberIds.Length);
            var missingMemberCount = 0;
            foreach (var memberId in memberIds)
            {
                if (!simIndex.TryGetValue(memberId, out var sim))
                {
                    missingMemberCount++;
                    continue;
                }

                var species = NormalizeSpecies(sim.extended_species);
                var isHumanLike = IsHumanLikeSpecies(species);
                members.Add(new SaveMemberItem
                {
                    SimId = sim.sim_id,
                    FirstName = sim.first_name,
                    LastName = sim.last_name,
                    Age = sim.age,
                    Gender = sim.gender,
                    Species = species,
                    OccultFlags = 0,
                    IsHumanLike = isHumanLike,
                    CanRenderThumbnail = isHumanLike
                });
            }

            var householdSize = memberIds.Length;
            var exportBlockReason = ResolveExportBlockReason(householdSize, missingMemberCount, members);
            var homeZoneName = zoneIndex.TryGetValue(household.home_zone, out var zone)
                ? zone.name
                : string.Empty;

            households.Add(new SaveHouseholdItem
            {
                HouseholdId = household.household_id,
                Name = string.IsNullOrWhiteSpace(household.name)
                    ? ResolveFallbackHouseholdName(household.household_id, members)
                    : household.name,
                Description = household.description,
                Funds = household.money,
                HomeZoneId = household.home_zone,
                HomeZoneName = homeZoneName,
                HouseholdSize = householdSize,
                Members = members,
                CanExport = string.IsNullOrWhiteSpace(exportBlockReason),
                ExportBlockReason = exportBlockReason
            });
        }

        return new SaveHouseholdSnapshot
        {
            SavePath = Path.GetFullPath(saveFilePath),
            GameSlotName = save.save_slot?.slot_name ?? string.Empty,
            LastWriteTimeLocal = File.GetLastWriteTime(saveFilePath),
            Households = households,
            RawHouseholds = householdIndex,
            RawSims = simIndex
        };
    }

    private static string ResolveExportBlockReason(
        int householdSize,
        int missingMemberCount,
        IReadOnlyList<SaveMemberItem> members)
    {
        if (householdSize < 1 || householdSize > 8)
        {
            return "Only households with 1 to 8 members are supported.";
        }

        if (missingMemberCount > 0)
        {
            return "Some household members are missing from save sim data.";
        }

        if (members.Count == 0)
        {
            return "No supported household members were found.";
        }

        if (members.Any(member => !member.IsHumanLike))
        {
            return "Only human-like households are supported in this phase.";
        }

        return string.Empty;
    }

    private static string ResolveFallbackHouseholdName(ulong householdId, IReadOnlyList<SaveMemberItem> members)
    {
        var fromMembers = members.FirstOrDefault()?.LastName;
        if (!string.IsNullOrWhiteSpace(fromMembers))
        {
            return fromMembers;
        }

        return $"Household {householdId:X}";
    }

    private static string ResolveHouseholdSortKey(HouseholdData household)
    {
        if (!string.IsNullOrWhiteSpace(household.name))
        {
            return household.name;
        }

        return $"~{household.household_id:X16}";
    }

    private static uint NormalizeSpecies(uint rawSpecies)
    {
        return rawSpecies;
    }

    private static bool IsHumanLikeSpecies(uint species)
    {
        return species is 0 or 1;
    }
}
