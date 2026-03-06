namespace SimsModDesktop.Application.Saves;

public interface ISavePreviewArtifactStore
{
    bool IsCurrent(string saveFilePath, string householdKey, SavePreviewArtifactRecord record);

    bool TryLoad(string saveFilePath, string householdKey, out SavePreviewArtifactRecord record);

    void Save(string saveFilePath, string householdKey, SavePreviewArtifactRecord record);

    void Clear(string saveFilePath);
}

public sealed record SavePreviewArtifactRecord
{
    public string SourceSavePath { get; init; } = string.Empty;
    public string HouseholdKey { get; init; } = string.Empty;
    public long SourceLength { get; init; }
    public DateTime SourceLastWriteTimeUtc { get; init; }
    public string ArtifactSchemaVersion { get; init; } = string.Empty;
    public string ArtifactRootPath { get; init; } = string.Empty;
    public IReadOnlyList<string> GeneratedFileNames { get; init; } = Array.Empty<string>();
    public DateTime LastPreparedUtc { get; init; }
}
