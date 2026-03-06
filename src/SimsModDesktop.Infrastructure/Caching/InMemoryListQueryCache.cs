using System.Collections.Concurrent;
using SimsModDesktop.Application.Caching;

namespace SimsModDesktop.Infrastructure.Caching;

public sealed class InMemoryListQueryCache : IListQueryCache
{
    private readonly ConcurrentDictionary<string, object> _entries = new(StringComparer.Ordinal);

    public bool TryGet<TValue>(string domain, string key, out TValue? value)
    {
        var cacheKey = BuildKey(domain, key);
        if (_entries.TryGetValue(cacheKey, out var stored) && stored is TValue typed)
        {
            value = typed;
            return true;
        }

        value = default;
        return false;
    }

    public void Set<TValue>(string domain, string key, TValue value)
    {
        _entries[BuildKey(domain, key)] = value!;
    }

    public void InvalidateDomain(string domain, string? keyPrefix = null)
    {
        var prefix = BuildKey(domain, keyPrefix ?? string.Empty);
        foreach (var key in _entries.Keys.Where(entry => entry.StartsWith(prefix, StringComparison.Ordinal)))
        {
            _entries.TryRemove(key, out _);
        }
    }

    public void Clear()
    {
        _entries.Clear();
    }

    private static string BuildKey(string domain, string key)
    {
        var normalizedDomain = domain?.Trim() ?? string.Empty;
        var normalizedKey = key?.Trim() ?? string.Empty;
        return normalizedDomain + "|" + normalizedKey;
    }
}
