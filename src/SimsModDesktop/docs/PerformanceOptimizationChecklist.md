# Performance Optimization Execution Checklist

## 1. Execution Rules

The implementation phase must follow these rules:

* workstreams must be executed in order
* the next workstream must not begin until the previous one is accepted
* checklist state must be updated after each completed item
* each workstream must include code changes, tests, logging checks, and document updates as applicable
* no hidden compatibility fallback may be reintroduced while implementing performance work

### 1.1 Approved Deviations (2026-03-05)

The following deviations are approved and treated as baseline for execution and review:

* WS5 bounded multi-item export concurrency is `8` (supersedes the earlier draft baseline of `2`)
* during fast iteration on very large local `Mods` datasets, long-running suites may be temporarily excluded by filter
* when filters are used, excluded suites must be explicitly listed in the verification log with rationale and follow-up gate

## 2. Workstream Checklists

### WS1: Mods Inventory Incremental Persistence

Implementation:

- [x] Refactor inventory persistence to delta update
- [x] Add `PackageFingerprintKey`
- [x] Add root+fingerprint index
- [x] Keep `RefreshAsync` contract unchanged

Tests:

- [x] Added package path persists without full rewrite
- [x] Changed package path updates in place
- [x] Removed package path deletes only stale rows
- [x] Empty Mods root still returns valid snapshot

Logging:

- [x] `modcache.inventory.delta` log added

Acceptance:

- [x] No full `DELETE` + full `INSERT` remains in the production inventory persistence path

### WS2: Mod Index Pipeline Parallelization

Implementation:

- [x] Introduce bounded parse worker pool for fast pass
- [x] Introduce bounded parse worker pool for deep pass
- [x] Introduce single writer batch persistence
- [x] Reuse package parse context per package

Tests:

- [x] Parallel fast pass produces the same final rows as the old logic
- [x] Deep pass still prioritizes current page packages
- [x] Cancellation does not leave the writer in an invalid state

Logging:

- [x] `modcache.fastindex.batch` log added
- [x] `modcache.deepindex.batch` log added
- [x] `modcache.storewriter.batch` log added

Acceptance:

- [x] Scheduler no longer processes all packages strictly one by one

### WS3: Mod Search FTS5

Implementation:

- [x] Add FTS5 table
- [x] Sync insert/update/delete with `ModIndexedItems`
- [x] Route search queries through `MATCH`

Tests:

- [x] Search results remain functionally equivalent for key scenarios
- [x] No-search query path remains valid

Logging:

- [x] Optional debug log for FTS query build added

Acceptance:

- [x] Production search path no longer depends on `LIKE '%...%'`

### WS4: Tray Package Cache Refactor

Implementation:

- [x] Remove snapshot JSON blob persistence
- [x] Add package-level parse cache tables
- [x] Add root manifest tables
- [x] `BuildSnapshotAsync` consumes `ChangedPackageFiles` and `RemovedPackagePaths`
- [x] `TryLoadSnapshotAsync` materializes from manifest

Tests:

- [x] Small change reuses unchanged package cache
- [x] Removed package is excluded from the rebuilt root manifest
- [x] Old `PackageIndexSnapshots` table is no longer read

Logging:

- [x] `traycache.packagecache.hit` log added
- [x] `traycache.packagecache.miss` log added
- [x] `traycache.manifest.rebuild` log added
- [x] `traycache.snapshot.materialize` log added

Acceptance:

- [x] Tray rebuild no longer reparses all packages on small deltas

### WS5: Tray Export / Analysis Strict Snapshot Reuse

Implementation:

- [x] Keep export hard-fail when `PreloadedSnapshot` is missing
- [x] Keep analysis hard-fail when `PreloadedSnapshot` is missing
- [x] Add bounded multi-item export concurrency at `8` (approved deviation from earlier `2`)

Tests:

- [x] Export without snapshot fails immediately
- [x] Analysis without snapshot fails immediately
- [x] Multi-select export runs with bounded concurrency and fail-fast rollback semantics

Logging:

- [x] Export logs include item-level concurrency-safe summaries

Acceptance:

- [x] No hidden cache rebuild path is reintroduced

### WS6: Tray Preview Root Snapshot + Filter Projection

Implementation:

- [x] Split root snapshot from filtered projection
- [x] Root scan keyed only by tray root fingerprint
- [x] Metadata loads only for metadata-dependent filters
- [x] Keep thumbnail lazy behavior unchanged

Tests:

- [x] Changing preset/time/build filter does not trigger full root rescan
- [x] Author/search filter only loads missing metadata
- [x] Root change invalidates the root snapshot correctly

Logging:

- [x] Add root snapshot hit/miss debug logs

Acceptance:

- [x] Normal filter changes stay in the memory-only path

### WS7: Hash And Toolkit Throughput Control

Implementation:

- [x] Replace unbounded `Task.WhenAll` hashing with a bounded worker pool
- [x] Add `HashBatchRequest`
- [x] Wire `HashWorkerCount` into batch hashing
- [x] Add bounded parallelism to `Merge`

Tests:

- [x] Large batch hashing respects worker count
- [x] Merge output remains equivalent under concurrency
- [x] Cancellation stops the worker pool cleanly

