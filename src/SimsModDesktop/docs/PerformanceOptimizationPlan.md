# Performance Optimization Plan

## 1. Background

The current codebase already eliminated the largest hidden regression in the Tray workflow: export and analysis no longer trigger a hidden full `Mods` validation path. That baseline is good, but the remaining heavy paths are still dominated by repeated scans, repeated parsing, repeated persistence, and underutilized CPU-bound stages.

This plan is an architecture-level performance refactor, not a set of isolated micro-optimizations. The goal is to restructure hot paths so that repeated work is removed, bounded parallelism is applied consistently, and cache layers become incrementally reusable instead of whole-root rebuild oriented.

The highest remaining bottleneck classes are:

* full inventory validation followed by non-incremental persistence
* package parsing stages that still re-read or re-parse the same package more than once
* cache materialization and persistence paths that still rebuild or serialize more than necessary
* search and batch compute paths that do not fully use efficient indexing or bounded worker control

## 2. Goals

This optimization program has five primary goals:

* reduce blocking warmup time for the `Mods` page
* reduce blocking warmup time for the `Tray` page
* eliminate hidden heavy work from export and analysis entrypoints
* improve real CPU and IO utilization across heavy workflows
* preserve the current UI semantics and avoid user-visible workflow regression

More specifically:

* the app must keep page-triggered blocking warmup rather than silently doing work at startup
* repeated visits to the same `ModsRoot` with small deltas should avoid full rebuild behavior
* `Tray` export and `Tray` analysis must continue to consume only a validated ready snapshot
* long-running heavy flows must use bounded concurrency instead of either strict serialization or unbounded fan-out

## 3. Non-Goals

This plan intentionally does not do the following:

* change the product rule of "validate before showing or enabling operations"
* restore startup-time background warmup
* introduce external telemetry, hosted monitoring, or remote observability systems
* change user-visible entrypoints, page layout, or the overall UI interaction model
* rewrite the dependency matching semantics inside `SimsModDesktop.TrayDependencyEngine`
* change the requirement that export and analysis fail fast when a validated snapshot is not ready

## 4. Current Intensive Operations

The current heavy paths are grouped by operational domain rather than by file.

### Page Warmup

* `Mods inventory validation`
  * Type: `IO-bound`
  * Current bottleneck: full recursive traversal of `Mods/**/*.package` plus full-row persistence behavior
  * Anchors: `src/SimsModDesktop.Infrastructure/Mods/SqliteModPackageInventoryService.cs`, `src/SimsModDesktop.Presentation/ViewModels/MainWindowCacheWarmupController.cs`

* `Mod fast index`
  * Type: `Mixed`
  * Current bottleneck: per-package DBPF index reads and sequential package processing
  * Anchors: `src/SimsModDesktop.Application/Mods/ModItemIndexScheduler.cs`, `src/SimsModDesktop.Application/Mods/FastModItemIndexService.cs`

* `Mod deep enrich`
  * Type: `Mixed`
  * Current bottleneck: repeated package reads, texture analysis, and sequential deep processing
  * Anchors: `src/SimsModDesktop.Application/Mods/ModItemIndexScheduler.cs`, `src/SimsModDesktop.Application/Mods/DeepModItemEnrichmentService.cs`

* `Tray dependency snapshot build`
  * Type: `Mixed`
  * Current bottleneck: full snapshot rebuild logic and expensive in-memory index construction
  * Anchors: `src/SimsModDesktop.Presentation/ViewModels/MainWindowCacheWarmupController.cs`, `src/SimsModDesktop.TrayDependencyEngine/TrayDependencyExportService.cs`, `src/SimsModDesktop.PackageCore/DbpfPackageCatalog.cs`

* `Tray preview root scan`
  * Type: `IO-bound`
  * Current bottleneck: root scan and descriptor rebuilding can be repeated when filter combinations change
  * Anchors: `src/SimsModDesktop.Infrastructure/Tray/SimsTrayPreviewService.cs`

