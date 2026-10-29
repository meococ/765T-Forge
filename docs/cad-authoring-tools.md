# Authoring tools — draw, modify, blocks, sheets, precision edit, styles (C20–C28)

docs/cad-job-tools.md sibling. Status: **C20–C28 shipped** (**107 total MCP tools**, **54 tools** in the `authoring` profile; pure math + contract sync unit-tested, **live AutoCAD smoke pending** for C25–C28).
Evidence base: competitor survey 2026-10-15 — puran-water/autocad-mcp (557★), U-C4N/Autocad-MCP
(247 tools), LokmenoWer/best-cad-mcp (plan-execute-verify), TopoSpatial-CAD-MCP (topological editing),
bimwright/dwg-mcp, thepiruthvirajan/autocad-mcp-server, plus Blender/Revit/Rhino/FreeCAD/OnShape MCPs.
Consensus lessons adopted here: typed per-primitive tools, high-level shape generators (rectangle with
fillet/chamfer, regular polygons, ellipses, splines, revclouds, leaders, tables), pure analytical DB API
fillet/chamfer/trim/extend/join/explode, handle-first references, batch step-list per call (15 primitive
types in one call with per-step nested transactions), rich geometry read-back after every mutation, dry-run for destructive ops,
pinned coordinates, no dialogs, serialized execution.

## Contract (every tool below)

- **Coordinates:** WCS, drawing units, `[x, y]` or `[x, y, z]` (z defaults to 0). Angles in
  **degrees** at the tool boundary (converted to radians internally), CCW positive.
- **Handles:** entities are referenced and returned by AutoCAD handle strings (uppercase hex).
  Creation and mutation results carry the affected handles; `ObjectId`s never cross the boundary.
- **Read-back:** every write returns a `ForgeVerification` whose `ReadBack` holds the created or
  modified facts read back from the AutoCAD `Database` (handles, geometry echo, counts) — what was drawn, not what was requested.
- **Safety/metadata:** additive-only tools (`forge_draw_*`, `forge_layer_create`, `forge_layout_create`, `forge_linetype_load`) set `RequiresBackup=false` with the C15-style justification; mutating tools
  (`forge_entity_*` writes, `forge_style_text_create`, `forge_style_dim_create`, `forge_block_create`, `forge_undo`) set `Destructive=true, RequiresBackup=true` and run under undo marks. All
  authoring tools are `OpenWorld=false`, so SafetyPolicy allow-lists them structurally — params
  are typed values, never command strings. `forge_entity_erase` and bulk mutations take explicit
  handles only; there is no "ALL" path anywhere.
- **Dry-run:** all writes accept `dryRun=true`, run full parameter/layer/geometry validation, and return the plan without touching the drawing.
- **No dialogs, no prompts:** pure DB API for all C20–C28 geometry/edit/style handlers (`forge_undo` invokes `Editor.Command("_.U")` to pop one undo group).
- **No-document state:** all authoring tools need a drawing; on the Start tab they return
  `no_command_context` like every other document tool.

## C20 & C25 — forge_draw_* (group `draw`)

