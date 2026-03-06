namespace SimsModDesktop.Application.Caching;

public sealed record CacheSourceVersion
{
    public required string SourceKind { get; init; }
    public required string SourceKey { get; init; }
    public required string VersionToken { get; init; }
}