### Index / Cache Build

* `Mod SQLite writes`
  * Type: `IO-bound`
  * Current bottleneck: row-by-row writes for package state, items, and textures inside tight loops
  * Anchors: `src/SimsModDesktop.Infrastructure/Mods/SqliteModItemIndexStore.cs`

* `Mod search`
  * Type: `CPU-bound`
  * Current bottleneck: `%LIKE%` search on `SearchText`, which does not scale on larger index sets
  * Anchors: `src/SimsModDesktop.Infrastructure/Mods/SqliteModItemIndexStore.cs`

* `Tray snapshot persistence/load`
  * Type: `Mixed`
  * Current bottleneck: whole-snapshot JSON serialization and whole-snapshot materialization
  * Anchors: `src/SimsModDesktop.TrayDependencyEngine/TrayDependencyExportService.cs`

* `Tray metadata load`
  * Type: `Mixed`
  * Current bottleneck: metadata misses are parsed serially for tray item files
  * Anchors: `src/SimsModDesktop.Infrastructure/Tray/TrayMetadataService.cs`

* `Tray thumbnail generation`
  * Type: `Mixed`
  * Current bottleneck: embedded image extraction, cache writes, bitmap loads, and UI marshalling
  * Anchors: `src/SimsModDesktop.Infrastructure/Tray/TrayThumbnailService.cs`, `src/SimsModDesktop.Presentation/ViewModels/MainWindowTrayPreviewController.cs`

### Export / Analysis

* `Tray export`
  * Type: `Mixed`
  * Current bottleneck: tray bundle parse, direct matching, dependency expansion, and large file copy phases
  * Anchors: `src/SimsModDesktop.TrayDependencyEngine/TrayDependencyExportService.cs`, `src/SimsModDesktop.Presentation/ViewModels/Preview/TrayPreviewWorkspaceViewModel.cs`

* `Tray dependency analysis`
  * Type: `Mixed`
  * Current bottleneck: tray parse, direct matching, dependency expansion, and optional CSV/export output generation
  * Anchors: `src/SimsModDesktop.TrayDependencyEngine/TrayDependencyAnalysisService.cs`, `src/SimsModDesktop.Presentation/ViewModels/MainWindowExecutionController.cs`

### Toolkit / Asset Processing

* `Find Duplicate`
  * Type: `Mixed`
  * Current bottleneck: full directory enumeration plus batch hashing
  * Anchors: `src/SimsModDesktop.Application/Execution/ExecutionCoordinator.cs`

* `Hash batch compute`
  * Type: `IO-bound`
  * Current bottleneck: current batch path can fan out too aggressively and does not use explicit worker bounds
  * Anchors: `src/SimsModDesktop.Infrastructure/Services/CrossPlatformHashComputationService.cs`

* `Flatten`
  * Type: `IO-bound`
  * Current bottleneck: heavy file move/copy workloads under conflict checks
  * Anchors: `src/SimsModDesktop.Application/Execution/FlattenTransformationModeHandler.cs`

* `Merge`
  * Type: `IO-bound`
  * Current bottleneck: currently serial file processing in large merge sets
  * Anchors: `src/SimsModDesktop.Application/Execution/MergeTransformationModeHandler.cs`

* `Normalize`
  * Type: `IO-bound`
  * Current bottleneck: high file-count rename workloads
  * Anchors: `src/SimsModDesktop.Application/Execution/NormalizeTransformationModeHandler.cs`

* `Organize`
  * Type: `Mixed`
  * Current bottleneck: zip enumeration, extraction, cleanup, and follow-up file system operations
  * Anchors: `src/SimsModDesktop.Application/Execution/OrganizeTransformationModeHandler.cs`

* `Texture Compress`
  * Type: `CPU-bound`
  * Current bottleneck: decode, resize, transcode, and buffer allocation churn
  * Anchors: `src/SimsModDesktop.Presentation/ViewModels/MainWindowExecutionController.cs`, `src/SimsModDesktop.Infrastructure/TextureProcessing/TextureTranscodePipeline.cs`

