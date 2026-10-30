# 765T-Forge Agent Skill

Use this skill when an agent drives AutoCAD through **765T-Forge** for metro/AEC drawing-production (issue-set inspect → fix → fill → gate → publish → verify).

## Hard rules

- Prefer **typed tools** (`forge_<group>_<action>`) before generic executors.
- Always call `forge_system_health` first (and again after AutoCAD restart / NETLOAD). Confirm `Data.pipe` matches `FORGE_PIPE_NAME`. On the AutoCAD Start tab (no open drawing) health/version/`forge_doc_list_open` still answer and all other tools return `no_command_context` — open a drawing first.
- Use `dryRun=true` before write, publish, script, or executor calls.
- **Never invent drawing numbers** — call `forge_registry_load` / `forge_registry_lookup` when a register exists; otherwise read from the drawing or ask the human.
- Load `forge_pack_load` for project standards when available; preflight merges pack rules.
- If a tool returns a safety denial, do **not** retry via another executor; switch to a typed scoped tool or ask for a narrower target.
- Treat `forge_exec_dotnet` as disabled unless the human explicitly enabled unsafe ops for this session (`FORGE_ENABLE_UNSAFE_OPS` + `unsafeAcknowledged`).
- Never use executors for broad selections such as `ERASE ALL` or `ERASE *` (harmless `ZOOM *` / layer filters are OK).
- `forge_exec_lisp` returns the evaluated value synchronously (`result`, `resultText`, `resultType`; a caught LISP error comes back as `lisp_error` with the message). Only `mode=queued_fallback` means it was queued and nothing was returned.
- If `forge_exec_command` / `forge_exec_lisp` returns `queued=true` or `completed=false`, **do not** chain writes — read back first.
- On `plugin_request_timeout` (the plugin watchdog answers; the pipe keeps serving), call **`forge_qa_readback_after_timeout`** — never retry the write blind; `plugin_request_stale` means nothing executed.
- Prefer `forge_issue_set_validate` when an IssueSetContract / sheet inventory exists; do not invent the sheet set.

## Profiles

Call `forge_system_tool_profile` (`core` | `plot` | `qa` | `linework` | `authoring`) to shrink the tool surface for the current task. MCP resources: `forge://profiles/{name}`.

## Inspect before write / publish

1. `forge_doc_list_layouts`
2. `forge_xref_list` or `forge_qa_check_xrefs`
3. `forge_layer_list` / `forge_layer_state_list`
4. `forge_block_list_attributes`
5. **`forge_qa_preflight`** before any real publish; do not force past a failed gate unless the human explicitly requests `force`.

Prefer **`forge_recipe_issue_set`** for full issue-set runs.

## Metro hot path

1. Health + `forge_registry_load` / `forge_pack_load` when project files exist.
2. List layouts; choose the target sheet set.
3. Check xrefs; `forge_xref_normalize_relative` / repath / reload.
4. Restore layer state (`forge_layer_state_restore`) — dry-run first.
5. Fill titleblock via `forge_block_campaign` / `forge_block_set_attr` (registry values only).
6. Page setup apply (prefer sync import/apply).
7. Preflight → plot/publish with overwrite ack when needed.
8. Verify again (`forge_qa_*`).

### Publish honesty

- Prefer `forge_system_capabilities` before choosing device/paper/CTB·STB.
- Require `overwriteAcknowledged=true` when replacing an existing PDF/pack folder.
- **Do not claim PDFs were published** unless `verification.passed` / receipt PDF probe passed and the output file exists. See `docs/capability-matrix.md`.
- After publish, keep the **PublishReceipt** artifact path for audit / `forge_issue_set_diff`.

## Safety expectations

Every write should produce an audit record and, when a DWG path is available, a backup. On `plugin_request_timeout` (the plugin watchdog answers; the pipe keeps serving), call `forge_qa_readback_after_timeout` before any retry — `plugin_request_stale` means nothing executed. Use `forge_audit_summarize` for a redacted session overview.

## Reading the drawing back (do not draw blind)

