# Capability matrix

Honest status of MCP tools. **Default verified AutoCAD host: 2026.** 2017–2025 are plugin build targets when `AUTOCAD_<year>_ROOT` points at that install; they are not smoke-tested, and plugin binaries are not interchangeable across years. A DLL year that does not match the host, or an `ACADVER` value that cannot be read, returns `autocad_version_unsupported` before the tool runs. 2017–2018 build as `net46` (documented CLR 4.6; JSON via Newtonsoft.Json because System.Text.Json 8 cannot target `net46`). Smoke lab runs on AutoCAD 2026 only. Civil 3D, full SSM **write**, and geometry megakits are out of scope.

| Status | Meaning |
|--------|---------|
| **implemented** | Documented happy path works in current code |
| **partial** | Present but limited (e.g. import still command-queued) |
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
| `forge_plot_to_pdf` | implemented | Configurable device/paper/CTB·STB/area; overwrite ack. Probes `%PDF-` and page count; probe failure is `Ok=false` / `plot_probe_failed` |
| `forge_plot_publish` | implemented | DSD + `Publisher.PublishDsd` on 2017–2026; **PublishReceipt** + PDF probe; preflight gate |
| `forge_qa_*` (verify/check/audit/readback) | implemented | |
| `forge_qa_readback_after_timeout` | implemented | Timeout recovery protocol — never retry write blind |
| `forge_qa_preflight` | implemented | Writes a QaReport file (not read-only). `passed=false` is `Ok=false` |
| `forge_audit_summarize` | implemented | Server-side last-N audit summary (no raw args) |
| `forge_issue_set_validate` | implemented | IssueSetContract layouts↔drawingNos↔rev |
| `forge_sheet_inventory_import` | implemented | CSV → contract JSON. Writes `outputContractPath` when set (no DST/SSM). Not idempotent when `contractId` is omitted |
| `forge_issue_set_diff` | implemented | Diff two PublishReceipt artifacts |
| `forge_recipe_issue_set` | implemented | Normalize → fill → gate → publish |
| `forge_pack_and_go` | implemented | Host+xrefs+styles + `manifest.json` |
| `forge_batch_run` | implemented | AccoreConsole queue + **resume** via `resumeBatchId`. Console year is selectable; default 2026; missing exe is `accoreconsole_not_found` before spawn |
| `forge_batch_status` | implemented | Load saved batch resume state |

## Registry / standards / viewport / profiles

| Tool | Status | Notes |
|------|--------|-------|
| `forge_registry_load` / `_lookup` | implemented | Fail-closed drawing numbers for titleblock tags |
| `forge_pack_load` / `_status` | implemented | Declarative project standards pack (v2 plot bindings) |
| `forge_system_tool_profile` | implemented | Profiles `core` / `plot` / `qa` (server-side) |
| `forge_viewport_list` | implemented | Paper-space viewports |
| `forge_viewport_set_layer_freeze` | implemented | VP freeze/thaw by handle |

## Linework (CAD ↔ model QA)

| Tool | Status | Notes |
|------|--------|-------|
| `forge_linework_dump` | implemented | Entities (line/polyline/arc/circle) as ordered vertices; `source=drawing` (live, model space + xref contents) or `source=dumpFile` (pl_dump.txt, server-side — no AutoCAD); xref-aware layer filter |
| `forge_linework_trace` | implemented | Point/handle → owning entity, nearest segment index, context neighbours |
| `forge_linework_topology` | implemented | Shared-vertex nodes, edges, T-junctions, X-crossings, runs, dead ends |
| `forge_linework_coverage` | implemented | CAD-vs-model coverage: missing / partial / extra + uncovered sub-ranges |
| `forge_linework_segments` | implemented | Flat segment rows (stable `seg` id, endpoints, layer, length); `units=meters`; optional transform → `sT`/`eT` |
| `forge_linework_compare` | implemented | Per-item marking `matched`/`partial`/`missing_in_revit`/`extra_off_cad` + nearest counterpart + `distanceM` + matched pairs; `minZ`/`maxZ` model filter; optional SVG overlay via `overlayPath` |
| `forge_linework_transform` | implemented (server-only) | Calibrate/apply CAD↔Revit similarity/affine transform from anchor `pairs`; persists calibration JSON via `transformPath`; warns when residual > 0.5 m |

Layer filtering on all `forge_linework_*`: `layerFilter` patterns match the full layer name **and** the bare tail after the last `$`/`|`, so `A-Drainage-Pipe` matches `XREF$0$A-Drainage-Pipe`. `layerSuffix` forces an explicit EndsWith on the bare name; `layerMatch` = `auto|exact|suffix|prefix|substring` controls plain-pattern semantics (`prefix` is the legacy startswith that drops xref content).

## Executors

| Tool | Status | Notes |
|------|--------|-------|
| `forge_exec_command` / `_lisp` | implemented | Prefer sync `Editor.Command`; else `Ok=false`, `queued=true`, `completed=false` |
| `forge_run_script` | implemented | AccoreConsole; **server-only** SafetyPolicy (ADR 0002). Console year is selectable; default 2026; missing exe is `accoreconsole_not_found` before spawn |
| `forge_exec_dotnet` | implemented | Dual-gated; sync snippets only. AutoCAD 2017–2018 (`net46`) returns `autocad_version_unsupported` because Roslyn targets netstandard2.0 |

## Still planned / deferred

| Item | Notes |
|------|-------|
| Visual viewport screenshot | Competitor parity; only after PDF probes |
| Full SSM **write** | Deferred (GUID/path fragility); read-only CSV inventory is shipped |
| Civil 3D / geometry megakit / remote multi-tenant MCP | Explicitly out of scope |
| netDxf offline path | Optional later — not SoT for plot truth |
| NuGet `McpServer` / registry | After Releases packaging — see docs/packaging-nuget.md |
| MCP Resources / Prompts | implemented (`forge://…`, issue_set_runbook) |
