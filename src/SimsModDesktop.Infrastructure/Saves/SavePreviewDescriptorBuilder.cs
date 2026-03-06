using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Infrastructure.Saves;

public sealed class SavePreviewDescriptorBuilder : ISavePreviewDescriptorBuilder
{
    private const string DescriptorSchemaVersion = "save-preview-descriptor-v1";
    private readonly ISaveHouseholdReader _saveHouseholdReader;
    private readonly ISavePreviewDescriptorStore _descriptorStore;
    private readonly ILogger<SavePreviewDescriptorBuilder> _logger;

    public SavePreviewDescriptorBuilder(
        ISaveHouseholdReader saveHouseholdReader,
        ISavePreviewDescriptorStore descriptorStore,
        ILogger<SavePreviewDescriptorBuilder>? logger = null)
    {
        _saveHouseholdReader = saveHouseholdReader;
        _descriptorStore = descriptorStore;
        _logger = logger ?? NullLogger<SavePreviewDescriptorBuilder>.Instance;
    }

    public Task<SavePreviewDescriptorBuildResult> BuildAsync(
        string saveFilePath,
        IProgress<SavePreviewDescriptorBuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var normalizedSavePath = Path.GetFullPath(saveFilePath.Trim());

            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var buildStartedUtc = DateTime.UtcNow;
                var snapshot = _saveHouseholdReader.Load(normalizedSavePath);
                var previewNames = SavePreviewIdentity.BuildPreviewNameLookup(snapshot.Households);
                var entries = new List<SavePreviewDescriptorEntry>(snapshot.Households.Count);
                var readyCount = 0;
                var blockedCount = 0;
                var exportableCount = snapshot.Households.Count(item => item.CanExport);

                for (var index = 0; index < snapshot.Households.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var household = snapshot.Households[index];
                    var displayName = previewNames.TryGetValue(household.HouseholdId, out var resolvedName)
                        ? resolvedName
                        : household.Name;
                    var entry = BuildEntry(normalizedSavePath, household, displayName);
                    entries.Add(entry);

                    if (string.Equals(entry.BuildState, "Ready", StringComparison.OrdinalIgnoreCase))
                    {
                        readyCount++;
                    }
                    else
                    {
                        blockedCount++;
                    }

                    progress?.Report(new SavePreviewDescriptorBuildProgress
                    {
                        Percent = snapshot.Households.Count == 0
                            ? 100
                            : (int)Math.Round(((index + 1) / (double)snapshot.Households.Count) * 100d),
                        Detail = $"Preparing {entry.DisplayTitle}"
                    });
                }

                var saveInfo = new FileInfo(normalizedSavePath);
                var manifest = new SavePreviewDescriptorManifest
                {
                    SourceSavePath = normalizedSavePath,
                    SourceLength = saveInfo.Exists ? saveInfo.Length : 0,
                    SourceLastWriteTimeUtc = saveInfo.Exists ? saveInfo.LastWriteTimeUtc : DateTime.MinValue,
                    DescriptorSchemaVersion = DescriptorSchemaVersion,
                    BuildStartedUtc = buildStartedUtc,
                    BuildCompletedUtc = DateTime.UtcNow,
                    TotalHouseholdCount = snapshot.Households.Count,
                    ExportableHouseholdCount = exportableCount,
                    ReadyHouseholdCount = readyCount,
                    BlockedHouseholdCount = blockedCount,
                    Entries = entries
                };

                _descriptorStore.SaveDescriptor(normalizedSavePath, manifest);
                progress?.Report(new SavePreviewDescriptorBuildProgress
                {
                    Percent = 100,
                    Detail = "Save preview descriptor ready."
                });

                _logger.LogInformation(
                    "savepreview.descriptor.build.done savePath={SavePath} householdCount={HouseholdCount} readyCount={ReadyCount} blockedCount={BlockedCount}",
                    normalizedSavePath,
                    manifest.TotalHouseholdCount,
                    manifest.ReadyHouseholdCount,
                    manifest.BlockedHouseholdCount);

                return new SavePreviewDescriptorBuildResult
                {
                    Succeeded = true,
                    Snapshot = snapshot,
                    Manifest = manifest
                };
            }
            catch (OperationCanceledException)
            {
                _descriptorStore.ClearDescriptor(normalizedSavePath);
                return new SavePreviewDescriptorBuildResult
                {
                    Succeeded = false
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "savepreview.descriptor.build.fail savePath={SavePath}",
                    normalizedSavePath);
                return new SavePreviewDescriptorBuildResult
                {
                    Succeeded = false,
                    Error = ex.Message
                };
            }
        }, cancellationToken);
    }

    private static SavePreviewDescriptorEntry BuildEntry(
        string saveFilePath,
        SaveHouseholdItem household,
        string displayName)
    {
        var trayItemKey = SavePreviewIdentity.ComputeTrayItemKey(saveFilePath, household.HouseholdId);
        var stableInstanceId = SavePreviewIdentity.ComputeStableInstanceId(saveFilePath, household.HouseholdId);
        var buildState = household.CanExport ? "Ready" : "Blocked";
        var description = string.IsNullOrWhiteSpace(household.Description)
            ? household.LocationLabel
            : household.Description.Trim();
        var primaryMeta = household.HouseholdSize <= 0
            ? "0 sims"
            : household.HouseholdSize == 1
                ? "1 sim"
                : $"{household.HouseholdSize} sims";
        var secondaryMeta = string.IsNullOrWhiteSpace(household.HomeZoneName)
            ? $"Zone 0x{household.HomeZoneId:X}"
            : household.HomeZoneName.Trim();
        var tertiaryMeta = household.Funds > 0
            ? $"Funds §{household.Funds:N0}"
            : string.Empty;
        var searchParts = household.Members
            .Select(member => member.FullName)
            .Append(displayName)
            .Append(household.Name)
            .Append(household.HomeZoneName)
            .Append(household.Description)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new SavePreviewDescriptorEntry
        {
            HouseholdId = household.HouseholdId,
            TrayItemKey = trayItemKey,
            StableInstanceIdHex = $"0x{stableInstanceId:X16}",
            HouseholdName = displayName,
            HomeZoneName = household.HomeZoneName ?? string.Empty,
            HouseholdSize = household.HouseholdSize,
            CanExport = household.CanExport,
            BuildState = buildState,
            LastError = household.CanExport ? string.Empty : household.ExportBlockReason,
            SearchText = string.Join(" ", searchParts),
            DisplayTitle = displayName,
            DisplaySubtitle = secondaryMeta,
            DisplayDescription = description,
            DisplayPrimaryMeta = primaryMeta,
            DisplaySecondaryMeta = secondaryMeta,
            DisplayTertiaryMeta = tertiaryMeta
        };
    }
}
