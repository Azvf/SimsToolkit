using System.Text.Json.Serialization;
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

internal sealed class SavePreviewCacheManifestPayload
{
    [JsonPropertyName("sourceSavePath")]
    public string SourceSavePath { get; init; } = string.Empty;

    [JsonPropertyName("sourceLength")]
    public long SourceLength { get; init; }

    [JsonPropertyName("sourceLastWriteTimeUtc")]
    public DateTime SourceLastWriteTimeUtc { get; init; }

    [JsonPropertyName("cacheSchemaVersion")]
    public string CacheSchemaVersion { get; init; } = string.Empty;

    [JsonPropertyName("buildStartedUtc")]
    public DateTime BuildStartedUtc { get; init; }

    [JsonPropertyName("buildCompletedUtc")]
    public DateTime BuildCompletedUtc { get; init; }

    [JsonPropertyName("totalHouseholdCount")]
    public int TotalHouseholdCount { get; init; }

    [JsonPropertyName("exportableHouseholdCount")]
    public int ExportableHouseholdCount { get; init; }

    [JsonPropertyName("readyHouseholdCount")]
    public int ReadyHouseholdCount { get; init; }

    [JsonPropertyName("failedHouseholdCount")]
    public int FailedHouseholdCount { get; init; }

    [JsonPropertyName("blockedHouseholdCount")]
    public int BlockedHouseholdCount { get; init; }

    [JsonPropertyName("entries")]
    public List<SavePreviewCacheHouseholdEntry> Entries { get; init; } = new();
}
