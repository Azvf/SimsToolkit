using System.Globalization;
using System.Xml.Linq;

namespace SimsModDesktop.PackageCore;

public sealed class Ts4CasModifierTuningCatalog
{
    public static Ts4CasModifierTuningCatalog Empty { get; } = new()
    {
        ByModifierHash = new Dictionary<ulong, Ts4CasModifierTuningEntry>(),
        SourceResourceCount = 0,
        LoadIssues = Array.Empty<string>()
    };

    public required IReadOnlyDictionary<ulong, Ts4CasModifierTuningEntry> ByModifierHash { get; init; }
    public required int SourceResourceCount { get; init; }
    public required IReadOnlyList<string> LoadIssues { get; init; }
}

public sealed class Ts4CasModifierTuningEntry
{
    public required ulong ModifierHash { get; init; }
    public required string DisplayName { get; init; }
    public required IReadOnlyList<string> KnownNames { get; init; }
    public required IReadOnlyList<Ts4CasModifierScaleRule> ScaleRules { get; init; }
    public required IReadOnlyList<Ts4CasModifierDmapConversionRule> DmapConversions { get; init; }
    public required IReadOnlyList<Ts4CasModifierDampeningRule> DampeningRules { get; init; }
}

public sealed class Ts4CasModifierRestriction
{
    public IReadOnlyList<string> Species { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Occults { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Ages { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Genders { get; init; } = Array.Empty<string>();
}

public sealed class Ts4CasModifierScaleRule
{
    public required float Scale { get; init; }
    public required Ts4CasModifierRestriction Restriction { get; init; }
}

public sealed class Ts4CasModifierDmapConversionRule
{
    public required string DmapName { get; init; }
    public required Ts4CasModifierRestriction Restriction { get; init; }
    public uint? ConditionalBodyType { get; init; }
    public string? ConditionalPartGender { get; init; }
    public bool ActiveIfSimDoesntHaveBreasts { get; init; }
}

public sealed class Ts4CasModifierDampeningRule
{
    public required Ts4CasModifierRestriction Restriction { get; init; }
    public required IReadOnlyDictionary<ulong, float> SculptLimits { get; init; }
}

public sealed class Ts4CasModifierTuningCatalogLoader
{
    private const string CasModifierTuningClass = "Client_CASModifierTuning";
    private const string CasModifierPartGenderDmapsClass = "Client_CASModifierPartGenderDmaps";
    private const string CasModifierDampeningClass = "Client_CASModifierDampening";

    private readonly IDbpfResourceReader _resourceReader;

    public Ts4CasModifierTuningCatalogLoader(IDbpfResourceReader? resourceReader = null)
    {
        _resourceReader = resourceReader ?? new DbpfResourceReader();
    }

    public bool TryLoadFromPackage(string packagePath, out Ts4CasModifierTuningCatalog catalog, out string? error)
    {
        catalog = Ts4CasModifierTuningCatalog.Empty;
        error = null;

        if (string.IsNullOrWhiteSpace(packagePath))
        {
            error = "CASModifierTuning package path is empty.";
            return false;
        }

        if (!File.Exists(packagePath))
        {
            error = $"CASModifierTuning package was not found at '{packagePath}'.";
            return false;
        }

        DbpfPackageIndex packageIndex;
        try
        {
            packageIndex = DbpfPackageIndexReader.ReadPackageIndex(packagePath);
        }
        catch (Exception ex)
        {
            error = $"Failed to read CASModifierTuning package index: {ex.Message}";
            return false;
        }

        var mutable = new Dictionary<ulong, MutableModifierEntry>();
        var issues = new List<string>();
        var parsedResources = 0;

        using var session = _resourceReader.OpenSession(packagePath);
        for (var entryIndex = 0; entryIndex < packageIndex.Entries.Length; entryIndex++)
        {
            var entry = packageIndex.Entries[entryIndex];
            if (entry.IsDeleted ||
                (entry.Type != Sims4ResourceTypeRegistry.Tuning1 && entry.Type != Sims4ResourceTypeRegistry.Tuning2))
            {
                continue;
            }

            if (!session.TryReadBytes(entry, out var bytes, out var readError))
            {
                issues.Add($"[{entryIndex}] read failed: {readError ?? "unknown error"}");
                continue;
            }

            if (!TryDecodeXml(bytes, out var xml))
            {
                issues.Add($"[{entryIndex}] payload is not recognized XML.");
                continue;
            }

            if (!TryParseResourceXml(xml, entryIndex, mutable, out var parseError))
            {
                issues.Add(parseError ?? $"[{entryIndex}] parser failed.");
                continue;
            }

            parsedResources++;
        }

        catalog = new Ts4CasModifierTuningCatalog
        {
            ByModifierHash = mutable.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.ToImmutableEntry()),
            SourceResourceCount = parsedResources,
            LoadIssues = issues
        };
        return true;
    }

    private static bool TryDecodeXml(ReadOnlySpan<byte> bytes, out string xml)
    {
        xml = string.Empty;
        foreach (var encoding in new[] { System.Text.Encoding.UTF8, System.Text.Encoding.Unicode, System.Text.Encoding.BigEndianUnicode })
        {
            var text = encoding.GetString(bytes);
            var start = text.IndexOf('<');
            if (start < 0)
            {
                continue;
            }

            var candidate = text[start..].Trim('\0', '\uFEFF', ' ', '\t', '\r', '\n');
            if (candidate.Length == 0)
            {
                continue;
            }

            try
            {
                _ = XDocument.Parse(candidate, LoadOptions.None);
                xml = candidate;
                return true;
            }
            catch
            {
                // Try next candidate encoding.
            }
        }

        return false;
    }

    private static bool TryParseResourceXml(
        string xml,
        int entryIndex,
        IDictionary<ulong, MutableModifierEntry> mutable,
        out string? error)
    {
        error = null;
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (Exception ex)
        {
            error = $"[{entryIndex}] invalid XML: {ex.Message}";
            return false;
        }

        var node = doc
            .Descendants()
            .FirstOrDefault(candidate =>
            {
                var className = candidate.Attribute("c")?.Value;
                return string.Equals(className, CasModifierTuningClass, StringComparison.Ordinal) ||
                       string.Equals(className, CasModifierPartGenderDmapsClass, StringComparison.Ordinal) ||
                       string.Equals(className, CasModifierDampeningClass, StringComparison.Ordinal);
            });

        if (node is null)
        {
            error = $"[{entryIndex}] no supported CAS modifier tuning class found.";
            return false;
        }

        var className = node.Attribute("c")?.Value ?? string.Empty;
        if (string.Equals(className, CasModifierTuningClass, StringComparison.Ordinal))
        {
            ParseTuningNode(node, mutable);
            return true;
        }

        if (string.Equals(className, CasModifierPartGenderDmapsClass, StringComparison.Ordinal))
        {
            ParseConversionNode(node, mutable);
            return true;
        }

        if (string.Equals(className, CasModifierDampeningClass, StringComparison.Ordinal))
        {
            ParseDampeningNode(node, mutable);
            return true;
        }

        error = $"[{entryIndex}] unsupported class '{className}'.";
        return false;
    }

    private static void ParseTuningNode(XElement node, IDictionary<ulong, MutableModifierEntry> mutable)
    {
        var restriction = ParseRestriction(node, includeAgeDefaults: false);
        string? currentModifierName = null;
        foreach (var namedElement in EnumerateNamedElements(node))
        {
            var name = namedElement.Attribute("n")?.Value;
            if (string.Equals(name, "ModifierName", StringComparison.Ordinal))
            {
                currentModifierName = ReadElementText(namedElement);
                if (!string.IsNullOrWhiteSpace(currentModifierName))
                {
                    EnsureModifier(mutable, currentModifierName);
                }

                continue;
            }

            if (string.Equals(name, "Scale", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(currentModifierName) &&
                float.TryParse(ReadElementText(namedElement), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            {
                var modifier = EnsureModifier(mutable, currentModifierName!);
                modifier.ScaleRules.Add(new Ts4CasModifierScaleRule
                {
                    Scale = scale,
                    Restriction = restriction
                });
            }
        }
    }

    private static void ParseConversionNode(XElement node, IDictionary<ulong, MutableModifierEntry> mutable)
    {
        var restriction = ParseRestriction(node, includeAgeDefaults: false);
        var conditionalBodyType = TryParseUInt32(ReadNamedText(node, "ConditionalCasPartBodyType"));
        var conditionalPartGender = NormalizeToken(ReadNamedText(node, "ConditionalCasPartGender"));
        var activeIfNoBreasts = bool.TryParse(ReadNamedText(node, "ActiveIfSimDoesntHaveBreasts"), out var parsedActive) && parsedActive;

        string? currentModifierName = null;
        foreach (var namedElement in EnumerateNamedElements(node))
        {
            var name = namedElement.Attribute("n")?.Value;
            if (string.Equals(name, "Modifier", StringComparison.Ordinal))
            {
                currentModifierName = ReadElementText(namedElement);
                if (!string.IsNullOrWhiteSpace(currentModifierName))
                {
                    EnsureModifier(mutable, currentModifierName);
                }

                continue;
            }

            if (!string.Equals(name, "DmapName", StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(currentModifierName))
            {
                continue;
            }

            var dmapName = ReadElementText(namedElement);
            if (string.IsNullOrWhiteSpace(dmapName))
            {
                continue;
            }

            var modifier = EnsureModifier(mutable, currentModifierName!);
            modifier.DmapConversions.Add(new Ts4CasModifierDmapConversionRule
            {
                DmapName = dmapName,
                Restriction = restriction,
                ConditionalBodyType = conditionalBodyType,
                ConditionalPartGender = string.IsNullOrWhiteSpace(conditionalPartGender) ? null : conditionalPartGender,
                ActiveIfSimDoesntHaveBreasts = activeIfNoBreasts
            });
        }
    }

    private static void ParseDampeningNode(XElement node, IDictionary<ulong, MutableModifierEntry> mutable)
    {
        var restriction = ParseRestriction(node, includeAgeDefaults: true);
        string? currentModifierName = null;
        ulong currentSculptHash = 0;
        var currentLimits = new Dictionary<ulong, float>();

        void CommitCurrentModifier()
        {
            if (string.IsNullOrWhiteSpace(currentModifierName))
            {
                return;
            }

            var modifier = EnsureModifier(mutable, currentModifierName);
            modifier.DampeningRules.Add(new Ts4CasModifierDampeningRule
            {
                Restriction = restriction,
                SculptLimits = new Dictionary<ulong, float>(currentLimits)
            });
        }

        foreach (var namedElement in EnumerateNamedElements(node))
        {
            var name = namedElement.Attribute("n")?.Value;
            if (string.Equals(name, "ModifierName", StringComparison.Ordinal))
            {
                if (!string.IsNullOrWhiteSpace(currentModifierName))
                {
                    CommitCurrentModifier();
                }

                currentModifierName = ReadElementText(namedElement);
                currentSculptHash = 0;
                currentLimits.Clear();
                if (!string.IsNullOrWhiteSpace(currentModifierName))
                {
                    EnsureModifier(mutable, currentModifierName);
                }

                continue;
            }

            if (string.Equals(name, "SculptName", StringComparison.Ordinal))
            {
                var sculptName = ReadElementText(namedElement);
                currentSculptHash = string.IsNullOrWhiteSpace(sculptName)
                    ? 0
                    : Ts4FnvHash.Fnv64(sculptName);
                continue;
            }

            if (string.Equals(name, "Limit", StringComparison.Ordinal) &&
                currentSculptHash != 0 &&
                float.TryParse(ReadElementText(namedElement), NumberStyles.Float, CultureInfo.InvariantCulture, out var limit))
            {
                currentLimits[currentSculptHash] = limit;
            }
        }

        if (!string.IsNullOrWhiteSpace(currentModifierName))
        {
            CommitCurrentModifier();
        }
    }

    private static Ts4CasModifierRestriction ParseRestriction(XElement node, bool includeAgeDefaults)
    {
        var species = CollectTokens(node, "Species");
        var occults = CollectTokens(node, "OccultType");
        var ages = CollectTokens(node, "Ages", "Age");
        var genders = CollectTokens(node, "Genders", "Gender");

        if (species.Count == 0)
        {
            species.Add("HUMAN");
        }

        if (occults.Count == 0)
        {
            occults.Add("HUMAN");
        }

        if (genders.Count == 0)
        {
            genders.Add("MALE");
            genders.Add("FEMALE");
        }

        if (includeAgeDefaults && ages.Count == 0)
        {
            ages.Add("CHILD");
            ages.Add("ADULT");
        }

        return new Ts4CasModifierRestriction
        {
            Species = species,
            Occults = occults,
            Ages = ages,
            Genders = genders
        };
    }

    private static List<string> CollectTokens(XElement node, params string[] fieldNames)
    {
        var tokenSet = new HashSet<string>(StringComparer.Ordinal);
        var fields = new HashSet<string>(fieldNames, StringComparer.Ordinal);

        foreach (var field in EnumerateNamedElements(node))
        {
            var name = field.Attribute("n")?.Value;
            if (name is null || !fields.Contains(name))
            {
                continue;
            }

            foreach (var token in ExtractNestedTokens(field))
            {
                if (!string.IsNullOrWhiteSpace(token))
                {
                    tokenSet.Add(token);
                }
            }
        }

        return tokenSet.OrderBy(static token => token, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<XElement> EnumerateNamedElements(XElement root)
    {
        return root
            .Descendants()
            .Where(static candidate => candidate.Attribute("n") is not null);
    }

    private static IEnumerable<string> ExtractNestedTokens(XElement node)
    {
        var leaves = node
            .DescendantsAndSelf()
            .Where(static candidate => !candidate.HasElements)
            .Select(static candidate => NormalizeToken(candidate.Value))
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        return leaves;
    }

    private static string ReadElementText(XElement node)
    {
        return node
            .DescendantsAndSelf()
            .Where(static candidate => !candidate.HasElements)
            .Select(static candidate => candidate.Value?.Trim() ?? string.Empty)
            .FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value))
            ?? string.Empty;
    }

    private static string ReadNamedText(XElement node, string fieldName)
    {
        var field = EnumerateNamedElements(node)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.Attribute("n")?.Value, fieldName, StringComparison.Ordinal));
        return field is null ? string.Empty : ReadElementText(field);
    }

    private static MutableModifierEntry EnsureModifier(IDictionary<ulong, MutableModifierEntry> mutable, string modifierName)
    {
        var hash = Ts4FnvHash.Fnv64(modifierName);
        if (!mutable.TryGetValue(hash, out var entry))
        {
            entry = new MutableModifierEntry(hash, modifierName);
            mutable[hash] = entry;
        }

        entry.KnownNames.Add(modifierName);
        return entry;
    }

    private static string NormalizeToken(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        return raw.Trim().ToUpperInvariant();
    }

    private static uint? TryParseUInt32(string raw)
    {
        return uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private sealed class MutableModifierEntry
    {
        public MutableModifierEntry(ulong modifierHash, string displayName)
        {
            ModifierHash = modifierHash;
            DisplayName = displayName;
            KnownNames = new HashSet<string>(StringComparer.Ordinal);
            ScaleRules = new List<Ts4CasModifierScaleRule>();
            DmapConversions = new List<Ts4CasModifierDmapConversionRule>();
            DampeningRules = new List<Ts4CasModifierDampeningRule>();
        }

        public ulong ModifierHash { get; }
        public string DisplayName { get; private set; }
        public HashSet<string> KnownNames { get; }
        public List<Ts4CasModifierScaleRule> ScaleRules { get; }
        public List<Ts4CasModifierDmapConversionRule> DmapConversions { get; }
        public List<Ts4CasModifierDampeningRule> DampeningRules { get; }

        public Ts4CasModifierTuningEntry ToImmutableEntry()
        {
            if (KnownNames.Count > 0)
            {
                DisplayName = KnownNames.OrderBy(static name => name, StringComparer.Ordinal).First();
            }

            return new Ts4CasModifierTuningEntry
            {
                ModifierHash = ModifierHash,
                DisplayName = DisplayName,
                KnownNames = KnownNames.OrderBy(static name => name, StringComparer.Ordinal).ToArray(),
                ScaleRules = ScaleRules.ToArray(),
                DmapConversions = DmapConversions.ToArray(),
                DampeningRules = DampeningRules.ToArray()
            };
        }
    }
}
