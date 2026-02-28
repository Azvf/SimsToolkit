namespace SimsModDesktop.Application.Modules;

// State contracts keep action modules decoupled from concrete UI panel implementations.
public interface IOrganizeModuleState
{
    string SourceDir { get; set; }
    string ZipNamePattern { get; set; }
    string ModsRoot { get; set; }
    string UnifiedModsFolder { get; set; }
    string TrayRoot { get; set; }
    bool KeepZip { get; set; }
}

public interface IFlattenModuleState
{
    string RootPath { get; set; }
    bool FlattenToRoot { get; set; }
}

public interface INormalizeModuleState
{
    string RootPath { get; set; }
}

public interface IMergeModuleState
{
    string TargetPath { get; set; }
    IReadOnlyList<string> CollectSourcePaths();
    string SerializeSourcePaths();
    void ApplySourcePathsText(string? rawValue);
    void ReplaceSourcePaths(IReadOnlyList<string> sourcePaths);
}

public interface IFindDupModuleState
{
    string RootPath { get; set; }
    string OutputCsv { get; set; }
    bool Recurse { get; set; }
    bool Cleanup { get; set; }
}

public interface ITrayDependenciesModuleState
{
    string TrayPath { get; set; }
    string ModsPath { get; set; }
    string TrayItemKey { get; set; }
    string S4tiPath { get; set; }
    string MinMatchCountText { get; set; }
    string TopNText { get; set; }
    string MaxPackageCountText { get; set; }
    bool ExportUnusedPackages { get; set; }
    bool ExportMatchedPackages { get; set; }
    string OutputCsv { get; set; }
    string UnusedOutputCsv { get; set; }
    string ExportTargetPath { get; set; }
    string ExportMinConfidence { get; set; }
}

public interface ITrayPreviewModuleState
{
    string TrayRoot { get; set; }
    string PresetTypeFilter { get; set; }
    string AuthorFilter { get; set; }
    string TimeFilter { get; set; }
    string SearchQuery { get; set; }
}
