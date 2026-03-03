namespace SimsModDesktop.Application.Configuration;

/// <summary>
/// 跨平台配置提供器接口
/// </summary>
public interface IConfigurationProvider
{
    Task<T?> GetConfigurationAsync<T>(string key, CancellationToken cancellationToken = default);
    Task SetConfigurationAsync<T>(string key, T value, CancellationToken cancellationToken = default);
    Task<bool> ContainsKeyAsync(string key, CancellationToken cancellationToken = default);
    Task<bool> RemoveConfigurationAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetAllKeysAsync(CancellationToken cancellationToken = default);
    Task<bool> IsPlatformSpecificAsync(string key, CancellationToken cancellationToken = default);
    string GetPlatformSpecificPrefix();
    T? GetDefaultValue<T>(string key);
    Task<bool> ResetToDefaultAsync(string key, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, object?>> GetConfigurationsAsync(
        IReadOnlyList<string> keys,
        CancellationToken cancellationToken = default);
    Task SetConfigurationsAsync(
        IReadOnlyDictionary<string, object> configurations,
        CancellationToken cancellationToken = default);
}
