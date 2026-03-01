namespace SimsModDesktop.Models;

public sealed class TrayMemberDisplayMetadata
{
    public int SlotIndex { get; init; }
    public string FullName { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
}
