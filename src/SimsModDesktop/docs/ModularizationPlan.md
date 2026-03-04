# Modularization Status and Next Steps

## 1. Baseline (Current)

The current structure is already modularized around layered boundaries:

* `Presentation`: panel/view-model state + UI controllers.
* `Application`: planning, validation, execution contracts, coordinators.
* `Infrastructure`: adapters, persistence, OS/file/hash implementations.

Execution is centered on `ToolkitActionPlanner` + `ExecutionCoordinator` + feature services.

---

## 2. Completed Work

### 2.1 Boundary Cleanup
* Removed legacy routing/strategy artifacts from active architecture.
* Added architecture tests to prevent reintroduction of forbidden legacy types.

### 2.2 State Contract Isolation
* Module state interfaces are used across layer boundaries instead of concrete UI types.
* Presentation layer maps concrete panel VMs to module state contracts via DI.

### 2.3 Execution Consolidation
* `Flatten/Normalize/Merge/Organize` consolidated under `UnifiedFileTransformationEngine`.
* `FindDuplicates` executed in-process with shared file/hash services.

---

## 3. Remaining Modularization Targets

1. Reduce `MainWindowViewModel` orchestration weight by moving more flows into focused coordinators/services.
2. Replace current `NoOp*UseCase` placeholders with real use-case implementations (or remove unused seams).
3. Standardize action result contracts across toolkit, tray, save, and mod-index flows.
4. Continue splitting large presentation controllers where responsibilities overlap.

---

## 4. Recommended Sequence

1. Finalize use-case extraction for high-traffic paths (toolkit execution, tray preview, mod index).
2. Introduce explicit result envelopes per flow and remove stringly-typed status propagation.
3. Add contract tests for each module state interface and planner plan builder path.
4. Keep architecture guard tests updated as new layers/services are introduced.
