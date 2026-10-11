# Authoring tools — draw, modify, blocks, sheets (C20–C24)

docs/cad-job-tools.md sibling. Status: **C20–C23 shipped** (this change), C24+ roadmap.
Evidence base: competitor survey 2026-10-11 — puran-water/autocad-mcp (557★), U-C4N/Autocad-MCP
(247 tools), LokmenoWer/best-cad-mcp (plan-execute-verify), thepiruthvirajan/autocad-mcp-server,
plus Blender/Revit/Rhino/FreeCAD/OnShape MCPs. Consensus lessons adopted here: typed per-primitive
tools, handle-first references, batch step-list per call, read-back after every mutation,
dry-run for destructive ops, pinned coordinates, no dialogs, serialized execution.

## Contract (every tool below)

- **Coordinates:** WCS, drawing units, `[x, y]` or `[x, y, z]` (z defaults to 0). Angles in
  **degrees** at the tool boundary (converted to radians internally), CCW positive.
- **Handles:** entities are referenced and returned by AutoCAD handle strings (uppercase hex).
  Creation and mutation results carry the affected handles; `ObjectId`s never cross the boundary.
- **Read-back:** every write returns a `ForgeVerification` whose `ReadBack` holds the created or
  modified facts (handles, geometry echo, counts) — what was drawn, not what was requested.
- **Safety/metadata:** additive-only tools (new entities or table records, never touching existing
  geometry) set `RequiresBackup=false` with the C15-style justification; mutating tools
  (entity ops, block_create, undo) set `RequiresBackup=true` and run under undo marks. All
  authoring tools are `OpenWorld=false`, so SafetyPolicy allow-lists them structurally — params
  are typed values, never command strings. `forge_entity_erase` and bulk mutations take explicit
  handles only; there is no "ALL" path anywhere.
- **Dry-run:** all writes accept `dryRun=true` and return the plan without touching the drawing.
- **No dialogs, no prompts:** pure DB API — nothing sets FILEDIA/CMDDIA-class variables and
  nothing calls `Editor` prompts; results are synchronous inside the command context.
- **No-document state:** all authoring tools need a drawing; on the Start tab they return
  `no_command_context` like every other document tool.

## C20 — forge_draw_* (group `draw`)

| Tool | Params (camelCase) | Returns / read-back | Notable errors |
|---|---|---|---|
| `forge_draw_line` | start, end, layer?, colorIndex? | handle | layer_not_found |
| `forge_draw_polyline` | vertices (≥2), closed?, bulges? (per-vertex, tan(θ/4) semantics), constantWidth?, layer?, colorIndex? | handle | layer_not_found, too_few_vertices |
| `forge_draw_circle` | center, radius (>0), layer?, colorIndex? | handle | invalid_radius, layer_not_found |
| `forge_draw_arc` | center, radius, startAngleDeg, endAngleDeg (CCW, wraps through 0), layer?, colorIndex? | handle | invalid_radius, zero_sweep |
| `forge_draw_text` | position, height (>0), text (single line — CR/LF refused), rotationDeg?, layer?, colorIndex? | handle | illegal_control_character |
| `forge_draw_mtext` | position, height, text (`\P` newline allowed), width? (0 = no wrap), attachmentPoint? (9-point, default topLeft), rotationDeg?, layer?, colorIndex? | handle | invalid_attachment |
| `forge_draw_hatch` | pattern (default `ANSI31`), scale? (default 1), angleDeg?, boundary `{handle}` (closed curve) or `{vertices}` (a closed polyline is created first), associative? (default true for handle boundaries), layer? | hatch handle (+ boundary handle when created) | boundary_not_closed, boundary_not_found |
| `forge_draw_dimension` | kind: `linear`\|`aligned`\|`radius`\|`diameter`; p1, p2 (linear: the two extension points + dimLinePoint; radius/diameter: center + chordPoint), textOverride? (`<>` keeps measured value), layer? | handle + measured value | unsupported_kind (angular — C24), invalid_points |
| `forge_draw_batch` | steps[] (≤500): `{ tool, …params }` with the shapes above | per-step `ok/handle/error` list, createdHandles[]; runs in one transaction and continues past failed steps | batch_too_large |

All `forge_draw_*` create new entities only: `RequiresBackup=false`, `Idempotent=false`
(two identical calls draw twice — the read-back says so). Layers must already exist
(`forge_layer_create` first); nothing is silently created.

## C21 — forge_entity_* (group `entity`)

