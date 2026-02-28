# Bottom-Layer Modularization Plan

## Current Baseline
- Execution pipeline is already modular: `Validator -> CLI Mapper -> Execution Strategy -> Coordinator`.
- Action behavior is modularized by `IActionModule` + `ActionModuleRegistry`.
- Main bottleneck was boundary coupling: modules depended on concrete panel ViewModels.

## Phase 1 (Completed)
- Introduced module state contracts in `Application/Modules/ActionModuleStateContracts.cs`.
- Updated each `*ActionModule` to depend on state interfaces instead of concrete panel classes.
- Updated panel ViewModels to implement those contracts.

Result:
- Application module layer no longer requires concrete UI implementation types.
- Easier to swap panel implementations and create lightweight test doubles.

## Phase 2 (Recommended Next)
- Split `MainWindowViewModel` by responsibility:
  - Execution orchestration
  - Tray preview paging/dashboard state
  - Validation debounce and command state refresh
  - Settings capture/apply
- Keep one facade VM for binding, but move logic to dedicated collaborators.

### Phase 2 Progress
- Added `Application/Settings/MainWindowSettingsProjection` and moved settings capture/apply projection logic out of `MainWindowViewModel`.
- `MainWindowViewModel` now delegates settings projection and module load/save orchestration to this service.

## Phase 3 (Recommended Next)
- Introduce explicit use-case services in `Application`:
  - `IToolkitExecutionUseCase`
  - `ITrayPreviewUseCase`
  - `ISettingsProjectionService`
- VM becomes a thin adapter: bind UI state + dispatch commands + display results.

### Phase 3 Progress
- Added `Application/Execution/MainWindowPlanBuilder` to encapsulate:
  - Shared options parsing
  - Global execution option assembly
  - Toolkit CLI plan building
  - Tray preview input plan building
- `MainWindowViewModel` now delegates plan construction to this service.
- Added execution runners:
  - `IToolkitExecutionRunner` / `ToolkitExecutionRunner`
  - `ITrayPreviewRunner` / `TrayPreviewRunner`
- `MainWindowViewModel` now delegates coordinator invocation and exception-to-status translation to runners.

## Phase 4 (Optional Hardening)
- Add contract-level tests for each module state interface.
- Add architecture guard tests (no `Application/*` dependency on `ViewModels/*` concrete types).
- Add structured error/result objects to reduce string-based error propagation.

### Phase 4 Progress
- Added architecture boundary guard test in `SimsModDesktop.Tests/ArchitectureBoundaryTests.cs`.
