namespace SimsModDesktop.Models;

public enum TrayThumbnailSourceKind
{
    Embedded,
    Localthumbcache,
    Placeholder
}

public sealed class TrayThumbnailResult
{
    public string CacheFilePath { get; init; } = string.Empty;
    public bool FromCache { get; init; }
    public TrayThumbnailSourceKind SourceKind { get; init; } = TrayThumbnailSourceKind.Placeholder;
    public bool Success { get; init; }
}
