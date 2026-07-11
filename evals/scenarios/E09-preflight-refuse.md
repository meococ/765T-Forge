# E09 — Preflight refuses issue-set publish

## Setup
- Open a demo sheet DWG with at least one layout.
- Ensure a required titleblock tag (e.g. `DWG_NO`) is empty or missing.

## Call
`forge_recipe_issue_set` with:
- `outputPath` pointing to a temp PDF
- `layouts`: one layout name
- `requiredTitleblockTags`: `["DWG_NO"]`
- `dryRun`: false
- `force`: false
- `overwriteAcknowledged`: true

## Expect
- `Ok=false`
- `Error.Code=issue_set_preflight_failed`
- `Data.preflight.passed=false`
- No PDF written (or previous PDF unchanged)
