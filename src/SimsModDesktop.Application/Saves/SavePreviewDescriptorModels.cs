using SimsModDesktop.SaveData.Models;

namespace SimsModDesktop.Application.Saves;

public sealed class SavePreviewDescriptorManifest
{
    public string SourceSavePath { get; init; } = string.Empty;
    public long SourceLength { get; init; }
    public DateTime SourceLastWriteTimeUtc { get; init; }
    public string DescriptorSchemaVersion { get; init; } = string.Empty;
    public DateTime BuildStartedUtc { get; init; }
    public DateTime BuildCompletedUtc { get; init; }
    public int TotalHouseholdCount { get; init; }
    public int ExportableHouseholdCount { get; init; }
    public int ReadyHouseholdCount { get; init; }
    public int BlockedHouseholdCount { get; init; }
    public IReadOnlyList<SavePreviewDescriptorEntry> Entries { get; init; } = Array.Empty<SavePreviewDescriptorEntry>();
}

public sealed class SavePreviewDescriptorEntry
{
    public ulong HouseholdId { get; init; }
    public string TrayItemKey { get; init; } = string.Empty;
    public string StableInstanceIdHex { get; init; } = string.Empty;
    public string HouseholdName { get; init; } = string.Empty;
    public string HomeZoneName { get; init; } = string.Empty;
    public int HouseholdSize { get; init; }
    public bool CanExport { get; init; }
    public string BuildState { get; init; } = string.Empty;
    public string LastError { get; init; } = string.Empty;
    public string SearchText { get; init; } = string.Empty;
    public string DisplayTitle { get; init; } = string.Empty;
    public string DisplaySubtitle { get; init; } = string.Empty;
    public string DisplayDescription { get; init; } = string.Empty;
    public string DisplayPrimaryMeta { get; init; } = string.Empty;
    public string DisplaySecondaryMeta { get; init; } = string.Empty;
    public string DisplayTertiaryMeta { get; init; } = string.Empty;
}

public sealed class SavePreviewDescriptorBuildResult
{
    public bool Succeeded { get; init; }
    public string Error { get; init; } = string.Empty;
    public SaveHouseholdSnapshot? Snapshot { get; init; }
    public SavePreviewDescriptorManifest? Manifest { get; init; }
}

public sealed class SavePreviewDescriptorBuildProgress
{
    public int Percent { get; init; }
    public string Detail { get; init; } = string.Empty;
}
