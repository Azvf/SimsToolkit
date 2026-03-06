# Modularization Status and Current Structure

## 1. Current Shape

The project is now modularized around three main layers plus dedicated feature engines:

* `Presentation`
  owns page behavior, shell navigation, warmup orchestration, and ViewModel state.
* `Application`
  owns validators, planners, execution coordinators, and stable service contracts.
* `Infrastructure`
  owns persistence, file/hash/config implementations, and tray/save/mod adapters.
* feature engines
  provide heavy domain logic:
  * `PackageCore`
  * `SaveData`
  * `TrayDependencyEngine`

## 2. What Is Already Modularized

### 2.1 Toolkit Execution

Toolkit action execution is centralized behind:

* `ToolkitActionPlanner`
* `ExecutionCoordinator`
* `UnifiedFileTransformationEngine`

This keeps the UI from directly composing file-operation pipelines.

### 2.2 Preview Domains

Preview behavior is split by responsibility:

* `IPreviewQueryService`
  is now the preview-facing query boundary consumed by surfaces and workspaces.
* `PreviewQueryService`
  is the single preview query implementation behind that boundary, and tray-root scanning, projection/filtering, page building, metadata indexing, persistence mapping, and save-descriptor source loading are split into dedicated helper components.
* tray preview persistence and caches live in Infrastructure.
* save preview now uses `PreviewSourceRef` + `SavePreviewDescriptor` rather than a tray-root-only model.

### 2.3 Save Domain

Save logic is separated into:

* `SaveData` for parsing and household export primitives
* `SaveHouseholdCoordinator` for application-facing operations
* descriptor/artifact stores for preview acceleration

### 2.4 Cache / Warmup Infrastructure

Background prewarm and page-triggered warmup are no longer tray-only concepts.

Shared seams now exist for:

* background prewarm job scheduling
* domain-facing warmup interfaces for mods / tray / save / startup
* list query caching
* tray bundle analysis reuse
* save preview descriptor/artifact readiness

## 3. Current Pressure Points

The main remaining high-weight areas are:

1. `MainWindowViewModel`
   still coordinates many shell-facing responsibilities even after controller extraction.
2. `MainWindowCacheWarmupController`
   is no longer a business boundary. Workspace/shell callers now go through `ModsWarmupService`, `TrayWarmupService`, `SaveWarmupService`, and `IStartupPrewarmService`; the remaining work here is shared runtime plumbing such as inventory reuse, gates, watchers, and host/session helpers.
3. `PreviewQueryService`
   still owns cache orchestration and invalidation, although tray-root scanning, projection/filtering, metadata/page-build, persistence mapping, and save-descriptor responsibilities have been split out.
4. `SaveWorkspaceViewModel`
   now delegates export, dependency analysis, and preview lifecycle flows into dedicated presentation controllers, but still owns selection and page state.

## 4. Recommended Next Modularization Moves

1. Extract more page-specific orchestration from large workspace view models into small presentation coordinators.
2. Keep service contracts source-oriented.
   `PreviewSourceRef`, `CacheSourceVersion`, and descriptor/artifact contracts are the right direction.
3. Avoid folding domain stores together just because they share caching behavior.
   Reuse warmup/query infrastructure, but keep mods/tray/save persistence separate.
4. Prefer adding explicit contract tests when introducing a new seam.
   This is especially important for warmup reuse, preview source routing, and cache invalidation.

## 5. Related Docs

* `ArchitectureOverview.md`
* `CacheWarmupSequence.md`
* `PerformanceOptimizationPlan.md`
* `EngineeringConventions.md`
