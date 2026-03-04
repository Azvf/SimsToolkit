namespace SimsModDesktop.Application.Validation;

internal static class ValidationHelpers
{
    public static string? ToNullIfWhiteSpace(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