- Done with a scratch drawing? **`forge_doc_close`** (`name`/`path`) with `discardAcknowledged=true` to drop it, or `save=true` to save it to its own file first. It refuses the last open drawing (`forge_doc_new` first) and unsaved changes without one of the two flags; `ok=true` means AutoCAD no longer lists it.
- Several drawings open? Call `forge_doc_list_open`, then **`forge_doc_activate`** (`name` = exact file name, or `path` = full path) and check `data.active` before working. `ambiguous_document` → pass the full path; `doc_activate_unconfirmed` → read `forge_doc_list_open` again before touching either drawing. The per-call `document` argument still works for single calls.
- Content inside blocks is invisible to plain reads. Use **`forge_text_list`** (TEXT / MTEXT / ATTRIB / ATTDEF / MLEADER, nested blocks included by default, world position/height/rotation, `effectiveLayer`, `insertPath`) to read room names, tags and titleblock values, and pass **`includeBlocks=true`** to `forge_entity_list` / `forge_linework_*` when the geometry lives in blocks (furniture, doors, unit blocks). Check `blockTraversal` for what was skipped (depth limit, bbox, xref, cycle) and raise `blockDepth` if needed.
- `forge_linework_dump` defaults to a pipe-oriented layer filter; for architecture/structure pass `layerFilter="*"` (or the exact layers).
- `forge_view_capture window=[[x1,y1],[x2,y2]]` zooms to a world box before the screenshot (`padding`, `cropToDrawing=true` to drop the ribbon/palettes); the result's `view.worldBox` says what you are looking at.
- `forge_plot_to_pdf` never opens a PDF viewer (it plots through a Forge-owned no-viewer PC3 clone). To make the drawing fill the sheet use `plotArea=extents` or `plotArea=window` + `window`, with `scale=fit` (default). `looksTiny` is measured from the PDF itself: with `plotArea=extents` + `scale=fit` a tiny or empty PDF fails (`plot_content_tiny` / `plot_content_empty`); otherwise `looksTiny=true` or a warning means re-plot — do not ship a drawing sitting small in a corner. `looksTiny=null` means the PDF could not be measured: check it by eye. `dryRun=true` shows the plan (device, media, area, rotation, fit).
- `forge_run_script` accepts `.lsp` directly (a loader `.scr` is generated) and returns readable stdout. It does not save the DWG for you, and QSAVE/SAVEAS inside the script are refused (`deny_save_wblock_overwrite`) — use it for headless checks/reports; persist edits with live tools + `forge_doc_save`.

## Fast drawing production (speed wave)

The slow part of a drawing session is round trips and payload, not AutoCAD. Prefer one call that does a whole job:
- **Bring geometry in with `forge_doc_import`**, never by opening a DXF and copy/pasting: `mode=block` (one block + one INSERT, `replaceExisting=true` to update a background you imported before), `merge` (exploded into model space) or `xref`. Units follow INSUNITS (`matchUnits`); rename source layers on the way in with `layerMap` (`{"A-*":"M-*"}` maps A-WALL to M-WALL; exact keys win) or `layerPrefix`. Run `dryRun=true` first on an unknown file — it lists counts by type/layer, units and the layer plan.
- **Load the office standards once with `forge_standards_import`** (`templatePath` = office DWT): text styles, dim styles, mleader styles, layers, linetypes, table styles, page setups. `onConflict=skip` (default) keeps what the drawing has; `overwrite` restyles existing objects.
- **Place equipment/tags with `forge_block_insert_bulk`** (≤2000 items, attributes per item, optional MLEADER tag per item, missing blocks from `libraryPath`). It is all-or-nothing: fix every listed error and resend.
- **`forge_draw_batch` for linework and annotation**: `atomic=true` for large batches (validate everything, then one transaction) and `resultMode=summary` once you no longer need per-step handles (a 1000-step `full` result is ~87 KB of JSON; `summary` is <1 KB). Annotation keys: `textStyle`, `backgroundMask=true` (+ `maskScale`) on mtext/leader text that sits over linework, `leaderStyle`, `contentBlock`+`attributes` for a tag-bubble MLEADER, `dimStyle` on dimensions.
- **Check presentation with `forge_qa_annotation_clash` before any PNG review** (`plotScale` = sheet scale, e.g. 100): it lists overlapping text, linework through text, tags in door swings, text on walls and text below the minimum plotted height, with handles and world points. Fix the findings (move the text, add `backgroundMask`, change height) and re-run until `passed=true`; then use `forge_view_capture` for a final visual check. Door/wall detection is by layer/block name (`doorLayers`, `doorBlocks`, `wallLayers` wildcards) — set them when the drawing does not use `*DOOR*` / `*WALL*` names.
- **Set up sheets with `forge_plot_preset_apply`** (`layouts=["*"]`, paper, `plotStyle`, `plotArea`, `scale`, optional `setupName`) instead of one `forge_layout_page_setup_apply` per layout; then `forge_plot_to_pdf` / `forge_plot_publish`.

## Linework (CAD ↔ Revit model QA)

