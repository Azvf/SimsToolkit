using System.Text.Json.Nodes;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Presets;

public sealed class QuickPresetCatalog : IQuickPresetCatalog
{
    private static readonly HashSet<string> AllowedPresetKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "id",
        "name",
        "description",
        "action",
        "autoRun",
        "actionPatch",
        "sharedPatch"
    };

    private static readonly HashSet<string> AllowedSharedPatchKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "skipPruneEmptyDirs",
        "modFilesOnly",
        "verifyContentOnNameConflict",
        "modExtensions",
        "prefixHashBytes",
        "hashWorkerCount"
    };

    private static readonly IReadOnlyDictionary<SimsAction, HashSet<string>> AllowedActionPatchKeys =
        new Dictionary<SimsAction, HashSet<string>>
        {
            [SimsAction.Organize] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sourceDir",
                "zipNamePattern",
                "modsRoot",
                "unifiedModsFolder",
                "trayRoot",
                "keepZip"
            },
            [SimsAction.Flatten] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "rootPath",
                "flattenToRoot"
            },
            [SimsAction.Normalize] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "rootPath"
            },
            [SimsAction.Merge] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "sourcePaths",
                "targetPath"
            },
            [SimsAction.FindDuplicates] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "rootPath",
                "outputCsv",
                "recurse",
                "cleanup"
            },
            [SimsAction.TrayDependencies] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "trayPath",
                "modsPath",
                "trayItemKey",
                "analysisMode",
                "s4tiPath",
                "minMatchCount",
                "topN",
                "maxPackageCount",
                "exportUnusedPackages",
                "exportMatchedPackages",
                "outputCsv",
                "unusedOutputCsv",
                "exportTargetPath",
                "exportMinConfidence"
            },
            [SimsAction.TrayPreview] = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "trayPath",
                "trayItemKey",
                "topN",
                "maxFilesPerItem"
            }
        };

    private readonly object _gate = new();
    private readonly string _defaultPresetPath;
    private readonly string _userPresetPath;

    private List<QuickPresetDefinition> _presets = new();
    private List<string> _warnings = new();

    public QuickPresetCatalog(string? defaultPresetPath = null, string? userPresetPath = null)
    {
        _defaultPresetPath = defaultPresetPath ?? ResolveDefaultPresetPath();
        _userPresetPath = userPresetPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SimsModDesktop",
            "quick-presets.json");
    }

    public IReadOnlyList<string> LastWarnings
    {
        get
        {
            lock (_gate)
            {
                return _warnings.ToList();
            }
        }
    }

    public string UserPresetDirectory => Path.GetDirectoryName(_userPresetPath) ?? string.Empty;
    public string UserPresetPath => _userPresetPath;

    public IReadOnlyList<QuickPresetDefinition> GetAll()
    {
        lock (_gate)
        {
            return _presets.ToList();
        }
    }

    public async Task ReloadAsync(CancellationToken cancellationToken = default)
    {
        var warnings = new List<string>();
        var merged = new Dictionary<string, QuickPresetDefinition>(StringComparer.OrdinalIgnoreCase);

        await LoadSourceIntoAsync(_defaultPresetPath, isOptional: false, merged, warnings, cancellationToken);
        await LoadSourceIntoAsync(_userPresetPath, isOptional: true, merged, warnings, cancellationToken);

        lock (_gate)
        {
            _presets = merged.Values
                .OrderBy(preset => preset.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            _warnings = warnings;
        }
    }

    private static string ResolveDefaultPresetPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "assets", "quick-presets.default.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "assets", "quick-presets.default.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "SimsModDesktop", "assets", "quick-presets.default.json")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static async Task LoadSourceIntoAsync(
        string path,
        bool isOptional,
        Dictionary<string, QuickPresetDefinition> merged,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            if (!isOptional)
            {
                warnings.Add($"Preset file not found: {path}");
            }

            return;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken);
            var parsed = Parse(content, path, warnings);
            foreach (var preset in parsed)
            {
                merged[preset.Id] = preset;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Failed to load presets from {path}: {ex.Message}");
        }
    }

    private static IReadOnlyList<QuickPresetDefinition> Parse(string content, string source, List<string> warnings)
    {
        try
        {
            var rootNode = JsonNode.Parse(content) as JsonObject;
            if (rootNode is null)
            {
                warnings.Add($"Preset file '{source}' has invalid root JSON object.");
                return Array.Empty<QuickPresetDefinition>();
            }

            if (rootNode["presets"] is not JsonArray presetsNode)
            {
                warnings.Add($"Preset file '{source}' is missing array field 'presets'.");
                return Array.Empty<QuickPresetDefinition>();
            }

            var presets = new List<QuickPresetDefinition>();
            for (var i = 0; i < presetsNode.Count; i++)
            {
                if (presetsNode[i] is not JsonObject presetObject)
                {
                    warnings.Add($"Preset[{i}] in '{source}' is not an object.");
                    continue;
                }

                if (!TryParsePreset(presetObject, source, i, out var preset, out var error))
                {
                    warnings.Add(error);
                    continue;
                }

                presets.Add(preset);
            }

            return presets;
        }
        catch (Exception ex)
        {
            warnings.Add($"Preset file '{source}' parse failed: {ex.Message}");
            return Array.Empty<QuickPresetDefinition>();
        }
    }

    private static bool TryParsePreset(
        JsonObject presetObject,
        string source,
        int index,
        out QuickPresetDefinition preset,
        out string error)
    {
        preset = null!;
        error = string.Empty;

        foreach (var key in presetObject.Select(pair => pair.Key))
        {
            if (!AllowedPresetKeys.Contains(key))
            {
                error = $"Preset[{index}] in '{source}' contains unknown field '{key}'.";
                return false;
            }
        }

        if (!TryReadRequiredString(presetObject, "id", out var id, out error) ||
            !TryReadRequiredString(presetObject, "name", out var name, out error) ||
            !TryReadRequiredString(presetObject, "action", out var actionText, out error))
        {
            error = $"Preset[{index}] in '{source}': {error}";
            return false;
        }

        if (!Enum.TryParse<SimsAction>(actionText, ignoreCase: true, out var action))
        {
            error = $"Preset[{index}] in '{source}' has invalid action '{actionText}'.";
            return false;
        }

        var description = TryReadOptionalString(presetObject, "description");
        var autoRun = TryReadOptionalBoolean(presetObject, "autoRun", defaultValue: true, out var boolError);
        if (!string.IsNullOrWhiteSpace(boolError))
        {
            error = $"Preset[{index}] in '{source}': {boolError}";
            return false;
        }

        var actionPatch = presetObject["actionPatch"] as JsonObject ?? new JsonObject();
        if (presetObject["actionPatch"] is not null && presetObject["actionPatch"] is not JsonObject)
        {
            error = $"Preset[{index}] in '{source}' field 'actionPatch' must be an object.";
            return false;
        }

        if (AllowedActionPatchKeys.TryGetValue(action, out var allowedActionPatchKeys))
        {
            foreach (var key in actionPatch.Select(pair => pair.Key))
            {
                if (!allowedActionPatchKeys.Contains(key))
                {
                    error = $"Preset[{index}] in '{source}' actionPatch contains unknown field '{key}' for action '{action}'.";
                    return false;
                }
            }
        }

        JsonObject? sharedPatch = null;
        if (presetObject["sharedPatch"] is not null)
        {
            if (presetObject["sharedPatch"] is not JsonObject sharedPatchObject)
            {
                error = $"Preset[{index}] in '{source}' field 'sharedPatch' must be an object.";
                return false;
            }

            foreach (var key in sharedPatchObject.Select(pair => pair.Key))
            {
                if (!AllowedSharedPatchKeys.Contains(key))
                {
                    error = $"Preset[{index}] in '{source}' sharedPatch contains unknown field '{key}'.";
                    return false;
                }
            }

            sharedPatch = sharedPatchObject;
        }

        preset = new QuickPresetDefinition
        {
            Id = id,
            Name = name,
            Description = description,
            Action = action,
            AutoRun = autoRun,
            ActionPatch = actionPatch,
            SharedPatch = sharedPatch
        };

        return true;
    }

    private static bool TryReadRequiredString(JsonObject value, string key, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;
        if (value[key] is not JsonValue raw || !raw.TryGetValue<string>(out var parsed) || string.IsNullOrWhiteSpace(parsed))
        {
            error = $"Missing required string field '{key}'.";
            return false;
        }

        text = parsed.Trim();
        return true;
    }

    private static string TryReadOptionalString(JsonObject value, string key)
    {
        if (value[key] is JsonValue raw && raw.TryGetValue<string>(out var parsed) && !string.IsNullOrWhiteSpace(parsed))
        {
            return parsed.Trim();
        }

        return string.Empty;
    }

    private static bool TryReadOptionalBoolean(JsonObject value, string key, bool defaultValue, out string error)
    {
        error = string.Empty;
        if (value[key] is null)
        {
            return defaultValue;
        }

        if (value[key] is JsonValue raw)
        {
            if (raw.TryGetValue<bool>(out var boolValue))
            {
                return boolValue;
            }

            if (raw.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed))
            {
                return parsed;
            }
        }

        error = $"Field '{key}' must be a boolean.";
        return defaultValue;
    }
}
