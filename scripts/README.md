# Scripts Layout

`SimsToolkit` keeps a single public entry script at repository root:

- `sims-mod-cli.ps1`: unified CLI entry (`organize/flatten/normalize/merge/finddup/trayprobe`)

Internal scripts are grouped by concern:

- `scripts/fileops/`
  - `organize-sims-zips.ps1`
  - `flatten-mods-into-top-folder.ps1`
  - `normalize-name-only-folders.ps1`
  - `merge-into-single-folder.ps1`
  - `find-md5-duplicates.ps1`
- `scripts/analysis/`
  - `tray-mod-dependency-probe.ps1`

Shared PowerShell modules stay in:

- `modules/`

Notes:

- GUI/Desktop and normal CLI usage should call `sims-mod-cli.ps1`.
- Sub-scripts remain executable directly for debugging and local experiments.