| Tool | Params (camelCase) | Returns / read-back | Notable errors |
|---|---|---|---|
| `forge_draw_line` | start, end, layer?, colorIndex? | handle | layer_not_found |
| `forge_draw_polyline` | vertices (≥2), closed?, bulges? (per-vertex, tan(θ/4) semantics), constantWidth?, layer?, colorIndex? | handle | layer_not_found, too_few_vertices |
| `forge_draw_rectangle` | (p1 + p2) **or** (center + width + height) — mutually exclusive; filletRadius?, chamferDistance1?, chamferDistance2?, rotationDeg?, constantWidth?, layer?, colorIndex? | handle + vertexCount/closed/length/area | invalid_args, layer_not_found |
| `forge_draw_polygon` | center, sides (3..1024), radius (>0), inscribed? (default true; false = circumscribed), rotationDeg?, constantWidth?, layer?, colorIndex? | handle + sides/inscribed/length/area | invalid_sides, invalid_radius, layer_not_found |
| `forge_draw_circle` | center, radius (>0), layer?, colorIndex? | handle | invalid_radius, layer_not_found |
| `forge_draw_arc` | center, radius, startAngleDeg, endAngleDeg (CCW, wraps through 0), layer?, colorIndex? | handle | invalid_radius, zero_sweep |
| `forge_draw_ellipse` | center, majorAxis? `[dx,dy,(dz)]` **or** (majorRadius (>0) + rotationDeg?), minorRadius? **or** radiusRatio? (0 < ratio ≤ 1, minor/major), startAngleDeg?, endAngleDeg? (parametric angles from major axis), layer?, colorIndex? | handle + majorRadius/minorRadius/radiusRatio/closed | invalid_radius, invalid_ratio, invalid_args, layer_not_found |
| `forge_draw_spline` | fitPoints (≥2; ≥3 when closed), closed? (periodic NURBS without duplicating endpoints), degree? (2..11, default 3), startTangent?, endTangent? (non-zero vectors, open splines), layer?, colorIndex? | handle + degree/fitPointCount/closed/length | too_few_points, invalid_degree, invalid_tangent, layer_not_found |
| `forge_draw_revcloud` | (p1 + p2) **or** vertices (≥3), arcLength? (default = perimeter/24), bulgeMagnitude? in `(0,1]` (default 0.52), constantWidth?, layer?, colorIndex? | handle + arcCount/length (always outward lobes regardless of CW/CCW winding) | invalid_args, too_few_vertices, layer_not_found |
| `forge_draw_text` | position, height (>0), text (single line — CR/LF refused), rotationDeg?, layer?, colorIndex? | handle | illegal_control_character |
| `forge_draw_mtext` | position, height, text (`\P` newline allowed), width? (0 = no wrap), attachmentPoint? (9-point, default topLeft), rotationDeg?, layer?, colorIndex? | handle | invalid_attachment |
| `forge_draw_leader` | points (≥2: `[arrowTip, ..., landing]`), text? (optional MText callout), textHeight? (default 2.5), arrowSize?, hasArrowHead? (default true; false sets ArrowSize=0), layer?, colorIndex? | MLeader handle + vertexCount/leaderCount/hasText | too_few_points, invalid_height, layer_not_found |
| `forge_draw_table` | position, rows (`string[][]`, max 10,000 cells), title? (prepends a merged title row above `rows`), hasTitleRow? (when true without `title`, merges `rows[0]`; when false, unmerges row 0), rowHeight? (default 8), columnWidth? (default 30), columnWidths[]?, textHeight? (default 2.5), layer?, colorIndex? | Table handle + rows/columns/cell summary from DB | invalid_args, table_too_large, invalid_width, layer_not_found |
| `forge_draw_hatch` | pattern (default `ANSI31`), scale? (default 1), angleDeg?, boundary `{handle}` (closed curve) or `{vertices}` (a closed polyline is created first), layer? | hatch handle (+ boundaryHandle when created) | boundary_not_closed, boundary_not_found |
| `forge_draw_dimension` | kind (required): `linear`\|`aligned`\|`radius`\|`diameter`\|`angular`\|`ordinate`; p1, p2?, vertex? (required for `angular` via `Point3AngularDimension`), dimLinePoint?, rotationDeg? (rotated linear dim), useXAxis? (ordinate X vs Y), textOverride?, dimStyle? (must exist), dimScale? (>0), layer?, colorIndex? | handle + measurement + dimStyle from DB | unsupported_kind, dimstyle_not_found, invalid_points, invalid_scale |
| `forge_draw_batch` | steps[] (≤1000): `{ tool, …params }` supporting all 15 primitives (`line`, `polyline`, `rectangle`, `polygon`, `circle`, `arc`, `ellipse`, `spline`, `revcloud`, `text`, `mtext`, `hatch`, `dimension`, `leader`, `block_insert`) | per-step nested transaction (`ok/handle/error`), createdHandles[]; failed steps roll back cleanly without leaving partial entities | batch_too_large, batch_all_failed |

All `forge_draw_*` create new entities only: `RequiresBackup=false`, `Idempotent=false`
(two identical calls draw twice — the read-back says so). Layers and dimension styles must already exist
(`forge_layer_create` / `forge_style_dim_create` first); nothing is silently created.

## C21, C26 & C28 — forge_entity_* (group `entity`)