* `Mod package texture analysis`
  * Type: `Mixed`
  * Current bottleneck: package index read plus repeated texture resource reads and classification
  * Anchors: `src/SimsModDesktop.Application/TextureCompression/ModPackageTextureAnalysisService.cs`

* `Save preview descriptor / artifact pipeline`
  * Type: `Mixed`
  * Current bottleneck: full save read for descriptor build plus on-demand single-household artifact generation
  * Anchors: `src/SimsModDesktop.Infrastructure/Saves/SavePreviewDescriptorBuilder.cs`, `src/SimsModDesktop.Infrastructure/Saves/SavePreviewArtifactProvider.cs`

* `Save household export`
  * Type: `IO-bound`
  * Current bottleneck: repeated file emission per exported household bundle
  * Anchors: `src/SimsModDesktop.SaveData/Services/HouseholdTrayExporter.cs`

## 5. Optimization Strategy

The implementation must follow these architectural principles:

* `Inventory as single source of truth`
  * `ModPackageInventory` remains the only file-system validation truth source for `Mods`.

* `Delta-first rebuild`
  * When input changes are known, changed and removed sets must drive the rebuild path.

* `Bounded parallelism`
  * CPU-heavy stages should use explicit bounded concurrency. No heavy production path should rely on unbounded `Task.WhenAll`.

* `Single-writer database persistence`
  * Parsing may fan out; SQLite writes should converge into batched, ordered persistence.

* `No hidden fallback rebuild`
  * Export and analysis must never silently rebuild caches when a ready validated snapshot is missing.

* `Cache invalidation by schema/version`
  * Cache invalidation should be explicit and versioned. Old cache formats may be invalidated and rebuilt rather than supported through dual-read compatibility paths.

## 6. Workstreams

### Workstream 1: Mods Inventory Incremental Persistence

#### Current Problem

The current inventory validation path recomputes deltas but still rewrites the effective root inventory too broadly. Validation does useful work, but persistence still behaves closer to a whole-root rewrite than a true delta-first update path.

#### Target Design

`Mods` inventory validation continues to scan the full file set for correctness, but persistence becomes incrementally applied:

* `Added` paths are inserted
* `Changed` paths are updated in place
* `Removed` paths are deleted
* unchanged rows remain untouched

#### Implementation Notes

* Add `PackageFingerprintKey` to inventory entries
* Add a root+fingerprint index for lookup and reuse
* Keep `RefreshAsync()` contract and return model unchanged
* Retain the current product rule that validation is full before page readiness

#### Expected Gain

* significantly reduced SQLite write volume during repeat validations
* reduced write amplification on large `Mods` roots with small deltas
* lower warmup wall-clock time without relaxing validation semantics

#### Dependencies

* no prerequisite workstream

### Workstream 2: Mod Index Pipeline Parallelization

#### Current Problem

The current scheduler is effectively single-lane for package processing. Fast pass and deep pass are package-serial, which underutilizes CPU and keeps parsing tightly coupled to storage writes.

#### Target Design

Refactor the Mod indexing pipeline into:

* bounded parse workers for fast pass
* bounded parse workers for deep pass
* a single batched writer for SQLite persistence

#### Implementation Notes

* Keep the external `QueueRefreshAsync(ModIndexRefreshRequest, ...)` entrypoint
* Reuse per-package parse context inside a single refresh
* Separate parse output from DB write application
* Preserve current prioritization semantics for current-page packages

#### Expected Gain

* higher CPU utilization during index construction
* reduced long-tail latency on large changed package sets
* more predictable DB contention through controlled writer behavior

#### Dependencies

* depends on Workstream 1

### Workstream 3: Mod Search FTS5

#### Current Problem

Current search uses `%LIKE%` against `SearchText`, which does not scale for larger local indexes.

