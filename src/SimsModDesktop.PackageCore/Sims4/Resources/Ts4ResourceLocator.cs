namespace SimsModDesktop.PackageCore;

public sealed class Ts4ResourceLocator : ITS4ResourceLocator
{
    public IReadOnlyList<ResourceLocation> Find(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var matches = new List<ResourceLocation>();
        var dedupe = new HashSet<ResourceLocation>();

        if (snapshot.ExactIndex.TryGetValue(key, out var exactMatches))
        {
            foreach (var location in exactMatches)
            {
                if (dedupe.Add(location))
                {
                    matches.Add(location);
                }
            }
        }

        if (matches.Count == 0 &&
            snapshot.TypeInstanceIndex.TryGetValue(new TypeInstanceKey(key.Type, key.Instance), out var typeInstanceMatches))
        {
            foreach (var location in typeInstanceMatches)
            {
                if (dedupe.Add(location))
                {
                    matches.Add(location);
                }
            }
        }

        if (matches.Count == 0)
        {
            return Array.Empty<ResourceLocation>();
        }

        return policy switch
        {
            ResourceLookupPolicy.PreserveCatalogOrder => matches,
            ResourceLookupPolicy.PreferGameSdxMods => matches.OrderBy(ScoreGameSdxMods).ToArray(),
            _ => matches.OrderBy(ScoreModsSdxGame).ToArray()
        };
    }

    public bool TryResolveFirst(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, out ResourceLocation location, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame)
    {
        var matches = Find(snapshot, key, policy);
        if (matches.Count > 0)
        {
            location = matches[0];
            return true;
        }

        location = default;
        return false;
    }

    private static int ScoreModsSdxGame(ResourceLocation location)
    {
        return ResolveSourceKind(location.FilePath) switch
        {
            ResourceSourceKind.Mods => 0,
            ResourceSourceKind.Sdx => 1,
            _ => 2
        };
    }

    private static int ScoreGameSdxMods(ResourceLocation location)
    {
        return ResolveSourceKind(location.FilePath) switch
        {
            ResourceSourceKind.Game => 0,
            ResourceSourceKind.Sdx => 1,
            _ => 2
        };
    }

    private static ResourceSourceKind ResolveSourceKind(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ResourceSourceKind.Game;
        }

        var segments = filePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (string.Equals(segment, "Mods", StringComparison.OrdinalIgnoreCase))
            {
                return ResourceSourceKind.Mods;
            }

            if (string.Equals(segment, "content", StringComparison.OrdinalIgnoreCase))
            {
                return ResourceSourceKind.Sdx;
            }
        }

        return ResourceSourceKind.Game;
    }

    private enum ResourceSourceKind
    {
        Game = 0,
        Sdx = 1,
        Mods = 2
    }
}
