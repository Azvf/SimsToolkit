namespace SimsModDesktop.PackageCore;

public enum Ts4ResourceMatchMode
{
    Exact = 0,
    TypeInstanceFallback = 1,
    NotFound = 2
}

public enum Ts4ResourceSourceKind
{
    Mods = 0,
    Sdx = 1,
    Game = 2
}

public sealed class Ts4ResourceResolutionCandidate
{
    public required ResourceLocation Location { get; init; }
    public required Ts4ResourceSourceKind SourceKind { get; init; }
    public required Ts4ResourceMatchMode MatchMode { get; init; }
    public required int Score { get; init; }
    public required bool Selected { get; init; }
}

public sealed class Ts4ResourceResolution
{
    public required DbpfResourceKey RequestedKey { get; init; }
    public required ResourceLookupPolicy Policy { get; init; }
    public required Ts4ResourceMatchMode MatchMode { get; init; }
    public required IReadOnlyList<Ts4ResourceResolutionCandidate> Candidates { get; init; }
    public int SelectedCandidateIndex { get; init; } = -1;

    public bool Found =>
        SelectedCandidateIndex >= 0 &&
        SelectedCandidateIndex < Candidates.Count;

    public ResourceLocation? SelectedLocation =>
        Found ? Candidates[SelectedCandidateIndex].Location : null;

    public DbpfResourceKey? ResolvedKey =>
        Found
            ? new DbpfResourceKey(
                Candidates[SelectedCandidateIndex].Location.Entry.Type,
                Candidates[SelectedCandidateIndex].Location.Entry.Group,
                Candidates[SelectedCandidateIndex].Location.Entry.Instance)
            : null;
}
