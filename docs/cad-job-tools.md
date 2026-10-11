# CAD job tools — design (cad-connector-1007, C1)

Status: rev 3 — market-informed engine architecture added 2026-10-07 (owner directive: research
market features and mechanisms first; two independent peer studies, records in
`var/flow/cad-connector-1007/research/`). Rev 2 content stands; the Rev 3 section at the end adds
the multi-engine architecture (C10-C13) and amends C3/C5/C7/C9. Rev 2 was closed after peer
review (R1 facts/feasibility, R2 adversarial — findings closed in place, record in
`var/flow/cad-connector-1007/verify/C1-1/`). Lead: the CAD development session (owner
assignment 2026-10-07).
Goal: AI agents do CAD jobs end-to-end — extract quantities from CAD layers (bóc tách), prepare
clean, rebased plan/section DWGs, and keep drawings clean — with the same typed, fail-closed style
the existing Forge tools use.

Two drivers:

1. **Owner decision (2026-10-07):** the Flow install will ship AutoCAD support; Forge is developed
   separately first and uplifted into Flow after full testing. This wave builds the capability here.
2. **Production feedback `4b8846f2dca84fe0ba40cada4d66193c`** (feature, 2026-10-07, still `new` by
   owner order — nothing is marked seen until integration lands): a customer preparing clean DWGs
   for overlay QC against a Revit model hit (1) `forge_exec_lisp` returning no data, (2) a ~60 s
   timeout on large `forge_run_script` calls + UTF-16 garbled stdout, (3) no native "extract a
   window/region into a NEW DWG rebased to 0,0,0 with scale/rotation", (4) no typed entity dump per
   layer (splines, bulge polylines, hatches, text, block transforms).

## Verified code facts this design stands on

- `ExecLisp` runs `ActiveEditor.Command(lisp)` and answers `{queued:false, completed:true,
  mode:"editor_command"}` — no output capture exists (`PluginCommandProcessor.cs:1299-1337`).
- `forge_run_script` timeout is a named tool argument: default 300 s, clamped 5..3600
  (`HeadlessAccoreConsoleRunner.cs:239,244`, tool argument `ForgeMcpTools.cs:488`). No 60 s cap
  exists anywhere in the server request path (`ForgeToolRunner.cs`, `ForgePipeClient.cs` — 10 s
  connect (now configurable via `FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS`), 120 s default pipe response —
  and `Program.cs` checked). The customer's ~60 s is most
  likely the MCP client's own tool timeout (**unverified**); the tool `[Description]` never
  advertises `timeoutSeconds`, so a caller cannot discover it — that discoverability gap is real
  and fixed here.
- `ProcessStartInfo` in `RunOneAsync` sets redirection without
  `StandardOutputEncoding`/`StandardErrorEncoding` (`HeadlessAccoreConsoleRunner.cs:220-226`) —
  garbled non-ASCII stdout is real.
- `LineworkKind` is exactly `Line, Polyline, Arc, Circle` as XY tessellations
  (`Linework.cs:16-52`); no spline, bulge (as data — bulge is already *read* internally and
  tessellated away, `PluginCommandProcessor.Linework.cs:225,289`), hatch, text or blockref fields.
- Plugin references per-root AutoCAD DLLs through `$(AutoCadRoot)` fed by `AUTOCAD_2026_ROOT`
  (`Forge.Plugin.csproj:6-7`); `ForgeConstants.AutoCadVersion` pins the string "2026"
  (`ForgeConstants.cs:15` — superseded by C2 — per-year ForgeTargetYear stamp + auto-detected
  root); `build-plugin.ps1:18-24` defaults that env and only warns when the
  DLLs are missing; `ForgeEnvironment.DefaultAutoCadRoot()` returns the 2026 path
  (`ForgeEnvironment.cs:16,26,57` — superseded by C2 — per-year ForgeTargetYear stamp +
  auto-detected root).
