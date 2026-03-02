using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SimsModDesktop.Application.Execution;

namespace SimsModDesktop.Application.Configuration;

/// <summary>
/// 跨平台配置提供器接口
/// </summary>
public interface IConfigurationProvider
{
    /// <summary>
    /// 获取配置值
    /// </summary>
    /// <typeparam name="T">配置值类型</typeparam>
    /// <param name="key">配置键</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>配置值</returns>
    Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置配置值
    /// </summary>
    /// <typeparam name="T">配置值类型</typeparam>
    /// <param name="key">配置键</param>
    /// <param name="value">配置值</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查配置键是否存在
    /// </summary>
    /// <param name="key">配置键</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除配置键
    /// </summary>
    /// <param name="key">配置键</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有配置键
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查配置是否为平台特定的
    /// </summary>
    /// <param name="key">配置键</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取当前平台特定的配置前缀
    /// </summary>
    string GetPlatformSpecificPrefix();

    /// <summary>
    /// 获取默认配置值（跨平台）
    /// </summary>
    /// <typeparam name="T">配置值类型</typeparam>
    /// <param name="key">配置键</param>
    /// <returns>默认配置值</returns>
    T? GetDefaultValue<T>(string key);

    /// <summary>
    /// 重置配置为默认值
    /// </summary>
    /// <param name="key">配置键</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量获取配置
    /// </summary>
    /// <param name="keys">配置键列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default);

    /// <summary>
    /// 批量设置配置
    /// </summary>
    /// <param name="configurations">配置键值对</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task SetConfigurationsAsync(IReadOnlyDictionary<string, object> configurations, CancellationToken cancellationToken = default);
}

/// <summary>
/// 跨平台配置提供器实现
/// </summary>
public sealed class CrossPlatformConfigurationProvider : IConfigurationProvider
{
    private readonly ILogger<CrossPlatformConfigurationProvider> _logger;
    private readonly Dictionary<string, object> _configurationCache;
    private readonly Dictionary<string, object> _defaultValues;
    private readonly HashSet<string> _platformSpecificKeys;

    public CrossPlatformConfigurationProvider(ILogger<CrossPlatformConfigurationProvider> logger)
    {
        _logger = logger;
        _configurationCache = new Dictionary<string, object>();
        _defaultValues = InitializeDefaultValues();
        _platformSpecificKeys = InitializePlatformSpecificKeys();
    }

