# Debug Config Table Module

## Goal
Provide a persistent, runtime-editable configuration table for debug and feature toggles so developers can change behavior without code edits.

## Storage
- Provider: `SharpConfig` (`sharpconfig` NuGet package)
- File: `%AppData%/SimsModDesktop/debug-config.ini`
- Section: `[debug]`
- Shape:
  - `key=value` (currently parsed as bool for toggle options)

## Current Pipeline
1. `ShellSettingsController` defines toggle metadata (key/name/description/default).
2. On startup, controller loads `debug-config.ini` through `IDebugConfigStore` and maps entries to `DebugConfigToggleItemViewModel`.
3. Controller ensures a template exists in `debug-config.ini` (auto-create missing file and missing keys with comments).
4. Settings UI renders the table with current/default values.
5. On persist, controller writes the current toggle values back to `debug-config.ini`.

## UI
- Location: Settings page, section `Debug Config Table`.
- Columns:
  - Option (name, description, key)
  - Default
  - Current (checkbox)
- Utility action:
  - `Reset Debug Config` resets all toggles to defaults.

## Active Toggles
- No active toggles are currently defined in `ShellSettingsController` (`DebugToggleDefinitions = []`).
- The debug config file is still maintained and can be extended when new toggles are introduced.

## Extension Pattern
To add a new toggle:
1. Add a `DebugToggleDefinition` entry in `ShellSettingsController`.
2. Bind the definition to the target runtime flow and read it from `DebugConfigItems`.
3. Consume the property in the target runtime path.
4. No schema migration needed; defaults apply automatically when key is missing.
