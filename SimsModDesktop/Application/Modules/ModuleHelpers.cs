using System.Text.Json.Nodes;

namespace SimsModDesktop.Application.Modules;

internal static class ModuleHelpers
{
    public static bool TryResolveScriptPath(string rawScriptPath, out string scriptPath, out string error)
    {
        scriptPath = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawScriptPath))
        {
            error = "Script path is required.";
            return false;
        }

        scriptPath = Path.GetFullPath(rawScriptPath.Trim());
        return true;
    }

    public static string? ToNullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static bool TryGetString(JsonObject patch, string key, out bool hasValue, out string? value, out string error)
    {
        hasValue = false;
        value = null;
        error = string.Empty;

        if (!patch.TryGetPropertyValue(key, out var node))
        {
            return true;
        }

        hasValue = true;
        if (node is null)
        {
            value = string.Empty;
            return true;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
        {
            value = stringValue;
            return true;
        }

        error = $"Preset field '{key}' must be a string.";
        return false;
    }

    public static bool TryGetBoolean(JsonObject patch, string key, out bool hasValue, out bool value, out string error)
    {
        hasValue = false;
        value = default;
        error = string.Empty;

        if (!patch.TryGetPropertyValue(key, out var node))
        {
            return true;
        }

        hasValue = true;
        if (node is null)
        {
            error = $"Preset field '{key}' cannot be null.";
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<bool>(out var boolValue))
            {
                value = boolValue;
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var textValue) &&
                bool.TryParse(textValue, out var parsedBool))
            {
                value = parsedBool;
                return true;
            }
        }

        error = $"Preset field '{key}' must be a boolean.";
        return false;
    }

    public static bool TryGetInt32(JsonObject patch, string key, out bool hasValue, out int value, out string error)
    {
        hasValue = false;
        value = default;
        error = string.Empty;

        if (!patch.TryGetPropertyValue(key, out var node))
        {
            return true;
        }

        hasValue = true;
        if (node is null)
        {
            error = $"Preset field '{key}' cannot be null.";
            return false;
        }

        if (node is JsonValue jsonValue)
        {
            if (jsonValue.TryGetValue<int>(out var intValue))
            {
                value = intValue;
                return true;
            }

            if (jsonValue.TryGetValue<string>(out var textValue) &&
                int.TryParse(textValue, out var parsedInt))
            {
                value = parsedInt;
                return true;
            }
        }

        error = $"Preset field '{key}' must be an integer.";
        return false;
    }

    public static bool TryGetStringList(
        JsonObject patch,
        string key,
        out bool hasValue,
        out IReadOnlyList<string> values,
        out string error)
    {
        hasValue = false;
        values = Array.Empty<string>();
        error = string.Empty;

        if (!patch.TryGetPropertyValue(key, out var node))
        {
            return true;
        }

        hasValue = true;
        if (node is null)
        {
            values = Array.Empty<string>();
            return true;
        }

        if (node is JsonArray array)
        {
            var buffer = new List<string>();
            foreach (var item in array)
            {
                if (item is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
                {
                    if (!string.IsNullOrWhiteSpace(stringValue))
                    {
                        buffer.Add(stringValue.Trim());
                    }

                    continue;
                }

                error = $"Preset field '{key}' contains a non-string item.";
                return false;
            }

            values = buffer
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return true;
        }

        error = $"Preset field '{key}' must be a string array.";
        return false;
    }
}