#### Target Design

Introduce an FTS5-backed search table synchronized with `ModIndexedItems`, and route non-empty search queries through `MATCH` rather than `%LIKE%`.

#### Implementation Notes

* add an FTS5 virtual table for `ItemKey` and `SearchText`
* keep no-search queries on the normal query path
* use whitespace-tokenized prefix matching for the default query builder

#### Expected Gain

* much better search performance on larger indexes
* lower CPU cost for repeated filtered page queries

#### Dependencies

* depends on Workstream 2

### Workstream 4: Tray Package Cache Refactor

#### Current Problem

The Tray cache no longer hides validation in export or analysis, but it still behaves too much like a full-root rebuild and still persists too much data as a whole-snapshot object.

#### Target Design

Replace whole-snapshot blob persistence with:

* a package-level parse cache keyed by package fingerprint
* a root-level manifest keyed by `ModsRootPath + InventoryVersion`

#### Implementation Notes

* remove snapshot JSON blob persistence from the production path
* materialize `PackageIndexSnapshot` from a manifest plus package parse cache rows
* make `ChangedPackageFiles` and `RemovedPackagePaths` drive rebuild behavior
* allow full invalidation and rebuild on schema version change

#### Expected Gain

* small deltas only re-parse small deltas
* lower serialization overhead
* reduced memory churn on snapshot load

#### Dependencies

* depends on Workstream 1

### Workstream 5: Tray Export / Analysis Strict Snapshot Reuse

#### Current Problem

The current mainline behavior is already correct, but the rule needs to remain explicit and future-proof as more performance work lands.

#### Target Design

Keep export and analysis as strict snapshot consumers:

* export fails immediately if `PreloadedSnapshot` is missing
* analysis fails immediately if `PreloadedSnapshot` is missing
* no hidden cache rebuild path is allowed back into these entrypoints

Also add bounded multi-item export concurrency at the UI orchestration layer.

#### Implementation Notes

* preserve fail-fast semantics
* only parallelize across selected export items
* keep per-item export logic behaviorally unchanged
* cap item-level concurrency at `2`

#### Expected Gain

* no semantic drift back toward hidden heavy work
* lower multi-select export wall-clock time

#### Dependencies

* depends on Workstream 4

### Workstream 6: Tray Preview Root Snapshot + Filter Projection

#### Current Problem

Tray preview cache keys are currently too tightly coupled to filter combinations, which can trigger avoidable repeated root-scan style work.

#### Target Design

Split tray preview caching into:

* a root snapshot keyed by tray root fingerprint
* a filtered projection layer that operates in memory

Only metadata-dependent filters should trigger metadata loading.

#### Implementation Notes

* keep thumbnail generation lazy
* normal filter changes should project in memory only
* only author/search filters are allowed to demand metadata misses

#### Expected Gain

* faster filter changes
* fewer repeated directory scans
* better reuse of root-level tray descriptors

#### Dependencies

* depends on Workstream 5

### Workstream 7: Hash And Toolkit Throughput Control

#### Current Problem

The batch hash path does not explicitly enforce a bounded worker model, and `Merge` currently leaves throughput on the table by staying serial.

#### Target Design

Introduce:

* a bounded worker pool for batch hashing
* an explicit `HashBatchRequest`
* `HashWorkerCount` wiring into batch hashing
* bounded parallelism for `Merge`

#### Implementation Notes

* batch hash APIs move to a request-object model
* cancellation remains first-class
* `Normalize` stays serial in this phase

#### Expected Gain

* improved IO utilization without unbounded disk pressure
* more stable and configurable throughput for duplicate detection
* better throughput on large merge sets

#### Dependencies

* no prerequisite workstream

### Workstream 8: Texture / Resource Read Micro-Optimizations

#### Current Problem

Texture transcode and DBPF resource reads still allocate more transient buffers than necessary and re-read more than ideal in downstream flows.

#### Target Design

