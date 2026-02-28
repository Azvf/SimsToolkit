namespace SimsModDesktop.Application.Results;

public sealed record FindDupResultRow
{
    public string FilePath { get; init; } = string.Empty;
    public string Md5Hash { get; init; } = string.Empty;
    public int GroupId { get; init; }
    public int FileCount { get; init; }
    public long FileSize { get; init; }
}