- `forge_linework_dump` / `_segments` extract linework from the live drawing (`source=drawing`, model space + xref contents transformed to host space) or a `pl_dump.txt` file (`source=dumpFile` — runs server-side, no AutoCAD needed).
- Layer names may be xref-prefixed (`XREF$0$Layer`). `layerFilter` is prefix-aware, but prefer **`layerSuffix`** (EndsWith on the bare layer name) when targeting a specific layer; never assume `prefix` matching is safe — it silently drops xref content.
- `forge_linework_transform` calibrates/persists/applies the CAD→Revit transform (anchor `pairs` → similarity or affine fit → `transformPath` file). Re-check `maxResidualM`; > 0.5 m means the anchors do not describe one transform.
- `forge_linework_compare` marks every CAD segment and every Revit pipe segment (`modelSegments`/`modelSegmentsPath`) as `matched` / `partial` / `missing_in_revit` / `extra_off_cad` with distance; `overlayPath` writes a color-coded SVG for human review.

## AI CAD Drawing & Authoring (C20–C30: Understand → Plan → Execute → Verify)

When asked to draw or modify CAD geometry from scratch or on an existing drawing:
1. **Understand existing drawings & standards first**:
   - On an existing or unfamiliar DWG, call **`forge_drawing_summary`** first for a 1-call X-Ray (units, extents, entity counts by type/layer, blocks, styles, xrefs), and **`forge_entity_search_text`** to locate room names, grid labels, callouts, or table cells (with MText formatting automatically stripped).
   - Load linetypes (`forge_linetype_load`, e.g., `name="DASHED"` or `names=["DASHED","CENTER","HIDDEN"]`), create text/dimension styles (`forge_style_text_create` with TrueType `fontName="Arial"` for Vietnamese or `.shx`, `forge_style_dim_create`; pass `updateExisting=true` to modify an existing style), and create semantic layers (`forge_layer_create`) before drawing entities.
2. **Use high-level AEC generators, primitives & `forge_draw_batch`**:
   - For architectural/structural plans, start with **`forge_draw_grid`** (draws axis lines, grid bubbles, bay + overall dimensions, and returns the `"A-1": [x,y]` intersection map) and **`forge_draw_wall`** (draws mitered double-line walls with `center`/`left`/`right` justification, slices out door/window `openings` with jamb caps, returns opening `center` + `angleDeg` for block placement, and optionally hatches wall loops).
   - Prefer `forge_draw_rectangle` (`p1`+`p2` or `center`+`width`+`height`, `filletRadius`, `chamferDistance1/2`, `rotationDeg`), `forge_draw_polygon` (`inscribed=true/false`), `forge_draw_ellipse` (`majorAxis` or `majorRadius`+`rotationDeg`), `forge_draw_spline`, `forge_draw_revcloud` (always outward lobes), `forge_draw_leader` (`points`, `text`, `hasArrowHead`), and `forge_draw_table` (`rows`, `title`, `hasTitleRow`) instead of manually calculating raw lines.
   - Use **`forge_draw_batch`** (up to 1000 steps in one call across all 15 step types: `line`, `polyline`, `rectangle`, `polygon`, `circle`, `arc`, `ellipse`, `spline`, `revcloud`, `text`, `mtext`, `hatch`, `dimension`, `leader`, `block_insert`; each step isolated in a nested transaction) to build complete details, floor plans, or mechanical parts in one call.
3. **Precision CAD editing, OSNAP & geometric intelligence**:
   - Never eyeball coordinates on existing geometry: call **`forge_entity_snap`** (`point`, `radius`, `fromPoint`, `snapTypes`: `endpoint`, `midpoint`, `center`, `quadrant`, `insertion`, `nearest`, `perpendicular`, `intersection`) for exact OSNAP coordinates, **`forge_entity_intersect`** (`none` / `extendFirst` / `extendSecond` / `extendBoth`) for exact 3D curve intersections, and **`forge_entity_get`** / **`forge_measure_batch`** to read exact vertices, bulges, areas, and dimensions.
   - Use pure DB API editing tools: `forge_entity_fillet` (two lines with optional `pickPoint1`/`pickPoint2`, or all straight corners of a polyline; `radius=0` for sharp corner trim/extend), `forge_entity_chamfer`, `forge_entity_trim` (`targetHandle`, `cuttingHandles`, `pickPoint`), `forge_entity_extend` (`targetHandle`, `boundaryHandles`, `endToExtend`: `end`|`start`|`both`|`auto`|`near`), `forge_entity_join`, `forge_entity_offset`, `forge_entity_array`, and `forge_entity_explode` (BURST-style attribute preservation on blocks).
4. **Visual & metric self-verification**:
   - After drawing or editing, run **`forge_qa_annotation_clash`** (with the sheet `plotScale`) and fix its findings first; then call **`forge_view_capture`** (`fit=true` or `window`) and view the PNG to inspect proportions, dimension placement and hatches. Use `forge_undo` if a step needs to be rolled back cleanly.

## Capability awareness

Trust [docs/capability-matrix.md](../../docs/capability-matrix.md) and [docs/cad-authoring-tools.md](../../docs/cad-authoring-tools.md). Prompts: `issue_set_runbook`, `safety_first`.
