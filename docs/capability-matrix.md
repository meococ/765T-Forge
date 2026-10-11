# Capability matrix

Honest status of MCP tools. AutoCAD targets: **2025 and 2026** through the typed year table `build/AutoCadYears.props` (`build-plugin.ps1 -Year`; 2026 is the default reference root — 2025 is qualified live on the dev machine, 2026 is not qualified live there). Civil 3D and full SSM **write** are out of scope; geometry authoring ships as typed tools (C20–C23, [docs/cad-authoring-tools.md](cad-authoring-tools.md)).

| Status | Meaning |
|--------|---------|
| **implemented** | Documented happy path works in current code |
| **partial** | Present but limited (e.g. import still command-queued) |
| **planned** | Not shipped |

## System

| Tool | Status | Notes |
|------|--------|-------|
| `forge_system_health` | implemented | Also answers with no open document (Start tab), alongside `forge_system_version` / `forge_doc_list_open`; other tools fail `no_command_context` there |
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

## Authoring (C20–C23)

| Tool | Status | Notes |
|------|--------|-------|
| `forge_draw_line` / `_polyline` / `_circle` / `_arc` | implemented | Typed WCS geometry; handle + entity read-back; polyline supports bulges/width/closed |
| `forge_draw_text` / `_mtext` | implemented | DBText single-line (CR/LF refused); MText converts newlines to `\P`, 9-point attachment |
| `forge_draw_hatch` | implemented | Associative hatch on a closed-curve handle or a freshly drawn boundary polyline; pattern/scale/angle |
| `forge_draw_dimension` | implemented | linear / aligned / radius / diameter on the current dimstyle; angular is C24 |
| `forge_draw_batch` | implemented | ≤500 steps in one call/transaction, per-step results, continues past failures |
| `forge_entity_get` / `_list` | implemented | Handle read; list requires window or layer — no select-all |
| `forge_entity_move` / `_copy` / `_rotate` / `_scale` / `_mirror` / `_offset` | implemented | Explicit handles, `TransformBy`; plane-normal offset for deterministic sides; backup + undo mark |
| `forge_entity_erase` | implemented | Explicit handles only (≤1000; >100 needs `acknowledgeBulk`), dry-run |
| `forge_entity_set_props` | implemented | Layer/color/linetype/lineweight by handle |
| `forge_undo` | implemented | One Forge write = one undo group; pops exactly one |
| `forge_block_create` / `_insert` / `_list` | implemented | Create consumes source entities (clone→transform→erase, one transaction); insert applies attribute values after binding |
| `forge_layer_create` / `forge_layout_create` | implemented | Idempotent layer create; layout refuses duplicates |

Authoring contract ([docs/cad-authoring-tools.md](cad-authoring-tools.md)): WCS + drawing units, degrees at the boundary, handles as the object currency, verification read-back on every write, dry-run everywhere, pure DB API (no dialogs). Live AutoCAD smoke for this wave is pending.

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
| `forge_plot_to_pdf` | implemented | Configurable device/paper/CTB·STB/area; overwrite ack |
| `forge_plot_publish` | implemented | DSD + `Publisher.PublishDsd`; **PublishReceipt** + PDF probe; preflight gate |
| `forge_qa_*` (verify/check/audit/readback) | implemented | |
| `forge_qa_readback_after_timeout` | implemented | Recovery for `plugin_request_timeout` (plugin watchdog) — never retry write blind; pipe stays up |
| `forge_qa_preflight` | implemented | QaReport + pack v2 + foreground plot + issue-set contract findings |
| `forge_audit_summarize` | implemented | Server-side last-N audit summary (no raw args) |
| `forge_issue_set_validate` | implemented | IssueSetContract layouts↔drawingNos↔rev |
| `forge_sheet_inventory_import` | implemented | Read-only CSV → contract (no DST/SSM write) |
| `forge_issue_set_diff` | implemented | Diff two PublishReceipt artifacts |
| `forge_recipe_issue_set` | implemented | Normalize → fill → gate → publish |
| `forge_pack_and_go` | implemented | Host+xrefs+styles + `manifest.json` |
| `forge_batch_run` | implemented | AccoreConsole queue + **resume** via `resumeBatchId` |
| `forge_batch_status` | implemented | Load saved batch resume state |

## Conversion

| Tool | Status | Notes |
|------|--------|-------|
| `forge_dgn_import` | partial | DGN **V8** → new DWG via `-DGNIMPORT` in a fresh temp document; V7 refused by header sniff; overwrite ack + destination backup; model-space entity count verification. Logic unit-tested — **live AutoCAD smoke pending** (C15), incl. temp-document context behaviour |

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
| MCP Resources / Prompts | implemented (`forge://…`, issue_set_runbook) |
