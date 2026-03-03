namespace SimsModDesktop.Models;

public sealed record TS4PathDiscoveryResult
{
    public string Ts4RootPath { get; init; } = string.Empty;
    public string GameExecutablePath { get; init; } = string.Empty;
    public string ModsPath { get; init; } = string.Empty;
    public string TrayPath { get; init; } = string.Empty;
    public string SavesPath { get; init; } = string.Empty;
    public IReadOnlyList<string> GameExecutableCandidates { get; init; } = Array.Empty<string>();
}