Introduce pooled resource read paths and reduce transient allocation pressure in texture encode/decode/transcode.

#### Implementation Notes

* add pooled DBPF read APIs
* switch high-frequency readers to pooled paths first
* keep behavior and output semantics identical
* this work is intentionally a micro-optimization stage, not a feature rewrite

#### Expected Gain

* lower GC pressure
* smoother deep enrichment and dependency expansion under load
* reduced transient memory spikes in texture flows

#### Dependencies

* depends on Workstream 2 for Mod deep-path consumers
* depends on Workstream 4 for Tray dependency read-path consumers

## 7. API / Contract Changes

The document step does not change code yet, but these are the planned contract changes for implementation.

### `IHashComputationService`

* Change Type: `Modified`
* Compatibility: `Breaking`
* Migration Rule:
  * batch hash methods will move from raw file-path lists to `HashBatchRequest`
  * callers must supply `FilePaths`
  * callers should pass configured worker count instead of relying on implicit fan-out

### `IPackageIndexCache`

* Change Type: `Modified`
* Compatibility: `Internal-only`
* Migration Rule:
  * keep `TryLoadSnapshotAsync(...)`
  * keep `BuildSnapshotAsync(...)`
  * expand implementation behind these methods to package-level parse cache plus root manifest
  * add explicit root invalidation support where needed

### `IModItemIndexScheduler`

* Change Type: `Extended`
* Compatibility: `Internal-only`
* Migration Rule:
  * keep the external refresh entrypoint shape
  * move internal execution to bounded parse workers plus single-writer persistence
  * existing callers should not change behaviorally

### Planned New Types

The implementation phase is expected to introduce:

* `HashBatchRequest`
* `ModPackageParseContext`
* `PooledDbpfReadBuffer`

## 8. Data / Schema Changes

### `app-cache.db`

Planned changes:

* upgrade `ModPackageInventory` schema
* add `PackageFingerprintKey` to inventory entries
* add root+fingerprint lookup index
* upgrade `ModItemIndex` schema to include an FTS5-backed search table

Upgrade policy:

* schema-version driven invalidation
* old data may be dropped and rebuilt
* no dual-read compatibility layer is required

### `TrayDependencyPackageIndex/cache.db`

Planned changes:

* add package-level parse cache tables
* add root manifest tables
* remove the production dependency on whole-snapshot JSON blob persistence
* deprecate the old `PackageIndexSnapshots` whole-snapshot blob model

Upgrade policy:

* invalidate and rebuild on schema change
* do not support historical dual-read compatibility

### `SQLite FTS additions`

Planned changes:

* add FTS5 virtual table for Mod indexed search
* keep it synchronized with `ModIndexedItems`
* rebuild FTS rows as part of index rebuild or schema upgrade

## 9. Logging And Observability

### Mods

Planned events:

* `modcache.inventory.delta`
* `modcache.fastindex.batch`
* `modcache.deepindex.batch`
* `modcache.storewriter.batch`

Required fields:

* `ModsRoot`
* `InventoryVersion`
* `AddedCount`
* `ChangedCount`
* `RemovedCount`
* `BatchSize`
* `WorkerCount`
* `ElapsedMs`

Default level:

* totals and batch summaries: `Information`
* optional internal batch detail: `Debug`

### Tray

Planned events:

* `traycache.packagecache.hit`
* `traycache.packagecache.miss`
* `traycache.manifest.rebuild`
* `traycache.snapshot.materialize`

Required fields:

* `ModsRoot`
* `InventoryVersion`
* `PackageCount`
* `ChangedPackageCount`
* `ReusedPackageCount`
* `ElapsedMs`

Default level:

* mainline summaries: `Information`

### Hash

Planned events:

* `hash.batch.start`
* `hash.batch.done`

Required fields:

* `FileCount`
* `TotalBytes`
* `WorkerCount`
* `ElapsedMs`

Default level:

* batch lifecycle summaries: `Information`

