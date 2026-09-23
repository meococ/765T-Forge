# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Product SemVer is independent of Phase labels and MCP protocol/SDK versions.
See [docs/versioning.md](docs/versioning.md).

## [Unreleased]

### Added

- `FORGE_ALLOW_FORCE_PUBLISH` (default false) — `force=true` on publish/recipe returns `force_not_allowed` unless enabled (ADR 0004)
- `forge_publish_ceremony_check` optional evidence: `dryRunAuditId`, `preflightAuditId`, `receiptAuditId`
- Nested xref BFS depth report on `forge_xref_closure` / pin / dependency_closure (`maxDepth` default 4, optional `failClosed`)
- DSD publish seat checklist in [docs/smoke-lab.md](docs/smoke-lab.md)
- `forge_linework_*` tool family (`dump`, `trace`, `topology`, `coverage`, `segments`, `compare`, `transform`) — server-side, AutoCAD-free CAD linework extraction and CAD↔model QA from a `pl_dump.txt` dump, with xref-safe layer filtering, a flat segment feed, per-item `matched`/`partial`/`missing_in_revit`/`extra_off_cad` marking, an optional SVG overlay, and anchor-pair similarity/affine calibration. Unit-tested only; not yet exercised against a real drawing. Live-drawing extraction is not dispatched in this build (`live_source_unavailable`)

### Changed

- Plugin now ships dual-target builds: .NET Framework 4.6.2 (AutoCAD 2017–2024) and .NET 8 (AutoCAD 2025+).
- `PublishReceipt.AuditId` stamped from command audit correlation
- DSD publish forces `BGCOREPUBLISH=0` (with restore) and richer fallback diagnostics (`singlePdfUnmerged`, exception type, fallback reason)
- Product SemVer **0.3.0** (Unreleased — do not tag until dual-seat DSD primary PASS)

### Fixed

- Doc drift: README env table + getting-started note for `FORGE_ALLOW_FORCE_PUBLISH`; packaging-nuget SemVer; smoke-lab v0.2.1 tag status; evals README 0.3 test pointer

### Security

- Free-text executors (`forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_batch_run`, `forge_exec_dotnet`) now require the same unsafe dual-gate (`FORGE_ENABLE_UNSAFE_OPS` + `unsafeAcknowledged`) instead of free-text command filtering.
- Secrets such as `hmacKey` are redacted from audit records.

## [0.2.1] - 2026-07-11

### Added

- [docs/getting-started.md](docs/getting-started.md) dual-install matrix; [docs/install/](docs/install/) (Cursor, Claude Desktop, VS Code, plugin)
- Atomic dual-zip release workflow (draft until plugin attached); SBOM on release; `.editorconfig` / `.gitattributes`
- VS Code MCP example; `.mcp/server.json`; `PackAsTool` on `Forge.Server` (`765T.Forge.Server`)
- Exclusive AEC tools: plot fingerprint, dependency closure, dual-source titleblock, modal trap, xref closure/pin, transmittal seal, publish ceremony/budget, CDE gate + sidecar
- Archive index for historical brief

### Changed

- `forge_plot_publish` marked **partial** in capability matrix; demo-60s prefers smoke-verified `forge_plot_to_pdf`
- Roadmap SemVer reconciled: evidence chain / IssueSet already in 0.2.x; 0.3.0 = further determinism hardening
- Moved aspirational brief to [docs/archive/build-brief.md](docs/archive/build-brief.md); stripped from README hero
- Product SemVer **0.2.1**

### Fixed

- `forge_plot_publish` falls back to per-layout `-PLOT` when `PublishDsd` fails or produces no output (`fallback=plot_to_pdf`); receipt includes preflight hash when gated

## [0.2.0] - 2026-07-11

> Superseded for public announce by **0.2.1** Technical Preview. In-tree history retained.

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
- Autoloader R25.1; embedded matrix/safety in server publish; `RELEASE_STATUS.txt`

#### Wave 1 — Real publish

- Configurable `forge_plot_to_pdf` (device, paper, CTB/STB, area, orientation, scale)
- DSD `forge_plot_publish` via `Publisher.PublishDsd` + PDF verify (seat-dependent; see 0.2.1 honesty)
- Sync layer-state restore and page-setup apply
- Overwrite acknowledgement for PDF/publish/pack
- Undo marks around typed writes

#### Wave 2 — Moat QA + campaign

- `forge_qa_preflight` + structured `QaReport` artifacts
- `forge_block_campaign` titleblock campaigns with dry-run diffs
- `forge_xref_normalize_relative`
- `forge_system_capabilities`
- PublishReceipt + PDF probes + IssueSetContract + pack v2 + differential receipts + sheet inventory + batch resume

#### Wave 3 — Scale

- `forge_batch_run` AccoreConsole job queue
- `forge_recipe_issue_set` issue-set orchestrator
- `forge_pack_and_go` + `manifest.json`
- Eval scenarios under `evals/`

### Security

- Removed `default-secret-token` production fallback
- Scoped open-world `*` denylist; headless AccoreConsole = server-side SafetyPolicy only (ADR 0002)

## [0.1.0] - 2026-07-11

### Added

- Phase 0 foundation: MCP stdio server, AutoCAD 2026 plugin, shared safety/audit/backup
- Named pipe transport with ACL + token auth
- Typed hot-path tools and gated open-world executors
- `765T-Forge.ServerOnly.slnf` and unit tests

[0.3.0]: https://github.com/765T/765T-Forge/releases/tag/v0.3.0
[0.2.1]: https://github.com/765T/765T-Forge/releases/tag/v0.2.1
[0.2.0]: https://github.com/765T/765T-Forge/releases/tag/v0.2.0
[0.1.0]: https://github.com/765T/765T-Forge/releases/tag/v0.1.0