    /// <inheritdoc />
    public async Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));
        }

        try
        {
            // 首先检查缓存
            if (_configurationCache.TryGetValue(key, out var cachedValue))
            {
                return ConvertValue<T>(cachedValue);
            }

            // 检查平台特定配置
            var platformKey = GetPlatformSpecificKey(key);
            if (_configurationCache.TryGetValue(platformKey, out var platformValue))
            {
                return ConvertValue<T>(platformValue);
            }

            // 返回默认值
            var defaultValue = GetDefaultValue<T>(key);
            
            _logger.LogDebug("Using default value for configuration key: {Key}", key);
            return defaultValue;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get configuration for key: {Key}", key);
            return default;
        }
    }

    /// <inheritdoc />
    public async Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException("Configuration key cannot be null or empty", nameof(key));
        }

        try
        {
            var actualKey = GetPlatformSpecificKey(key);
            _configurationCache[actualKey] = value ?? throw new ArgumentNullException(nameof(value));
            
            _logger.LogInformation("Set configuration: {Key} = {Value}", actualKey, value);
            
            // 模拟异步操作
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set configuration for key: {Key}", key);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var platformKey = GetPlatformSpecificKey(key);
        return _configurationCache.ContainsKey(key) || _configurationCache.ContainsKey(platformKey);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        try
        {
            var platformKey = GetPlatformSpecificKey(key);
            var removed = _configurationCache.Remove(key) || _configurationCache.Remove(platformKey);
            
            if (removed)
            {
                _logger.LogInformation("Removed configuration: {Key}", key);
            }
            
            return removed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove configuration for key: {Key}", key);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var keys = _configurationCache.Keys.ToList();
            return keys.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get all configuration keys");
            return Array.Empty<string>();
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        return _platformSpecificKeys.Contains(key) || key.StartsWith(GetPlatformSpecificPrefix());
    }

    /// <inheritdoc />
    public string GetPlatformSpecificPrefix()
    {
        var platform = RuntimeInformation.OSDescription.ToLowerInvariant();
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows_";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux_";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return "macos_";
        }
        else
        {
            return "unknown_";
        }
    }

    /// <inheritdoc />
    public T? GetDefaultValue<T>(string key)
    {
        if (_defaultValues.TryGetValue(key, out var defaultValue))
        {
            return ConvertValue<T>(defaultValue);
        }

        return default;
    }

    /// <inheritdoc />
    public async Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        try
        {
            if (_defaultValues.TryGetValue(key, out var defaultValue))
            {
                await SetConfigurationAsync(key, defaultValue, cancellationToken);
                _logger.LogInformation("Reset configuration to default: {Key}", key);
                return true;
            }

            // 如果没有默认值，则删除配置
            return await RemoveConfigurationAsync(key, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset configuration to default for key: {Key}", key);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(IReadOnlyList<string> keys, CancellationToken cancellationToken = default)
    {
        if (keys == null || keys.Count == 0)
        {
            return new Dictionary<string, object?>();
        }

        var result = new Dictionary<string, object?>();

        foreach (var key in keys)
        {
            try
            {
                var value = await GetConfigurationAsync<object>(key, cancellationToken);
                result[key] = value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get configuration for key: {Key}", key);
                result[key] = null;
            }
        }

        return result;
    }

    /// <inheritdoc />
    public async Task SetConfigurationsAsync(IReadOnlyDictionary<string, object> configurations, CancellationToken cancellationToken = default)
    {
        if (configurations == null || configurations.Count == 0)
        {
            return;
        }

        foreach (var kvp in configurations)
        {
            try
            {
                await SetConfigurationAsync(kvp.Key, kvp.Value, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to set configuration for key: {Key}", kvp.Key);
            }
        }
    }

    #region 私有方法

    private string GetPlatformSpecificKey(string key)
    {
        var prefix = GetPlatformSpecificPrefix();
        return key.StartsWith(prefix) ? key : prefix + key;
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
            _logger.LogWarning("Failed to convert value {Value} to type {Type}", value, typeof(T).Name);
            return default;
        }
    }

    private Dictionary<string, object> InitializeDefaultValues()
    {
        return new Dictionary<string, object>
        {
            // 文件操作默认配置
            ["FileOperation.ConflictStrategy"] = ConflictResolutionStrategy.Prompt,
            ["FileOperation.VerifyContent"] = false,
            ["FileOperation.PrefixHashBytes"] = 102400,
            ["FileOperation.WorkerCount"] = 8,
            ["FileOperation.Recursive"] = true,
            ["FileOperation.WhatIf"] = false,

            // 执行路由默认配置
            ["Execution.UseUnifiedEngine"] = true,
            ["Execution.EnableFallbackToPowerShell"] = true,
            ["Execution.FallbackOnValidationFailure"] = false,

            // Normalize默认配置
            ["Normalize.ReplaceInvalidChars"] = true,
            ["Normalize.CasePolicy"] = "keep",

            // Merge默认配置
            ["Merge.FailOnOverlappingPaths"] = false,

            // Organize默认配置
            ["Organize.CleanupMacOsArtifacts"] = true,
            
            // 哈希计算默认配置
            ["HashComputation.DefaultAlgorithm"] = "MD5",
            ["HashComputation.BufferSize"] = 8192,
            ["HashComputation.ParallelThreshold"] = 100,
            
            // 平台特定默认配置
            ["Platform.Windows.RecycleBinEnabled"] = true,
            ["Platform.Linux.TrashDirectory"] = "~/.local/share/Trash",
            ["Platform.MacOS.TrashDirectory"] = "~/.Trash",
            
            // 性能默认配置
            ["Performance.MaxWorkerCount"] = 16,
            ["Performance.MemoryLimitMB"] = 512,
            ["Performance.IOParallelism"] = 4,
            
            // 日志默认配置
            ["Logging.Level"] = LogLevel.Information,
            ["Logging.EnableConsole"] = true,
            ["Logging.EnableFile"] = true,
            ["Logging.FilePath"] = "logs/sims-toolkit-{Date}.log"
        };
    }

    private HashSet<string> InitializePlatformSpecificKeys()
    {
        return new HashSet<string>
        {
            "Platform.Windows.RecycleBinEnabled",
            "Platform.Linux.TrashDirectory",
            "Platform.MacOS.TrashDirectory",
            "Platform.Windows.PowerShellPath",
            "Platform.Linux.ShellPath",
            "Platform.MacOS.ShellPath"
        };
    }

    #endregion
}