| Tool | Params | Notes |
|---|---|---|
| `forge_entity_get` | handle | Read-only (C28 upgraded): consistent `{entity, kind, kindError?}` envelope with common summary (type, layer, color, linetype, lineweight, bbox) plus rich kind geometry (`Line`, `Circle`, `Arc`, `Ellipse`, `Spline`, `Polyline` with per-vertex `startWidth`/`endWidth` up to 1000 vertices + `truncated`, `Polyline2d`/`3d`, `DBText`, `MText`, `BlockReference` + attributes + `effectiveName`, `Dimension` measurement, `Hatch` area, `Table` cells up to 50×20, `MLeader`, `Region` area, `Xline`/`Ray`) |
| `forge_entity_list` | window [[x1,y1],[x2,y2]] **or** layer, type?, maxEntities (default 1000, cap 10000) | Read-only spatial/table query — there is deliberately no unbounded "all" |
| `forge_entity_intersect` | handle1, handle2, extendMode? (`none`\|`extendFirst`\|`extendSecond`\|`extendBoth`, plus `first`/`second`/`both` aliases) | Read-only (C28): exact 3D intersection points between two entities via `Entity.IntersectWith`; unknown mode returns `invalid_extend_mode` |
| `forge_entity_move` | handles[], delta | TransformBy displacement |
| `forge_entity_copy` | handles[], delta, copies? (default 1) | Returns new handles |
| `forge_entity_rotate` | handles[], basePoint, angleDeg | CCW about Z |
| `forge_entity_scale` | handles[], basePoint, factor (>0) | Uniform |
| `forge_entity_mirror` | handles[], p1, p2, eraseSource? (default false) | Axis through p1–p2 |
| `forge_entity_offset` | handle (single curve), distance (signed), layer? | Uses plane-normal offset for deterministic sides; read-back shows resulting geometry class |
| `forge_entity_fillet` | radius (≥0; 0 = sharp corner), handle1 / handle, handle2? (omit for single Polyline), pickPoint1?, pickPoint2?, layer? | C26 pure analytical math (`CadGeometryMath` + `CadEditMath`): fillets two coplanar `Line`s (requiring `pickPoint1`/`pickPoint2` with `ambiguous_side` when a line straddles the corner) or fillets all straight corners of a `Polyline` (preserving per-vertex widths and reporting `filletedCorners` + `skippedCorners`; fails with `nothing_filleted` if 0 corners qualify) |
| `forge_entity_chamfer` | handle1, handle2, distance1 (>0), distance2? (>0, defaults to distance1), pickPoint1?, pickPoint2?, layer? | C26 pure analytical math: trims/extends two coplanar `Line` entities and inserts the bevel `Line` in the source owner space; returns `ambiguous_side` on unpicked X-crossing |
| `forge_entity_trim` | targetHandle, cuttingHandles[], pickPoint `[x,y]` | C26 pure DB API: splits `targetHandle` at normalized intersections (`CadEditMath.NormalizeCutDistances`; ≥2 cuts required on closed curves or `insufficient_cuts`), removes ONLY the segment closest to `pickPoint`, and preserves all other surviving segments (`handlePreserved=true` when an open `Line`/`Arc` keeps 1 segment in place) |
| `forge_entity_extend` | targetHandle, boundaryHandles[], endToExtend? (`end` default \| `start` \| `both` \| `auto` \| `near`), nearPoint? | C26 pure DB API: extends open `Line`, `Arc`, or straight-ended `Polyline` only to intersections strictly **beyond** the extended endpoint (`CadEditMath.SelectLineExtension` / `SelectArcExtension`), never shortening the curve; fails with `no_boundary_intersection` when none exist |
| `forge_entity_join` | handles[] (≥2, ≤1000), layer? | C26: converts `handles[0]` (if `Line`/`Arc`) to a `Polyline` preserving properties/elevation/arc bulge sign in the source owner space and calls `JoinEntities`; fails with `nothing_joined` if 0 secondary curves connect |
| `forge_entity_explode` | handles[] (≤1000), eraseSource? (default true) | C26: explodes compound entities into their owner space via `Entity.Explode`; converts visible non-constant `BlockReference` `AttributeReference`s to standalone `DBText`/`MText` (BURST-style) and reports per-handle `failed:[{handle,reason}]` |
| `forge_entity_erase` | handles[] (≤1000; above 100 requires acknowledgeBulk=true), dryRun | Destructive; explicit handles only |
| `forge_entity_set_props` | handles[], layer?, colorIndex?, linetype?, lineweight? | null = unchanged; layer/linetype must exist |
| `forge_undo` | — | Undoes the last undo group (`_.U`); returns honestly what AutoCAD reported |

Mutating entity tools check locked layers before upgrading to write (`layer_locked`) and set `Destructive=true`, `RequiresBackup=true`. `forge_entity_get` / `forge_entity_list` / `forge_entity_intersect`: `ReadOnly=true`.

## C22 — forge_block_* (group `block`)

