# Adding a New Action

This project now follows a fixed workflow for adding actions.

## Required touch points
1. Add a new action input model in `Application/Requests`.
2. Add a validator in `Application/Validation` and register it in `App.axaml.cs` DI.
3. Add CLI mapping in `Application/Cli/SimsCliArgumentBuilder.cs`.
4. Add panel UI + panel ViewModel and wire it into `MainWindowViewModel` + `MainWindow.axaml`.

## Optional touch points
- Add tray-preview style paging logic only if the action is client-side preview.
- Add settings fields to `Models/AppSettings.cs` when persistence is needed.

## Required tests
- Validator test: required fields, path checks, range checks.
- CLI mapping test: switches/arguments for the new action.
- If output parsing logic is touched, add parser tests.

## Acceptance checklist
- `dotnet build` passes with 0 warnings and 0 errors.
- `dotnet test` passes.
- Manual smoke run logs include `[start]`, `[action]`, `[exit]`.
