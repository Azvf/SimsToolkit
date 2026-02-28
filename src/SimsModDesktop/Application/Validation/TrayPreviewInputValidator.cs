using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class TrayPreviewInputValidator : IActionInputValidator<TrayPreviewInput>
{
    public bool TryValidate(TrayPreviewInput input, out string error)
    {
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(input.TrayPath))
        {
            error = "TrayPath is required for tray preview.";
            return false;
        }

        if (!Directory.Exists(input.TrayPath))
        {
            error = "TrayPath does not exist.";
            return false;
        }

        if (input.PageSize < 1 || input.PageSize > 500)
        {
            error = $"Value {input.PageSize} is out of range [1, 500].";
            return false;
        }

        var timeFilter = input.TimeFilter?.Trim();
        if (!string.IsNullOrWhiteSpace(timeFilter) &&
            !string.Equals(timeFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(timeFilter, "Last24h", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(timeFilter, "Last7d", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(timeFilter, "Last30d", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(timeFilter, "Last90d", StringComparison.OrdinalIgnoreCase))
        {
            error = $"Unsupported time filter: {timeFilter}.";
            return false;
        }

        return true;
    }
}
