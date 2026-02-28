using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class TrayPreviewInputValidator : IActionInputValidator<TrayPreviewInput>
{
    private static readonly HashSet<string> SupportedBuildSizeFilters = new(StringComparer.OrdinalIgnoreCase)
    {
        "15 x 20",
        "20 x 20",
        "30 x 20",
        "30 x 30",
        "40 x 30",
        "40 x 40",
        "50 x 40",
        "50 x 50",
        "64 x 64"
    };

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

        var buildSizeFilter = input.BuildSizeFilter?.Trim();
        if (!string.IsNullOrWhiteSpace(buildSizeFilter) &&
            !string.Equals(buildSizeFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            !SupportedBuildSizeFilters.Contains(buildSizeFilter))
        {
            error = $"Unsupported build size filter: {buildSizeFilter}.";
            return false;
        }

        var householdSizeFilter = input.HouseholdSizeFilter?.Trim();
        if (!string.IsNullOrWhiteSpace(householdSizeFilter) &&
            !string.Equals(householdSizeFilter, "All", StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(householdSizeFilter, out var size) || size < 1 || size > 8)
            {
                error = $"Unsupported household size filter: {householdSizeFilter}.";
                return false;
            }
        }

        return true;
    }
}
