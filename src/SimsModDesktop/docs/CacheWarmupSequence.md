# Cache Warmup Sequence

This document captures the current page-level cache warmup flow after the cache refactor.

Current baseline:

* There is no startup-time Tray dependency warmup.
* `Mods` and `Tray` use page-triggered blocking warmup.
* A single `MainWindowCacheWarmupController` serializes blocking warmup work.
* Tray export and Tray dependency analysis both consume a preloaded ready snapshot.
* `TrayDependencyEngine` no longer performs hidden `Mods` directory validation for export or analysis.

Relevant implementation anchors:

* `src/SimsModDesktop.Presentation/ViewModels/MainWindowCacheWarmupController.cs`
* `src/SimsModDesktop.Presentation/ViewModels/Preview/ModPreviewWorkspaceViewModel.cs`
* `src/SimsModDesktop.Presentation/ViewModels/Preview/TrayPreviewWorkspaceViewModel.cs`
* `src/SimsModDesktop.Infrastructure/Mods/SqliteModPackageInventoryService.cs`
* `src/SimsModDesktop.TrayDependencyEngine/TrayDependencyExportService.cs`
* `src/SimsModDesktop.TrayDependencyEngine/TrayDependencyAnalysisService.cs`

---

## 1. System Overview

```mermaid
sequenceDiagram
    participant User
    participant Shell as MainShellViewModel
    participant ModsVM as ModPreviewWorkspaceViewModel
    participant TrayVM as TrayPreviewWorkspaceViewModel
    participant Warmup as MainWindowCacheWarmupController

    User->>Shell: Switch to Mods or Tray section
    Shell->>ModsVM: SetIsActive(true) for Mods
    Shell->>TrayVM: SetIsActive(true) for Tray

    alt Mods section
        ModsVM->>Warmup: EnsureModsWorkspaceReadyAsync(modsRoot)
        Warmup-->>ModsVM: validated and ready
    else Tray section
        TrayVM->>Warmup: EnsureTrayWorkspaceReadyAsync(modsRoot)
        Warmup-->>TrayVM: validated and ready snapshot
    end

    Note over Warmup: A single SemaphoreSlim gate\nserializes blocking warmup work
```

---

## 2. Mods Page First Load

```mermaid
sequenceDiagram
    participant User
    participant ModsVM as ModPreviewWorkspaceViewModel
    participant Warmup as MainWindowCacheWarmupController
    participant Inventory as IModPackageInventoryService
    participant AppCache as app-cache.db
    participant Indexer as IModItemIndexScheduler
    participant Catalog as IModItemCatalogService

    User->>ModsVM: Open Mods page
    ModsVM->>ModsVM: Enter blocking loading state
    ModsVM->>Warmup: EnsureModsWorkspaceReadyAsync(modsRoot)

    Warmup->>Warmup: Wait for serial gate
    Warmup->>Inventory: RefreshAsync(modsRoot, progress)

    Inventory->>Inventory: Scan Mods/**/*.package
    Inventory->>AppCache: Load previous inventory rows
    Inventory->>Inventory: Compute Added / Changed / Removed / Unchanged
    Inventory->>AppCache: Persist new inventory + InventoryVersion
    Inventory-->>Warmup: ModPackageInventoryRefreshResult

    alt Same root and same inventory already ready in session
        Warmup-->>ModsVM: ready
    else Fast refresh required
        Warmup->>Indexer: QueueRefreshAsync(changed, removed, AllowDeepEnrichment=false)
        Indexer-->>Warmup: Fast pass complete
        Warmup-->>ModsVM: ready
    end

    ModsVM->>Catalog: QueryPageAsync(current query)
    Catalog-->>ModsVM: Real list rows
    ModsVM->>ModsVM: Exit blocking loading state
    ModsVM->>Warmup: QueueModsPriorityDeepEnrichment(current page packages)

    Note over ModsVM,Warmup: Current-page detail enrichment continues in background\nbut the list is already based on validated data
```

---

## 3. Tray Page First Load

