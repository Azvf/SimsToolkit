using System.Collections.Concurrent;

namespace SimsModDesktop.PackageCore;

public interface ISims4StblLookup
{
    string? TryResolveString(
        DbpfPackageReadSession session,
        DbpfPackageIndex index,
        uint stringKey);
}

public sealed class Sims4StblLookup : ISims4StblLookup
{
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<uint, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string? TryResolveString(
        DbpfPackageReadSession session,
        DbpfPackageIndex index,
        uint stringKey)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(index);

        if (stringKey == 0)
        {
            return null;
        }

        var cacheKey = $"{index.FilePath}|{index.Fingerprint.Length}|{index.Fingerprint.LastWriteUtcTicks}";
        var table = _cache.GetOrAdd(cacheKey, _ => BuildLookup(session, index));
        return table.TryGetValue(stringKey, out var value) ? value : null;
    }

    private static IReadOnlyDictionary<uint, string> BuildLookup(
        DbpfPackageReadSession session,
        DbpfPackageIndex index)
    {
        var result = new Dictionary<uint, string>();

        foreach (var entry in index.Entries)
        {
            if (entry.IsDeleted || !Sims4ResourceTypeRegistry.IsStringTableType(entry.Type))
            {
                continue;
            }

            if (!session.TryReadBytes(entry, out var bytes, out _) ||
                !Sims4StblParser.TryParse(bytes, out var table, out _))
            {
                continue;
            }

            foreach (var pair in table.Entries)
            {
                if (!result.ContainsKey(pair.Key))
                {
                    result[pair.Key] = pair.Value;
                }
            }
        }

        return result;
    }
}
