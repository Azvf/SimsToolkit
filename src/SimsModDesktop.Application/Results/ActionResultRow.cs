namespace SimsModDesktop.Application.Results;

public sealed record ActionResultRow
{
    public string Name { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public long? SizeBytes { get; init; }
    public DateTime? UpdatedLocal { get; init; }
    public string PrimaryPath { get; init; } = string.Empty;
    public string Confidence { get; init; } = string.Empty;
    public string Category { get; init; } = string.Empty;
    public string Hash { get; init; } = string.Empty;
    public string DependencyInfo { get; init; } = string.Empty;
    public string RawSummary { get; init; } = string.Empty;
}
