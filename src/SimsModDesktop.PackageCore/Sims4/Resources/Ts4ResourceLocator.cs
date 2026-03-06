namespace SimsModDesktop.PackageCore;

public sealed class Ts4ResourceLocator : ITS4ResourceLocator
{
    public Ts4ResourceResolution Resolve(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var dedupe = new HashSet<ResourceLocation>();
        var rawMatches = new List<(ResourceLocation Location, Ts4ResourceMatchMode MatchMode, int CatalogIndex)>();
        var catalogIndex = 0;

        if (snapshot.ExactIndex.TryGetValue(key, out var exactMatches))
        {
            foreach (var location in exactMatches)
            {
                if (dedupe.Add(location))
                {
                    rawMatches.Add((location, Ts4ResourceMatchMode.Exact, catalogIndex));
                    catalogIndex++;
                }
            }
        }

        if (rawMatches.Count == 0 &&
            snapshot.TypeInstanceIndex.TryGetValue(new TypeInstanceKey(key.Type, key.Instance), out var typeInstanceMatches))
        {
            foreach (var location in typeInstanceMatches)
            {
                if (dedupe.Add(location))
                {
                    rawMatches.Add((location, Ts4ResourceMatchMode.TypeInstanceFallback, catalogIndex));
                    catalogIndex++;
                }
            }
        }

        if (rawMatches.Count == 0)
        {
            return new Ts4ResourceResolution
            {
                RequestedKey = key,
                Policy = policy,
                MatchMode = Ts4ResourceMatchMode.NotFound,
                Candidates = Array.Empty<Ts4ResourceResolutionCandidate>(),
                SelectedCandidateIndex = -1
            };
        }

        var orderedMatches = rawMatches
            .Select(item =>
            {
                var sourceKind = ResolveSourceKind(item.Location.FilePath);
                var score = ScoreByPolicy(policy, sourceKind);
                return (item.Location, item.MatchMode, item.CatalogIndex, SourceKind: sourceKind, Score: score);
            })
            .OrderBy(item => item.Score)
            .ThenBy(item => item.CatalogIndex)
            .ToArray();

        var candidates = new Ts4ResourceResolutionCandidate[orderedMatches.Length];
        for (var index = 0; index < orderedMatches.Length; index++)
        {
            var item = orderedMatches[index];
            candidates[index] = new Ts4ResourceResolutionCandidate
            {
                Location = item.Location,
                SourceKind = item.SourceKind,
                MatchMode = item.MatchMode,
                Score = item.Score,
                Selected = index == 0
            };
        }

        return new Ts4ResourceResolution
        {
            RequestedKey = key,
            Policy = policy,
            MatchMode = candidates[0].MatchMode,
            Candidates = candidates,
            SelectedCandidateIndex = 0
        };
    }

    public IReadOnlyList<ResourceLocation> Find(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame)
    {
        var resolution = Resolve(snapshot, key, policy);
        if (!resolution.Found)
        {
            return Array.Empty<ResourceLocation>();
        }

        return resolution.Candidates
            .Select(static candidate => candidate.Location)
            .ToArray();
    }

    public bool TryResolveFirst(DbpfCatalogSnapshot snapshot, DbpfResourceKey key, out ResourceLocation location, ResourceLookupPolicy policy = ResourceLookupPolicy.PreferModsSdxGame)
    {
        var resolution = Resolve(snapshot, key, policy);
        if (resolution.SelectedLocation is { } selectedLocation)
        {
            location = selectedLocation;
            return true;
        }

        location = default;
        return false;
    }

    private static int ScoreByPolicy(ResourceLookupPolicy policy, Ts4ResourceSourceKind sourceKind)
    {
        return policy switch
        {
            ResourceLookupPolicy.PreferGameSdxMods => sourceKind switch
            {
                Ts4ResourceSourceKind.Game => 0,
                Ts4ResourceSourceKind.Sdx => 1,
                _ => 2
            },
            ResourceLookupPolicy.PreserveCatalogOrder => 0,
            _ => sourceKind switch
            {
                Ts4ResourceSourceKind.Mods => 0,
                Ts4ResourceSourceKind.Sdx => 1,
                _ => 2
            }
        };
    }

    private static Ts4ResourceSourceKind ResolveSourceKind(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return Ts4ResourceSourceKind.Game;
        }

        var segments = filePath
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

        foreach (var segment in segments)
        {
            if (string.Equals(segment, "Mods", StringComparison.OrdinalIgnoreCase))
            {
                return Ts4ResourceSourceKind.Mods;
            }

            if (string.Equals(segment, "content", StringComparison.OrdinalIgnoreCase))
            {
                return Ts4ResourceSourceKind.Sdx;
            }
        }

        return Ts4ResourceSourceKind.Game;
    }
}
