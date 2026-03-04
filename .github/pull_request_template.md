## Summary

- Describe the user-facing or engineering change.

## Scope

- State which layer(s) are touched: `Desktop Host`, `Presentation`, `Application`, `Infrastructure`, `Feature Engine`, `Tests`, `Docs`.

## Structure Check

- [ ] New code is placed in the most natural project, not just the easiest one to reference.
- [ ] Directory and namespace match.
- [ ] No new reverse dependency was introduced.
- [ ] No implementation type was made `public` only to work around wrong placement.
- [ ] Host-only lifecycle code remains in `src/SimsModDesktop`.
- [ ] Presentation-only flow code remains in `src/SimsModDesktop.Presentation`.

## Logging / Diagnostics

- [ ] Host lifecycle diagnostics stay in `src/SimsModDesktop/Diagnostics`.
- [ ] Presentation flow timing/logging helpers stay in `src/SimsModDesktop.Presentation/Diagnostics`.
- [ ] High-frequency paths use summary/timing logs instead of per-item log spam.

## Tests

- [ ] Relevant tests were added or updated.
- [ ] Constructor signature changes were reflected in test helpers/manual `new`.
- [ ] I ran targeted tests for the changed area.

## Docs

- [ ] I reviewed `src/SimsModDesktop/docs/EngineeringConventions.md`.
- [ ] I updated docs/checklists if this PR changes structural expectations.
