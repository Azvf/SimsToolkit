# Adding a New Action

## 1. Current Extension Model

After the refactor, a toolkit action is added through:

1. **Request model + validator** in `Application`.
2. **Planning** in `ToolkitActionPlanner`.
3. **Execution dispatch** in `ExecutionCoordinator` (or dedicated coordinator/service).
4. **Presentation state + panel** in `Presentation`.
5. **DI registration** in layered service registration.

Legacy strategy/router artifacts are no longer used.

---

## 2. Required Touch Points

1. Add enum value in `Application/Models/SimsAction.cs`.
2. Add input model in `Application/Requests`.
3. Add validator in `Application/Validation` and register it in `Application/ServiceRegistration/ApplicationServiceRegistration.cs`.
4. Extend the module state contract in `Application/Modules/ActionModuleStateContracts.cs` only if the action needs UI-owned module state. Keep the contract in `Application`, not in `Presentation`.
5. Add/extend panel view model in `Presentation/ViewModels/Panels` and implement the related `Application` module state contract.
6. Register panel view model and state interface mapping in `Presentation/ServiceRegistration/PresentationServiceRegistration.cs`.
7. Extend `ToolkitActionPlanner`:
   * include action in supported actions list
   * build execution plan from module state
   * include settings load/save mapping if persistence is needed
8. Extend `ExecutionCoordinator` (or a dedicated coordinator) to execute the new request type.
9. Wire UI view in `src/SimsModDesktop/Views/Panels` (if the action has new visual surface).

---

## 3. Optional Touch Points

* Add settings fields in `Application/Models/AppSettings.cs`.
* Add persistence projection updates in settings-related controllers.
* Add result repository/event logging integration if action emits structured rows.
* Add feature-specific storage/service in `Infrastructure`.

---

## 4. Test Requirements

1. Validator tests: required fields, ranges, path checks.
2. Planner tests: build success/failure paths from state to plan.
3. Coordinator tests: action dispatch and output/exit behavior.
4. UI view-model tests: state transitions and command enablement.
5. Architecture tests: ensure no forbidden legacy dependencies are introduced.

---

## 5. Acceptance Checklist

* `dotnet build SimsDesktopTools.sln` passes.
* `dotnet test SimsDesktopTools.sln` passes.
* New action appears in UI and runs end-to-end.
* Settings round-trip works (if action has persisted fields).
