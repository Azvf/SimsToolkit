# Baseline Smoke Checklist

This checklist freezes expected behavior before and after the MVVM refactor.

## Global Preconditions
- `sims-mod-cli.ps1` is available and selectable.
- App launches and shows `Ready.` in status bar.
- Run button triggers execution and Cancel interrupts long-running actions.

## Action: organize
- Required input: `ScriptPath`.
- Optional inputs: `SourceDir`, `ZipNamePattern`, `ModsRoot`, `UnifiedModsFolder`, `TrayRoot`, `KeepZip`.
- Expected log keywords: `[start]`, `[action] organize`, `[exit] code=`.
- Expected status: `Completed in mm:ss.` or `Failed with exit code ...`.

## Action: flatten
- Required input: `ScriptPath`.
- Optional inputs: `FlattenRootPath`, `FlattenToRoot`, shared file options.
- Expected log keywords: `[action] flatten`, `[exit] code=`.

## Action: normalize
- Required input: `ScriptPath`.
- Optional inputs: `NormalizeRootPath`.
- Expected log keywords: `[action] normalize`, `[exit] code=`.

## Action: merge
- Required input: `ScriptPath`, at least one `MergeSourcePaths`.
- Optional inputs: `MergeTargetPath`, shared file options.
- Expected validation failure when source paths are empty.

## Action: finddup
- Required input: `ScriptPath`, existing `FindDupRootPath`.
- Optional inputs: `OutputCsv`, `Recurse`, `Cleanup`, shared file options.
- Expected validation failure when root path is empty or missing.

## Action: traypreview
- Required input: existing `TrayPath`.
- Optional inputs: `TrayItemKey`, `TopN`, `FilesPerItem`.
- Expected log keywords: `[action] traypreview`, `[preview] trayPath=`, `[preview] totalItems=`.
- Expected UI update: dashboard cards and page list populated.

## Action: tray dependencies
- Required input: existing `TrayPath`, existing `ModsPath`, `TrayItemKey`.
- Optional inputs: `Minimum Match Count`, `TopN`, `Max Package Count`, `Export Confidence`.
- Expected UI behavior: runs fully in-app without external tools.
