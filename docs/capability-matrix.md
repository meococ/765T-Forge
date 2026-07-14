# Capability matrix

Honest status of MCP tools. AutoCAD target: **2026**. Civil 3D, full SSM **write**, and geometry megakits are out of scope.

| Status | Meaning |
|--------|---------|
| **implemented** | Documented happy path works in current code |
| **partial** | Present but limited (e.g. DSD publish seat-dependent; may fall back) |
| **planned** | Not shipped |

## System

| Tool | Status | Notes |
|------|--------|-------|
| `forge_system_health` | implemented | |
| `forge_system_version` | implemented | Product + envelope versions |
| `forge_system_getvar` / `_setvar` | implemented | |
| `forge_system_capabilities` | implemented | Devices, media, page setups, layouts, layer states, plot styles |

## Document / xref / layer

| Tool | Status | Notes |
|------|--------|-------|
| `forge_doc_*` (list/open/save/layouts) | implemented | SaveAs overwrite ack |
| `forge_xref_list` / `_reload` / `_repath` | implemented | |
| `forge_xref_normalize_relative` | implemented | Absolute → host-relative + optional reload |
| `forge_layer_list` / `_state_list` | implemented | |
| `forge_layer_state_restore` | implemented | Sync via `LayerStateManager` |

## Block / layout

| Tool | Status | Notes |
|------|--------|-------|
| `forge_block_list/get/set_attr` | implemented | |
| `forge_block_campaign` | implemented | Multi-entry dry-run diffs, Unicode |
| `forge_layout_page_setup_import` | implemented | Prefers sync PlotSettings copy from template DWG/DWT; falls back to queued `-PSETUPIN` with `completed=false` |
| `forge_layout_page_setup_apply` | implemented | Sync `PlotSettings` copy onto layout |

## Plot / publish / QA / recipes

| Tool | Status | Notes |
|------|--------|-------|
| `forge_plot_to_pdf` | implemented | Configurable device/paper/CTB·STB/area; overwrite ack; smoke-verified |
| `forge_plot_publish` | partial | DSD + `Publisher.PublishDsd` + **PublishReceipt** (stamps `AuditId`) + PDF probe + preflight gate; on DSD no-output / failure, **falls back** to per-layout `-PLOT` (`fallback=plot_to_pdf`; multi-layout `singlePdf` may be unmerged); forces `BACKGROUNDPLOT`/`BGCOREPUBLISH=0`; `force=true` requires `FORGE_ALLOW_FORCE_PUBLISH` |
| `forge_qa_*` (verify/check/audit/readback) | implemented | |
| `forge_qa_readback_after_timeout` | implemented | Timeout recovery protocol — never retry write blind |
| `forge_qa_preflight` | implemented | QaReport + pack v2 + foreground plot + issue-set contract findings |
| `forge_audit_summarize` | implemented | Server-side last-N audit summary (no raw args) |
| `forge_issue_set_validate` | implemented | IssueSetContract layouts↔drawingNos↔rev |
| `forge_sheet_inventory_import` | implemented | Read-only CSV → contract (no DST/SSM write) |
| `forge_issue_set_diff` | implemented | Diff two PublishReceipt artifacts |
| `forge_recipe_issue_set` | implemented | Normalize → fill → gate → publish |
| `forge_pack_and_go` | implemented | Host+xrefs+styles + `manifest.json` |
| `forge_batch_run` | implemented | AccoreConsole queue + **resume** via `resumeBatchId` |
| `forge_batch_status` | implemented | Load saved batch resume state |

## Registry / standards / viewport / profiles

| Tool | Status | Notes |
|------|--------|-------|
| `forge_registry_load` / `_lookup` | implemented | Fail-closed drawing numbers for titleblock tags |
| `forge_pack_load` / `_status` | implemented | Declarative project standards pack (v2 plot bindings) |
| `forge_system_tool_profile` | implemented | Profiles `core` / `plot` / `qa` (server-side) |
| `forge_viewport_list` | implemented | Paper-space viewports |
| `forge_viewport_set_layer_freeze` | implemented | VP freeze/thaw by handle |

## Exclusive AEC gates (0.2.1+)

| Tool | Status | Notes |
|------|--------|-------|
| `forge_qa_plot_fingerprint` | implemented | Plot env fingerprint + pack lock findings |
| `forge_qa_dependency_closure` | implemented | Nested xref depth walk + pack CTB/STB; optional `failClosed` |
| `forge_qa_dual_source` | implemented | Registry vs live titleblock compare |
| `forge_qa_modal_trap` | implemented | FILEDIA/EXPERT automation readiness |
| `forge_xref_closure` | implemented | Nested BFS depth report (default maxDepth=4); not complete nested SoT |
| `forge_xref_pin_save` / `_verify` | implemented | Pin includes nested nodes when readable |
| `forge_transmittal_seal` | implemented | SHA256/HMAC seal over DWG+xref+PDF |
| `forge_publish_ceremony_check` | implemented | Attested flags + optional AuditId evidence binding (ADR 0004) |
| `forge_cde_gate_evaluate` | implemented | ISO 19650-lite status/rev/naming + optional sidecar |

## Executors

| Tool | Status | Notes |
|------|--------|-------|
| `forge_exec_command` / `_lisp` | implemented | Prefer sync `Editor.Command`; else `queued=true, completed=false` |
| `forge_run_script` | implemented | AccoreConsole; **server-only** SafetyPolicy (ADR 0002) |
| `forge_exec_dotnet` | implemented | Dual-gated; sync snippets only |

## Still planned / deferred

| Item | Notes |
|------|-------|
| Visual viewport screenshot | Competitor parity; only after PDF probes |
| Full SSM **write** | Deferred (GUID/path fragility); read-only CSV inventory is shipped |
| Civil 3D / geometry megakit / remote multi-tenant MCP | Explicitly out of scope |
| netDxf offline path | Optional later — not SoT for plot truth |
| NuGet `McpServer` / registry | After Releases packaging — see docs/packaging-nuget.md |

MCP Resources / Prompts (`forge://…`, `issue_set_runbook`) are **implemented** (not planned).
