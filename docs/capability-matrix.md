# Capability matrix

Honest status of MCP tools. AutoCAD target: plugin components for **2017–2024** (`net462`, prepared but **not yet compiled/verified**) and **2025–2026** (`net8.0-windows`, verified). The bundle declares the modern component for `SeriesMin=R25.0 SeriesMax=R26.0`, which includes AutoCAD 2027 (.NET 10), but no Forge runtime test on 2027 is recorded. Civil 3D, full SSM **write**, and geometry megakits are out of scope.

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
| `forge_system_getvar` / `_setvar` | implemented | `_setvar` refuses a fixed exact-name set of trust/startup variables with `deny_sysvar` (`SECURELOAD`, `TRUSTEDPATHS`, `TRUSTEDDOMAINS`, `LEGACYCODESEARCH`, `ACADLSPASDOC`, `SAFEMODE`, `TEXTEVAL`, `DEMANDLOAD`, `APPAUTOLOAD`, `AUTOLOAD`, `EXPERT`); the value is never inspected |
| `forge_system_capabilities` | implemented | Devices, media, page setups, layouts, layer states, plot styles; restores the previous plot device in a `finally` (`currentConfig.previousDevice` / `restored` / `restoreError`) |

**Plugin host gate (ACADVER):** every plugin command fails closed when `ACADVER` is missing, empty, whitespace, or unparseable (`autocad_host_mismatch`) or when its release series is outside the loaded build's supported series (`autocad_version_unsupported`; `net462` = R21.0–R24.3, `net8.0-windows` = R25.0–R26.0). The plugin refuses rather than run against an API surface it was not compiled for.

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
| `forge_block_list/get/set_attr` | implemented | A tag that matches nothing fails with `block_attribute_not_found` (not a silent `updated=0`) |
| `forge_block_campaign` | implemented | Multi-entry dry-run diffs, Unicode; commits in a single transaction |
| `forge_layout_page_setup_import` | implemented | Prefers sync PlotSettings copy from template DWG/DWT; falls back to queued `-PSETUPIN` with `completed=false` |
| `forge_layout_page_setup_apply` | implemented | Sync `PlotSettings` copy onto layout |

## Plot / publish / QA / recipes

| Tool | Status | Notes |
|------|--------|-------|
| `forge_plot_to_pdf` | implemented | Configurable device/paper/CTB·STB/area; overwrite ack; smoke-verified. Paper units are read from `PlotSettings.PlotPaperUnits`; if that read fails, the tool returns `plot_units_unavailable` instead of guessing from the paper name. The written file must pass the PDF probe or the call fails with `plot_probe_failed` (probe kept in `data`, verification block set) |
| `forge_plot_publish` | partial | DSD + `Publisher.PublishDsd` + **PublishReceipt** (stamps `AuditId`) + PDF probe + preflight gate; on DSD no-output / failure, **falls back** to per-layout `-PLOT` (`fallback=plot_to_pdf`; multi-layout `singlePdf` may be unmerged); forces `BACKGROUNDPLOT`/`BGCOREPUBLISH=0`; `force=true` requires `FORGE_ALLOW_FORCE_PUBLISH`; receipt `pageCount` is `null` / `pageCountSource=not_available` (no page-count guessing) |
| `forge_qa_*` (verify/check/audit/readback) | implemented | |
| `forge_qa_readback_after_timeout` | implemented | Timeout recovery protocol — never retry write blind |
| `forge_qa_preflight` | implemented | QaReport + pack v2 + foreground plot + issue-set contract findings |
| `forge_audit_summarize` | implemented | Server-side last-N audit summary (no raw args) |
| `forge_issue_set_validate` | implemented | IssueSetContract layouts↔drawingNos↔rev |
| `forge_sheet_inventory_import` | implemented | Read-only CSV → contract (no DST/SSM write) |
| `forge_issue_set_diff` | implemented | Diff two PublishReceipt artifacts |
| `forge_recipe_issue_set` | implemented | Normalize → fill → gate → publish; reports `steps[]` with `status` exactly `completed` or `failed`, stops at the first failure |
| `forge_pack_and_go` | implemented | Host+xrefs+styles + `manifest.json`; plans all destinations before copying, resolves same-named xrefs deterministically (suffix from the parent folder, recorded as `renamedFrom`), stages the whole pack, then moves it into place |
| `forge_batch_run` | implemented | AccoreConsole queue + **resume** via `resumeBatchId`; per-job `timeoutSeconds` (clamped 5–3600, default 300) and per-job/call `autoCadYear` (`AUTOCAD_<year>_ROOT`, no newer-year fallback) |
| `forge_batch_status` | implemented | Load saved batch resume state |

## Registry / standards / viewport / profiles

| Tool | Status | Notes |
|------|--------|-------|
| `forge_registry_load` / `_lookup` | implemented | Fail-closed drawing numbers for titleblock tags (`deny_unknown_drawing_no`) |
| `forge_pack_load` / `_status` | implemented | Declarative project standards pack (v2 plot bindings); caller/pack regexes fail closed on the 250 ms match timeout (`regex_timeout`) |
| `forge_system_tool_profile` | implemented | Profiles `core` / `plot` / `qa` / `linework` (server-side) |
| `forge_viewport_list` | implemented | Paper-space viewports |
| `forge_viewport_set_layer_freeze` | implemented | VP freeze/thaw by handle |

