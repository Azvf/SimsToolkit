using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SimsModDesktop.Application.Saves;

public sealed class SavePreviewCacheStore : ISavePreviewCacheStore
{
    private const string CacheSchemaVersion = "save-preview-v2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _cacheRootPath;

    public SavePreviewCacheStore()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SimsModDesktop",
                "Cache",
                "SavePreview"))
    {
    }

    internal SavePreviewCacheStore(string cacheRootPath)
    {
        _cacheRootPath = cacheRootPath;
    }

    public string GetCacheRootPath(string saveFilePath)
    {
        var normalized = NormalizePath(saveFilePath);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return Path.Combine(_cacheRootPath, ComputeShortHash(normalized));
    }

    public bool IsCurrent(string saveFilePath, SavePreviewCacheManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(manifest);

        var file = new FileInfo(Path.GetFullPath(saveFilePath));
        if (!file.Exists)
        {
            return false;
        }

        return string.Equals(manifest.SourceSavePath, file.FullName, StringComparison.OrdinalIgnoreCase) &&
               manifest.SourceLength == file.Length &&
               manifest.SourceLastWriteTimeUtc == file.LastWriteTimeUtc &&
               string.Equals(manifest.CacheSchemaVersion, CacheSchemaVersion, StringComparison.Ordinal);
    }

    public bool TryLoad(string saveFilePath, out SavePreviewCacheManifest manifest)
    {
        manifest = null!;
        var cacheRoot = GetCacheRootPath(saveFilePath);
        if (string.IsNullOrWhiteSpace(cacheRoot))
        {
            return false;
        }

        var manifestPath = Path.Combine(cacheRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(manifestPath);
            var payload = JsonSerializer.Deserialize<SavePreviewCacheManifestPayload>(stream, JsonOptions);
            if (payload is null)
            {
                return false;
            }

            manifest = new SavePreviewCacheManifest
            {
                SourceSavePath = payload.SourceSavePath,
                SourceLength = payload.SourceLength,
                SourceLastWriteTimeUtc = payload.SourceLastWriteTimeUtc,
                CacheSchemaVersion = payload.CacheSchemaVersion,
                BuildStartedUtc = payload.BuildStartedUtc,
                BuildCompletedUtc = payload.BuildCompletedUtc,
                TotalHouseholdCount = payload.TotalHouseholdCount,
                ExportableHouseholdCount = payload.ExportableHouseholdCount,
                ReadyHouseholdCount = payload.ReadyHouseholdCount,
                FailedHouseholdCount = payload.FailedHouseholdCount,
                BlockedHouseholdCount = payload.BlockedHouseholdCount,
                Entries = payload.Entries
            };
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Save(string saveFilePath, SavePreviewCacheManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(saveFilePath);
        ArgumentNullException.ThrowIfNull(manifest);

        var cacheRoot = GetCacheRootPath(saveFilePath);
        Directory.CreateDirectory(cacheRoot);
        var manifestPath = Path.Combine(cacheRoot, "manifest.json");

        var payload = new SavePreviewCacheManifestPayload
        {
            SourceSavePath = manifest.SourceSavePath,
            SourceLength = manifest.SourceLength,
            SourceLastWriteTimeUtc = manifest.SourceLastWriteTimeUtc,
            CacheSchemaVersion = CacheSchemaVersion,
            BuildStartedUtc = manifest.BuildStartedUtc,
            BuildCompletedUtc = manifest.BuildCompletedUtc,
            TotalHouseholdCount = manifest.TotalHouseholdCount,
            ExportableHouseholdCount = manifest.ExportableHouseholdCount,
            ReadyHouseholdCount = manifest.ReadyHouseholdCount,
            FailedHouseholdCount = manifest.FailedHouseholdCount,
            BlockedHouseholdCount = manifest.BlockedHouseholdCount,
            Entries = manifest.Entries.ToList()
        };

        using var stream = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, payload, JsonOptions);
    }

    public void Clear(string saveFilePath)
    {
        var cacheRoot = GetCacheRootPath(saveFilePath);
        if (string.IsNullOrWhiteSpace(cacheRoot) || !Directory.Exists(cacheRoot))
        {
            return;
        }

        try
        {
            Directory.Delete(cacheRoot, recursive: true);
        }
        catch
        {
        }
    }

    private static string NormalizePath(string? saveFilePath)
    {
        return string.IsNullOrWhiteSpace(saveFilePath)
            ? string.Empty
            : Path.GetFullPath(saveFilePath.Trim());
    }

    private static string ComputeShortHash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16];
    }
}
