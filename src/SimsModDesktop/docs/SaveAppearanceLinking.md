# Save Appearance Linking (Phase 1)

## Entry Point

- Application service: `ILoadSaveWithAppearanceLinksService`
- Core read API: `ITS4SimAppearanceService.BuildSnapshotAsync(savePath, gameRoot, modsRoot, ct)`

## Snapshot Shape

- `Ts4SimAppearanceSnapshot.Sims`: per-sim appearance data
- `Ts4SimAppearanceSnapshot.MorphGraphSummary`: SMOD/Sculpt links + referenced resource health
- `Ts4SimAppearanceSnapshot.ResourceStats`: total/resolved/missing/parse-failure counters
- `Ts4SimAppearanceSnapshot.Issues`: structured warnings/errors

## Structured Issue Codes

- `RESOURCE_NOT_FOUND`: resource key could not be resolved
- `RESOURCE_READ_FAILED`: package entry exists but payload read failed
- `PARSER_FAILED`: payload read succeeded but parser failed
- `MORPH_PAYLOAD_INVALID`: protobuf morph payload in save was invalid
- `MORPH_GRAPH_PARSE_FAILED`: SMOD/Sculpt graph parsing produced error details
- `PACKAGE_ENUMERATION_FAILED`: package enumeration under roots failed
- `SPECIES_LIMITATION`: non-human species may be partially supported in this phase

## Notes

- This pipeline is read-only.
- No UI binding is required in Phase 1.
- Missing resources and parser failures are reported as issues and do not terminate the snapshot build.
