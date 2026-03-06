namespace SimsModDesktop.Application.Caching;

public interface IListQueryCache
{
    bool TryGet<TValue>(string domain, string key, out TValue? value);

    void Set<TValue>(string domain, string key, TValue value);

    void InvalidateDomain(string domain, string? keyPrefix = null);

    void Clear();
}