- Measured on this machine (2026-10-07): the full solution builds green against
  `D:\AutoCAD 2025` (0 errors; 3 benign MSB3277 Microsoft.VisualBasic warnings) and the logic test
  suite passes 85/85 without AutoCAD.

## C2 — typed AutoCAD year table

`build/AutoCadYears.props`: one typed entry per supported year — `2025` and `2026` — each naming
its default reference root. `build-plugin.ps1` gains `-Year {2025|2026}` (default 2026); it
resolves the year's root from the table (an explicit `AUTOCAD_2026_ROOT` env keeps working as the
manual override for whatever year is being built) and passes it to the build as an MSBuild
property `-p:AutoCadRoot=<resolved>` — the csproj reads `$(AutoCadRoot)` after the env check, so
the property wins without a csproj condition change.

`Forge.Plugin.csproj` gains a static stamp: `<AssemblyMetadata Include="ForgeTargetYear"
Value="$(ForgeTargetYear)" />` fed by `-p:ForgeTargetYear=<year>` from the same script. The plugin
reads it at start and reports `targetYear` in `forge_system_version` / `forge_system_health`
beside the host year it already measures (`Application.Version`, the fact
`PluginCommandProcessor.cs:206` reads today).

**Year-mismatch policy:** `targetYear` ≠ measured host year refuses every dispatch with a named
error `HOST_YEAR_MISMATCH` (naming both years) until a live smoke on that year pair proves the
managed API surface used here is compatible; a proven pair moves into a typed compatibility table
in the same props file. The refusal runs as the first check in `Process`, before safety eval,
backup, undo mark and dispatch (`PluginCommandProcessor.cs:55-131`). No silent allow, no
warning-and-continue.

**Consumers swept in the same change (whole-class rule):** `ForgeConstants.AutoCadVersion`
(replaced by the per-year assembly stamp), `ForgeEnvironment.DefaultAutoCadRoot()` (returns the
table's default for the built year), `install-plugin.ps1` (gains `-Year`), the
`docs/capability-matrix.md` header ("AutoCAD target: 2026" becomes the year table),
`CLAUDE.md`'s build gotcha paragraph, and the `AUTOCAD_2026_ROOT` naming (kept as the manual
override; every grep hit for `2026`/`AUTOCAD_2026_ROOT` under `src/`, `scripts/`, `docs/`,
`build/` is reconciled in the C2 change or marked "unchanged, because …").

## C3 — `forge_dump_entities` (typed per-layer entity dump)

