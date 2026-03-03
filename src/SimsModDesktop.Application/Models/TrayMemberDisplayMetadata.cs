namespace SimsModDesktop.Models;

public sealed class TrayMemberDisplayMetadata
{
    public int SlotIndex { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public string SimId { get; init; } = string.Empty;
    public uint? Gender { get; init; }
    public ulong? AspirationId { get; init; }
    public uint? Age { get; init; }
    public uint? Species { get; init; }
    public bool? IsCustomGender { get; init; }
    public uint? OccultTypes { get; init; }
    public uint? BreedNameKey { get; init; }
    public ulong? FameRankedStatId { get; init; }
    public float? FameValue { get; init; }
    public ulong? DeathTrait { get; init; }
}
