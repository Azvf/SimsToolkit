using System.Text.Json.Nodes;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;
using SimsModDesktop.TrayDependencyEngine;

namespace SimsModDesktop.Application.Modules;

public sealed class TrayDependenciesActionModule : IActionModule
{
    private static readonly IReadOnlyList<string> ActionPatchKeys =
    [
        "trayItemKey",
        "minMatchCount",
        "topN",
        "maxPackageCount",
        "exportUnusedPackages",
        "exportMatchedPackages",
        "outputCsv",
        "unusedOutputCsv",
        "exportTargetPath",
        "exportMinConfidence"
    ];

    private readonly ITrayDependenciesModuleState _panel;

    public TrayDependenciesActionModule(ITrayDependenciesModuleState panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.TrayDependencies;
    public string ModuleKey => "traydeps";
    public string DisplayName => "Tray Dependencies";
    public bool UsesSharedFileOps => false;
    public IReadOnlyCollection<string> SupportedActionPatchKeys => ActionPatchKeys;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.TrayItemKey = settings.TrayDependencies.TrayItemKey;
        _panel.MinMatchCountText = settings.TrayDependencies.MinMatchCountText;
        _panel.TopNText = settings.TrayDependencies.TopNText;
        _panel.MaxPackageCountText = settings.TrayDependencies.MaxPackageCountText;
        _panel.ExportUnusedPackages = settings.TrayDependencies.ExportUnusedPackages;
        _panel.ExportMatchedPackages = settings.TrayDependencies.ExportMatchedPackages;
        _panel.OutputCsv = settings.TrayDependencies.OutputCsv;
        _panel.UnusedOutputCsv = settings.TrayDependencies.UnusedOutputCsv;
        _panel.ExportTargetPath = settings.TrayDependencies.ExportTargetPath;
        _panel.ExportMinConfidence = settings.TrayDependencies.ExportMinConfidence;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.TrayDependencies.TrayItemKey = _panel.TrayItemKey;
        settings.TrayDependencies.MinMatchCountText = _panel.MinMatchCountText;
        settings.TrayDependencies.TopNText = _panel.TopNText;
        settings.TrayDependencies.MaxPackageCountText = _panel.MaxPackageCountText;
        settings.TrayDependencies.ExportUnusedPackages = _panel.ExportUnusedPackages;
        settings.TrayDependencies.ExportMatchedPackages = _panel.ExportMatchedPackages;
        settings.TrayDependencies.OutputCsv = _panel.OutputCsv;
        settings.TrayDependencies.UnusedOutputCsv = _panel.UnusedOutputCsv;
        settings.TrayDependencies.ExportTargetPath = _panel.ExportTargetPath;
        settings.TrayDependencies.ExportMinConfidence = _panel.ExportMinConfidence;
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        _ = options;
        plan = null!;
        var trayPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.TrayPath);
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            error = "TrayPath is required for tray dependency analysis.";
            return false;
        }

        if (!Directory.Exists(trayPath))
        {
            error = "TrayPath does not exist for tray dependency analysis.";
            return false;
        }

        var modsPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.ModsPath);
        if (string.IsNullOrWhiteSpace(modsPath))
        {
            error = "ModsPath is required for tray dependency analysis.";
            return false;
        }

        if (!Directory.Exists(modsPath))
        {
            error = "ModsPath does not exist for tray dependency analysis.";
            return false;
        }

        var trayItemKey = ModuleHelpers.ToNullIfWhiteSpace(_panel.TrayItemKey);
        if (string.IsNullOrWhiteSpace(trayItemKey))
        {
            error = "TrayItemKey is required for tray dependency analysis.";
            return false;
        }

        var exportTargetPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.ExportTargetPath);
        if ((_panel.ExportMatchedPackages || _panel.ExportUnusedPackages) &&
            string.IsNullOrWhiteSpace(exportTargetPath))
        {
            error = "ExportTargetPath is required when exporting dependency packages.";
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(_panel.MinMatchCountText, 1, 1000, out var minMatchCount, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(_panel.TopNText, 1, 10000, out var topN, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(_panel.MaxPackageCountText, 0, 1000000, out var maxPackageCount, out error))
        {
            return false;
        }

        plan = new TrayDependenciesExecutionPlan(new TrayDependencyAnalysisRequest
        {
            TrayPath = trayPath,
            ModsRootPath = modsPath,
            TrayItemKey = trayItemKey,
            MinMatchCount = minMatchCount,
            TopN = topN,
            MaxPackageCount = maxPackageCount,
            ExportUnusedPackages = _panel.ExportUnusedPackages,
            ExportMatchedPackages = _panel.ExportMatchedPackages,
            OutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_panel.OutputCsv),
            UnusedOutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_panel.UnusedOutputCsv),
            ExportTargetPath = exportTargetPath,
            ExportMinConfidence = ModuleHelpers.ToNullIfWhiteSpace(_panel.ExportMinConfidence) ?? "Low"
        });
        error = string.Empty;
        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetString(patch, "trayItemKey", out var hasTrayItemKey, out var trayItemKey, out error) ||
            !ModuleHelpers.TryGetString(patch, "outputCsv", out var hasOutputCsv, out var outputCsv, out error) ||
            !ModuleHelpers.TryGetString(patch, "unusedOutputCsv", out var hasUnusedOutputCsv, out var unusedOutputCsv, out error) ||
            !ModuleHelpers.TryGetString(patch, "exportTargetPath", out var hasExportTargetPath, out var exportTargetPath, out error) ||
            !ModuleHelpers.TryGetString(patch, "exportMinConfidence", out var hasExportMinConfidence, out var exportMinConfidence, out error) ||
            !ModuleHelpers.TryGetBoolean(patch, "exportUnusedPackages", out var hasExportUnusedPackages, out var exportUnusedPackages, out error) ||
            !ModuleHelpers.TryGetBoolean(patch, "exportMatchedPackages", out var hasExportMatchedPackages, out var exportMatchedPackages, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetInt32(patch, "minMatchCount", out var hasMinMatchCount, out var minMatchCount, out error) ||
            !ModuleHelpers.TryGetInt32(patch, "topN", out var hasTopN, out var topN, out error) ||
            !ModuleHelpers.TryGetInt32(patch, "maxPackageCount", out var hasMaxPackageCount, out var maxPackageCount, out error))
        {
            return false;
        }

        if (hasTrayItemKey)
        {
            _panel.TrayItemKey = trayItemKey ?? string.Empty;
        }

        if (hasMinMatchCount)
        {
            _panel.MinMatchCountText = minMatchCount.ToString();
        }

        if (hasTopN)
        {
            _panel.TopNText = topN.ToString();
        }

        if (hasMaxPackageCount)
        {
            _panel.MaxPackageCountText = maxPackageCount.ToString();
        }

        if (hasExportUnusedPackages)
        {
            _panel.ExportUnusedPackages = exportUnusedPackages;
        }

        if (hasExportMatchedPackages)
        {
            _panel.ExportMatchedPackages = exportMatchedPackages;
        }

        if (hasOutputCsv)
        {
            _panel.OutputCsv = outputCsv ?? string.Empty;
        }

        if (hasUnusedOutputCsv)
        {
            _panel.UnusedOutputCsv = unusedOutputCsv ?? string.Empty;
        }

        if (hasExportTargetPath)
        {
            _panel.ExportTargetPath = exportTargetPath ?? string.Empty;
        }

        if (hasExportMinConfidence)
        {
            _panel.ExportMinConfidence = exportMinConfidence ?? string.Empty;
        }

        return true;
    }
}
