# Cache Warmup Sequence

This document captures the current page-level cache warmup flow after the cache refactor.

Current baseline:

* `Mods` and `Tray` still use page-triggered blocking warmup.
* startup idle prewarm is scheduled through `IStartupPrewarmService`, which delegates to domain warmup services.
* shared inventory/runtime helpers still use a keyed per-root gate to serialize work per `ModsRoot`.
* Tray export and Tray dependency analysis both consume a preloaded ready snapshot.
* `TrayDependencyEngine` no longer performs hidden `Mods` directory validation for export or analysis.

Relevant implementation anchors:

* `src/SimsModDesktop.Presentation/Warmup/ModsWarmupService.cs`
* `src/SimsModDesktop.Presentation/Warmup/TrayWarmupService.cs`
* `src/SimsModDesktop.Presentation/Warmup/SaveWarmupService.cs`
* `src/SimsModDesktop.Presentation/Services/StartupPrewarmService.cs`
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
    autonumber
    participant User
    participant Shell as MainShellViewModel
    participant Mods as Mods workspace
    participant Tray as Tray workspace
    participant ModsWarm as IModsWarmupService
    participant TrayWarm as ITrayWarmupService

    User->>Shell: Open Mods or Tray

    alt Mods section
        Shell->>Mods: Activate workspace
        Mods->>ModsWarm: EnsureWorkspaceReadyAsync(modsRoot)
        ModsWarm-->>Mods: Ready
    else Tray section
        Shell->>Tray: Activate workspace
        Tray->>TrayWarm: EnsureDependencyReadyAsync(modsRoot)
        TrayWarm-->>Tray: Ready snapshot
    end

    Note over ModsWarm,TrayWarm: Shared keyed gating remains internal.\nPages only depend on the domain warmup services.
```

---

## 2. Mods Page First Load

```mermaid
sequenceDiagram
    autonumber
    participant User
    participant Mods as ModPreviewWorkspaceViewModel
    participant Warmup as IModsWarmupService
    participant Inventory as IModPackageInventoryService
    participant Cache as app-cache.db
    participant Indexer as IModItemIndexScheduler
    participant Catalog as IModItemCatalogService

    User->>Mods: Open page
    Mods->>Warmup: EnsureWorkspaceReadyAsync(modsRoot)
    Warmup->>Inventory: RefreshAsync(modsRoot, progress)
    Inventory->>Cache: Load previous inventory rows
    Note over Inventory,Cache: Scan Mods/**/*.package,\ncompute Added / Changed / Removed / Unchanged,\npersist inventory + InventoryVersion
    Inventory-->>Warmup: Refresh result

    alt Session already has a ready snapshot
        Warmup-->>Mods: Ready
    else Fast refresh needed
        Warmup->>Indexer: QueueRefreshAsync(changed, removed, fast only)
        Indexer-->>Warmup: Fast pass complete
        Warmup-->>Mods: Ready
    end

    Mods->>Catalog: QueryPageAsync(current query)
    Catalog-->>Mods: Page rows
    Mods->>Warmup: QueuePriorityDeepEnrichment(current page)

    Note over Mods,Warmup: The page becomes usable after validated rows load.\nDetail enrichment keeps running in the background.
