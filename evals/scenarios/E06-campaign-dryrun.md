# E06 — Titleblock campaign dry-run

## Setup
- Use values from `evals/fixtures/drawing-registry.json` only.

## Call
`forge_block_campaign` with `dryRun=true` and Unicode title attributes from the registry.

## Expect
- Diffs include Unicode values
- No inventing of `DWG_NO` outside registry (after `forge_registry_load`)
