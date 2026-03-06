using System.Collections.Concurrent;

namespace SimsModDesktop.Presentation.Warmup;

internal sealed class WarmupSessionRegistry
{
    private readonly ConcurrentDictionary<string, object> _sessions = new(StringComparer.Ordinal);

    public bool TryGet<T>(WarmupSessionKey key, out WarmupTaskSession<T>? session)
    {
        if (_sessions.TryGetValue(key.ToString(), out var value) && value is WarmupTaskSession<T> typed)
        {
            session = typed;
            return true;
        }

        session = null;
        return false;
    }

    public void Set<T>(WarmupSessionKey key, WarmupTaskSession<T> session)
    {
        _sessions[key.ToString()] = session;
    }

    public bool TryRemove<T>(WarmupSessionKey key, out WarmupTaskSession<T>? session)
    {
        if (_sessions.TryRemove(key.ToString(), out var value) && value is WarmupTaskSession<T> typed)
        {
            session = typed;
            return true;
        }

        session = null;
        return false;
    }

    public IEnumerable<KeyValuePair<WarmupSessionKey, WarmupTaskSession<T>>> FindByDomainAndSource<T>(
        string sourceKey,
        SimsModDesktop.Application.Caching.CacheWarmupDomain domain)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value is not WarmupTaskSession<T> typed)
            {
                continue;
            }

            if (!TryParseKey(pair.Key, out var key) ||
                key.Domain != domain ||
                !string.Equals(key.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new KeyValuePair<WarmupSessionKey, WarmupTaskSession<T>>(key, typed);
        }
    }

    public IEnumerable<KeyValuePair<WarmupSessionKey, WarmupTaskSession<T>>> FindBySourcePrefix<T>(
        string sourcePrefix,
        SimsModDesktop.Application.Caching.CacheWarmupDomain domain)
    {
        foreach (var pair in _sessions)
        {
            if (pair.Value is not WarmupTaskSession<T> typed)
            {
                continue;
            }

            if (!TryParseKey(pair.Key, out var key) ||
                key.Domain != domain ||
                !key.SourceKey.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            yield return new KeyValuePair<WarmupSessionKey, WarmupTaskSession<T>>(key, typed);
        }
    }

    public void Clear()
    {
        _sessions.Clear();
    }

    private static bool TryParseKey(string raw, out WarmupSessionKey key)
    {
        var parts = raw.Split('|');
        if (parts.Length < 4 ||
            !Enum.TryParse(parts[0], out SimsModDesktop.Application.Caching.CacheWarmupDomain domain))
        {
            key = default;
            return false;
        }

        key = new WarmupSessionKey(
            domain,
            parts[1],
            string.IsNullOrWhiteSpace(parts[2]) ? null : parts[2],
            string.IsNullOrWhiteSpace(parts[3]) ? null : parts[3]);
        return true;
    }
}