Logging:

- [x] `hash.batch.start` log added
- [x] `hash.batch.done` log added

Acceptance:

- [x] No production batch hash path remains unbounded

### WS8: Texture / Resource Read Micro-Optimizations

Implementation:

- [x] Add pooled DBPF read path
- [x] Update deep enrich and tray dependency readers to use pooled reads
- [x] Reduce texture transcode transient allocations

Tests:

- [x] Functional output remains identical for representative packages
- [x] Unsupported texture handling remains unchanged

Logging:

- [x] Optional debug counters for pooled read usage added

Acceptance:

- [x] No behavioral regression while lowering transient allocation pressure

## 3. Verification Matrix

| Workstream | Verification | Expected Result |
| --- | --- | --- |
| WS1 | Build | Solution builds cleanly after inventory schema changes. |
| WS1 | Automated Tests | Existing tests plus targeted inventory delta tests pass. |
| WS1 | Manual Scenario | Revalidating a mostly unchanged `Mods` root avoids whole-root rewrite behavior. |
| WS2 | Build | Solution builds cleanly after scheduler and store pipeline changes. |
| WS2 | Automated Tests | Existing tests plus targeted fast/deep pipeline tests pass. |
| WS2 | Manual Scenario | `Mods` warmup shows stable progress and completes faster on multi-package delta sets. |
| WS3 | Build | Solution builds cleanly after FTS table integration. |
| WS3 | Automated Tests | Existing tests plus targeted search equivalence tests pass. |
| WS3 | Manual Scenario | Large-set Mod search responds without `%LIKE%`-style scan regressions. |
| WS4 | Build | Solution builds cleanly after Tray cache schema changes. |
| WS4 | Automated Tests | Existing tests plus targeted package cache delta tests pass. |
| WS4 | Manual Scenario | Small `Mods` delta rebuild reuses unchanged package cache rows. |
| WS5 | Build | Solution builds cleanly after export/analysis orchestration updates. |
| WS5 | Automated Tests | Existing tests plus targeted snapshot-required export/analysis tests pass. |
| WS5 | Manual Scenario | Multi-select Tray export uses ready cache only and does not rebuild in entrypoints. |
| WS6 | Build | Solution builds cleanly after tray preview cache refactor. |
| WS6 | Automated Tests | Existing tests plus targeted filter projection tests pass. |
| WS6 | Manual Scenario | Normal tray filter changes do not trigger full root rescan. |
| WS7 | Build | Solution builds cleanly after hash API and merge throughput changes. |
| WS7 | Automated Tests | Existing tests plus targeted bounded-hash and merge concurrency tests pass. |
| WS7 | Manual Scenario | Large duplicate scans respect worker limits and keep stable progress. |
| WS8 | Build | Solution builds cleanly after pooled read and texture micro-optimizations. |
| WS8 | Automated Tests | Existing tests plus targeted pooled-read and texture output equivalence tests pass. |
| WS8 | Manual Scenario | Intensive texture and dependency read paths show lower transient allocation pressure without output changes. |

Use these verification modes consistently:

* Build: solution or affected projects compile cleanly
* Automated Tests: existing tests plus new targeted tests for the changed workstream
* Manual Scenario: representative large `Mods` and `Tray` libraries are exercised for behavior and performance

## 4. Final Sign-off

- [x] All workstreams completed in order
- [x] Core suites pass and excluded long-running suites are explicitly tracked in verification notes
- [x] No hidden fallback rebuild path remains
- [x] Logs and timing cover all major intensive operations
- [x] README links updated
- [ ] Performance baselines rechecked on representative large Mods and Tray libraries

## 5. Verification Log (2026-03-04)

Automated verification executed during WS4-WS8 implementation:

* `dotnet test src/SimsModDesktop.PackageCore.Tests/SimsModDesktop.PackageCore.Tests.csproj --configuration Release --no-restore` -> pass (`7/7`)
* `dotnet test src/SimsModDesktop.TrayDependencyEngine.Tests/SimsModDesktop.TrayDependencyEngine.Tests.csproj --configuration Release --no-restore` -> pass (`9/9`)
* `dotnet test src/SimsModDesktop.Tests/SimsModDesktop.Tests.csproj --configuration Release --no-restore --filter "FullyQualifiedName!~TrayPreviewWorkspaceViewModelTests&FullyQualifiedName!~TrayDependencyEngineTests"` -> pass (`205/205`)

Filtered run rationale:

* local representative `Mods` dataset is large; full run duration materially slows inner-loop iteration
* filtered suites are tracked and must be reintroduced for milestone/release validation

Pending manual-only verification:

* representative large `Mods`/`Tray` baseline recheck (memory, warmup duration, export duration) on production-like data

Additional verification (2026-03-05, WS3/WS8 logging completion):

* `dotnet test src/SimsModDesktop.Tests/SimsModDesktop.Tests.csproj --configuration Release --no-restore` -> pass (`220/220`)
* `dotnet test src/SimsModDesktop.TrayDependencyEngine.Tests/SimsModDesktop.TrayDependencyEngine.Tests.csproj --configuration Release --no-restore` -> pass (`10/10`)
