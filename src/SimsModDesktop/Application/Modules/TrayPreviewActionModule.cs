using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Application.Validation;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public sealed class TrayPreviewActionModule : IActionModule
{
    private static readonly IReadOnlyList<string> ActionPatchKeys =
    [
        "trayPath",
        "trayItemKey",
        "topN",
        "maxFilesPerItem"
    ];

    private readonly ITrayPreviewModuleState _panel;

    public TrayPreviewActionModule(ITrayPreviewModuleState panel)
    {
        _panel = panel;
    }

    public SimsAction Action => SimsAction.TrayPreview;
    public string ModuleKey => "traypreview";
    public string DisplayName => "Tray Preview";
    public bool UsesSharedFileOps => false;
    public IReadOnlyCollection<string> SupportedActionPatchKeys => ActionPatchKeys;

    public void LoadFromSettings(AppSettings settings)
    {
        _panel.TrayRoot = settings.TrayPreview.TrayRoot;
        _panel.TrayItemKey = settings.TrayPreview.TrayItemKey;
        _panel.TopNText = settings.TrayPreview.TopNText;
        _panel.FilesPerItemText = settings.TrayPreview.FilesPerItemText;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.TrayPreview.TrayRoot = _panel.TrayRoot;
        settings.TrayPreview.TrayItemKey = _panel.TrayItemKey;
        settings.TrayPreview.TopNText = _panel.TopNText;
        settings.TrayPreview.FilesPerItemText = _panel.FilesPerItemText;
    }

    public bool TryBuildPlan(GlobalExecutionOptions options, out ModuleExecutionPlan plan, out string error)
    {
        plan = null!;

        var trayPath = _panel.TrayRoot.Trim();
        if (string.IsNullOrWhiteSpace(trayPath))
        {
            error = "TrayPath is required for tray preview.";
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(_panel.TopNText, 1, 50000, out var topN, out error))
        {
            return false;
        }

        if (!InputParsing.TryParseOptionalInt(_panel.FilesPerItemText, 1, 200, out var filesPerItem, out error))
        {
            return false;
        }

        plan = new TrayPreviewExecutionPlan(new TrayPreviewInput
        {
            TrayPath = Path.GetFullPath(trayPath),
            TrayItemKey = _panel.TrayItemKey.Trim(),
            TopN = topN,
            MaxFilesPerItem = filesPerItem ?? 12,
            PageSize = 50
        });
        error = string.Empty;
        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetString(patch, "trayPath", out var hasTrayPath, out var trayPath, out error) ||
            !ModuleHelpers.TryGetString(patch, "trayItemKey", out var hasTrayItemKey, out var trayItemKey, out error))
        {
            return false;
        }

        if (!ModuleHelpers.TryGetInt32(patch, "topN", out var hasTopN, out var topN, out error) ||
            !ModuleHelpers.TryGetInt32(patch, "maxFilesPerItem", out var hasMaxFilesPerItem, out var maxFilesPerItem, out error))
        {
            return false;
        }

        if (hasTrayPath)
        {
            _panel.TrayRoot = trayPath ?? string.Empty;
        }

        if (hasTrayItemKey)
        {
            _panel.TrayItemKey = trayItemKey ?? string.Empty;
        }

        if (hasTopN)
        {
            _panel.TopNText = topN.ToString();
        }

        if (hasMaxFilesPerItem)
        {
            _panel.FilesPerItemText = maxFilesPerItem.ToString();
        }

        return true;
    }
}
