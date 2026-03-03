# Adding a New Action

This project now follows a module-registry workflow for adding actions.

## Required touch points
1. Add a new action input model in `Application/Requests`.
2. Add a validator in `Application/Validation`. The shell composition now pulls validators through `Composition/ServiceCollectionExtensions.cs` via `AddSimsDesktopShell()` and the layered `Application` registration.
3. Add a CLI mapper in `Application/Cli/*CliArgumentMapper.cs`. The shell composition now resolves mappers through `AddSimsDesktopShell()` and the layered `Application` registration.
4. Register an execution strategy (`ActionExecutionStrategy<TInput>`) in `Application/ServiceRegistration/ApplicationServiceRegistration.cs`.
5. Add an action module in `Application/Modules`, expose `SupportedActionPatchKeys`, and register it through `AddSimsDesktopShell()`'s legacy shell view-model/module wiring.
6. Add or extend a state contract in `Application/Modules/ActionModuleStateContracts.cs`.
7. Add panel UI + panel ViewModel and implement the corresponding state contract interface.

## Optional touch points
- Add client-only execution plan (`ModuleExecutionKind.Client`) if the action is not CLI-backed.
- Add settings fields to `Models/AppSettings.cs` when persistence is needed.
- Extend module `TryApplyActionPatch` support if action patching behavior is required.

## Required tests
- Validator test: required fields, path checks, range checks.
- CLI mapping test: switches/arguments for the new action mapper.
- Module plan test: `TryBuildPlan` success/failure paths.
- If output parsing logic is touched, add parser tests.

## Acceptance checklist
- `dotnet build` passes with 0 warnings and 0 errors.
- `dotnet test` passes.
- Manual smoke run logs include `[start]`, `[action]`, `[exit]`.
