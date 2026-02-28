namespace SimsModDesktop.Models;

public sealed record LaunchGameResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public int? ProcessId { get; init; }
}
