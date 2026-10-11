# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Product SemVer is independent of Phase labels and MCP protocol/SDK versions.
See [docs/versioning.md](docs/versioning.md).

## [Unreleased]

### Added

- Typed AutoCAD year table: `build/AutoCadYears.props` (2025 + 2026), `build-plugin.ps1 -Year` with per-year output and a `ForgeTargetYear` assembly stamp, `install-plugin.ps1 -Year`; the plugin refuses a host of another year with `HOST_YEAR_MISMATCH` until the year pair is proven by a live smoke (`docs/cad-job-tools.md`, cad-connector-1007)
- `forge_dgn_import`: typed DGN V8 → new DWG conversion via `-DGNIMPORT` in a fresh temporary document (source DGN and active drawing never modified); V7 refused by header sniff, target-open and overwrite-ack guards, destination backup, dry-run plan, model-space entity-count verification (`docs/cad-job-tools.md` C15 — live AutoCAD smoke pending)
- Publish-ready Wave 0: Autoloader R25.1 + valid GUIDs; `%APPDATA%` install guidance; NOTICE upstream attribution; `docs/history/build-brief.md`; embedded matrix/safety in server publish; `RELEASE_STATUS.txt` from pack script
- PublishReceipt + PDF probes + actionable preflight findings + foreground plot policy
- IssueSetContract lite, standards pack v2 plot bindings, field/rev gates, timeout read-back protocol
- Differential issue-set receipts, read-only sheet inventory, batch resume metadata
- Plugin-dispatch CI sync; Dependabot; scoped open-world `*` denylist; ADR honesty for headless AccoreConsole path
- Linework QA wave: `forge_linework_segments` (flat segment feed, meters/transformed endpoints), `forge_linework_compare` (per-item CAD↔Revit marking `matched`/`partial`/`missing_in_revit`/`extra_off_cad` + matched pairs + Z filter + SVG overlay), `forge_linework_transform` (server-side similarity/affine calibration from anchor pairs with persisted calibration file and >0.5 m residual warning); explicit `layerSuffix`/`layerMatch` xref-safe layer filtering on all `forge_linework_*` tools
- Authoring wave C20–C23 (`docs/cad-authoring-tools.md`, competitor-evidence design): 25 typed tools so agents can actually draw and modify — `forge_draw_line/_polyline/_circle/_arc/_text/_mtext/_hatch/_dimension` plus `forge_draw_batch` (≤500 shapes in one call/transaction with per-step results), `forge_entity_get/_list/_move/_copy/_rotate/_scale/_mirror/_offset/_erase/_set_props` and `forge_undo`, `forge_block_create/_insert/_list`, `forge_layer_create`, `forge_layout_create`. Contract: WCS + drawing units, degrees at the boundary, AutoCAD handles as the object currency, verification read-back on every write, dry-run everywhere, pure DB API (no dialogs, no prompts). Additive-only tools skip the pre-write DWG copy; entity mutations are destructive with backup + one undo group each; no authoring tool is OpenWorld (typed params, no command strings)

### Fixed

- Start-tab wedge: calling any tool on AutoCAD's Start tab (no open document) hung the first request inside `ExecuteInCommandContextAsync` — which never dispatches without a command context — and the single pipe instance then refused every later connection until AutoCAD restart (re-NETLOAD could not recover). The plugin now answers doc-free diagnostics (`forge_system_health`, `forge_system_version`, `forge_doc_list_open`) via an `Application.Idle` marshal and fast-fails document tools with a named `no_command_context` error
- Per-request plugin watchdog: a request AutoCAD never returns is answered `plugin_request_timeout` and the pipe keeps serving instead of wedging permanently; not-yet-started callbacks past the deadline are refused as `plugin_request_stale` so abandoned writes cannot execute late, while already-started work still runs to completion (read-back-after-timeout protocol applies). Accept-loop failures back off 1s instead of tight-retry spinning
- Error suggestions no longer hardcode "AutoCAD 2026"; `FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS` (default 10) makes the pipe connect timeout configurable
- `ForgeEnvironment.DefaultAutoCadRoot()` resolves the newest installed plain "AutoCAD 20xx" directory (Program Files\Autodesk, then fixed-drive roots for non-default installs like `D:\AutoCAD 2025`) instead of always assuming the 2026 path, so headless accoreconsole resolution works on 2025-only machines
- `scripts/pack-release.ps1` builds the plugin through `build-plugin.ps1 -Year` so release plugin zips carry the `ForgeTargetYear` stamp (the old second build path bypassed the year table and stamped 2026)
- `forge_doc_open` restores FILEDIA in a `finally` (a failed `Documents.Open` used to leave FILEDIA=0)

### Changed

- Roadmap SemVer reconciled with capability matrix (registry/packs/resources are 0.2.0 shipped, not 0.3.0 targets)
- Removed `companion-skill/` stub; added `docs/README.md` + `AGENTS.md`
- Maintainer smoke-lab pass recorded (2026-07-11) against live AutoCAD 2026; `scripts/smoke-lab.ps1` helper

### Security

- Open-world denylist no longer blocks every `*` (scoped to selection idioms); headless script path documented as server-side SafetyPolicy only

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