```mermaid
sequenceDiagram
    participant User
    participant TrayVM as TrayPreviewWorkspaceViewModel
    participant Warmup as MainWindowCacheWarmupController
    participant Inventory as IModPackageInventoryService
    participant AppCache as app-cache.db
    participant PkgCache as IPackageIndexCache
    participant TrayCache as TrayDependencyPackageIndex/cache.db
    participant TrayPreview as ITrayPreviewCoordinator

    User->>TrayVM: Open Tray page
    TrayVM->>TrayVM: Enter blocking loading state
    TrayVM->>Warmup: EnsureTrayWorkspaceReadyAsync(modsRoot)

    Warmup->>Warmup: Wait for serial gate
    Warmup->>Inventory: RefreshAsync(modsRoot, progress)
    Inventory->>AppCache: Validate and persist inventory
    Inventory-->>Warmup: InventoryResult + InventoryVersion

    alt Same root and same snapshot already ready in session
        Warmup-->>TrayVM: ready snapshot
    else Try persisted snapshot
        Warmup->>PkgCache: TryLoadSnapshotAsync(modsRoot, inventoryVersion)
        alt Persisted hit
            PkgCache->>TrayCache: Read PackageIndexSnapshots
            PkgCache-->>Warmup: snapshot
        else Persisted miss
            Warmup->>PkgCache: BuildSnapshotAsync(package files, changed files, removed paths)
            PkgCache->>TrayCache: Persist (ModsRootPath, InventoryVersion, SnapshotJson)
            PkgCache-->>Warmup: snapshot
        end
        Warmup-->>TrayVM: ready snapshot
    end

    TrayVM->>TrayVM: Mark IsTrayDependencyCacheReady = true
    TrayVM->>TrayPreview: LoadAsync / LoadPageAsync
    TrayPreview-->>TrayVM: Tray page data

    Note over TrayVM: Thumbnail and metadata loading stay lazy\nand are not part of blocking warmup
```

---

## 4. Tray Export

```mermaid
sequenceDiagram
    participant User
    participant ExportCtl as MainWindowTrayExportController
    participant Warmup as MainWindowCacheWarmupController
    participant ExportSvc as TrayDependencyExportService

    User->>ExportCtl: Export selected Tray items
    ExportCtl->>Warmup: TryGetReadyTraySnapshot(modsRoot)

    alt Snapshot is not ready
        Warmup-->>ExportCtl: false
        ExportCtl->>ExportCtl: Block export
        Note over ExportCtl: Logs trayexport.blocked.cache-not-ready\nNo hidden Mods validation
    else Snapshot is ready
        Warmup-->>ExportCtl: snapshot
        ExportCtl->>ExportSvc: ExportAsync(request with PreloadedSnapshot)
        ExportSvc->>ExportSvc: Validate snapshot matches ModsRoot
        ExportSvc->>ExportSvc: Parse tray, match direct refs, expand deps, copy files
        ExportSvc-->>ExportCtl: Export result
    end
```

---

## 5. Tray Dependency Analysis

```mermaid
sequenceDiagram
    participant User
    participant ExecCtl as MainWindowExecutionController
    participant Warmup as MainWindowCacheWarmupController
    participant AnalyzeSvc as TrayDependencyAnalysisService

    User->>ExecCtl: Run Tray Dependencies
    ExecCtl->>Warmup: EnsureTrayWorkspaceReadyAsync(modsRoot)
    Warmup-->>ExecCtl: ready snapshot

    ExecCtl->>AnalyzeSvc: AnalyzeAsync(request with PreloadedSnapshot)
    AnalyzeSvc->>AnalyzeSvc: Validate snapshot exists and matches ModsRoot
    AnalyzeSvc->>AnalyzeSvc: Parse tray, match direct refs, expand deps, write outputs
    AnalyzeSvc-->>ExecCtl: Analysis result

    Note over AnalyzeSvc: Analysis no longer scans Mods or builds cache on its own
```

---

## 6. Cache Clear

```mermaid
sequenceDiagram
    participant User
    participant ShellOps as ShellSystemOperationsController
    participant CacheSvc as IAppCacheMaintenanceService
    participant ModsVM as ModPreviewWorkspaceViewModel
    participant TrayVM as TrayPreviewWorkspaceViewModel
    participant Warmup as MainWindowCacheWarmupController

    User->>ShellOps: Clear Cache
    ShellOps->>CacheSvc: ClearAsync()
    CacheSvc-->>ShellOps: Disk cache folders cleared

    ShellOps->>ModsVM: ResetAfterCacheClear()
    ModsVM->>Warmup: Reset()

    ShellOps->>TrayVM: ResetAfterCacheClear()
    TrayVM->>Warmup: Reset()

    Note over Warmup: In-memory ready state is cleared\nNext page entry rebuilds blocking warmup state
```

---

## 7. Key Invariants

* No startup-time cache warmup path exists anymore.
* `Mods` and `Tray` blocking warmup both flow through the same coordinator.
* `SqliteModPackageInventoryService` is the package inventory source used by page warmup.
* Tray dependency snapshots are keyed by `(ModsRootPath, InventoryVersion)`.
* Export and analysis require `PreloadedSnapshot` and fail fast if cache is not ready.
* `TrayPreview` thumbnails and metadata remain lazy-loaded and are not promoted to a blocking page warmup stage.
