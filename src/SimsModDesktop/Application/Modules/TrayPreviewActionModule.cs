using System.Text.Json.Nodes;
using SimsModDesktop.Application.Requests;
using SimsModDesktop.Models;

namespace SimsModDesktop.Application.Modules;

public sealed class TrayPreviewActionModule : IActionModule
{
    private static readonly IReadOnlyList<string> ActionPatchKeys =
    [
        "presetTypeFilter",
        "authorFilter",
        "timeFilter",
        "searchQuery"
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
        _panel.PresetTypeFilter = settings.TrayPreview.PresetTypeFilter;
        _panel.AuthorFilter = settings.TrayPreview.AuthorFilter;
        _panel.TimeFilter = settings.TrayPreview.TimeFilter;
        _panel.SearchQuery = settings.TrayPreview.SearchQuery;
    }

    public void SaveToSettings(AppSettings settings)
    {
        settings.TrayPreview.PresetTypeFilter = _panel.PresetTypeFilter;
        settings.TrayPreview.AuthorFilter = _panel.AuthorFilter;
        settings.TrayPreview.TimeFilter = _panel.TimeFilter;
        settings.TrayPreview.SearchQuery = _panel.SearchQuery;
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

        plan = new TrayPreviewExecutionPlan(new TrayPreviewInput
        {
            TrayPath = Path.GetFullPath(trayPath),
            PageSize = 50,
            PresetTypeFilter = NormalizeFilter(_panel.PresetTypeFilter),
            AuthorFilter = _panel.AuthorFilter.Trim(),
            TimeFilter = NormalizeFilter(_panel.TimeFilter),
            SearchQuery = _panel.SearchQuery.Trim()
        });
        error = string.Empty;
        return true;
    }

    public bool TryApplyActionPatch(JsonObject patch, out string error)
    {
        error = string.Empty;

        if (!ModuleHelpers.TryGetString(patch, "presetTypeFilter", out var hasPresetTypeFilter, out var presetTypeFilter, out error) ||
            !ModuleHelpers.TryGetString(patch, "authorFilter", out var hasAuthorFilter, out var authorFilter, out error) ||
            !ModuleHelpers.TryGetString(patch, "timeFilter", out var hasTimeFilter, out var timeFilter, out error) ||
            !ModuleHelpers.TryGetString(patch, "searchQuery", out var hasSearchQuery, out var searchQuery, out error))
        {
            return false;
        }

        if (hasPresetTypeFilter)
        {
            _panel.PresetTypeFilter = NormalizeFilter(presetTypeFilter);
        }

        if (hasAuthorFilter)
        {
            _panel.AuthorFilter = authorFilter ?? string.Empty;
        }

        if (hasTimeFilter)
        {
            _panel.TimeFilter = NormalizeFilter(timeFilter);
        }

        if (hasSearchQuery)
        {
            _panel.SearchQuery = searchQuery ?? string.Empty;
        }

        return true;
    }

    private static string NormalizeFilter(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "All"
            : value.Trim();
    }
}
