namespace SimsModDesktop.Models;

public sealed class AppSettings
{
    public string ScriptPath { get; set; } = string.Empty;
    public SimsAction SelectedAction { get; set; } = SimsAction.Organize;
    public bool WhatIf { get; set; }
    public OrganizeSettings Organize { get; set; } = new();
    public FlattenSettings Flatten { get; set; } = new();
    public NormalizeSettings Normalize { get; set; } = new();
    public MergeSettings Merge { get; set; } = new();
    public FindDupSettings FindDup { get; set; } = new();
    public TrayDependenciesSettings TrayDependencies { get; set; } = new();
    public TrayPreviewSettings TrayPreview { get; set; } = new();
    public SharedFileOpsSettings SharedFileOps { get; set; } = new();

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
        public string TrayPath { get; set; } = string.Empty;
        public string ModsPath { get; set; } = string.Empty;
        public string TrayItemKey { get; set; } = string.Empty;
        public string AnalysisMode { get; set; } = "StrictS4TI";
        public string S4tiPath { get; set; } = string.Empty;
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

    public sealed class TrayPreviewSettings
    {
        public string TrayRoot { get; set; } = string.Empty;
        public string TrayItemKey { get; set; } = string.Empty;
        public string TopNText { get; set; } = string.Empty;
        public string FilesPerItemText { get; set; } = "12";
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
}
