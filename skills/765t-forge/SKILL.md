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
   - After drawing or editing, call **`forge_view_capture`** (`fit=true`) and view the PNG to visually inspect proportions, text overlaps, dimension placement, and hatches. Use `forge_undo` if a step needs to be rolled back cleanly.

## Capability awareness

Trust [docs/capability-matrix.md](../../docs/capability-matrix.md) and [docs/cad-authoring-tools.md](../../docs/cad-authoring-tools.md). Prompts: `issue_set_runbook`, `safety_first`.