| Tool | Params | Notes |
|---|---|---|
| `forge_block_create` | name (unique, no `<>/"":;?*|\=`), basePoint, handles[] (geometry **moved** into the definition: cloned, transformed by −basePoint, originals erased in the same transaction), attributeDefs? [{tag (uppercase), prompt?, height, position, invisible?}] | Destructive (source entities are consumed); read-back lists definition handle + attribute tags |
| `forge_block_insert` | name, position, scale? (uniform, default 1), rotationDeg?, layer?, attributes? {tag: value} | Values are set **after** `SetAttributesFromBlock` (that call resets them — verified against forum reports); read-back echoes the attributes |
| `forge_block_list` | — | Read-only: definitions with handle, attribute tags, insert count |

## C23 & C27 — layers, layouts, linetypes & styles (groups `layer`, `layout`, `style`)

| Tool | Params | Notes |
|---|---|---|
| `forge_layer_create` | name, colorIndex? (default 7), linetype? (must already exist), plottable? (default true) | Idempotent — an existing layer returns `alreadyExists=true`; additive, no backup |
| `forge_layout_create` | name | Idempotent refusal with `layout_not_unique` when taken; additive |
| `forge_linetype_load` | name? and/or names[]? (e.g. `DASHED`, `CENTER`, `HIDDEN`, `PHANTOM`), file? (default `acadiso.lin` with `acad.lin` fallback via `FindFile`) | C27: idempotent (`alreadyLoaded=true` without reloading); errors: `linetype_not_found_in_file`, `lin_file_not_found` |
| `forge_style_text_create` | name, fontName? (TrueType family default `Arial` via `FontDescriptor` — supports Vietnamese — or `.ttf`/`.shx` file), bigFont?, textSize? (default 0 = variable height), widthFactor? (default 1.0), obliqueAngleDeg? (-85..85), makeCurrent?, updateExisting? (default false) | C27 (`Destructive=true, RequiresBackup=true`): refuses existing style with `style_exists` unless `updateExisting=true`; updates modify ONLY supplied fields |
| `forge_style_dim_create` | name, textHeight? (DIMTXT), arrowSize? (DIMASZ), dimScale? (DIMSCALE), extensionOffset? (DIMEXO), extensionExtend? (DIMEXE), textGap? (DIMGAP), precision? (DIMDEC 0..8), linearScaleFactor? (DIMLFAC), textStyle? (DIMTXSTY, must exist or `textstyle_not_found`), colorIndex? (DIMCLRD/E/T), makeCurrent?, updateExisting? (default false) | C27 (`Destructive=true, RequiresBackup=true`): refuses existing style with `style_exists` unless `updateExisting=true`; calls `SetDimstyleData` when `makeCurrent=true` |

## C24 — visual QA, measurement, document lifecycle (shipped)

| Tool | Params | Notes |
|---|---|---|
| `forge_view_capture` | outputPath, fit? (zoom extents first), overwriteAcknowledged? | The agent's eyes: PrintWindow renders AutoCAD's main window to a PNG even while backgrounded (not minimized); the client reads the PNG to visually verify work. Read-only on the drawing; `System.Drawing.Common` |
| `forge_measure_batch` | handles[]?, p1+p2? | True engine measurement — per-handle length/area/type/layer, pointDistance, bbox union. Never re-read vertices (shoelace math silently loses area on curved segments) |
| `forge_entity_array` | handles[], kind: `rectangular` (rows, columns, rowSpacing, colSpacing) \| `polar` (center, count 2..5000, angleTotalDeg default 360, rotateItems default true) | Clones + TransformBy placements, sources untouched; destructive + backup like copy |
| `forge_layer_set` | name, isOn?, isFrozen? (refused on the current layer), isLocked?, isPlottable?, colorIndex?, makeCurrent? | Idempotent state write with full state read-back |
| `forge_doc_new` | templatePath? | New document (blank or DWT/DWG); it becomes active |
| `forge_doc_export_dxf` | outputPath, overwriteAcknowledged? | `DxfOut(path, 16, false)` — 16-digit precision; exports the in-memory state; destination backup + overwrite ack |
| `forge_draw_polyline_from_csv` | csvPath, hasHeader?, delimiter?, xColumn?, yColumn?, closed?, constantWidth?, layer?, colorIndex? | Survey/centerline point lists from CSV without token cost; reports dropped lines |
| `forge_viewport_add` | layout, center, width, height | Rectangular model-space viewport on a paper-space layout, read-back carries the viewport number |

## C29 — Spatial & Semantic Intelligence (`forge_drawing_summary`, `forge_entity_search_text`, `forge_entity_snap`)

