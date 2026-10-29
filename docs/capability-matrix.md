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
| `forge_doc_activate` | implemented | Switch the active drawing among open ones by exact file name or full path (no suffix match; `ambiguous_document` fails closed); runs in application context via the Idle marshal and reads the active document back (`doc_activate_unconfirmed` when AutoCAD has not switched within ~2 s). **Live AutoCAD smoke pending** |
| `forge_xref_list` / `_reload` / `_repath` | implemented | |
| `forge_xref_normalize_relative` | implemented | Absolute → host-relative + optional reload |
| `forge_layer_list` / `_state_list` | implemented | |
| `forge_layer_state_restore` | implemented | Sync via `LayerStateManager` |

## Authoring, Precision Editing, Spatial Intelligence & AEC Generators (C20–C30)

| Tool | Status | Notes |
|------|--------|-------|
| `forge_draw_line` / `_polyline` / `_circle` / `_arc` | implemented | Typed WCS geometry; handle + entity read-back; polyline supports bulges/width/closed |
| `forge_draw_rectangle` / `_polygon` / `_ellipse` / `_spline` / `_revcloud` | implemented | High-level shapes (C25): rectangle with corner fillet/chamfer/rotation; inscribed/circumscribed regular N-gon; full/elliptical arc (`majorAxis` or `majorRadius`+`rotationDeg`); NURBS spline (periodic when closed, optional tangents); outward-lobe revision cloud regardless of CW/CCW winding. Pure math unit-tested — **live AutoCAD smoke pending** |
| `forge_draw_text` / `_mtext` / `_leader` / `_table` | implemented | DBText single-line (CR/LF refused); MText 9-point attachment; MLeader callouts (`points`, `hasArrowHead`, `textHeight`); native AutoCAD `Table` grids (`title`, `hasTitleRow`, unmerged data row 0, 10,000-cell cap) — **live AutoCAD smoke pending** for `_leader`/`_table` |
| `forge_draw_hatch` | implemented | Associative hatch on a closed-curve handle or a freshly drawn boundary polyline; `PatternScale`/`PatternAngle` set before `SetHatchPattern` |
| `forge_draw_dimension` | implemented | `linear` (rotated), `aligned`, `radius`, `diameter` (`DiametricDimension` across full chord `2*p1-p2`), `angular` (`Point3AngularDimension` with `vertex`, `p1`, `p2`, `dimLinePoint`), `ordinate` (X/Y datum), with `dimStyle` and `dimScale` overrides + `RecomputeDimensionBlock(true)` |
| `forge_draw_batch` | implemented | ≤1000 steps in one call across all 15 primitive/annotation types (`line`, `polyline`, `rectangle`, `polygon`, `circle`, `arc`, `ellipse`, `spline`, `revcloud`, `text`, `mtext`, `hatch`, `dimension`, `leader`, `block_insert`); each step runs in a nested transaction so failed steps never leave partial geometry (`batch_all_failed` if all fail) |
| `forge_drawing_summary` / `forge_entity_search_text` / `forge_entity_snap` | implemented | Spatial & semantic CAD intelligence (C29, `CadSpatialMath` unit-tested, **live AutoCAD smoke pending**): 1-call DWG X-Ray summary (units, extents, entity counts by type/layer, blocks, styles, xrefs); deep text search across `DBText`/`MText` (formatting stripped)/block attributes/dimensions/`MLeader`/`Table` cells; AI OSNAP engine (`endpoint`, `midpoint`, `center`, `quadrant`, `insertion`, `nearest`, `perpendicular`, `intersection`) |
| `forge_draw_grid` / `forge_draw_wall` | implemented | High-level AEC/structural generators (C30, `CadSpatialMath` unit-tested, **live AutoCAD smoke pending**): structural column grid (axes, bubbles, bay + overall dimensions, and named intersection map `"A-1": [x,y]`); mitered double-line walls (`center`/`left`/`right` justification, continuous corner turns, door/window openings with jamb caps + returned opening centers/angles, optional associative hatch) |
| `forge_entity_get` / `_list` / `_intersect` | implemented | `forge_entity_list includeBlocks=true` (+ `blockDepth`, default 8 / max 32) also lists entities nested in INSERTs with world bboxes, `effectiveLayer`, `depth`, `insertPath`/`blockPath`, and `blockTraversal` skip counters. Fault-isolated `{entity, kind, kindError?}` read-back (C28: per-vertex polyline widths up to 1000 + `truncated`, `Polyline2d/3d`, `Region`, `Xline/Ray`, dynamic block `effectiveName` + attributes, `Hatch` area, `Table` cells); spatial/layer query; exact 3D intersections via `IntersectWith` |
| `forge_entity_move` / `_copy` / `_rotate` / `_scale` / `_mirror` / `_offset` | implemented | Explicit handles, `TransformBy`; plane-normal offset for deterministic sides; backup + undo mark |
| `forge_entity_fillet` / `_chamfer` / `_trim` / `_extend` / `_join` / `_explode` | implemented | Precision CAD editing (C26, `CadGeometryMath` + `CadEditMath` unit-tested, **live AutoCAD smoke pending**): line-line fillet/chamfer with `pickPoint1`/`pickPoint2` (`ambiguous_side` on unpicked X-crossing) and polyline fillet preserving per-vertex widths (`filletedCorners`/`skippedCorners`); multi-piece curve trim (`insufficient_cuts` on closed curves with <2 cuts, in-place handle preservation for 1 surviving `Line`/`Arc`); strictly-beyond-endpoint extend (`Line`/`Arc`/`Polyline`); owner-space join (`nothing_joined` if 0 connect); BURST-style explode preserving visible block attributes as `DBText`/`MText` |
| `forge_entity_erase` | implemented | Explicit handles only (≤1000; >100 needs `acknowledgeBulk`), dry-run |
| `forge_entity_set_props` | implemented | Layer/color/linetype/lineweight by handle |
| `forge_undo` | implemented | One Forge write = one undo group; pops exactly one via `_.U` |
| `forge_block_create` / `_insert` / `_list` | implemented | Create consumes source entities (clone→transform→erase, one transaction); insert applies attribute values after binding |
| `forge_layer_create` / `forge_layout_create` | implemented | Idempotent layer create; layout refuses duplicates |
| `forge_linetype_load` / `forge_style_text_create` / `forge_style_dim_create` | implemented | Standards & styles (C27, **live AutoCAD smoke pending**): load linetypes (`name`/`names[]`, `file`, `alreadyLoaded`, `linetype_not_found_in_file`); create/update `TextStyleTableRecord` (TTF `FontDescriptor` default `Arial` for Vietnamese, or `.ttf`/`.shx`) and `DimStyleTableRecord` (`style_exists` unless `updateExisting=true`, partial field updates) |
| `forge_view_capture` | implemented | PrintWindow PNG of the AutoCAD window (backgrounded OK, not minimized); `fit` zooms extents first, `window=[[x1,y1],[x2,y2]]` (+ `padding`) zooms to a world box; `cropToDrawing` crops to the drawing area; returns the view read-back (`worldBox`, center, twist) — the agent's visual read-back. Window zoom/crop **live AutoCAD smoke pending** |
| `forge_text_list` | implemented | Block-aware text read: TEXT / MTEXT / ATTRIB (incl. multiline) / ATTDEF / MLEADER content with layer + effective layer, style, world position/alignment point, world height and rotation, mirrored flag, `insertPath`; filters `window` (world), `layer` (wildcards, xref tails), `contains`/`regex`, `types`, `blockDepth`, `maxResults` (≤5000). **Live AutoCAD smoke pending** |
| `forge_measure_batch` | implemented | Engine-measured length/area/distance/bbox — never re-reads vertices |
| `forge_entity_array` | implemented | Rectangular + polar clones by explicit handles; sources untouched |
| `forge_layer_set` / `forge_doc_new` / `forge_doc_export_dxf` | implemented | Layer state (freeze refused on current), template document, 16-digit DXF out |
| `forge_draw_polyline_from_csv` / `forge_viewport_add` | implemented | CSV point lists → one polyline; model-space viewport on a layout tab |

