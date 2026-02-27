namespace SimsModDesktop.Models;

public sealed class SimsTrayPreviewDashboard
{
    public int TotalItems { get; init; }
    public int TotalFiles { get; init; }
    public long TotalBytes { get; init; }
    public double TotalMB { get; init; }
    public DateTime LatestWriteTimeLocal { get; init; }
    public string PresetTypeBreakdown { get; init; } = string.Empty;
}
