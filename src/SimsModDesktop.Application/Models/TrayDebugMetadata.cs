namespace SimsModDesktop.Models;

public sealed class TrayDebugMetadata
{
    private string? _contentFingerprint;

    public string TrayItemKey { get; init; } = string.Empty;
    public string TrayInstanceId { get; init; } = string.Empty;
    public string ContentFingerprint
    {
        get
        {
            if (!string.IsNullOrEmpty(_contentFingerprint))
            {
                return _contentFingerprint;
            }

            _contentFingerprint = TrayContentFingerprint.Compute(SourceFilePaths);
            return _contentFingerprint;
        }
        init => _contentFingerprint = value;
    }

    public string CreatorId { get; init; } = string.Empty;
    public string CreatorName { get; init; } = string.Empty;
    public int FileCount { get; init; }
    public double TotalMB { get; init; }
    public DateTime LatestWriteTimeLocal { get; init; }
    public string Extensions { get; init; } = string.Empty;
    public string ResourceTypes { get; init; } = string.Empty;
    public string FileListPreview { get; init; } = string.Empty;
    public IReadOnlyList<string> SourceFilePaths { get; init; } = Array.Empty<string>();

    public string SourceFilePathsPreview => string.Join(Environment.NewLine, SourceFilePaths);
}
