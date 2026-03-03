using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Execution;

namespace SimsModDesktop.Infrastructure.Configuration;

/// <summary>
/// 跨平台配置提供器实现，包含线程安全缓存和落盘持久化。
/// </summary>
public sealed class CrossPlatformConfigurationProvider : IConfigurationProvider
{
    private readonly ILogger<CrossPlatformConfigurationProvider> _logger;
    private readonly ConcurrentDictionary<string, JsonElement> _configurationCache;
    private readonly Dictionary<string, object> _defaultValues;
    private readonly HashSet<string> _platformSpecificKeys;
    private readonly SemaphoreSlim _ioGate = new(1, 1);
    private readonly string _storagePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true
    };

    public CrossPlatformConfigurationProvider(ILogger<CrossPlatformConfigurationProvider> logger, string? storagePath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configurationCache = new ConcurrentDictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        _defaultValues = InitializeDefaultValues();
        _platformSpecificKeys = InitializePlatformSpecificKeys();
        _storagePath = string.IsNullOrWhiteSpace(storagePath) ? GetConfigurationStoragePath() : storagePath;
        LoadFromDisk();
    }

    public Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));
        }

        if (_configurationCache.TryGetValue(key, out var directValue))
        {
            return Task.FromResult(DeserializeJsonElement<T>(directValue));
        }

        var platformKey = GetPlatformSpecificKey(key);
        if (_configurationCache.TryGetValue(platformKey, out var platformValue))
        {
            return Task.FromResult(DeserializeJsonElement<T>(platformValue));
        }

        return Task.FromResult(GetDefaultValue<T>(key));
    }

    public async Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value));
        }

        var actualKey = GetPlatformSpecificKey(key);
        _configurationCache[actualKey] = JsonSerializer.SerializeToElement(value, _jsonOptions);
        await PersistAsync(cancellationToken);
    }

    public Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(false);
        }

        var platformKey = GetPlatformSpecificKey(key);
        return Task.FromResult(_configurationCache.ContainsKey(key) || _configurationCache.ContainsKey(platformKey));
    }

    public async Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var platformKey = GetPlatformSpecificKey(key);
        var removed = _configurationCache.TryRemove(key, out _) || _configurationCache.TryRemove(platformKey, out _);
        if (removed)
        {
            await PersistAsync(cancellationToken);
        }

        return removed;
    }

    public Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> keys = _configurationCache.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray();
        return Task.FromResult(keys);
    }

    public Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_platformSpecificKeys.Contains(key) || key.StartsWith(GetPlatformSpecificPrefix(), StringComparison.OrdinalIgnoreCase));
    }

    public string GetPlatformSpecificPrefix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows_";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux_";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macos_";
        }

        return "unknown_";
    }

    public T? GetDefaultValue<T>(string key)
    {
        if (!_defaultValues.TryGetValue(key, out var defaultValue))
        {
            return default;
        }

        return ConvertValue<T>(defaultValue);
    }

    public async Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        if (_defaultValues.TryGetValue(key, out var defaultValue))
        {
            await SetConfigurationAsync(key, defaultValue, cancellationToken);
            return true;
        }

        return await RemoveConfigurationAsync(key, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default)
    {
        if (keys.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in keys)
        {
            result[key] = await GetConfigurationAsync<object>(key, cancellationToken);
        }

        return result;
    }

    public async Task SetConfigurationsAsync(
        IReadOnlyDictionary<string, object> configurations,
        CancellationToken cancellationToken = default)
    {
        if (configurations.Count == 0)
        {
            return;
        }

        foreach (var kvp in configurations)
        {
            var actualKey = GetPlatformSpecificKey(kvp.Key);
            _configurationCache[actualKey] = JsonSerializer.SerializeToElement(kvp.Value, _jsonOptions);
        }

        await PersistAsync(cancellationToken);
    }

    private string GetPlatformSpecificKey(string key)
    {
        var prefix = GetPlatformSpecificPrefix();
        return key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? key : prefix + key;
    }

    private static T? DeserializeJsonElement<T>(JsonElement value)
    {
        try
        {
            return value.Deserialize<T>();
        }
        catch
        {
            return default;
        }
    }

    private T? ConvertValue<T>(object value)
    {
        if (value is T typedValue)
        {
            return typedValue;
        }

        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            _logger.LogWarning("Failed to convert default value {Value} to {Type}", value, typeof(T).Name);
            return default;
        }
    }

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        await _ioGate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
            var snapshot = _configurationCache.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var json = JsonSerializer.Serialize(snapshot, _jsonOptions);
            await File.WriteAllTextAsync(_storagePath, json, cancellationToken);
        }
        finally
        {
            _ioGate.Release();
        }
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(_storagePath))
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(_storagePath);
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var data = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(text);
            if (data is null)
            {
                return;
            }

            foreach (var pair in data)
            {
                _configurationCache[pair.Key] = pair.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load configuration from {Path}", _storagePath);
        }
    }

    private static string GetConfigurationStoragePath()
    {
        var baseDir = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(baseDir))
        {
            baseDir = AppContext.BaseDirectory;
        }

        return Path.Combine(baseDir, "SimsToolkit", "configuration.json");
    }

    private static Dictionary<string, object> InitializeDefaultValues()
    {
        return new Dictionary<string, object>
        {
            ["FileOperation.ConflictStrategy"] = ConflictResolutionStrategy.Prompt,
            ["FileOperation.VerifyContent"] = false,
            ["FileOperation.PrefixHashBytes"] = 102400,
            ["FileOperation.WorkerCount"] = 8,
            ["FileOperation.Recursive"] = true,
            ["FileOperation.WhatIf"] = false,
            ["Execution.UseUnifiedEngine"] = true,
            ["Execution.EnableFallbackToPowerShell"] = true,
            ["Execution.FallbackOnValidationFailure"] = false,
            ["Normalize.ReplaceInvalidChars"] = true,
            ["Normalize.CasePolicy"] = "keep",
            ["Merge.FailOnOverlappingPaths"] = false,
            ["Organize.CleanupMacOsArtifacts"] = true,
            ["HashComputation.DefaultAlgorithm"] = "MD5",
            ["HashComputation.BufferSize"] = 8192,
            ["HashComputation.ParallelThreshold"] = 100,
            ["Platform.Windows.RecycleBinEnabled"] = true,
            ["Platform.Linux.TrashDirectory"] = "~/.local/share/Trash",
            ["Platform.MacOS.TrashDirectory"] = "~/.Trash",
            ["Performance.MaxWorkerCount"] = 16,
            ["Performance.MemoryLimitMB"] = 512,
            ["Performance.IOParallelism"] = 4,
            ["Logging.EnableConsole"] = true,
            ["Logging.EnableFile"] = true,
            ["Logging.FilePath"] = "logs/sims-toolkit-{Date}.log"
        };
    }

    private static HashSet<string> InitializePlatformSpecificKeys()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Platform.Windows.RecycleBinEnabled",
            "Platform.Linux.TrashDirectory",
            "Platform.MacOS.TrashDirectory",
            "Platform.Windows.PowerShellPath",
            "Platform.Linux.ShellPath",
            "Platform.MacOS.ShellPath"
        };
    }
}
