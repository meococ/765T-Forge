# Versioning

765T-Forge uses **four independent version axes**. Do not collapse them into one number.

## 1. Product SemVer (Git tags / CHANGELOG)

- Format: `MAJOR.MINOR.PATCH` (example `0.1.0`, `0.2.0`, current in-tree `0.3.0` Unreleased)
- Source of truth: [CHANGELOG.md](../CHANGELOG.md) and Git tags `v*`
- **0.1.x** — Phase 0 foundation + Wave 0 publish hygiene (preview)
- **0.2.0** — Waves 1–2 (real DSD publish, preflight, campaign, capabilities) and Wave 3 packaging as available
- **0.2.1** — Technical Preview: exclusive AEC gates, publish honesty / DSD→plot fallback, dual-zip + getting-started
- **0.3.0** — Unreleased: force env gate, ceremony evidence IDs, nested xref depth report, DSD diagnostics (tag after dual-seat smoke)
- Breaking MCP tool renames or envelope changes bump **MINOR** while major is `0`, or **MAJOR** after `1.0.0`

## 2. Phase (docs / brief)

| Phase | Meaning |
|-------|---------|
| Phase 0 | Foundation: transport, safety, audit, backup, first-pass hot path |
| Phase 1+ / Waves 1–3 | Product roadmap in the GitHub plan (publish, moat QA, scale) |
| Phase 4 (brief) | Evals / broader coverage — tracked separately from SemVer |

Phase labels are **documentation milestones**, not NuGet or Git tag versions.

## 3. MCP protocol / SDK

- Wire protocol version is defined by the Model Context Protocol specification and the `ModelContextProtocol` package version in `Directory.Packages.props`.
- Product `0.1.0` may ship on MCP SDK `1.3.0` (or whatever is pinned) — those numbers are unrelated.
- When upgrading the SDK, note it under Changed in the CHANGELOG; do not retag product solely for a patch SDK bump unless behavior breaks.

## 4. AutoCAD target

- Current supported host: **AutoCAD 2026** (`net8.0-windows`, `AUTOCAD_2026_ROOT`).
- A future AutoCAD year is a **platform target**, documented explicitly (and may require a minor product bump plus matrix updates).
- Plugin binaries are not interchangeable across AutoCAD major years without rebuild.

## Assembly / file versions

Project `AssemblyVersion` / `FileVersion` may lag or lead product SemVer during development. **Release tags and CHANGELOG win** for what users should cite.

## Envelope / wire compatibility

If the named-pipe JSON command/result shape breaks, document it as a breaking change even when MCP tool names stay the same. Prefer additive fields when possible.

## Quick reference

| Question | Look at |
|----------|---------|
| What did we ship to GitHub? | Product SemVer + CHANGELOG |
| How mature is the codebase vs brief? | Phase / Wave docs |
| Which MCP SDK? | `Directory.Packages.props` |
| Which CAD year? | README + this file (AutoCAD 2026) |
