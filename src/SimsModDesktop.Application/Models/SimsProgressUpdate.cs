namespace SimsModDesktop.Models;

public sealed class SimsProgressUpdate
{
    public required string Stage { get; init; }
    public required int Current { get; init; }
    public required int Total { get; init; }
    public required int Percent { get; init; }
    public string Detail { get; init; } = string.Empty;
}
