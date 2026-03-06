namespace SimsModDesktop.Application.TrayPreview;

public enum PreviewSourceKind
{
    TrayRoot = 0,
    SaveDescriptor = 1
}

public sealed record PreviewSourceRef
{
    public required PreviewSourceKind Kind { get; init; }
    public required string SourceKey { get; init; }
    public string VersionToken { get; init; } = string.Empty;

    public static PreviewSourceRef ForTrayRoot(string trayRootPath)
    {
        return new PreviewSourceRef
        {
            Kind = PreviewSourceKind.TrayRoot,
            SourceKey = trayRootPath?.Trim() ?? string.Empty
        };
    }

    public static PreviewSourceRef ForSaveDescriptor(string saveFilePath)
    {
        return new PreviewSourceRef
        {
            Kind = PreviewSourceKind.SaveDescriptor,
            SourceKey = saveFilePath?.Trim() ?? string.Empty
        };
    }
}
