namespace SimsModDesktop.Models;

public sealed class TrayMetadataResult
{
    public string TrayItemPath { get; init; } = string.Empty;
    public string ItemType { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string CreatorName { get; init; } = string.Empty;
    public string CreatorId { get; init; } = string.Empty;
    public int? FamilySize { get; init; }
    public int? PendingBabies { get; init; }
    public int? SizeX { get; init; }
    public int? SizeZ { get; init; }
    public int? PriceValue { get; init; }
    public int? NumBedrooms { get; init; }
    public int? NumBathrooms { get; init; }
    public int? Height { get; init; }
    public bool IsModdedContent { get; init; }
    public IReadOnlyList<TrayMemberDisplayMetadata> Members { get; init; } = Array.Empty<TrayMemberDisplayMetadata>();
}