Args: `window` (two WCS corners, optional), `layout` or `viewport` name (exclusive alternatives to
`window`), `layers` (the existing `LineworkQueryArgs` filter semantics, reused verbatim, same
`layerMatch` modes), `include` (kinds to dump; explicit opt-in per kind — the default is the
closed set minus the two largest, hatch loops and spline flats, which are opt-in), `maxEntities`
(named cap, same mechanism and declared default as the linework family's `MaxEntities`),
`splineFlattenToleranceM` (optional declared tolerance).

Returns `CadEntityDump/1`:

- `line`, `arc`, `circle` — as today's linework shapes plus true radius/center;
- `polyline` — ordered vertices **with per-vertex bulge** (closed flag, perimeter); covers
  LWPOLYLINE, and `Polyline2d`/`Polyline3d` map to the same shape (declared here, per R1);
- `spline` — degree, control points, knots, fit points when present, and flattened points only
  when a tolerance was declared;
- `hatch` — boundary loops, each returned as loop type + polyline vertices of the loop curves
  flattened at the declared tolerance, pattern name, associative flag;
- `text` / `mtext` — content, anchor, height, rotation, style;
- `blockref` — block name, insertion, rotation, scale, transform, attribute tag/value pairs;
- every item carries its handle and layer; the dump header carries the DWG's `$INSUNITS`
  (refused as `UNITS_UNDECLARED` when 0) and the selection that produced it.

**Answer-size contract (amended by rev 3 → C10):** the result carries `truncated` and the named
`maxEntities` cap; when the serialized answer exceeds the spill threshold the full dump is written
to an artifact file and the MCP result carries summary + path + bytes + sha256 (mechanism already
shipped as `WriteArtifactOrSuccess`, `LineworkLocal.cs:486-510`). The rev 2 continuation-token
paging is dropped: the consumer is an agent that can read a local file, so spill needs no
tool-stateful paging and the same contract uploads cleanly to a hosted artifact store at C9. A cap
hit is never a silent clip — the same honesty the linework family's
`Truncated`/`TooLargeMessage` already ships.
Read-only tool. A filter matching zero entities is an honest empty answer, never an error. Unknown
window/layout names are named errors (`WINDOW_NOT_FOUND`, `LAYOUT_NOT_FOUND`). Implementation lives
in the plugin (live drawing), shared DTOs in `Forge.Shared` next to `Linework`; the existing
`forge_linework_*` family keeps its shape and consumes nothing new (bulge-aware length in C6
reuses `ExpandBulges`, `PluginCommandProcessor.Linework.cs:289`).

## C4 — `forge_extract_region_safe` (window → new rebased DWG)

Args: source selection (`window` two WCS corners, or `layout`/`viewport` name), `layers` filter
(optional), `targetDwgPath` (absolute), `basePoint` (drawing coordinates that become 0,0,0),
`scale` (optional, uniform, **must be > 0**; negative scale/mirror refused `SCALE_INVALID` —
explicitly out of scope v1), `rotationDeg` (optional), `overwriteAcknowledged`, `dryRun`
(**default true**, declared like C5).

Guards (typed, in order): `TARGET_IS_SOURCE` when the canonicalized target path (`Path.GetFullPath`,
`OrdinalIgnoreCase`) equals the canonicalized source DWG path — the source is never written, no
exceptions; `TARGET_EXISTS` when the target exists without `overwriteAcknowledged` (the same ack
rule SaveAs/plot already use); `REGION_EMPTY` when the region holds zero entities, naming the
per-layer zero counts — no silent empty DWG; `SCALE_INVALID` on scale ≤ 0.

Behavior: `dryRun` returns the plan — entity count per layer inside the region, target path, the
transform that would be applied (echoed, never re-derived). Execution: clone the region's entities
into a fresh Database (`WblockCloneObjects` with a typed filter; soft references — text styles,
linetypes, layers the cloned entities use — are brought over by the same mapping; the new
database's `$INSUNITS` and DwgVersion are copied from the source and echoed), apply rebase →
rotation → scale about the base point, `SaveAs` the target. The source DWG is never modified —
`RequiresBackup` stays unset on that stated basis, and the `TARGET_IS_SOURCE` guard is what makes
the statement true.

**Readback is part of the tool contract** (the repo rule every write tool carries):
`ForgeVerification` in the success result = reopen the saved target, count entities per layer, and
echo the applied transform — counts must equal the plan's, or the result is
`EXTRACT_READBACK_MISMATCH` naming the layer. On a pipe timeout the caller distinguishes
"written" from "not written" by re-running the readback against the target path — the same
never-blind-retry rule `forge_qa_readback_after_timeout` implements for writes, stated here for
extracts.

Metadata: destructive, `RequiresBackup` unset (source untouched, target new).

## C5 — `forge_doc_cleanup_safe` (clean the drawing)

Closed op set, each declared in the call (an empty op list refuses `NO_CLEANUP_OPS`):

- `purge_unused` — regapps, layers, blocks via the database purge API; the plan lists what is
  purgeable by class and count;
- `delete_empty_layers` — only layers carrying zero entities; the plan names every layer;
- `remove_exact_duplicates` — entities of the same type, same layer, and identical defining data
  (exact equality of geometry parameters; no tolerance, no fuzzy match). For block references,
  **attributes participate in equality**: the same block at the same insert with different
  attribute values is not a duplicate and is never removed (typed, tested).

`dryRun` (default true) returns the per-op plan. Execution: one undo mark, pre-write backup
(`RequiresBackup`), then a readback that re-counts per op — a mismatch refuses
`CLEANUP_READBACK_MISMATCH` naming the op. Overwrite/save paths keep the existing
acknowledgement rule. A `SafetyPolicyTests` case pins that the typed op names
(`purge_unused`, …) never trip the denylist's `\b_?-?PURGE\b` regex (`SafetyPolicy.cs:19`) —
the interaction R1 flagged, now a test.

## C6 — `forge_cad_takeoff` (quantities per layer)

Built on C3's engine, read-only; returns per-layer aggregates only (bounded by layer count, no
per-entity dump in the answer). Args: the same selection/layers as C3, optional `legendPath`
(the typed legend file, versioned, same shape philosophy as the W3-T12 legend). Returns per
layer: total length (spline/bulge-aware via `ExpandBulges`), entity counts by kind, closed areas
(closed polylines, circles), and unresolved rows. Legend semantics: layer patterns reuse the
linework family's declared `layerMatch` modes — no new pattern language. A layer matching no
legend entry is `unresolved` (data, never a guess); a layer matching **multiple** legend entries
refuses `LEGEND_AMBIGUOUS` naming the layer and the matching patterns — no silent precedence
(R2). All values in declared units.

## C7 — executor honesty

- `forge_run_script`: capture stdout/stderr as raw bytes and decode by BOM (`FF FE`/`FE FF` →
  UTF-16 LE/BE, `EF BB BF` → UTF-8, else the machine's OEM codepage via
  `CultureInfo.CurrentCulture.TextInfo.OEMCodePage`); the result names the encoding used
  (`stdoutEncoding`) so two machines cannot decode differently in silence. The tool description
  documents `timeoutSeconds` (default 300, range 5..3600) and points long runs at
  `forge_batch_run` (resume). The customer's ~60 s is most likely the client's own timeout
  (**unverified** — stated as such; the server-side fix and the C8 smoke that runs beyond 60 s
  with `timeoutSeconds: 300` close the server half of the ask).
- `forge_exec_lisp`: keeps `Editor.Command` execution; its result and description state plainly
  that no output capture exists, and queries belong in `forge_dump_entities`. The denylist keeps
  covering it unchanged. (A captured eval channel through accoreconsole is a later, separate
  design — deliberately out of scope here.)

## C8 — live smoke (AutoCAD 2025, this machine)

One fixture DWG with known geometry (a window containing splines, bulged polylines, hatch, text,
blocks, and two exact duplicates on one layer including one blockref with differing attributes; a
second window deliberately empty). The smoke proves: dump counts/kinds per layer match the
fixture, the cap + continuation pages honestly; extract_region produces a DWG whose readback
(reopen + count + transform echo) matches the plan, refuses overwrite without ack, and refuses
`TARGET_IS_SOURCE` on the aliased paths; cleanup dry-run → execute → readback matches, source DWG
closed without saving; takeoff totals match the fixture within declared units; run_script
completes inside a declared raised budget with un-garbled output. Records go to
`var/flow/cad-connector-1007/verify/C8-1/` (Flow-side records stay outside Git).

## C9 — uplift into Flow (after C2-C8 are fully green)

AutoCAD connector rides the existing `connector/2` channel (hub-signed commands, instanceId
opaque) — **this is the connector path on Flow's hub, not a Forge-side multi-tenant MCP**; the
`capability-matrix.md`/`roadmap.md` exclusions ("HTTP remote multi-tenant MCP") are untouched by
this item. `forge_*` typed tools enter Flow's catalog with full per-registration metadata
(riskClass/checkpointMode/creditClass/documentScope) — **executors (`forge_exec_command`,
`forge_exec_lisp`, `forge_run_script`, `forge_exec_dotnet`) never uplift** (Flow's
"never deliver arbitrary code" rule); installer gains the AutoCAD year table, installer-guard
allowlist rows and update-manifest artifacts; Flow-side `cad_extract_network` gains splines and
bulge (the customer's point 4, Flow side of the house).

## Consumers to update per tool (repo convention, same change as the tool)

For each of the four new tools: the `ForgeMcpTools` registration, the `ToolMetadata`/
`ForgeToolRegistry` entry, the `PluginCommandProcessor` switch case (`ToolMetadataTests` and the
dispatch-sync tests guard the first two pairs — a miss fails CI), registry tests asserting the
intended `ReadOnly`/`Destructive`/`RequiresBackup` flags (dump/takeoff `ReadOnly=true` so they
short-circuit the denylist; cleanup `RequiresBackup=true`; extract destructive, backup unset),
plus `docs/capability-matrix.md`, `skills/765t-forge/SKILL.md` and `CHANGELOG.md`.

## Explicitly out of scope this wave

Civil 3D; SSM write; visual screenshots (proposed as C14 below — owner decision pending); LISP
eval-with-capture channel; negative scale/mirror; any change to the denylist or to the
`forge_linework_*` output shape.

## Decisions taken in review (closed — were open questions in rev 1)

1. Year-mismatch: refuse-by-name (`HOST_YEAR_MISMATCH`) until a live smoke proves a pair; proven
   pairs enter a typed compatibility table.
2. `remove_exact_duplicates`: exact equality only for v1; blockref attributes participate in
   equality. A declared-tolerance mode stays out (was open question 2).
3. `forge_dump_entities` default `include`: explicit opt-in per kind, with hatch loops and spline
   flats opt-in only — the answer-size cap plus the opt-in bound the answer (was open question 3).

## Rev 3 — engine architecture from market research (2026-10-07)

Owner directive: study market features and mechanisms and make the engine better before C3+
implementation. Two independent read-only peer studies — market gap analysis (session
`4dffa844`) and engine-mechanisms deep-dive (session `b9c64b7f`) — ground this section; raw
market evidence downloaded to `var/flow/cad-connector-1007/research/` (readme-puran.md,
readme-uc4n.md, acadsharp-readme.md, acadsharp-license.md) and read from files, per repo rules.

**Market pattern (evidence):** the two top open-source AutoCAD MCP servers converged
independently on "one typed contract, multiple engines": puran-water/autocad-mcp (File IPC
needing AutoCAD LT vs ezdxf headless anywhere; 8 consolidated tools) and U-C4N/Autocad-MCP Pro
(live COM vs headless ezdxf; 122 tools; per-engine capability keys; refusal-by-name where the
headless path cannot be honest). ACadSharp v3.8.0 gives C# read/write of DWG+DXF with **no
AutoCAD installed** — MIT licence verified (`acadsharp-license.md`). Forge rev 2 shipped exactly
one engine (live plugin) plus untyped headless scripts.

**Forge differentiators the market evidence does not show (keep and advertise):** the
AccoreConsole headless path runs **real AutoCAD full-fidelity** (both competitors' headless paths
are ezdxf-class and self-admit ACIS/REGION opacity); dual safety evaluation with a fail-safe
registry default; dry-run default + pre-write backup + one undo mark per write; backup manifests,
publish receipts, drawing registry and standards packs; the verified-readback contract.

**Architecture: one typed contract, three engines.** The DTOs (`CadEntityDump/1`, the takeoff
contract, the cleanup plan) live in `Forge.Shared` once; three engines implement them:

- **live** — in-process plugin over the pipe, today's full-API path;
- **headless** (C11) — the *same* plugin handlers inside `accoreconsole.exe` with no AutoCAD UI:
  a registered `FORGE_HEADLESS` command reads a `ForgeCommand` JSON file, runs the existing
  `PluginCommandProcessor.Process` verbatim (token → year gate → dual safety → backup → undo
  mark → dispatch), writes the `ForgeResult` JSON to an output file. Server side, a
  `HeadlessTypedRunner` reuses the proven spawn/timeout/kill pattern
  (`HeadlessAccoreConsoleRunner.cs:221-272`) and the batch/resume state machine (200-job cap,
  dedupe, `resumeBatchId`, per-job save, `:27-170`): `BatchJobArgs` gains a tool/args field and
  typed jobs dispatch through it.
- **offline** (C12) — a server-resident read engine on ACadSharp for `dump_entities` and
  `cad_takeoff` with no AutoCAD on the machine. It lives in `Forge.Server` (the precedent is
  `LineworkLocal.Execute`, already an offline engine routed server-side —
  `ForgeToolRunner.cs:86-88`); the package is pinned in `Directory.Packages.props` and the MIT
  notice added. Writes are refused offline **by name** — Forge never re-implements its write
  doctrine (backup, undo mark, readback) on a third-party writer.

**Routing rule:** the new tool contracts carry an explicit `engine` argument
(`"live" | "headless" | "offline"`, default live). There is no automatic live→offline fallback: a
silent fallback is banned doctrine, so an unsupported combination fails with a named error naming
the engine that can do the job.

**What offline cannot do (stated honestly in the contract):** plotting/publishing, paper-space
layout/viewport rendering, dynamic-block effective geometry, proxy/custom objects, layer-state
restore, anything touching `Editor` or `Application`; spline/ellipse curve parity is subject to
the C12a spike.

### New plan rows (rev 3)

- **C10 — automatic artifact spill for oversized answers.** When a serialized answer exceeds the
  spill threshold (explicit config, default 256 KB), the full payload is written to
  `%LOCALAPPDATA%\765T-Forge\artifacts\<tool>-<id>.json` and the MCP result carries summary +
  path + bytes + sha256; `maxEntities` + `truncated` remain the filter-level cap. Mechanism is
  already shipped (`WriteArtifactOrSuccess`) — this only changes who triggers it. Retention is
  explicit config, never a heuristic. Priority P0: highest value per unit risk, and C3/C6 depend
  on it.
- **C11 — headless typed runner**, preceded by **C11a spike**: one `.scr` doing `NETLOAD` of the
  plugin DLL + `FORGE_HEADLESS` on a fixture DWG. The spike settles the load carrier (bundle
  autoload vs explicit NETLOAD — accoreconsole behaviour unverified) and whether `Editor.Command`
  undo marks behave headless. Everything else assembles from proven parts. Verdict LIKELY. This
  is the foundation for folder-scale takeoff/cleanup without opening AutoCAD.
- **C12 — offline read engine**, preceded by **C12a spike**: pinned-version target-framework
  check plus an entity-parity table (Line/Polyline/Arc/Circle plus spline, hatch, text, blockref
  reads) against a fixture DWG dumped live. Then `engine:"offline"` on the dump/takeoff
  contracts, with per-kind parity declared in capability metadata. LIKELY for the four linework
  kinds. This is also Flow's W2-T6 SP4 answer at uplift.
- **C13 — native selection engine** (deferred until C4 lands): `Editor.SelectCrossingWindow` +
  `SelectionFilter` (`TypedValue(8, layerFilter)`) as the candidate-set provider for windowed
  dump/extract on drawings with 10^5-10^6 entities; `LineworkQuery.Apply` stays the second-stage
  filter; the xref walk is unchanged (Editor selection never crosses xrefs). No `SelectionSet`
  usage exists in `src` today — NEEDS-SPIKE for the perf claim.
- **C14 — `forge_view_snapshot` (plot-to-PNG) — PROPOSED, owner decision pending.** Both market
  leaders ship screenshots/previews and their agent loops depend on them; shape: plot a named
  layout or window to PNG reusing the `forge_plot_publish` plotting path, read-only, one typed
  tool. Rev 2 scoped it out; it needs the owner's yes to enter.
- **C15 — `forge_dgn_import` (DGN → DWG typed conversion) — implemented 2026-10-09, live smoke
  pending.** Owner ask (conversation 2026-10-09): convert MicroStation DGN drawings to DWG with
  risk calculated before dispatch. Shape: `-DGNIMPORT` — prompt order per Autodesk Help
  (filename → design model, Enter accepts the DGN default → conversion units Master/Sub →
  mapping setup, Enter accepts the default; `Editor.Command` maps `""` to Enter) — executed in
  a **fresh temporary document** (`Documents.Add("")`), so neither the source DGN nor the
  user's active drawing is ever written; then `Database.SaveAs(target, DwgVersion.Current)`,
  restore the previous active document, close the temp document best-effort
  (`tempDocumentClosed` is in the answer). `RequiresBackup=false` in the registry on that
  stated basis — an existing target is still backed up explicitly before SaveAs (same rule as
  `forge_doc_save`). Guards, in order: `missing_dgn_args`; `dgn_not_found`;
  `dgn_v7_not_supported` — V7 detected by the Intergraph TCB word `08 09`/`09 08` in the first
  two bytes (V8 is a different container; AutoCAD imports V8 only; sniff is best-effort —
  a non-V7 header is left to the importer plus post-import verification);
  `dgn_target_document_open` — SaveAs over any open document is refused;
  `illegal_control_character` on paths/model name; `dgn_units_invalid` — units is a typed
  `master|sub` choice because the wrong one rescales the whole drawing (unit mapping per the
  DGNIMPORT docs); `dgn_target_overwrite_not_acknowledged` (ack → destination backup →
  proceed). dryRun returns the plan: canonical paths, source bytes, targetExists, units,
  model, mapping=default. Post-import verification: model-space entity + layer counts — zero
  entities refuses `dgn_import_empty` naming the model, never a fake success. Risk register:
  (1) prompt-sequence variance across host years — the tokens are the documented 2026
  sequence; a mismatch surfaces as `dgn_import_failed` with the exception text, and the C8
  live smoke must confirm 2025+2026 before the matrix row moves to implemented;
  (2) creating/activating a document inside another document's command context — the same
  class of host behaviour the year gate defers to smoke; every cleanup path is best-effort
  and reported honestly; (3) large DGN vs the 120 s default pipe response timeout — the
  failure suggestion names `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS`, and the MCP
  `[Description]` advertises it up front (C7); (4) font/level fidelity uses the importer's
  default automatic mapping — substitution is possible and not measured here;
  (5) headless batch conversion via accoreconsole is NOT claimed — `forge_run_script` +
  `-DGNIMPORT` is untested and stays out of the matrix until someone proves it.

### Amendments to rev 2 rows

- **C3:** answer-size contract is now maxEntities + truncated + automatic spill (C10); the
  continuation token is dropped. The dump header additionally states the selection semantics
  actually applied (window vs layout vs viewport, per-layer counts before the cap) — selection
  honesty per the U-C4N pattern.
- **C5:** later extension candidates for the closed op set: audit-with-fix and overkill-style
  dedup as typed, backup-wrapped ops (both are top QC asks; the safety policy blocks them as raw
  commands and nothing typed offers them today). Not in v1.
- **C7:** executor honesty extends to every tool: advertised default budget and max answer size
  in the tool `[Description]`.
- **C9:** the offline engine is Flow's W2-T6 SP4 mechanism; catalog token-budget profiles
  (`ToolProfiles.cs` exists) are noted as uplift-time, P2.

### Unverified (owned by the C11a/C12a/C8 spikes)

accoreconsole bundle-autoload/NETLOAD behaviour and headless `Editor.Command` undo marks;
ACadSharp pinned-version TFM and spline/ellipse parity; `Editor.Select*` behaviour inside
accoreconsole; star counts and U-C4N's self-graded rubric are vendor claims, not reproduced;
C15 `-DGNIMPORT` prompt behaviour on a live host (2025 and 2026) and the fresh-temp-document
context — logic is unit-tested, the live smoke is not run yet.
