using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SimsModDesktop.Application.Saves;
using SimsModDesktop.SaveData.Models;
using SimsModDesktop.SaveData.Services;

namespace SimsModDesktop.Infrastructure.Saves;

public sealed class SavePreviewArtifactProvider : ISavePreviewArtifactProvider
{
    private readonly ISavePreviewDescriptorStore _descriptorStore;
    private readonly ISavePreviewDescriptorBuilder _descriptorBuilder;
    private readonly ISavePreviewArtifactStore _artifactStore;
    private readonly IHouseholdTrayExporter _householdTrayExporter;
    private readonly ILogger<SavePreviewArtifactProvider> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _saveGates = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _artifactBasePath;

    public SavePreviewArtifactProvider(
        ISavePreviewDescriptorStore descriptorStore,
        ISavePreviewDescriptorBuilder descriptorBuilder,
        ISavePreviewArtifactStore artifactStore,
        IHouseholdTrayExporter householdTrayExporter,
        ILogger<SavePreviewArtifactProvider>? logger = null)
    {
        _descriptorStore = descriptorStore;
        _descriptorBuilder = descriptorBuilder;
        _artifactStore = artifactStore;
        _householdTrayExporter = householdTrayExporter;
        _logger = logger ?? NullLogger<SavePreviewArtifactProvider>.Instance;
        _artifactBasePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SimsModDesktop",
            "Cache",
            "SavePreviewArtifacts");
    }

    public async Task<string?> EnsureBundleAsync(
        string saveFilePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(householdKey);

        var normalizedSavePath = Path.GetFullPath(saveFilePath.Trim());
        if (TryResolveReadyBundleRoot(normalizedSavePath, householdKey, out var existingRoot))
        {
            _logger.LogDebug(
                "savepreview.artifact.hit savePath={SavePath} householdKey={HouseholdKey} purpose={Purpose}",
                normalizedSavePath,
                householdKey,
                purpose);
            return existingRoot;
        }

        var gate = _saveGates.GetOrAdd(normalizedSavePath, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (TryResolveReadyBundleRoot(normalizedSavePath, householdKey, out existingRoot))
            {
                _logger.LogDebug(
                    "savepreview.artifact.hit.postwait savePath={SavePath} householdKey={HouseholdKey} purpose={Purpose}",
                    normalizedSavePath,
                    householdKey,
                    purpose);
                return existingRoot;
            }

            _logger.LogInformation(
                "savepreview.artifact.build savePath={SavePath} householdKey={HouseholdKey} purpose={Purpose}",
                normalizedSavePath,
                householdKey,
                purpose);
            var bundleRoot = await BuildArtifactAsync(normalizedSavePath, householdKey, purpose, cancellationToken)
                .ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(bundleRoot) ? null : bundleRoot;
        }
        finally
        {
            gate.Release();
        }
    }

    public void Clear(string saveFilePath)
    {
        if (string.IsNullOrWhiteSpace(saveFilePath))
        {
            return;
        }

        var normalizedSavePath = Path.GetFullPath(saveFilePath.Trim());
        _artifactStore.Clear(normalizedSavePath);

        var artifactRoot = Path.Combine(_artifactBasePath, SavePreviewIdentity.ComputeSaveHash(normalizedSavePath));
        try
        {
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
        catch
        {
        }
    }

    private bool TryResolveReadyBundleRoot(
        string normalizedSavePath,
        string householdKey,
        out string? bundleRoot)
    {
        bundleRoot = null;
        if (!_descriptorStore.TryLoadDescriptor(normalizedSavePath, out var manifest) ||
            !_descriptorStore.IsDescriptorCurrent(normalizedSavePath, manifest))
        {
            return false;
        }

        var entry = ResolveEntry(manifest, householdKey);
        if (entry is null || !string.Equals(entry.BuildState, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!_artifactStore.TryLoad(normalizedSavePath, entry.TrayItemKey, out var record) ||
            !_artifactStore.IsCurrent(normalizedSavePath, entry.TrayItemKey, record))
        {
            return false;
        }

        bundleRoot = record.ArtifactRootPath;
        return true;
    }

    private async Task<string?> BuildArtifactAsync(
        string normalizedSavePath,
        string householdKey,
        string purpose,
        CancellationToken cancellationToken)
    {
        SavePreviewDescriptorManifest manifest;
        if (!_descriptorStore.TryLoadDescriptor(normalizedSavePath, out manifest) ||
            !_descriptorStore.IsDescriptorCurrent(normalizedSavePath, manifest))
        {
            var descriptorBuild = await _descriptorBuilder
                .BuildAsync(normalizedSavePath, progress: null, cancellationToken)
                .ConfigureAwait(false);
            if (!descriptorBuild.Succeeded || descriptorBuild.Manifest is null)
            {
                _logger.LogWarning(
                    "savepreview.artifact.descriptor.fail savePath={SavePath} householdKey={HouseholdKey} purpose={Purpose} error={Error}",
                    normalizedSavePath,
                    householdKey,
                    purpose,
                    descriptorBuild.Error);
                return null;
            }

            manifest = descriptorBuild.Manifest;
        }

        var entry = ResolveEntry(manifest, householdKey);
        if (entry is null || !string.Equals(entry.BuildState, "Ready", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (_artifactStore.TryLoad(normalizedSavePath, entry.TrayItemKey, out var existingRecord) &&
            _artifactStore.IsCurrent(normalizedSavePath, entry.TrayItemKey, existingRecord))
        {
            return existingRecord.ArtifactRootPath;
        }

        var artifactRoot = GetArtifactRoot(normalizedSavePath, entry.TrayItemKey);
        ResetArtifactDirectory(artifactRoot);
        var exportResult = _householdTrayExporter.Export(new SaveHouseholdExportRequest
        {
            SourceSavePath = normalizedSavePath,
            HouseholdId = entry.HouseholdId,
            ExportRootPath = artifactRoot,
            OutputDirectoryOverride = artifactRoot,
            InstanceIdOverride = Convert.ToUInt64(entry.StableInstanceIdHex[2..], 16),
            MetadataNameOverride = entry.DisplayTitle,
            MetadataDescriptionOverride = entry.DisplayDescription,
            CreatorName = "SimsModDesktop Save Preview",
            CreatorId = 0x53494D5350524556,
            GenerateThumbnails = false
        });
        if (!exportResult.Succeeded)
        {
            _logger.LogWarning(
                "savepreview.artifact.export.fail savePath={SavePath} householdKey={HouseholdKey} purpose={Purpose} error={Error}",
                normalizedSavePath,
                householdKey,
                purpose,
                exportResult.Error);
            return null;
        }

        var saveInfo = new FileInfo(normalizedSavePath);
        var record = new SavePreviewArtifactRecord
        {
            SourceSavePath = normalizedSavePath,
            HouseholdKey = entry.TrayItemKey,
            SourceLength = saveInfo.Exists ? saveInfo.Length : 0,
            SourceLastWriteTimeUtc = saveInfo.Exists ? saveInfo.LastWriteTimeUtc : DateTime.MinValue,
            ArtifactSchemaVersion = "save-preview-artifact-v1",
            ArtifactRootPath = artifactRoot,
            GeneratedFileNames = exportResult.WrittenFiles
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Cast<string>()
                .ToArray(),
            LastPreparedUtc = DateTime.UtcNow
        };
        _artifactStore.Save(normalizedSavePath, entry.TrayItemKey, record);
        return record.ArtifactRootPath;
    }

    private SavePreviewDescriptorEntry? ResolveEntry(SavePreviewDescriptorManifest manifest, string householdKey)
    {
        return manifest.Entries.FirstOrDefault(candidate =>
            string.Equals(candidate.TrayItemKey, householdKey, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(candidate.HouseholdId.ToString(), householdKey, StringComparison.OrdinalIgnoreCase));
    }

    private string GetArtifactRoot(string saveFilePath, string householdKey)
    {
        var saveHash = SavePreviewIdentity.ComputeSaveHash(saveFilePath);
        return Path.Combine(_artifactBasePath, saveHash, householdKey);
    }

    private static void ResetArtifactDirectory(string artifactRoot)
    {
        try
        {
            if (Directory.Exists(artifactRoot))
            {
                Directory.Delete(artifactRoot, recursive: true);
            }
        }
        catch
        {
        }

        Directory.CreateDirectory(artifactRoot);
    }
}
