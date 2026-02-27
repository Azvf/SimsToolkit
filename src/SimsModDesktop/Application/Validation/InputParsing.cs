namespace SimsModDesktop.Application.Validation;

public static class InputParsing
{
    public static bool TryParseOptionalInt(string rawValue, int min, int max, out int? value, out string error)
    {
        value = null;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return true;
        }

        if (!int.TryParse(rawValue.Trim(), out var parsedValue))
        {
            error = $"Invalid number: {rawValue}.";
            return false;
        }

        if (parsedValue < min || parsedValue > max)
        {
            error = $"Value {parsedValue} is out of range [{min}, {max}].";
            return false;
        }

        value = parsedValue;
        return true;
    }

    public static List<string> ParseDelimitedList(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return new List<string>();
        }

        return rawValue
            .Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
