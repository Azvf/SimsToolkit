namespace SimsModDesktop.Application.Validation;

internal static class ValidationHelpers
{
    public static bool ValidateScriptPath(string scriptPath, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(scriptPath))
        {
            error = "Script path is required.";
            return false;
        }

        if (!File.Exists(scriptPath))
        {
            error = "Script path does not exist.";
            return false;
        }

        return true;
    }

    public static string? ToNullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
