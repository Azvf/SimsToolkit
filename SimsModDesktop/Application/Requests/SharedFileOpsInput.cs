namespace SimsModDesktop.Application.Requests;

public sealed record SharedFileOpsInput
{
    public bool SkipPruneEmptyDirs { get; init; }
    public bool ModFilesOnly { get; init; }
    public IReadOnlyList<string> ModExtensions { get; init; } = Array.Empty<string>();
    public bool VerifyContentOnNameConflict { get; init; }
    public int? PrefixHashBytes { get; init; }
    public int? HashWorkerCount { get; init; }
}
