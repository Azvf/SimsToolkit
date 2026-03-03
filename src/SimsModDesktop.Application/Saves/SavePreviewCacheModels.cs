using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Application.Saves;

public sealed class SavePreviewCacheManifest
{
    public string SourceSavePath { get; init; } = string.Empty;
    public long SourceLength { get; init; }
    public DateTime SourceLastWriteTimeUtc { get; init; }
    public string CacheSchemaVersion { get; init; } = string.Empty;
    public DateTime BuildStartedUtc { get; init; }
    public DateTime BuildCompletedUtc { get; init; }
    public int TotalHouseholdCount { get; init; }
    public int ExportableHouseholdCount { get; init; }
    public int ReadyHouseholdCount { get; init; }
    public int FailedHouseholdCount { get; init; }
    public int BlockedHouseholdCount { get; init; }
    public IReadOnlyList<SavePreviewCacheHouseholdEntry> Entries { get; init; } = Array.Empty<SavePreviewCacheHouseholdEntry>();
}

public sealed class SavePreviewCacheHouseholdEntry
{
    public ulong HouseholdId { get; init; }
    public string HouseholdName { get; init; } = string.Empty;
    public string HomeZoneName { get; init; } = string.Empty;
    public int HouseholdSize { get; init; }
    public string BuildState { get; init; } = string.Empty;
    public string TrayInstanceId { get; init; } = string.Empty;
    public string TrayItemKey { get; init; } = string.Empty;
    public string LastError { get; init; } = string.Empty;
    public IReadOnlyList<string> GeneratedFileNames { get; init; } = Array.Empty<string>();
}

public sealed class SavePreviewCacheBuildResult
{
    public bool Succeeded { get; init; }
    public string CacheRootPath { get; init; } = string.Empty;
    public string Error { get; init; } = string.Empty;
    public SaveHouseholdSnapshot? Snapshot { get; init; }
    public SavePreviewCacheManifest? Manifest { get; init; }
}

public sealed class SavePreviewCacheBuildProgress
{
    public int Percent { get; init; }
    public string Detail { get; init; } = string.Empty;
}

