# Logging Coverage Execution Checklist

## 1. Execution Rules

* Execute workstreams in order.
* Do not start next workstream until current acceptance is complete.
* Each workstream must include implementation, tests, logging checks, and docs sync.
* Do not keep compatibility fallback paths for removed logging systems.

## 2. Workstream Checklists

### WS1: ILogger Unification And UI Log Removal

Implementation:

- [x] Remove `IUiLogSink` and `UiLogSink`.
- [x] Remove UI log sink DI registration.
- [x] Refactor `MainWindowStatusController` to status/progress only.
- [x] Remove `MainWindowViewModel` log drawer and `LogText` bindings.
- [x] Remove `ToolkitLogDrawerOpen` / `TrayPreviewLogDrawerOpen` from settings model and projection.
- [x] Update `PerformanceLogScope` to `ILogger`-only output.
- [x] Add structured file logger provider (`application.log.jsonl`).

Tests:

- [x] Update affected tests to remove `LogText` and drawer state assertions.
- [x] Update test constructors to no longer pass `UiLogSink`.

Logging:

- [x] Keep timing events via `PerformanceLogScope` on `ILogger` only.
- [x] Ensure no UI text log persistence remains in production code.

Acceptance:

- [x] No production references to `IUiLogSink` / `UiLogSink`.
- [x] No production `LogText` / log drawer state fields in `MainWindowViewModel`.
- [x] Build and impacted tests pass on current branch.

### WS2: Key Interaction And Flow Logging Coverage

Implementation:

- [x] Add unified event taxonomy constants (`LogEvents`, `StartupLogEvents`).
- [x] Add startup pre-logger buffer and post-init flush (`StartupLogBuffer` + `AppStartupTelemetry.BindLogger`).
- [x] Add UI interaction/shortcut logs in key views (`WorkspaceView`, `TrayLikePreviewSurfaceView`, `MainShellView`, `ConfirmationDialogWindow`).
- [x] Add page switch logs in `MainShellViewModel`, `NavigationService`, and cross-page mark logs in `TrayDependenciesLauncher`.
- [x] Add command entry logs for main commands (browse/run/export/clear cache/launch game).
- [x] Add structured tray export batch/item/stage/snapshot/rollback logs.
- [x] Add texture compress sub-stage logs (`decode/resize/encode`) in pipeline and service layers.

Tests:

- [x] Add test logger capture utility (`TestLoggerProvider`).
- [x] Add page switch event assertion in `MainShellViewModelTests`.
- [x] Keep affected interaction/engine/texture test suites green after logging changes.

Logging:

- [x] Startup milestones are flushed into structured `ILogger` after logger is ready.
- [x] Key command and navigation paths emit `ui.command.*` / `ui.page.switch.*` events.
- [x] Tray export emits batch/item/stage/snapshot-blocked/rollback structured events.
- [x] Texture pipeline emits `decode/resize/encode` stage events.

Acceptance:

- [x] Key UI interactions and shortcuts are visible in structured logs.
- [x] Navigation flow can be reconstructed from `ui.page.switch.*` events.
- [x] Startup path from `process.main.enter` to `first_content_visible` is retained in file logs.
- [x] No business behavior changes introduced by logging-only implementation.

## 3. Verification Matrix

| Workstream | Verification | Expected Result |
| --- | --- | --- |
| WS1 | `dotnet build SimsDesktopTools.sln` | Build succeeds after UI log removal and logger unification. |
| WS1 | Impacted tests (`MainWindowViewModelInteractionTests`, `MainShellViewModelTests`, `MainWindowSettingsProjectionTests`, `TrayPreviewWorkspaceViewModelTests`) | Tests pass with status/progress assertions and updated constructors. |
| WS1 | `rg -n "IUiLogSink|UiLogSink|LogText|IsToolkitLogDrawerOpen|IsTrayPreviewLogDrawerOpen" src` | Matches only docs/history text, no production code references. |
| WS2 | `dotnet build SimsDesktopTools.sln` | Build succeeds with WS2 logging instrumentation and startup buffer changes. |
| WS2 | `dotnet test src/SimsModDesktop.Tests/SimsModDesktop.Tests.csproj --filter "FullyQualifiedName~MainShellViewModelTests|FullyQualifiedName~MainWindowViewModelInteractionTests|FullyQualifiedName~TrayDependencyEngineTests|FullyQualifiedName~TextureProcessingPipelineTests"` | Targeted suites pass with new structured logging coverage. |
| WS2 | Manual cold-start + key action run | `startup.*`, `ui.*`, `trayexport.*`, `texture.compress.*` events visible in structured log file. |

## 4. Final Sign-off

- [x] WS1 fully implemented and verified.
- [x] Build and impacted tests pass.
- [x] Engineering conventions updated to ILogger-only policy.
- [x] WS2 key logging coverage implemented and verified.

## 5. Verification Log (WS1)

* `dotnet build SimsDesktopTools.sln`
  * Result: passed (`0` errors).
* `dotnet test src/SimsModDesktop.Tests/SimsModDesktop.Tests.csproj --filter "FullyQualifiedName~MainWindowViewModelInteractionTests|FullyQualifiedName~MainShellViewModelTests|FullyQualifiedName~MainWindowSettingsProjectionTests|FullyQualifiedName~TrayPreviewWorkspaceViewModelTests"`
  * Result: passed (`35` passed, `0` failed).
* `rg -n "IUiLogSink|UiLogSink|LogText|IsToolkitLogDrawerOpen|IsTrayPreviewLogDrawerOpen" src`
  * Result: matches only `docs/LoggingCoverageChecklist.md`.

## 6. Verification Log (WS2)

* `dotnet build SimsDesktopTools.sln`
  * Result: passed (`0` errors).
* `dotnet test src/SimsModDesktop.Tests/SimsModDesktop.Tests.csproj --filter "FullyQualifiedName~MainShellViewModelTests|FullyQualifiedName~MainWindowViewModelInteractionTests|FullyQualifiedName~TrayDependencyEngineTests|FullyQualifiedName~TextureProcessingPipelineTests"`
  * Result: passed (`56` passed, `0` failed).
