namespace SimsModDesktop.Models;

public sealed record LaunchGameRequest
{
    public required string ExecutablePath { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyList<string> Arguments { get; init; } = Array.Empty<string>();
}
