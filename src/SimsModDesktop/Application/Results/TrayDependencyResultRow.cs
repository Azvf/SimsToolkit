namespace SimsModDesktop.Application.Results;

public sealed record TrayDependencyResultRow
{
    public string PackagePath { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public int MatchInstanceCount { get; init; }
    public string MatchRatePct { get; init; } = string.Empty;
}
