namespace SimsModDesktop.Application.Caching;

public enum CacheWarmupDomain
{
    ModsCatalog,
    TrayDependency,
    TrayPreviewMetadata
}

public sealed record CacheWarmupProgress
{
    public CacheWarmupDomain Domain { get; init; }
    public string Stage { get; init; } = string.Empty;
    public int Percent { get; init; }
    public int Current { get; init; }
    public int Total { get; init; }
    public string Detail { get; init; } = string.Empty;
    public bool IsBlocking { get; init; }
}