## 10. Rollout Order

The implementation must follow this order exactly:

1. `Mods inventory incremental persistence`
2. `Hash worker pool + Merge throughput control`
3. `Mod index pipeline + batch store writes`
4. `Mod search FTS5`
5. `Tray package cache refactor`
6. `Tray preview projection refactor`
7. `Texture / resource read optimizations`

No later stage should begin until the previous stage passes its targeted verification and acceptance checks.

## 11. Risks And Rollback

### SQLite schema migration failure

* Risk:
  * schema change fails or leaves the cache store unusable
* Trigger Signal:
  * startup or page warmup throws schema/open exceptions
* Rollback:
  * bump back to previous schema handling
  * clear the affected cache database
  * disable the new schema path behind the implementation branch if needed

### Cache inconsistency after partial rebuild

* Risk:
  * delta rebuild writes only part of the expected cache state
* Trigger Signal:
  * snapshot loads with missing rows, missing packages, or mismatched counts
* Rollback:
  * invalidate the affected root cache
  * fall back to a full rebuild for that root within the new architecture

### Over-parallelization causing IO regression

* Risk:
  * more workers increase disk contention and reduce throughput
* Trigger Signal:
  * CPU remains underutilized while elapsed time worsens and disk queue pressure rises
* Rollback:
  * lower default worker counts
  * reduce maximum bounds for parse or hash workers

### FTS query mismatch

* Risk:
  * FTS search returns materially different results than the old `%LIKE%` path
* Trigger Signal:
  * search regression tests fail or user-visible search misses known items
* Rollback:
  * keep the FTS table but route production search back to the old query path until query normalization is corrected

### Tray package cache materialization bug

* Risk:
  * manifest-based materialization builds an incomplete or inconsistent in-memory snapshot
* Trigger Signal:
  * export or analysis mismatches current ready snapshot expectations
* Rollback:
  * invalidate the manifest-based root and rebuild from package-level cache rows
  * if necessary, temporarily disable package-level manifest reuse in the active branch

## 12. Acceptance Criteria

The full optimization effort is accepted only when all of the following are true:

* `Mods` warmup no longer performs a full inventory delete+rewrite pattern
* `Tray` rebuilds only changed package cache rows on small deltas
* no hidden fallback rebuild path exists in export or analysis
* large-set Mod search no longer depends on `%LIKE%`
* no production batch hash path uses unbounded `Task.WhenAll`
* page-triggered blocking semantics remain unchanged
* major intensive operations are covered by timing and summary logs

## 13. Linked Checklist

Execution must follow the companion checklist:

* [PerformanceOptimizationChecklist.md](PerformanceOptimizationChecklist.md)

## 14. Round2 Aggressive Worker Profile (2026-03)

This profile extends the original plan with a high-throughput worker strategy while preserving product semantics.

Worker defaults:

* `ModIndex.FastWorkers`: `clamp(CPU, 4, 16)`
* `ModIndex.DeepWorkers`: `clamp(ceil(CPU/2), 3, 10)`
* `TrayCache.ParseWorkers`: `clamp(CPU, 4, 16)`
* `TrayCache.WriteBatchSize`: `512`
* `TrayMetadata.ParseWorkers`: `clamp(ceil(CPU/2), 4, 12)`
* `Organize.MaxParallelArchives`: `clamp(ceil(CPU/2), 4, 8)`
* `SavePreview.Workers`: `clamp(ceil(CPU/2), 4, 12)`
* `HashWorkerCount`: default `12`

Adaptive down-throttle rules:

* sample window: `5s`
* if throughput is below `85%` of previous-6-window average for `3` consecutive windows, reduce workers to `floor(current * 0.75)`
* if throughput recovers to `>=95%` of previous average for `4` consecutive windows, increase workers by `+1` up to target
* if process working set exceeds `120%` of baseline for `10s`, force one downscale

Baseline script:

* `scripts/perf/run-round2-baseline.ps1`
