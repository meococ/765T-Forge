# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Product SemVer is independent of Phase labels and MCP protocol/SDK versions.
See [docs/versioning.md](docs/versioning.md).

## [Unreleased]

### Added

- Speed wave (2026-10-29, 119 total MCP tools, 66 in the `authoring` profile) — fewer round trips, smaller payloads, presentation QA without screenshots:
  - `forge_doc_import`: DXF/DWG into the active drawing in one call — `mode=block` (one definition + one INSERT; `replaceExisting` redefines in place so existing INSERTs update), `merge` (`Database.Insert` with a position/scale/rotation/basePoint matrix) or `xref` (attach/overlay; a DXF is first saved as DWG to `xrefSavePath`). Side-database read (`DxfIn`/`ReadDwgFile`), INSUNITS matching (`matchUnits`), `layerMap` (exact names then wildcards, a `*` in the target takes what the key's `*` matched so `{"A-*":"M-*"}` maps A-WALL to M-WALL, swaps and merges handled) and `layerPrefix` applied in the side database (layers 0/Defpoints never renamed), counts by type and layer, `layersCreated`/`layersReused`, world extents, optional handles (≤5000), nested-block conflict warnings, `timingsMs`; `dryRun` returns the inventory and plan. Destructive + backup (block redefinition); failures roll back through the UNDO group
  - `forge_qa_annotation_clash` (read-only): one block-aware walk collects world text boxes (TEXT, ATTRIB, constant ATTDEF, MTEXT, MLEADER text/block content, dimension text) and linework, then reports `text_overlap`, `text_crossing` (background-masked MTEXT/MLEADER and DIMTFILL text exempt unless `reportMaskedCrossings`), `door_swing` (tag inside a door-swing sector from `doorLayers`/`doorBlocks`), `wall_straddle` (`wallLayers`) and `min_height` (plotted height at `plotScale` vs `minTextHeightMm`). Filters: `window`, `layout`, `textLayers`/`curveLayers`/`excludeLayers`; hidden layers skipped; text and lines of the same small block are not compared. Findings carry handles, layers, texts, world point, overlap ratio and insert paths. Geometry is `Forge.Shared.AnnotationClash` (SAT, Sutherland–Hodgman, Cyrus–Beck, grid index)
  - `forge_standards_import`: linetypes, text styles, layers, dim styles, mleader styles, table styles and page setups from a template DWG/DWT in dependency order, one `WblockCloneObjects` per kind, `onConflict=skip|overwrite`, name `filter`, protected names (0, Defpoints, ByLayer/ByBlock/Continuous) never overwritten, per-kind change report read back; style-tool safety class (destructive, backup, idempotent)
  - `forge_block_insert_bulk`: up to 2000 INSERTs in one transaction with per-item position/rotation/scale/layer/colour/attributes and an optional MLEADER tag (`leader.points`, `text`, `style`, `backgroundMask`); missing blocks loaded by name from `libraryPath` (one read, one clone; `redefine` replaces drawing definitions). All-or-nothing validation (numbers, blocks, layers, mleader styles, drawing-registry/standards-pack attribute rules); unknown attribute tags are warnings
  - `forge_plot_preset_apply`: one call applies device (default the no-viewer PDF clone), paper, CTB, area (layout/extents/display/limits/window), scale/fit, centring, units and orientation to one or many layouts (`"*"` = every paper layout), validated per layout with `PlotSettingsValidator` and all-or-nothing; optional named page setup (`setupName`) saved or replaced; per-layout read-back
  - Pure, unit-tested helpers in `Forge.Shared`: `DrawingImport`, `WildcardSet`, `AnnotationClash`, `StandardsImport`, `BlockBulkInsert`, `PlotPreset`
- Reliability wave (2026-10-29, 114 total MCP tools, 61 in the `authoring` profile) — agents read the drawing back instead of drawing blind:
  - `forge_doc_activate`: make an open drawing active by exact file name or full path (shared `DocumentMatcher`: no suffix match, `ambiguous_document` fails closed), executed in application context through the `Application.Idle` marshal (also from the Start tab) with an active-document read-back and up to ~2 s of polling; `doc_activate_unconfirmed` when AutoCAD has not switched. Not destructive, no backup, idempotent; in the `core`, `linework` and `authoring` profiles
  - `forge_text_list`: block-aware text read — TEXT, MTEXT, ATTRIB (incl. multiline attributes), ATTDEF, MLEADER content with own + effective layer, style, world insertion/alignment point, world height and rotation, mirrored flag, `depth`/`insertPath`/`blockPath`; filters `window` (world), `layer` (wildcards, xref tails), `contains`/`regex`, `types`, `blockDepth`, `includeXrefs`, `maxResults` (≤5000) with `countsByType` and `blockTraversal`
  - Block-aware traversal: `includeBlocks=true` (+ `blockDepth`, default 8 / max 32) on `forge_linework_dump/_trace/_topology/_coverage/_segments` and `forge_entity_list` walks nested INSERTs and MINSERT instances with transforms applied (world coordinates, arcs/circles re-sampled under non-uniform or mirrored transforms), layer-0 inheritance to the INSERT's layer (`ownLayer` kept), invisible dynamic-block members skipped, cycle guard, bbox pruning in world coordinates, `blockTraversal` counters (`insertsVisited`, `maxDepthReached`, `skippedDepthLimit`/`OutsideBbox`/`Cycle`/`Xref`/`Invisible`, `truncated`). Off by default — existing calls are unchanged
  - `forge_view_capture`: optional `window=[[x1,y1],[x2,y2]]` world box zoom (+ `padding`, default 5%), `cropToDrawing` (crop to the drawing area), and a view read-back (`worldBox`, centre, size, twist) in the result
  - Pure, unit-tested helpers in `Forge.Shared`: `LispEval`/`LispValueConverter`, `Pc3File`, `PlotLayoutMath`, `BlockWalk`/`AffineXf`, `TextRead`, `DocumentMatcher`; `Forge.Server.AccoreScriptPlan`
- Typed AutoCAD year table: `build/AutoCadYears.props` (2025 + 2026), `build-plugin.ps1 -Year` with per-year output and a `ForgeTargetYear` assembly stamp, `install-plugin.ps1 -Year`; the plugin refuses a host of another year with `HOST_YEAR_MISMATCH` until the year pair is proven by a live smoke (`docs/cad-job-tools.md`, cad-connector-1007)
- `forge_dgn_import`: typed DGN V8 → new DWG conversion via `-DGNIMPORT` in a fresh temporary document (source DGN and active drawing never modified); V7 refused by header sniff, target-open and overwrite-ack guards, destination backup, dry-run plan, model-space entity-count verification (`docs/cad-job-tools.md` C15 — live AutoCAD smoke pending)
- Publish-ready Wave 0: Autoloader R25.1 + valid GUIDs; `%APPDATA%` install guidance; NOTICE upstream attribution; `docs/history/build-brief.md`; embedded matrix/safety in server publish; `RELEASE_STATUS.txt` from pack script
- PublishReceipt + PDF probes + actionable preflight findings + foreground plot policy
- IssueSetContract lite, standards pack v2 plot bindings, field/rev gates, timeout read-back protocol
- Differential issue-set receipts, read-only sheet inventory, batch resume metadata
- Plugin-dispatch CI sync; Dependabot; scoped open-world `*` denylist; ADR honesty for headless AccoreConsole path
- Linework QA wave: `forge_linework_segments` (flat segment feed, meters/transformed endpoints), `forge_linework_compare` (per-item CAD↔Revit marking `matched`/`partial`/`missing_in_revit`/`extra_off_cad` + matched pairs + Z filter + SVG overlay), `forge_linework_transform` (server-side similarity/affine calibration from anchor pairs with persisted calibration file and >0.5 m residual warning); explicit `layerSuffix`/`layerMatch` xref-safe layer filtering on all `forge_linework_*` tools
- Authoring wave C20–C23 (`docs/cad-authoring-tools.md`, competitor-evidence design): 25 typed tools so agents can actually draw and modify — `forge_draw_line/_polyline/_circle/_arc/_text/_mtext/_hatch/_dimension` plus `forge_draw_batch` (≤500 shapes in one call/transaction with per-step results), `forge_entity_get/_list/_move/_copy/_rotate/_scale/_mirror/_offset/_erase/_set_props` and `forge_undo`, `forge_block_create/_insert/_list`, `forge_layer_create`, `forge_layout_create`. Contract: WCS + drawing units, degrees at the boundary, AutoCAD handles as the object currency, verification read-back on every write, dry-run everywhere, pure DB API (no dialogs, no prompts). Additive-only tools skip the pre-write DWG copy; entity mutations are destructive with backup + one undo group each; no authoring tool is OpenWorld (typed params, no command strings)
- Authoring wave C24 — the agent's eyes and measure: `forge_view_capture` (PrintWindow PNG of the AutoCAD window, works while backgrounded; `fit` zooms extents first — the MCP client reads the PNG to visually verify), `forge_measure_batch` (engine-measured length/area/point-distance/bbox union, never re-reads vertices), `forge_entity_array` (rectangular/polar clones by handle), `forge_layer_set` (state + makeCurrent, freeze refused on the current layer), `forge_doc_new`, `forge_doc_export_dxf` (16-digit `DxfOut` + destination backup), `forge_draw_polyline_from_csv` (survey point lists without token cost), `forge_viewport_add`; tool profile `authoring` bundles the whole authoring surface (`forge_system_tool_profile name=authoring`)
- Authoring waves C25–C30 (112 total MCP tools, 64 in the `authoring` profile):
  - **C25 Shapes & Annotations**: `forge_draw_rectangle` (chamfer/fillet corners, rotation), `forge_draw_polygon` (inscribed/circumscribed), `forge_draw_ellipse`, `forge_draw_spline` (cubic/degree fit points, start/end tangents, periodic closed), `forge_draw_revcloud` (convex-hull CCW outward arcs), `forge_draw_leader` (`MLeader` with multi-vertex leader lines + `MText`), `forge_draw_table` (styled AutoCAD `Table` with row heights, column widths, merged title row), upgraded `forge_draw_dimension` (`linear`/`aligned`/`angular`/`radius`/`diameter`/`ordinate`/`rotated` + `dimStyle`/`dimScale`), and upgraded `forge_draw_batch` (≤1,000 steps across 15 step types with per-step sub-transactions and drawing-registry/standards-pack attribute authorization on `block_insert`)
  - **C26/C28 Precision CAD Modifiers**: `forge_entity_fillet` (line-line with pick-side disambiguation + polyline corner filleting preserving per-vertex widths), `forge_entity_chamfer`, `forge_entity_trim` (in-place handle preservation when trimming one end of an open line/arc), `forge_entity_extend` (`start`/`end`/`both`/`auto`/`near`), `forge_entity_join`, `forge_entity_explode` (BURST-style `AttributeReference` → `DBText`/`MText` conversion), `forge_entity_intersect`, and rich geometry read-back in `forge_entity_get`
  - **C27 Styles & Linetypes**: `forge_linetype_load`, `forge_style_text_create` (TrueType `FontDescriptor` for Vietnamese diacritics + `.shx` support, `updateExisting` guard), `forge_style_dim_create`
  - **C29 Spatial Awareness & OSNAP**: `forge_drawing_summary` (one-call spatial digest: units, extents, layers/blocks/styles with entity counts, text samples), `forge_entity_search_text` (`DBText`/`MText`/`MLeader`/`Table`/block attributes/dimensions), `forge_entity_snap` (exact `end`/`mid`/`center`/`quad`/`perp`/`nearest`/`insertion` OSNAP resolution in WCS)
  - **C30 AEC Structural & Architectural**: `forge_draw_grid` (orthogonal structural grid axes, labeled bubbles, overall & bay dimensions in one transaction), `forge_draw_wall` (double-line architectural wall from centerline with left/right/center justification, end caps, optional hatch fill)

### Fixed

- `forge_exec_lisp` now returns the evaluated value synchronously. Root cause: the old path passed the whole expression to `Editor.Command` as one token, which AutoCAD always rejected (`eInvalidInput`), so every call fell back to `SendStringToExecute` and returned `queued=true` with no value. The plugin now calls `acedEvaluateLisp` (accore.dll) on the expression wrapped in a `vl-catch-all-apply` envelope and returns `result` (JSON), `resultText` (AutoLISP form), `resultType` and `mode=sync_eval`; a caught LISP error is `lisp_error` with the message; malformed input is refused before evaluation (`lisp_unbalanced`, `lisp_unterminated_string`, …). The queued path remains only when the export cannot be bound (`mode=queued_fallback`, `fallbackReason`). The unsafe two-key gate and the SafetyPolicy text scan are unchanged
- `forge_plot_to_pdf` hang: the PDF drivers' PC3 custom property `View_New_File=TRUE` makes AutoCAD open the PDF after plotting (an OpenWith/viewer window that blocks the plot on machines without a PDF app). Forge now plots through a Forge-owned clone `765T-Forge <device> (no viewer).pc3` in the same Plotters folder with the property forced to `FALSE` (PIA container rewritten with the correct Adler-32/length header), refreshes the device list, and fails closed with `plot_viewer_suppression_failed` instead of plotting with a viewer. `BACKGROUNDPLOT` is forced to 0 for the plot and restored
- `forge_plot_to_pdf` "drawing tiny in a corner": `plotArea=layout` on a bigger forced sheet (the old default media was ISO A1) kept the layout at its own size. The default area is now extents (Model, and whenever the requested paper differs from the layout's own), `scale=fit` is centered, orientation is honoured against the real media, `plotArea=window` takes `window=[[x1,y1],[x2,y2]]`, and every plot reports the validated settings, a fit/fill estimate, a content-coverage estimate (`looksTiny` + warning) and the PDF's real sheet size from `/MediaBox`; `dryRun` returns the whole plan. New `engine=api|command` switch (`command` = `-PLOT` on the no-viewer device)
- `forge_run_script` accepts `.lsp` directly: accoreconsole `/s` only runs `.scr` (given `x.lsp` it looked for `x.lsp.scr`), so the `.lsp` bytes that passed the safety scan are copied to a temp `.lsp` and driven by a generated temp `.scr` that `(load "...")`s it with a forward-slash, quote-escaped path (UTF-8 BOM when the path is not ASCII); other extensions are refused (`unsupported_script_type`). stdout/stderr are decoded (BOM, UTF-16LE, UTF-16LE shifted by one stray byte, UTF-8) and cleaned (NUL/control characters removed, doubled `\r\r\n` collapsed, very long output kept as head + tail); `scriptKind` is reported and both temp files are deleted
- Start-tab wedge: calling any tool on AutoCAD's Start tab (no open document) hung the first request inside `ExecuteInCommandContextAsync` — which never dispatches without a command context — and the single pipe instance then refused every later connection until AutoCAD restart (re-NETLOAD could not recover). The plugin now answers doc-free diagnostics (`forge_system_health`, `forge_system_version`, `forge_doc_list_open`) via an `Application.Idle` marshal and fast-fails document tools with a named `no_command_context` error
- Per-request plugin watchdog: a request AutoCAD never returns is answered `plugin_request_timeout` and the pipe keeps serving instead of wedging permanently; not-yet-started callbacks past the deadline are refused as `plugin_request_stale` so abandoned writes cannot execute late, while already-started work still runs to completion (read-back-after-timeout protocol applies). Accept-loop failures back off 1s instead of tight-retry spinning
- Error suggestions no longer hardcode "AutoCAD 2026"; `FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS` (default 10) makes the pipe connect timeout configurable
- `ForgeEnvironment.DefaultAutoCadRoot()` resolves the newest installed plain "AutoCAD 20xx" directory (Program Files\Autodesk, then fixed-drive roots for non-default installs like `D:\AutoCAD 2025`) instead of always assuming the 2026 path, so headless accoreconsole resolution works on 2025-only machines; `ForgeEnvironment.Get` now expands `%VAR%` environment variables in configured paths
- `scripts/pack-release.ps1` builds the plugin through `build-plugin.ps1 -Year` so release plugin zips carry the `ForgeTargetYear` stamp, and `build-plugin.ps1` restores `$env:AUTOCAD_2026_ROOT` on exit so consecutive multi-year builds in one shell session do not leak paths
- `forge_doc_open` restores FILEDIA in a `finally` (a failed `Documents.Open` used to leave FILEDIA=0)
- `forge_entity_mirror` with `eraseSource=false` now keeps both the original and mirrored clone in the source's owner space (`ModelSpace` or `PaperSpace`) using `Database.DeepCloneObjects` (preserving `BlockReference` `AttributeReference`s and `Polyline2d` vertices) and a vertical reflection `Plane` (`det = -1`) so `MIRRTEXT` and `+Z` normals are honored; `forge_entity_copy`, `forge_entity_array`, and `forge_block_create` likewise use `Database.DeepCloneObjects`; `forge_block_create` no longer double-translates geometry when `basePoint` is non-origin; `forge_block_insert` and `forge_draw_batch` `block_insert` enforce `DrawingRegistryStore` and `StandardsPackStore` attribute rules
- `WithUndoMark` skips non-drawing-mutation tools, pops empty undo groups when a handler returns `Ok=false`, and tracks committed undo depth per `Database`; `forge_view_capture` (`fit=true`) zooms via `Editor.SetCurrentView(ViewTableRecord)` instead of `_.ZOOM _E` so viewport captures never push a command onto AutoCAD's undo stack; `forge_undo` returns `undo_failed` when nothing can be undone
- `DsdWriter` emits `Type=6` (multi-sheet single PDF) / `Type=5` (single-sheet per-layout PDFs), `DWF=` and `OUT=` under `[Target]`, and `[SheetSet Properties]`; `forge_plot_publish` supports `singlePdf=false` (`Type=5`) per-layout overwrite checks and post-publish per-sheet probing, and returns `Ok=false` (`plot_pdf_probe_failed`) when any probe fails; `PdfProbeResult.Probe` verifies `%PDF-` header, `%%EOF` trailer, non-null matching `pageCount` when `expectedPages` is specified, and `notBeforeUtc` freshness
- `LineworkGeometry.TessellateBulge` places the arc center on the proper side of the chord for all `|bulge| < 1` and `|bulge| >= 1`; `SegmentGrid` uses `(long, long)` cell keys (safe at VN2000 millimeter coordinates), passes true segment endpoints `(x1, y1, x2, y2)` from `NearestDist`/`NearestSeg`, and uses exact 1D dominant-axis slice traversal + Liang-Barsky sparse scan (replacing the lossy ray-walk and `padCells=8` clamp); `TransformFitter.FitAffine` centers coordinates and rejects collinear anchor sets; `CadGeometryMath.IntersectInfiniteLines2D` uses local coordinates to avoid precision loss at VN2000 coordinates

### Changed

- `forge_draw_batch` throughput and annotation keys: per-batch id cache for layers, dim/text/multileader styles, blocks and model space (ids only — every step still opens what it writes in its own transaction); `atomic=true` validates every step first and creates everything in one transaction (`batch_invalid`/`batch_aborted` create nothing); `resultMode=full|errors|summary`; `countsByType`, `elapsedMs`, `entitiesPerSecond` in every result; the verification read-back no longer repeats the per-step results (payload for 1000 steps 166 KB → 87 KB in `full`, 0.7 KB in `summary`); entity types come from the new object id's class instead of reopening each entity. New step keys: `textStyle` (text/mtext/leader), `backgroundMask` + `maskScale` (mtext and leader text), `leaderStyle`, `contentBlock` + `attributes` (block-content MLEADER tag bubble at the landing point; unknown tags are warnings)
- `forge_entity_get` read-back now includes `textStyle` for TEXT/MTEXT, `backgroundMask` for MTEXT and MLEADER text, and `contentBlock` for block-content MLEADERs, so a batch's masks, styles and tag bubbles can be checked without a screenshot
- Roadmap SemVer reconciled with capability matrix (registry/packs/resources are 0.2.0 shipped, not 0.3.0 targets)
- Removed `companion-skill/` stub; added `docs/README.md` + `AGENTS.md`
- Maintainer smoke-lab pass recorded (2026-07-11) against live AutoCAD 2026; `scripts/smoke-lab.ps1` helper
- Business failures (`ForgeResult.Ok=false`) now set `CallToolResult.IsError=true` while preserving both a short human summary and the full JSON envelope; `forge_qa_preflight`, `forge_sheet_inventory_import`, and all 7 `forge_linework_*` tools advertise `ReadOnly=false`

### Security

- All three generic executors (`forge_exec_dotnet`, `forge_exec_command`, `forge_exec_lisp`) are now gated behind the two-key `Unsafe` gate (`FORGE_ENABLE_UNSAFE_OPS=true` + per-call `unsafeAcknowledged=true`), while still evaluating the `SafetyPolicy` denylist first (`SHELL`/`SH`/`START`, `NETLOAD`/`ARX`/`APPLOAD`/`VBALOAD`/`VBARUN`, `load`/`autoload`/`vl-load-all`, `open`/`write-line`/`vl-file-copy`, `SAVEAS`/`QSAVE`/`WBLOCK`/`QUIT`/`EXIT`, `(ssget "_X"|"_A")` + `ERASE`/`entdel`/`vla-delete`/`vlax-invoke 'Delete`, `startapp`/`vlax-create-object`/`vla-getinterfaceobject`, `eval`/`read`/`apply`, string-concatenation obfuscation `strcat`/`chr` inside `setvar`/`command`, and `SysvarPolicy`); scoped `PU` and `E` aliases to command position so `-LAYER M PU`, `-INSERT PU-VALVE`, and `text "E ALL PIPES"` are not false-positived
- `HeadlessAccoreConsoleRunner` copies verified script bytes to an isolated temp script before launching `accoreconsole.exe` (eliminating TOCTOU between safety scan and execution); `NamedPipePluginServer` enforces a 16 MB request line cap and constant-time token comparison (`CryptographicOperations.FixedTimeEquals`); `FileAuditSink` synchronizes cross-process writes with a named mutex; `forge_linework_*` validates `.json`/`.svg` artifact extensions on output paths

## [0.2.0] - 2026-07-11

> Tag/release when smoke-lab has a maintainer pass and **both** server + plugin zips are attached. Until then treat 0.2.0 as the product SemVer in-tree; GitHub Release links below apply after the first public tag.

### Added

#### Wave 0 — Publish hygiene

- MIT `LICENSE` and Autodesk `NOTICE.md`
- Community files: `SECURITY.md`, `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`
- Honest [docs/capability-matrix.md](docs/capability-matrix.md) and architecture/safety/install docs
- Cursor / Claude Desktop examples and `skills/765t-forge/`
- CI (`ci-server.yml`), release workflow/scripts, issue/PR templates
- Fail-closed pipe token (`FORGE_AUTOCAD_TOKEN`); `FORGE_DEV_ALLOW_DEFAULT_TOKEN` for local only
- `AUTOCAD_2026_ROOT`-driven plugin HintPath
- Product SemVer `0.2.0` + envelope version constant

#### Wave 1 — Real publish

- Configurable `forge_plot_to_pdf` (device, paper, CTB/STB, area, orientation, scale)
- Real DSD `forge_plot_publish` via `Publisher.PublishDsd` + PDF verify
- Sync layer-state restore and page-setup apply
- Overwrite acknowledgement for PDF/publish/pack
- Undo marks around typed writes

#### Wave 2 — Moat QA + campaign

- `forge_qa_preflight` + structured `QaReport` artifacts
- `forge_block_campaign` titleblock campaigns with dry-run diffs
- `forge_xref_normalize_relative`
- `forge_system_capabilities`

#### Wave 3 — Scale

- `forge_batch_run` AccoreConsole job queue
- `forge_recipe_issue_set` issue-set orchestrator
- `forge_pack_and_go` + `manifest.json`
- Eval scenarios under `evals/`

### Security

- Removed `default-secret-token` production fallback

## [0.1.0] - 2026-07-11

### Added

- Phase 0 foundation: MCP stdio server, AutoCAD 2026 plugin, shared safety/audit/backup
- Named pipe transport with ACL + token auth
- Typed hot-path tools and gated open-world executors
- `765T-Forge.ServerOnly.slnf` and unit tests

[0.2.0]: https://github.com/765T/765T-Forge/releases/tag/v0.2.0
[0.1.0]: https://github.com/765T/765T-Forge/releases/tag/v0.1.0
