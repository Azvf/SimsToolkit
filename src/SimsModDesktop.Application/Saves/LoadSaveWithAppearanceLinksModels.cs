using SimsModDesktop.PackageCore;

namespace SimsModDesktop.Application.Saves;

public sealed class LoadSaveWithAppearanceLinksRequest
{
    public string SavePath { get; init; } = string.Empty;
    public string GameRoot { get; init; } = string.Empty;
    public string ModsRoot { get; init; } = string.Empty;
}

public sealed class LoadSaveWithAppearanceLinksResult
{
    public bool Success { get; init; }
    public Ts4SimAppearanceSnapshot? Snapshot { get; init; }
    public IReadOnlyList<Ts4AppearanceIssue> Issues { get; init; } = Array.Empty<Ts4AppearanceIssue>();
    public string Error { get; init; } = string.Empty;
}
