# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Product SemVer is independent of Phase labels and MCP protocol/SDK versions.
See [docs/versioning.md](docs/versioning.md).

## [Unreleased]

### Added

- Per-year AutoCAD plugin builds for 2017–2026 (`AutoCadHostCatalog`, `AutoCadYear`, `scripts/build-plugin.ps1 -Year` / `-AllYears`, one autoloader component per R-series). Default verified host remains AutoCAD 2026. 2017–2025 are not smoke-tested; binaries are not interchangeable. 2017–2018 target `net46` (documented CLR 4.6). System.Text.Json 8 cannot restore for `net46` (NU1202), so that TFM serializes with Newtonsoft.Json. `forge_exec_dotnet` on those years returns `autocad_version_unsupported`. Pack and install write `Contents/Windows/<year>/Forge.Plugin.dll` and omit years that were not built.
- Publish-ready Wave 0: Autoloader R25.1 + valid GUIDs; `%APPDATA%` install guidance; NOTICE upstream attribution; `docs/history/build-brief.md`; embedded matrix/safety in server publish; `RELEASE_STATUS.txt` from pack script
- PublishReceipt + PDF probes + actionable preflight findings + foreground plot policy
- IssueSetContract lite, standards pack v2 plot bindings, field/rev gates, timeout read-back protocol
- Differential issue-set receipts, read-only sheet inventory, batch resume metadata
- Plugin-dispatch CI sync; Dependabot; scoped open-world `*` denylist; ADR honesty for headless AccoreConsole path
- Linework QA wave: `forge_linework_segments` (flat segment feed, meters/transformed endpoints), `forge_linework_compare` (per-item CAD↔Revit marking `matched`/`partial`/`missing_in_revit`/`extra_off_cad` + matched pairs + Z filter + SVG overlay), `forge_linework_transform` (server-side similarity/affine calibration from anchor pairs with persisted calibration file and >0.5 m residual warning); explicit `layerSuffix`/`layerMatch` xref-safe layer filtering on all `forge_linework_*` tools

### Changed

- `forge_plot_to_pdf` uses `PdfProbeResult` (`plot_probe_failed` when the probe fails). Missing or unparseable `ACADVER` fails closed. Headless `accoreconsole.exe` is chosen by `autoCadYear` or `FORGE_ACCORECONSOLE_YEAR` (default 2026, `AUTOCAD_<year>_ROOT`) and does not fall back to the 2026 console.
- MCP contract: `Ok=false` is `tools/call` `isError`, read from structured content or the JSON text (output schema stays off). Failed gates, probes, missing read-back, `updated=0`, and queued commands are not success. Backup path sits beside the payload. `forge_qa_preflight` and `forge_sheet_inventory_import` are not read-only. Server name `765T-Forge` with instructions. Task support is forbidden. Linework tools stay on the surface.
- Roadmap SemVer reconciled with capability matrix (registry/packs/resources are 0.2.0 shipped, not 0.3.0 targets)
- Removed `companion-skill/` stub; added `docs/README.md` + `AGENTS.md`
- Maintainer smoke-lab pass recorded (2026-07-11) against live AutoCAD 2026; `scripts/smoke-lab.ps1` helper

### Security

- Denylist additions (old patterns kept): `DELETE ALL`, `ARX` Load / `(arxload`, `-LAYER` mass delete of `*`/`ALL`, `ERASE` fence/window/crossing, `ssget` `C`/`W`/`F`/`WP`/`CP` with erase/delete/`command`, a `vlax-for` model-space `vla-erase` loop, and `(eval (read`. `ZOOM *` stays allowed. Assembling a command across separate executor calls is out of band for the denylist. `forge_system_setvar` refuses trust and startup variables (`deny_sysvar`).
- Denylist additions (old patterns kept): `E`/`_.E` + `ALL`, destructive `ssget "_X"`/`"_A"`, `strcat`/`eval` with `ALL` or `ssget`, executor `SAVE`/`QSAVE`/`SAVEAS`/`WBLOCK`, and command-anchored `NETLOAD`/`APPLOAD`/`ARXLOAD`/`(load`/`SCRIPT`/`SHELL`/`SH`. `ZOOM *` stays allowed. Batch dry-run scans script bodies. A missing backup blocks the write.
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
