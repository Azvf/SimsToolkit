using SimsModDesktop.Application.Requests;

namespace SimsModDesktop.Application.Validation;

public sealed class TrayDependenciesInputValidator : IActionInputValidator<TrayDependenciesInput>
{
    public bool TryValidate(TrayDependenciesInput input, out string error)
    {
        if (!ValidationHelpers.ValidateScriptPath(input.ScriptPath, out error))
        {
            return false;
        }

        var trayPath = ValidationHelpers.ToNullIfWhiteSpace(input.TrayPath);
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            error = "TrayPath is required for tray dependency probe.";
            return false;
        }

        if (!Directory.Exists(trayPath))
        {
            error = "TrayPath does not exist for tray dependency probe.";
            return false;
        }

        var modsPath = ValidationHelpers.ToNullIfWhiteSpace(input.ModsPath);
        if (string.IsNullOrWhiteSpace(modsPath))
        {
            error = "ModsPath is required for tray dependency probe.";
            return false;
        }

        if (!Directory.Exists(modsPath))
        {
            error = "ModsPath does not exist for tray dependency probe.";
            return false;
        }

        var s4tiPath = ValidationHelpers.ToNullIfWhiteSpace(input.S4tiPath);
        if (string.IsNullOrWhiteSpace(s4tiPath))
        {
            error = "S4TI path is required in StrictS4TI mode.";
            return false;
        }

        if (!Directory.Exists(s4tiPath))
        {
            error = "S4TI path does not exist.";
            return false;
        }

        if (input.MinMatchCount is int minMatchCount && (minMatchCount < 1 || minMatchCount > 1000))
        {
            error = $"Value {minMatchCount} is out of range [1, 1000].";
            return false;
        }

        if (input.TopN is int topN && (topN < 1 || topN > 10000))
        {
            error = $"Value {topN} is out of range [1, 10000].";
            return false;
        }

        if (input.MaxPackageCount is int maxPackageCount && (maxPackageCount < 0 || maxPackageCount > 1000000))
        {
            error = $"Value {maxPackageCount} is out of range [0, 1000000].";
            return false;
        }

        return true;
    }
}
