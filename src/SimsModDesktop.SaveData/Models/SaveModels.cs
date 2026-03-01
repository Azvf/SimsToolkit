using EA.Sims4.Persistence;

namespace SimsModDesktop.SaveData.Models;

public sealed class SaveFileEntry
{
    public string FilePath { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public DateTime LastWriteTimeLocal { get; init; }
    public long LengthBytes { get; init; }

    public string DisplayLabel =>
        $"{FileName} ({LastWriteTimeLocal:g}, {Math.Max(1, LengthBytes / 1024d / 1024d):0.##} MB)";
}

public sealed class SaveMemberItem
{
    public ulong SimId { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public uint Age { get; init; }
    public uint Gender { get; init; }
    public uint Species { get; init; }
    public uint OccultFlags { get; init; }
    public bool IsHumanLike { get; init; }
    public bool CanRenderThumbnail { get; init; }

    public string FullName
    {
        get
        {
            var fullName = $"{FirstName} {LastName}".Trim();
            return string.IsNullOrWhiteSpace(fullName) ? $"Sim {SimId:X}" : fullName;
        }
    }

    public string Subtitle => $"Age {Age}, Gender {Gender}, Species {Species}";
}

public sealed class SaveHouseholdItem
{
    public ulong HouseholdId { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public ulong Funds { get; init; }
    public ulong HomeZoneId { get; init; }
    public string HomeZoneName { get; init; } = string.Empty;
    public int HouseholdSize { get; init; }
    public IReadOnlyList<SaveMemberItem> Members { get; init; } = Array.Empty<SaveMemberItem>();
    public bool CanExport { get; init; }
    public string ExportBlockReason { get; init; } = string.Empty;

    public string DisplayLabel =>
        string.IsNullOrWhiteSpace(Name)
            ? $"Household {HouseholdId:X}"
            : $"{Name} ({HouseholdSize})";

    public string LocationLabel =>
        string.IsNullOrWhiteSpace(HomeZoneName)
            ? $"Zone 0x{HomeZoneId:X}"
            : HomeZoneName;

    public bool HasExportBlockReason => !string.IsNullOrWhiteSpace(ExportBlockReason);
}

public sealed class SaveHouseholdSnapshot
{
    public string SavePath { get; init; } = string.Empty;
    public string GameSlotName { get; init; } = string.Empty;
    public DateTime LastWriteTimeLocal { get; init; }
    public IReadOnlyList<SaveHouseholdItem> Households { get; init; } = Array.Empty<SaveHouseholdItem>();

    internal IReadOnlyDictionary<ulong, HouseholdData> RawHouseholds { get; init; } =
        new Dictionary<ulong, HouseholdData>();

    internal IReadOnlyDictionary<ulong, SimData> RawSims { get; init; } =
        new Dictionary<ulong, SimData>();
}

public sealed class SaveHouseholdExportRequest
{
    public string SourceSavePath { get; init; } = string.Empty;
    public ulong HouseholdId { get; init; }
    public string ExportRootPath { get; init; } = string.Empty;
    public string CreatorName { get; init; } = string.Empty;
    public ulong CreatorId { get; init; }
    public bool GenerateThumbnails { get; init; } = true;
}

public sealed class SaveHouseholdExportResult
{
    public bool Succeeded { get; init; }
    public string ExportDirectory { get; init; } = string.Empty;
    public string InstanceIdHex { get; init; } = string.Empty;
    public IReadOnlyList<string> WrittenFiles { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
    public string? Error { get; init; }
}
