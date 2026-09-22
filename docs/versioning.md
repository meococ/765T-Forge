# Versioning

765T-Forge uses **four independent version axes**. Do not collapse them into one number.

## 1. Product SemVer (Git tags / CHANGELOG)

- Format: `MAJOR.MINOR.PATCH` (example `0.1.0`, `0.2.0`)
- Source of truth: [CHANGELOG.md](../CHANGELOG.md) and Git tags `v*`
- **0.1.x** — Phase 0 foundation + Wave 0 publish hygiene (preview)
- **0.2.0** — Waves 1–2 (real DSD publish, preflight, campaign, capabilities) and Wave 3 packaging as available
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

- **Default verified host: AutoCAD 2026** (`net8.0-windows`, `AUTOCAD_2026_ROOT`, series R25.1). Smoke tests and CI plugin builds use this year. `ForgeConstants.AutoCadVersion` stays `2026`.
- **Build targets: AutoCAD 2017–2026.** Each year is a separate `Forge.Plugin` output (`-p:AutoCadYear=`, or `scripts/build-plugin.ps1 -Year` / `-AllYears`) referenced from `AUTOCAD_<year>_ROOT` (default `C:\Program Files\Autodesk\AutoCAD <year>`). The year table lives in `AutoCadHostCatalog`.
- TFM follows the host CLR where the SDK can compile our code: 2019–2020 `net47`, 2021–2024 `net48`, 2025–2026 `net8.0-windows`. 2017–2018 would be `net46`, but `System.Text.Json` 8.0.5 cannot restore for `net46` (NU1202; its lowest framework TFM is `net462`), so those years compile as **`net462`**. That is not a `net48` binary, and it is **not claimed to NETLOAD** on AutoCAD 2017–2018. `forge_exec_dotnet` on those years returns `autocad_version_unsupported`.
- 2017–2025 are **not smoke-tested** in this change. Plugin binaries are **not interchangeable** across major years. Autoloader `SeriesMin` = `SeriesMax` for that R-series only.
- Adding another AutoCAD year is a platform target (catalog + csproj + `PackageContents.xml` + matrix). It does not by itself bump product SemVer.

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
| Which CAD year? | README + this file. Default verified host is AutoCAD 2026; 2017–2025 are per-year build targets |
