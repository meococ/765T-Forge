# Forge eval scenarios (Phase 4)

These scenarios are designed for MCP Inspector / agent dry-runs against a sample metro sheet DWG.
Fixtures intentionally contain **metadata only** — no customer DWGs in-repo.

## Scenarios

| ID | Goal | Tools | Pass criteria |
|---|---|---|---|
| E01 | Health before work | `forge_system_health` | `ok` + pipe up |
| E02 | Capability discovery | `forge_system_capabilities` | devices + pageSetups listed |
| E03 | Layout inventory | `forge_doc_list_layouts` | ≥1 paper layout |
| E04 | Xref gate | `forge_qa_check_xrefs` / `forge_qa_preflight` | missing xref → `passed=false` and `Ok=false` |
| E05 | Relative xref normalize dry-run | `forge_xref_normalize_relative` dryRun | diffs show absolute→relative |
| E06 | Titleblock campaign dry-run | `forge_block_campaign` dryRun | Unicode values in diffs |
| E07 | Configurable plot dry-run | `forge_plot_to_pdf` dryRun | device/paper/plotStyle echoed |
| E08 | Publish overwrite guard | `forge_plot_publish` existing PDF, overwrite=false | `publish_overwrite_not_acknowledged` |
| E09 | Preflight refuse publish | `forge_recipe_issue_set` with empty required tag | `issue_set_preflight_failed` |
| E10 | Pack-and-go dry-run | `forge_pack_and_go` dryRun | manifest plan includes host+xrefs |

## Fixture notes

See `fixtures/sample-sheet-register.json` for a synthetic sheet register agents can use in campaigns.
Do not invent drawing numbers — always take them from the register or human input.
