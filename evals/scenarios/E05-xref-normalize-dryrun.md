# E05 — Relative xref normalize dry-run

## Call
`forge_xref_normalize_relative` with `dryRun=true`

## Expect
- `Ok=true`
- Diffs show absolute→relative candidates without mutating paths