Tool profile `authoring` bundles the whole **61-tool** authoring surface (out of **114** total tools) via `forge_system_tool_profile`.

Authoring contract ([docs/cad-authoring-tools.md](cad-authoring-tools.md)): WCS + drawing units, degrees at the boundary, handles as the object currency, verification read-back on every write, dry-run everywhere, no dialogs.

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
| `forge_plot_to_pdf` | implemented | Never opens a viewer: plots through a Forge-owned PC3 clone with `View_New_File=FALSE` (fails closed with `plot_viewer_suppression_failed`). `plotArea` = `extents` (Model default, and the default whenever the paper differs from the layout's own) / `layout` / `display` / `limits` / `window` (+ `window=[[x1,y1],[x2,y2]]`); `scale=fit` centered by default; orientation honoured against the real media. Read-back: validated plot settings, fit/fill estimate, content-coverage warning (`looksTiny`), PDF probe + sheet size from `/MediaBox`. `dryRun` returns the plan. Overwrite ack. Plot engine path **live AutoCAD smoke pending** |
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
| `forge_system_tool_profile` | implemented | Profiles `core` / `plot` / `qa` / `linework` / `authoring` (server-side) |
| `forge_viewport_list` | implemented | Paper-space viewports |
| `forge_viewport_set_layer_freeze` | implemented | VP freeze/thaw by handle |

## Linework (CAD ↔ model QA)

| Tool | Status | Notes |
|------|--------|-------|
| `forge_linework_dump` | implemented | Entities (line/polyline/arc/circle) as ordered vertices; `source=drawing` (live, model space + xref contents) or `source=dumpFile` (pl_dump.txt, server-side — no AutoCAD); xref-aware layer filter (default filter is pipe-like layers — pass `layerFilter="*"` for architecture). `includeBlocks=true` (+ `blockDepth`) walks nested INSERTs/MINSERTs with transforms applied (world coordinates, layer-0 inherits the INSERT layer, `insertPath`, `blockTraversal` counters); also on `_trace`/`_topology`/`_coverage`/`_segments`. Nested walk **live AutoCAD smoke pending** |
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
| `forge_exec_command` | implemented | Prefer sync `Editor.Command`; else `queued=true, completed=false` |
| `forge_exec_lisp` | implemented | Synchronous evaluation via `acedEvaluateLisp` inside a `vl-catch-all-apply` envelope: returns `result` (JSON), `resultText` (AutoLISP form), `resultType`, and `lisp_error` with the message on a caught error. The queued `SendStringToExecute` path remains only when the export is unavailable (`mode=queued_fallback`). **Live AutoCAD smoke pending** |
| `forge_run_script` | implemented | AccoreConsole; `.scr` or `.lsp` (a `.lsp` runs through a generated loader `.scr` with an escaped `(load "…")` path; the safety scan covers the user's `.lsp` bytes); stdout/stderr decoded from UTF-16 and cleaned (no NUL/control chars); **server-only** SafetyPolicy (ADR 0002) |
| `forge_exec_dotnet` | implemented | Dual-gated; sync snippets only |

## Still planned / deferred

| Item | Notes |
|------|-------|
| Full SSM **write** | Deferred (GUID/path fragility); CSV inventory → contract is shipped |
| Civil 3D / geometry megakit / remote multi-tenant MCP | Explicitly out of scope |
| netDxf offline path | Optional later — not SoT for plot truth |
| NuGet `McpServer` / registry | After Releases packaging — see docs/packaging-nuget.md |
| MCP Resources / Prompts | implemented (`forge://…`, issue_set_runbook) |
