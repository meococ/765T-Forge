# Versioning

765T-Forge uses **four independent version axes**. Do not collapse them into one number.

## 1. Product SemVer (Git tags / CHANGELOG)

- Format: `MAJOR.MINOR.PATCH` (example `0.1.0`, `0.2.0`, current in-tree `0.3.0` Unreleased)
- Source of truth: [CHANGELOG.md](../CHANGELOG.md) and Git tags `v*`
- **0.1.x** — Phase 0 foundation + Wave 0 publish hygiene (preview)
- **0.2.0** — Waves 1–2 (real DSD publish, preflight, campaign, capabilities) and Wave 3 packaging as available
- **0.2.1** — Technical Preview: exclusive AEC gates, publish honesty / DSD→plot fallback, dual-zip + getting-started
- **0.3.0** — Unreleased: force env gate, ceremony evidence IDs, nested xref depth report, DSD diagnostics, deterministic capability gate replacing the text denylist, dual-TFM plugin build (tag after dual-seat smoke)
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

## 4. AutoCAD target / plugin series

Autodesk's official .NET matrix:

| AutoCAD | .NET | Forge plugin component |
|---------|------|------------------------|
| 2017, 2018 | .NET Framework 4.6 | `net462`, Series R21.0+ |
| 2019, 2020 | .NET Framework 4.7 | `net462` |
| 2021–2024 | .NET Framework 4.8 | `net462` (Series up to R24.3) |
| 2025, 2026 | .NET 8 | `net8.0-windows` (verified) |
| 2027 | .NET 10 | `net8.0-windows` artifact target (Autodesk forward-compatibility; not tested by Forge) |

- The `net462` component must be compiled against **AutoCAD 2017 reference assemblies**; there is no fallback to a newer series.
- **Verified vs prepared:** the `net8.0-windows` component is the verified path (2025/2026). The `net462` component has been **prepared but not yet compiled on a machine with AutoCAD 2017 reference assemblies**, so 2017–2024 runtime support is **not yet verified**. The gate is `scripts/verify-plugin-series.ps1 -Series 2017`; CI must run it per series.
- Plugin binaries are not interchangeable across AutoCAD major years without rebuild.
- A new AutoCAD year is a **platform target**, documented explicitly (and may require a minor product bump plus matrix updates).

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
| Which CAD year? | README support matrix + plugin series + this file |
