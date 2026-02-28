using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public sealed class OrganizeActionModule : IActionModule
{
    private static readonly IReadOnlyList<string> ActionPatchKeys =
    [
        "sourceDir",
        "zipNamePattern",
        "modsRoot",
        "unifiedModsFolder",
        "trayRoot",
        "keepZip"
    ];

    private readonly IOrganizeModuleState _panel;

    public OrganizeActionModule(IOrganizeModuleState panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.Organize;
    public string ModuleKey => "organize";
    public string DisplayName => "Organize";
    public bool UsesSharedFileOps => false;
    public IReadOnlyCollection<string> SupportedActionPatchKeys => ActionPatchKeys;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.SourceDir = settings.Organize.SourceDir;
        _panel.ZipNamePattern = settings.Organize.ZipNamePattern;
        _panel.ModsRoot = settings.Organize.ModsRoot;
        _panel.UnifiedModsFolder = settings.Organize.UnifiedModsFolder;
        _panel.TrayRoot = settings.Organize.TrayRoot;
        _panel.KeepZip = settings.Organize.KeepZip;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.Organize.SourceDir = _panel.SourceDir;
        settings.Organize.ZipNamePattern = _panel.ZipNamePattern;
        settings.Organize.ModsRoot = _panel.ModsRoot;
        settings.Organize.UnifiedModsFolder = _panel.UnifiedModsFolder;
        settings.Organize.TrayRoot = _panel.TrayRoot;
        settings.Organize.KeepZip = _panel.KeepZip;
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        plan = null!;
        if (!ModuleHelpers.TryResolveScriptPath(options.ScriptPath, out var scriptPath, out error))
        {
            return false;
        }

        plan = new CliExecutionPlan(new OrganizeInput
        {
            ScriptPath = scriptPath,
            WhatIf = options.WhatIf,
            SourceDir = ModuleHelpers.ToNullIfWhiteSpace(_panel.SourceDir),
            ZipNamePattern = ModuleHelpers.ToNullIfWhiteSpace(_panel.ZipNamePattern),
            ModsRoot = ModuleHelpers.ToNullIfWhiteSpace(_panel.ModsRoot),
            UnifiedModsFolder = ModuleHelpers.ToNullIfWhiteSpace(_panel.UnifiedModsFolder),
            TrayRoot = ModuleHelpers.ToNullIfWhiteSpace(_panel.TrayRoot),
            KeepZip = _panel.KeepZip
        });
        error = string.Empty;
        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetString(patch, "sourceDir", out var hasSourceDir, out var sourceDir, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetString(patch, "zipNamePattern", out var hasZipNamePattern, out var zipNamePattern, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetString(patch, "modsRoot", out var hasModsRoot, out var modsRoot, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetString(patch, "unifiedModsFolder", out var hasUnifiedModsFolder, out var unifiedModsFolder, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetString(patch, "trayRoot", out var hasTrayRoot, out var trayRoot, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetBoolean(patch, "keepZip", out var hasKeepZip, out var keepZip, out error))
        {
            return false;
        }

        if (hasSourceDir)
        {
            _panel.SourceDir = sourceDir ?? string.Empty;
        }

        if (hasZipNamePattern)
        {
            _panel.ZipNamePattern = zipNamePattern ?? string.Empty;
        }

        if (hasModsRoot)
        {
            _panel.ModsRoot = modsRoot ?? string.Empty;
        }

        if (hasUnifiedModsFolder)
        {
            _panel.UnifiedModsFolder = unifiedModsFolder ?? string.Empty;
        }

        if (hasTrayRoot)
        {
            _panel.TrayRoot = trayRoot ?? string.Empty;
        }

        if (hasKeepZip)
        {
            _panel.KeepZip = keepZip;
        }

        return true;
    }
}
