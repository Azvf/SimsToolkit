namespace SimsModDesktop.Models;

public sealed class AppSettings
{
    public string UiLanguageCode { get; set; } = "en-US";
    public string ScriptPath { get; set; } = string.Empty;
    public AppWorkspace SelectedWorkspace { get; set; } = AppWorkspace.Toolkit;
    public SimsAction SelectedAction { get; set; } = SimsAction.Organize;
    public bool WhatIf { get; set; }
    public OrganizeSettings Organize { get; set; } = new();
    public FlattenSettings Flatten { get; set; } = new();
    public NormalizeSettings Normalize { get; set; } = new();
    public MergeSettings Merge { get; set; } = new();
    public FindDupSettings FindDup { get; set; } = new();
    public TrayDependenciesSettings TrayDependencies { get; set; } = new();
    public ModPreviewSettings ModPreview { get; set; } = new();
    public TrayPreviewSettings TrayPreview { get; set; } = new();
    public SharedFileOpsSettings SharedFileOps { get; set; } = new();
    public UiStateSettings UiState { get; set; } = new();
    public NavigationSettings Navigation { get; set; } = new();
    public FeatureFlagsSettings FeatureFlags { get; set; } = new();
    public GameLaunchSettings GameLaunch { get; set; } = new();
    public SavesSettings Saves { get; set; } = new();
    public ThemeSettings Theme { get; set; } = new();

    public sealed class OrganizeSettings
    {
        public string SourceDir { get; set; } = string.Empty;
        public string ZipNamePattern { get; set; } = "*";
        public string ModsRoot { get; set; } = string.Empty;
        public string UnifiedModsFolder { get; set; } = string.Empty;
        public string TrayRoot { get; set; } = string.Empty;
        public bool KeepZip { get; set; }
    }

    public sealed class FlattenSettings
    {
        public string RootPath { get; set; } = string.Empty;
        public bool FlattenToRoot { get; set; }
    }

    public sealed class NormalizeSettings
    {
        public string RootPath { get; set; } = string.Empty;
    }

    public sealed class MergeSettings
    {
        public string SourcePathsText { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
    }

    public sealed class FindDupSettings
    {
        public string RootPath { get; set; } = string.Empty;
        public string OutputCsv { get; set; } = string.Empty;
        public bool Recurse { get; set; } = true;
        public bool Cleanup { get; set; }
    }

    public sealed class TrayDependenciesSettings
    {
        public string TrayItemKey { get; set; } = string.Empty;
        public string MinMatchCountText { get; set; } = "1";
        public string TopNText { get; set; } = "200";
        public string MaxPackageCountText { get; set; } = "0";
        public bool ExportUnusedPackages { get; set; }
        public bool ExportMatchedPackages { get; set; }
        public string OutputCsv { get; set; } = string.Empty;
        public string UnusedOutputCsv { get; set; } = string.Empty;
        public string ExportTargetPath { get; set; } = string.Empty;
        public string ExportMinConfidence { get; set; } = "Low";
    }

    public sealed class ModPreviewSettings
    {
        public string ModsRoot { get; set; } = string.Empty;
        public string PackageTypeFilter { get; set; } = "All";
        public string ScopeFilter { get; set; } = "All";
        public string SortBy { get; set; } = "Last Updated";
        public string SearchQuery { get; set; } = string.Empty;
        public bool ShowOverridesOnly { get; set; }
    }

    public sealed class TrayPreviewSettings
    {
        public string PresetTypeFilter { get; set; } = "All";
        public string BuildSizeFilter { get; set; } = "All";
        public string HouseholdSizeFilter { get; set; } = "All";
        public string AuthorFilter { get; set; } = string.Empty;
        public string TimeFilter { get; set; } = "All";
        public string SearchQuery { get; set; } = string.Empty;
        public string LayoutMode { get; set; } = "Entry";
        public bool EnableDebugPreview { get; set; }
    }

    public sealed class SharedFileOpsSettings
    {
        public bool SkipPruneEmptyDirs { get; set; }
        public bool ModFilesOnly { get; set; }
        public bool VerifyContentOnNameConflict { get; set; }
        public string ModExtensionsText { get; set; } = ".package,.ts4script";
        public string PrefixHashBytesText { get; set; } = "102400";
        public string HashWorkerCountText { get; set; } = "8";
    }

    public sealed class UiStateSettings
    {
        public bool ToolkitLogDrawerOpen { get; set; }
        public bool TrayPreviewLogDrawerOpen { get; set; }
        public bool ToolkitAdvancedOpen { get; set; }
    }

    public sealed class NavigationSettings
    {
        public AppSection SelectedSection { get; set; } = AppSection.Toolkit;
    }

    public sealed class FeatureFlagsSettings
    {
        public bool EnableLaunchGame { get; set; } = true;
    }

    public sealed class GameLaunchSettings
    {
        public string Ts4RootPath { get; set; } = string.Empty;
        public string GameExecutablePath { get; set; } = string.Empty;
        public string ModsPath { get; set; } = string.Empty;
        public string TrayPath { get; set; } = string.Empty;
        public string SavesPath { get; set; } = string.Empty;
    }

    public sealed class SavesSettings
    {
        public string LastExportRoot { get; set; } = string.Empty;
        public bool GenerateThumbnails { get; set; } = true;
    }

    public sealed class ThemeSettings
    {
        public string RequestedTheme { get; set; } = "Dark";
    }
}