| Tool | Params | Notes |
|---|---|---|
| `forge_drawing_summary` | layout? (`Model` default) | Read-only 1-call DWG X-Ray: units (`INSUNITS`, `LUNITS`, `MEASUREMENT`), extents, current layer/styles, entity counts by type and layer, block definitions + insert counts + attribute tags, layouts, text/dim styles, loaded linetypes, and Xrefs |
| `forge_entity_search_text` | query, layer?, window? `[[minX,minY],[maxX,maxY]]`, layout?, caseSensitive?, exactMatch?, useRegex?, maxResults? (default 200, cap 1000) | Read-only semantic text search across `DBText`, `MText` (formatting stripped via `CadSpatialMath.StripMTextFormatting`), `BlockReference` attributes, `Dimension` measurement/override text, `MLeader` text, and `Table` cells |
| `forge_entity_snap` | point `[x,y]`, radius? (default 10), fromPoint? `[x,y]`, snapTypes? (`endpoint`, `midpoint`, `center`, `quadrant`, `insertion`, `nearest`, `perpendicular`, `intersection`), layer?, maxResults? (default 20, cap 100) | Read-only AI OSNAP engine (`CadSpatialMath.RankSnapCandidates`): computes exact geometric snaps within aperture `radius` and ranks exact snaps ahead of fallback `nearest` |

## C30 — High-Level AEC/Structural Generators (`forge_draw_grid`, `forge_draw_wall`)

| Tool | Params | Notes |
|---|---|---|
| `forge_draw_grid` | origin? `[x,y]`, xPositions? / xSpacings?, xLabels?, yPositions? / ySpacings?, yLabels?, extensionLength?, bubbleRadius?, bubbleSides? (`start`\|`end`\|`both`\|`none`), includeDimensions? (default true), layer?, gridLayer?, bubbleLayer?, dimLayer?, gridLinetype?, dimStyle?, colorIndex? | Draws complete column grid (axis lines, grid bubble circles + centered `DBText`, bay + overall `RotatedDimension`s) and returns named intersection dictionary (`"A-1": [x,y]`) for column/wall placement |
| `forge_draw_wall` | vertices `[[x,y],...]`, thickness (>0), justification? (`center`\|`left`\|`right`), closed?, openings? `[{segmentIndex, offsetFromStart, width}]`, hatchPattern?, hatchScale?, hatchAngleDeg?, layer?, hatchLayer?, colorIndex? | Draws mitered double-line walls with continuous corner turns, slices out door/window openings with jamb end-caps, optionally hatches wall loops, and returns opening `center` + `angleDeg` for door/window block insertion |

Tool profile `authoring` (`forge_system_tool_profile name=authoring`) lists the whole C20–C30
surface plus the reliability-wave tools (`forge_doc_activate`, `forge_text_list`) — **61 authoring tools** —
for agents that want one allowlist instead of all **114 tools**.

## Implementation notes (API cookbook — 2025/2026 verified)

- Entity creation & defaults: call `entity.SetDatabaseDefaults(db)` **immediately** after `new`, configure style/layer/color/overrides, then append via `AppendExistingToModelSpace(db, tr, entity)` (or `AppendToOwner` for edits) so `SetDatabaseDefaults` never wipes caller overrides. Dispose any non-DB-resident entity (`entity.ObjectId.IsNull`) on exception paths.
- Polyline: LWPolyline vertices are 2D on the entity plane (`Elevation`/`Normal`); bulge = tan(θ/4) on the segment starting at that vertex, positive CCW. Read widths per vertex (`GetStartWidthAt`/`GetEndWidthAt`), never `ConstantWidth` (which throws `eInvalidInput` on variable-width polylines).
- Hatch: `SetDatabaseDefaults(db)` → append to BTR → set `PatternScale` and `PatternAngle` **before** `SetHatchPattern` → `Associative = true` → `AppendLoop` → `EvaluateHatch(true)`.
- Dimensions: construct (`RotatedDimension`, `AlignedDimension`, `RadialDimension`, `DiametricDimension(chordPt, farChordPt, ...)`, `Point3AngularDimension(vertex, p1, p2, arcPt, ...)`, `OrdinateDimension`), call `SetDatabaseDefaults(db)`, set `DimensionStyle = styleId`, apply `Dimscale`/`DimensionText`, append, and call `RecomputeDimensionBlock(true)`.
- Blocks: entities cannot be re-parented — append clones transformed by −basePoint, erase the originals last, all in one transaction; `SetAttributesFromBlock` overwrites attribute values, so set values only after it.
- Undo marks: the dispatcher already groups every non-read-only typed write (`WithUndoMark`); `forge_undo` pops exactly one group.