| Tool | Params | Notes |
|---|---|---|
| `forge_entity_get` | handle | Read-only: type, layer, color, linetype, bbox, kind geometry summary |
| `forge_entity_list` | window [[x1,y1],[x2,y2]] **or** layer, type?, maxEntities (default 1000, cap 10000) | Read-only spatial/table query — there is deliberately no unbounded "all" |
| `forge_entity_move` | handles[], delta | TransformBy displacement |
| `forge_entity_copy` | handles[], delta, copies? (default 1) | Returns new handles |
| `forge_entity_rotate` | handles[], basePoint, angleDeg | CCW about Z |
| `forge_entity_scale` | handles[], basePoint, factor (>0) | Uniform |
| `forge_entity_mirror` | handles[], p1, p2, eraseSource? (default false) | Axis through p1–p2 |
| `forge_entity_offset` | handle (single curve), signedDistance (negative = toward the curve's closed side), layer? | Uses plane-normal offset for deterministic sides; read-back shows resulting geometry class |
| `forge_entity_erase` | handles[] (≤1000; above 100 requires acknowledgeBulk=true), dryRun | Destructive; explicit handles only |
| `forge_entity_set_props` | handles[], layer?, colorIndex?, linetype?, lineweight? | null = unchanged; layer/linetype must exist |
| `forge_undo` | — | Undoes the last undo group (forge writes are one group each); returns honestly what AutoCAD reported |

Mutating entity tools: `Destructive=true`, `RequiresBackup=true` (DWG copy before write, same
as every other Forge write). `forge_entity_get` / `forge_entity_list`: `ReadOnly=true`.

## C22 — forge_block_* (group `block`)

| Tool | Params | Notes |
|---|---|---|
| `forge_block_create` | name (unique, no `<>/"":;?*|\=`), basePoint, handles[] (geometry **moved** into the definition: cloned, transformed by −basePoint, originals erased in the same transaction), attributeDefs? [{tag (uppercase), prompt?, height, position, invisible?}] | Destructive (source entities are consumed); read-back lists definition handle + attribute tags |
| `forge_block_insert` | name, position, scale? (uniform, default 1), rotationDeg?, layer?, attributes? {tag: value} | Values are set **after** `SetAttributesFromBlock` (that call resets them — verified against forum reports); read-back echoes the attributes |
| `forge_block_list` | — | Read-only: definitions with handle, attribute tags, insert count |

## C23 — layers & layouts (groups `layer`, `layout`)

| Tool | Params | Notes |
|---|---|---|
| `forge_layer_create` | name, colorIndex? (default 7), linetype? (must already exist), plottable? (default true) | Idempotent — an existing layer returns `alreadyExists=true`; additive, no backup |
| `forge_layout_create` | name | Idempotent refusal with `layout_not_unique` when taken; additive |

## C24+ — roadmap (designed, not shipped)

Visual QA (`forge_view_capture` — PrintWindow works on background windows, plus
geometry-health blocks per rhinomcp PR#36), `forge_measure_batch` (true measured area/length —
never shoelace from vertices), `forge_doc_new` (from template), `forge_layer_set`
(on/off/freeze/lock/current), `forge_viewport_add`, dimension styles (`forge_style_dim_*`),
rectangular/polar array, trim/fillet/chamfer (selection semantics need a plan), `forge_draw_table`,
`forge_draw_polyline_from_csv`, DXF export, capability declaration grown out of
`forge_system_tool_profile`.

## Implementation notes (API cookbook — 2025/2026 verified)

- Model-space append: open BTR `ForWrite` → `SetDatabaseDefaults()` → `AppendEntity` →
  `tr.AddNewlyCreatedDBObject(ent, true)`; the DB owns the entity afterwards — never dispose it.
- Polyline: LWPolyline vertices are 2D on the entity plane (`Elevation`/`Normal`); bulge = tan(θ/4)
  on the segment starting at that vertex, positive CCW.
- Hatch: append + add first, `SetHatchPattern` **before** pattern scale/angle, `Assoc=true`
  before `AppendLoop` (ObjectId loops only), then `EvaluateHatch(true)` or it renders blank.
- Dimensions: pass style ObjectId in the constructor; `TextPosition` overrides placement,
  `DimensionText="<>"" keeps the measurement; wrong-looking dims = fixed-height text style or
  DIMTXT too small for the drawing scale.
- Blocks: entities cannot be re-parented — append clones transformed by −basePoint, erase the
  originals last, all in one transaction; `SetAttributesFromBlock` overwrites attribute values,
  so set values only after it.
- TransformBy needs `OpenMode.ForWrite` (UpgradeOpen); radians inside, degrees at the boundary;
  `GetOffsetCurves` sign conventions are unreliable — read back and report what happened.
- Undo marks: the dispatcher already groups every non-read-only typed write (`WithUndoMark`);
  `forge_undo` pops exactly one group.
