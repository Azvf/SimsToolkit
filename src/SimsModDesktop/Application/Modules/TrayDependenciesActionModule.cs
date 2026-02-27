using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;
using SimsModDesktop.ViewModels.Panels;

namespace SimsModDesktop.Application.Modules;

public sealed class TrayDependenciesActionModule : IActionModule
{
    private readonly TrayDependenciesPanelViewModel _panel;

    public TrayDependenciesActionModule(TrayDependenciesPanelViewModel panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.TrayDependencies;
    public string ModuleKey => "trayprobe";
    public string DisplayName => "Tray Dependencies";
    public bool UsesSharedFileOps => false;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.TrayPath = settings.TrayDependencies.TrayPath;
        _panel.ModsPath = settings.TrayDependencies.ModsPath;
        _panel.TrayItemKey = settings.TrayDependencies.TrayItemKey;
        _panel.AnalysisMode = settings.TrayDependencies.AnalysisMode;
        _panel.S4tiPath = settings.TrayDependencies.S4tiPath;
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
        settings.TrayDependencies.TrayPath = _panel.TrayPath;
        settings.TrayDependencies.ModsPath = _panel.ModsPath;
        settings.TrayDependencies.TrayItemKey = _panel.TrayItemKey;
        settings.TrayDependencies.AnalysisMode = _panel.AnalysisMode;
        settings.TrayDependencies.S4tiPath = _panel.S4tiPath;
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
        plan = null!;
        if (!ModuleHelpers.TryResolveScriptPath(options.ScriptPath, out var scriptPath, out error))
        {
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

        plan = new CliExecutionPlan(new TrayDependenciesInput
        {
            ScriptPath = scriptPath,
            WhatIf = options.WhatIf,
            TrayPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.TrayPath),
            ModsPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.ModsPath),
            TrayItemKey = ModuleHelpers.ToNullIfWhiteSpace(_panel.TrayItemKey),
            AnalysisMode = ModuleHelpers.ToNullIfWhiteSpace(_panel.AnalysisMode) ?? "StrictS4TI",
            S4tiPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.S4tiPath),
            MinMatchCount = minMatchCount,
            TopN = topN,
            MaxPackageCount = maxPackageCount,
            ExportUnusedPackages = _panel.ExportUnusedPackages,
            ExportMatchedPackages = _panel.ExportMatchedPackages,
            OutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_panel.OutputCsv),
            UnusedOutputCsv = ModuleHelpers.ToNullIfWhiteSpace(_panel.UnusedOutputCsv),
            ExportTargetPath = ModuleHelpers.ToNullIfWhiteSpace(_panel.ExportTargetPath),
            ExportMinConfidence = ModuleHelpers.ToNullIfWhiteSpace(_panel.ExportMinConfidence) ?? "Low"
        });
        error = string.Empty;
        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetString(patch, "trayPath", out var hasTrayPath, out var trayPath, out error) ||
            !ModuleHelpers.TryGetString(patch, "modsPath", out var hasModsPath, out var modsPath, out error) ||
            !ModuleHelpers.TryGetString(patch, "trayItemKey", out var hasTrayItemKey, out var trayItemKey, out error) ||
            !ModuleHelpers.TryGetString(patch, "analysisMode", out var hasAnalysisMode, out var analysisMode, out error) ||
            !ModuleHelpers.TryGetString(patch, "s4tiPath", out var hasS4tiPath, out var s4tiPath, out error) ||
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

        if (hasTrayPath)
        {
            _panel.TrayPath = trayPath ?? string.Empty;
        }

        if (hasModsPath)
        {
            _panel.ModsPath = modsPath ?? string.Empty;
        }

        if (hasTrayItemKey)
        {
            _panel.TrayItemKey = trayItemKey ?? string.Empty;
        }

        if (hasAnalysisMode)
        {
            _panel.AnalysisMode = analysisMode ?? string.Empty;
        }

        if (hasS4tiPath)
        {
            _panel.S4tiPath = s4tiPath ?? string.Empty;
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