## Linework (CAD ↔ model QA)

Seven `forge_linework_*` tools ship with `RequiresAutoCad=false`. Six of them (`dump`, `trace`, `topology`, `coverage`, `segments`, `compare`) resolve **server-side** for `source=dumpFile` and are dispatched to the **plugin** for `source=drawing`, so they cross the pipe only when the caller asks for the live drawing. `forge_linework_transform` is **server-only** — it is pure coordinate math. All seven are writers, not read-only: six create the caller-supplied `outputPath` when set, `forge_linework_compare` also writes `overlayPath`, and `forge_linework_transform` writes the calibration JSON to `transformPath` while calibrating. None of them edits a drawing, so none requires a backup.

Status is **partial** for the six drawing-reading tools: the `source=dumpFile` pipeline (a `pl_dump.txt`-format dump) is unit-tested, but **this port has not been exercised against a real drawing or a real Revit/pipe export** — the tests that would do so are gated on evidence files that are absent in this repo. The `source=drawing` plugin arm compiles against the installed AutoCAD assemblies but has never been run inside AutoCAD, so treat it as unverified too.

| Tool | Status | Notes |
|------|--------|-------|
| `forge_linework_dump` | partial | Entities (line/polyline/arc/circle) as ordered vertices; `source=dumpFile` (no AutoCAD) or `source=drawing` (plugin); xref-aware layer filter |
| `forge_linework_trace` | partial | Point/handle → owning entity, nearest segment index, context neighbours |
| `forge_linework_topology` | partial | Shared-vertex nodes, edges, T-junctions, X-crossings, runs, dead ends |
| `forge_linework_coverage` | partial | CAD-vs-model coverage: missing / partial / extra + uncovered sub-ranges |
| `forge_linework_segments` | partial | Flat segment rows (stable `seg` id, endpoints, layer, length); `units=meters`; optional transform → `sT`/`eT` |
| `forge_linework_compare` | partial | Per-item marking `matched`/`partial`/`missing_in_revit`/`extra_off_cad` + nearest counterpart + `distanceM` + matched pairs; `minZ`/`maxZ` model filter; optional SVG overlay via `overlayPath` |
| `forge_linework_transform` | implemented (server-only) | Calibrate/apply CAD↔Revit similarity/affine transform from anchor `pairs`; persists calibration JSON via `transformPath`; warns when residual > 0.5 m. Unit-tested only |

Layer filtering on all `forge_linework_*`: `layerFilter` patterns match the full layer name **and** the bare tail after the last `$`/`|`, so `A-Drainage-Pipe` matches `XREF$0$A-Drainage-Pipe`. `layerSuffix` forces an explicit EndsWith on the bare name; `layerMatch` = `auto|exact|suffix|prefix|substring` controls plain-pattern semantics (`prefix` is the legacy startswith that drops xref content). Coverage sampling is driven by caller parameters (`toleranceMeters`, `stepMeters`, `minCoverageFraction`), not by a hidden threshold.

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
| `forge_cde_gate_evaluate` | implemented | ISO 19650-lite status/rev/naming + optional sidecar; regex failures are `regex_timeout` |

## Executors (all five `Unsafe`)

All five free-text executors are flagged `Unsafe` in `ForgeToolRegistry` and are refused with `unsafe_not_acknowledged` unless `FORGE_ENABLE_UNSAFE_OPS=true` **and** the call passes `unsafeAcknowledged=true`. No argument text is inspected.

| Tool | Status | Notes |
|------|--------|-------|
| `forge_exec_command` / `_lisp` | implemented | Unsafe-gated; prefer sync `Editor.Command`; else `queued=true, completed=false, undoGrouped=false` |
| `forge_run_script` | implemented | Unsafe-gated; AccoreConsole; **server-only** capability gate (ADR 0002); `autoCadYear` selects the console by exact year; abort tokens in the output fail with `accoreconsole_script_error` (a clean run is not proof of success) |
| `forge_batch_run` | implemented | Unsafe-gated; AccoreConsole queue; **server-only** capability gate (ADR 0002); per-job timeouts and year selection; same script-error check |
| `forge_exec_dotnet` | implemented | Unsafe-gated; full process trust inside AutoCAD, not sandboxed; timeout reports `exec_dotnet_timeout` and cannot abort a running snippet; Roslyn assemblies ship with the plugin |

## Still planned / deferred

| Item | Notes |
|------|-------|
| Visual viewport screenshot | Competitor parity; only after PDF probes |
| Full SSM **write** | Deferred (GUID/path fragility); read-only CSV inventory is shipped |
| Civil 3D / geometry megakit / remote multi-tenant MCP | Explicitly out of scope |
| netDxf offline path | Optional later — not SoT for plot truth |
| NuGet `McpServer` / registry | After Releases packaging — see docs/packaging-nuget.md |
| AutoCAD 2017–2024 runtime verification | Legacy assembly prepared; needs `scripts/verify-plugin-series.ps1 -Series 2017` on a machine with AutoCAD 2017 reference assemblies |

MCP Resources / Prompts (`forge://…`, `issue_set_runbook`) are **implemented** (not planned).
