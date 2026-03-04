# Performance Optimization Execution Checklist

## 1. Execution Rules

The implementation phase must follow these rules:

* workstreams must be executed in order
* the next workstream must not begin until the previous one is accepted
* checklist state must be updated after each completed item
* each workstream must include code changes, tests, logging checks, and document updates as applicable
* no hidden compatibility fallback may be reintroduced while implementing performance work

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

- [ ] Optional debug log for FTS query build added

Acceptance:

- [x] Production search path no longer depends on `LIKE '%...%'`

### WS4: Tray Package Cache Refactor

Implementation:

- [ ] Remove snapshot JSON blob persistence
- [ ] Add package-level parse cache tables
- [ ] Add root manifest tables
- [ ] `BuildSnapshotAsync` consumes `ChangedPackageFiles` and `RemovedPackagePaths`
- [ ] `TryLoadSnapshotAsync` materializes from manifest

Tests:

- [ ] Small change reuses unchanged package cache
- [ ] Removed package is excluded from the rebuilt root manifest
- [ ] Old `PackageIndexSnapshots` table is no longer read

Logging:

- [ ] `traycache.packagecache.hit` log added
- [ ] `traycache.packagecache.miss` log added
- [ ] `traycache.manifest.rebuild` log added
- [ ] `traycache.snapshot.materialize` log added

Acceptance:

- [ ] Tray rebuild no longer reparses all packages on small deltas

### WS5: Tray Export / Analysis Strict Snapshot Reuse

Implementation:

- [ ] Keep export hard-fail when `PreloadedSnapshot` is missing
- [ ] Keep analysis hard-fail when `PreloadedSnapshot` is missing
- [ ] Add bounded multi-item export concurrency at `2`

Tests:

- [ ] Export without snapshot fails immediately
- [ ] Analysis without snapshot fails immediately
- [ ] Multi-select export runs with bounded concurrency and independent results

Logging:

- [ ] Export logs include item-level concurrency-safe summaries

Acceptance:

- [ ] No hidden cache rebuild path is reintroduced

### WS6: Tray Preview Root Snapshot + Filter Projection

Implementation:

- [ ] Split root snapshot from filtered projection
- [ ] Root scan keyed only by tray root fingerprint
- [ ] Metadata loads only for metadata-dependent filters
- [ ] Keep thumbnail lazy behavior unchanged

Tests:

- [ ] Changing preset/time/build filter does not trigger full root rescan
- [ ] Author/search filter only loads missing metadata
- [ ] Root change invalidates the root snapshot correctly

Logging:

- [ ] Add root snapshot hit/miss debug logs

Acceptance:

- [ ] Normal filter changes stay in the memory-only path

### WS7: Hash And Toolkit Throughput Control

Implementation:

- [ ] Replace unbounded `Task.WhenAll` hashing with a bounded worker pool
- [ ] Add `HashBatchRequest`
- [ ] Wire `HashWorkerCount` into batch hashing
- [ ] Add bounded parallelism to `Merge`

Tests:

- [ ] Large batch hashing respects worker count
- [ ] Merge output remains equivalent under concurrency
- [ ] Cancellation stops the worker pool cleanly

Logging:

- [ ] `hash.batch.start` log added
- [ ] `hash.batch.done` log added

Acceptance:

- [ ] No production batch hash path remains unbounded

### WS8: Texture / Resource Read Micro-Optimizations

Implementation:

- [ ] Add pooled DBPF read path
- [ ] Update deep enrich and tray dependency readers to use pooled reads
- [ ] Reduce texture transcode transient allocations

Tests:

- [ ] Functional output remains identical for representative packages
- [ ] Unsupported texture handling remains unchanged

Logging:

- [ ] Optional debug counters for pooled read usage added

Acceptance:

- [ ] No behavioral regression while lowering transient allocation pressure

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

- [ ] All workstreams completed in order
- [ ] All affected tests pass
- [ ] No hidden fallback rebuild path remains
- [ ] Logs and timing cover all major intensive operations
- [ ] README links updated
- [ ] Performance baselines rechecked on representative large Mods and Tray libraries