```

---

## 3. Tray Page First Load

```mermaid
sequenceDiagram
    autonumber
    participant User
    participant Tray as TrayPreviewWorkspaceViewModel
    participant Warmup as ITrayWarmupService
    participant Inventory as IModPackageInventoryService
    participant AppCache as app-cache.db
    participant PkgCache as IPackageIndexCache
    participant DepCache as TrayDependencyPackageIndex/cache.db
    participant Preview as IPreviewQueryService

    User->>Tray: Open page
    Tray->>Warmup: EnsureDependencyReadyAsync(modsRoot)
    Warmup->>Inventory: RefreshAsync(modsRoot, progress)
    Inventory->>AppCache: Validate and persist inventory
    Inventory-->>Warmup: Inventory result + version

    alt Session already has a ready snapshot
        Warmup-->>Tray: Ready snapshot
    else Load or build snapshot
        Warmup->>PkgCache: TryLoadSnapshotAsync(modsRoot, inventoryVersion)
        alt Persisted snapshot exists
            PkgCache->>DepCache: Read PackageIndexSnapshots
            PkgCache-->>Warmup: Snapshot
        else Snapshot must be rebuilt
            Note over Warmup,PkgCache: Build from package files,\nchanged files, and removed paths
            PkgCache->>DepCache: Persist new snapshot
            PkgCache-->>Warmup: Snapshot
        end
        Warmup-->>Tray: Ready snapshot
    end

    Tray->>Preview: LoadAsync / LoadPageAsync
    Preview-->>Tray: Page data

    Note over Tray,Preview: Thumbnail and metadata loading stay lazy\noutside the blocking warmup path.
```

---

## 4. Tray Export

```mermaid
sequenceDiagram
    autonumber
    participant User
    participant Export as MainWindowTrayExportController
    participant Warmup as ITrayWarmupService
    participant Service as TrayDependencyExportService

    User->>Export: Export selected items
    Export->>Warmup: TryGetReadySnapshot(modsRoot)

    alt Snapshot missing
        Warmup-->>Export: false
        Note over Export: Block export and log\ntrayexport.blocked.cache-not-ready
    else Snapshot ready
        Warmup-->>Export: snapshot
        Export->>Service: ExportAsync(request + PreloadedSnapshot)
        Note over Service: Validate ModsRoot match,\nparse tray bundle,\nexpand deps,\ncopy files
        Service-->>Export: Export result
    end
```

---

## 5. Tray Dependency Analysis

```mermaid
sequenceDiagram
    autonumber
    participant User
    participant Exec as MainWindowExecutionController
    participant Warmup as ITrayWarmupService
    participant Service as TrayDependencyAnalysisService

    User->>Exec: Run Tray Dependencies
    Exec->>Warmup: EnsureDependencyReadyAsync(modsRoot)
    Warmup-->>Exec: Ready snapshot

    Exec->>Service: AnalyzeAsync(request + PreloadedSnapshot)
    Note over Service: Validate snapshot,\nparse tray bundle,\nexpand dependencies,\nwrite outputs
    Service-->>Exec: Analysis result

    Note over Service: Analysis no longer scans Mods\nor builds cache on its own.
```

---

## 6. Cache Clear

```mermaid
sequenceDiagram
    autonumber
    participant User
    participant Shell as ShellSystemOperationsController
    participant CacheSvc as IAppCacheMaintenanceService
    participant Mods as ModPreviewWorkspaceViewModel
    participant Tray as TrayPreviewWorkspaceViewModel
    participant ModsWarm as IModsWarmupService
    participant TrayWarm as ITrayWarmupService
    participant SaveWarm as ISaveWarmupService

    User->>Shell: Clear Cache
    Shell->>CacheSvc: ClearAsync()
    CacheSvc-->>Shell: Disk caches cleared

    Shell->>Mods: ResetAfterCacheClear()
    Mods->>ModsWarm: Reset()

    Shell->>Tray: ResetAfterCacheClear()
    Tray->>TrayWarm: Reset()

    Shell->>SaveWarm: Reset()

    Note over ModsWarm,TrayWarm: Warmup state is cleared.\nNext page entry rebuilds blocking readiness.
```

---

## 7. Key Invariants

* startup idle warmup is scheduled through `IStartupPrewarmService`, but build/ensure work still lives in domain warmup services.
* `Mods` and `Tray` blocking warmup both reuse the same internal inventory/runtime helpers while exposing separate service boundaries.
* `SqliteModPackageInventoryService` is the package inventory source used by page warmup.
* Tray dependency snapshots are keyed by `(ModsRootPath, InventoryVersion)`.
* Export and analysis require `PreloadedSnapshot` and fail fast if cache is not ready.
* `TrayPreview` thumbnails and metadata remain lazy-loaded and are not promoted to a blocking page warmup stage.
