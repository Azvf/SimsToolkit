# Adding a New Action

This project now follows a module-registry workflow for adding actions.

## Required touch points
1. Add a new action input model in `Application/Requests`.
2. Add a validator in `Application/Validation` and register it in `Composition/ServiceCollectionExtensions.cs` (`AddSimsDesktopExecution`).
3. Add a CLI mapper in `Application/Cli/*CliArgumentMapper.cs` and register it in `Composition/ServiceCollectionExtensions.cs` (`AddSimsDesktopExecution`).
4. Register an execution strategy (`ActionExecutionStrategy<TInput>`) for the action in `Composition/ServiceCollectionExtensions.cs` (`AddSimsDesktopExecution`).
5. Add an action module in `Application/Modules` and register it in `Composition/ServiceCollectionExtensions.cs` (`AddSimsDesktopModules`).
6. Add panel UI + panel ViewModel and bind through the module implementation.

## Optional touch points
- Add client-only execution plan (`ModuleExecutionKind.Client`) if the action is not CLI-backed.
- Add settings fields to `Models/AppSettings.cs` when persistence is needed.
- Add preset patch support in module `TryApplyActionPatch` for quick templates.

## Required tests
- Validator test: required fields, path checks, range checks.
- CLI mapping test: switches/arguments for the new action mapper.
- Module plan test: `TryBuildPlan` success/failure paths.
- If output parsing logic is touched, add parser tests.

## Acceptance checklist
- `dotnet build` passes with 0 warnings and 0 errors.
- `dotnet test` passes.
- Manual smoke run logs include `[start]`, `[action]`, `[exit]`.
