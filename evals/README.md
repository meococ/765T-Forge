# Forge eval scenarios (0.2.x baseline — brief “Phase 4”)

Phase labels are historical (build brief). Shipped truth: SemVer + [docs/capability-matrix.md](../docs/capability-matrix.md). Roadmap map: [docs/roadmap.md](../docs/roadmap.md). **0.3.0** trust gates (`FORGE_ALLOW_FORCE_PUBLISH`, ceremony AuditIds, nested xref depth) are covered in unit tests (`TrustHardening030Tests`) — live DWG evals for those are still maintainer smoke-lab. The 0.3.0 safety change (deterministic capability gate for the five free-text executors; no text denylist) is covered by `SafetyPolicyTests`.

These scenarios are designed for MCP Inspector / agent dry-runs against a sample metro sheet DWG.
Fixtures intentionally contain **metadata only** — no customer DWGs in-repo.

No scenario below should invoke a free-text executor: all five are `Unsafe` and require `FORGE_ENABLE_UNSAFE_OPS=true` plus `unsafeAcknowledged=true`, otherwise they return `unsafe_not_acknowledged`.

## Scenarios

| ID | Goal | Tools | Pass criteria |
|---|---|---|---|
| E01 | Health before work | `forge_system_health` | `ok` + pipe up |
| E02 | Capability discovery | `forge_system_capabilities` | devices + pageSetups listed |
| E03 | Layout inventory | `forge_doc_list_layouts` | ≥1 paper layout |
| E04 | Xref gate | `forge_qa_check_xrefs` / `forge_qa_preflight` | missing xref → `passed=false` |
| E05 | Relative xref normalize dry-run | `forge_xref_normalize_relative` dryRun | diffs show absolute→relative |
| E06 | Titleblock campaign dry-run | `forge_block_campaign` dryRun | Unicode values in diffs |
| E07 | Configurable plot dry-run | `forge_plot_to_pdf` dryRun | device/paper/plotStyle echoed |
| E08 | Publish overwrite acknowledgement | `forge_plot_publish` existing PDF, overwrite=false | `publish_overwrite_not_acknowledged` |
| E09 | Preflight refuse publish | `forge_recipe_issue_set` with empty required tag | `issue_set_preflight_failed` |
| E10 | Pack-and-go dry-run | `forge_pack_and_go` dryRun | manifest plan includes host+xrefs |

## Fixture notes

See `fixtures/sample-sheet-register.json` for a synthetic sheet register agents can use in campaigns.
Do not invent drawing numbers — always take them from the register or human input.
