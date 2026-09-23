# 765T-Forge

[![CI Server](https://github.com/765T/765T-Forge/actions/workflows/ci-server.yml/badge.svg)](https://github.com/765T/765T-Forge/actions/workflows/ci-server.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/765T/765T-Forge?include_prereleases)](https://github.com/765T/765T-Forge/releases)

All-C# MCP server and AutoCAD plugin for **metro/AEC issue-set drawing production** — inspect → fix → fill → gate → publish → verify.

**Technical Preview** — Windows. Not affiliated with Autodesk. See [NOTICE.md](NOTICE.md).

**Download:** [Latest Release](https://github.com/765T/765T-Forge/releases) — you need **both** `765T-Forge.Server-win-x64.zip` and `765T-Forge.Plugin.zip`.

## AutoCAD support

Autodesk's official .NET matrix for AutoCAD:

| AutoCAD | .NET |
|---|---|
| 2017, 2018 | .NET Framework 4.6 |
| 2019, 2020 | .NET Framework 4.7 |
| 2021, 2022, 2023, 2024 | .NET Framework 4.8 |
| 2025, 2026 | .NET 8 |
| 2027 | .NET 10 |

Forge ships two plugin components:

| Bundle component | AutoCAD | Build |
|---|---|---|
| `Contents/2017/Forge.Plugin.dll` | 2017–2024 (`SeriesMin=R21.0 SeriesMax=R24.3`) | `net462`, compiled against **AutoCAD 2017** reference assemblies |
| `Contents/2025/Forge.Plugin.dll` | 2025–2026 (`SeriesMin=R25.0 SeriesMax=R26.0`) | `net8.0-windows` |

**Honest limitation — read this before trusting the table above:** the 2017–2024 (`net462`)
assembly has been *prepared* but is **not yet compiled on a machine with AutoCAD 2017 reference
assemblies**. 2017–2024 runtime support is therefore **not yet verified**. The verification gate is
`scripts/verify-plugin-series.ps1 -Series 2017`, run on a machine that has those reference
assemblies; CI is expected to run it per series. Until that has been recorded, treat only
AutoCAD 2025–2026 as verified for the modern build and treat the legacy component as untested.

## Positioning

Forge competes on **reliable sheet-set publish**, not geometry tool count:

- Configurable plot (device / paper / CTB·STB) — single-layout `forge_plot_to_pdf` smoke-verified
- DSD multi-layout publish path + PDF verify (**partial** on some seats; fallback to per-layout plot)
- Publish readiness gate (`forge_qa_preflight`)
- Titleblock campaigns + issue-set recipe
- Audit, backup, dry-run, named-pipe ACL + required token

Honest status: [docs/capability-matrix.md](docs/capability-matrix.md) · Start here: [docs/getting-started.md](docs/getting-started.md) · Roadmap: [docs/roadmap.md](docs/roadmap.md)

Historical planning notes live under [docs/archive/](docs/archive/) — not the shipped tool list.

## Shape

- `src/Forge.Server` — MCP stdio server (`ModelContextProtocol` 1.3.0)
- `src/Forge.Plugin` — AutoCAD plugin (named pipe); `net8.0-windows` by default, `net462` with `-p:ForgeBuildLegacy=true`
- `src/Forge.Shared` — `netstandard2.0` envelope, capability gate, audit, backup, QA contracts (referenced by both sides)
- `tests/Forge.Tests` — unit tests (no AutoCAD required)
- `skills/765t-forge` — companion agent skill
- `examples/` — Cursor / Claude Desktop / VS Code MCP configs

Not shipped, not part of the product, and untracked in git: `docs/research/` (Taluy cut/fill R&D notes) and the R&D DWGs, scripts, and logs dropped under `tests/` (for example `tests/fixtures/taluy/` and `tests/taluy-*.log`). They are local research material; the tracked unit tests are `tests/Forge.Tests/`.

## Requirements

- Windows + .NET 8 SDK to build the server and tests; .NET 8 runtime for the Release server zip
- An AutoCAD install matching the plugin component you build:
  - modern build: AutoCAD 2025 or 2026 (.NET 8) reference assemblies
  - legacy build: AutoCAD 2017 reference assemblies (`-p:ForgeBuildLegacy=true`); no fallback
- `FORGE_AUTOCAD_TOKEN` (or `MCP_AUTOCAD_TOKEN`) **required** — no production default secret

## Quick install

See **[docs/getting-started.md](docs/getting-started.md)** for the full dual-install matrix (Release zips or build-from-source, Autoloader, Cursor / Claude / VS Code).

```powershell
.\scripts\build-server.ps1 -Configuration Release
.\scripts\build-plugin.ps1 -Configuration Release
.\scripts\install-plugin.ps1 -Configuration Release -SkipBuild
```

Then copy [examples/mcp.cursor.json](examples/mcp.cursor.json) and set the token + absolute `Forge.Server.exe` path.

## Build

Server, shared, and tests (no AutoCAD required):

```powershell
$env:FORGE_DEV_ALLOW_DEFAULT_TOKEN = "true"   # tests / local only
dotnet restore .\765T-Forge.ServerOnly.slnf
dotnet build .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test  .\765T-Forge.ServerOnly.slnf --no-build
```

Plugin (needs AutoCAD reference assemblies):

| Variable | Used by | Notes |
|---|---|---|
| `FORGE_AUTOCAD_MODERN_ROOT` | `net8.0-windows` | First choice; must contain `AcCoreMgd.dll`, `AcDbMgd.dll`, `AcMgd.dll` |
| `AUTOCAD_2025_ROOT` | `net8.0-windows` | Second choice |
| `FORGE_AUTOCAD_ROOT` | `net8.0-windows` | Third choice |
| `FORGE_AUTOCAD_LEGACY_ROOT` | `net462` | Required for the legacy TFM; no fallback |
| `AUTOCAD_2017_ROOT` | `net462` | Deprecated alias, still honored |

If none of the modern roots is set, the build resolves the installed AutoCAD 2025/2026 install
(`C:\Program Files\Autodesk\AutoCAD 2025` / `2026`) by exact `Exists` checks and **fails with an
explicit error** if the three assemblies are missing. The legacy build never substitutes a newer
series: with `FORGE_AUTOCAD_LEGACY_ROOT` unset, `dotnet build -p:ForgeBuildLegacy=true` fails.

```powershell
dotnet build .\765T-Forge.sln                                                        # modern
dotnet build .\src\Forge.Plugin\Forge.Plugin.csproj -p:ForgeBuildLegacy=true -c Release   # legacy (needs 2017 refs)
.\scripts\verify-plugin-series.ps1 -Series 2017,2025                                 # deterministic per-series gate
```

`verify-plugin-series.ps1` asserts by exact `Test-Path` that the requested series' reference
assemblies exist and then builds that series. It exits non-zero naming the exact missing path; it
never skips silently.

If AutoCAD has `NETLOAD`ed the plugin, use `765T-Forge.ServerOnly.slnf` or `-p:OutDir=artifacts\Forge.Plugin\`.

## Configuration

| Variable | Notes |
|---|---|
| `FORGE_AUTOCAD_TOKEN` / `MCP_AUTOCAD_TOKEN` | **Required** shared secret for named pipe |
| `FORGE_DEV_ALLOW_DEFAULT_TOKEN` | Dev-only escape hatch (`true` → insecure token) |
| `FORGE_PIPE_NAME` | Default `765T.Forge.AutoCAD` |
| `FORGE_BACKUP_DIR` / `FORGE_AUDIT_DIR` | Under `%LOCALAPPDATA%\765T-Forge\` by default |
| `FORGE_AUTOCAD_ROOT` | AutoCAD install root (runtime `accoreconsole.exe` + build fallback) |
| `AUTOCAD_2026_ROOT` | **Deprecated** alias for `FORGE_AUTOCAD_ROOT`; still honored |
| `FORGE_ENABLE_UNSAFE_OPS` | Default `false`; with per-call `unsafeAcknowledged=true` allows the five free-text executors |
| `FORGE_ALLOW_FORCE_PUBLISH` | Default `false`; required for `force=true` on publish/recipe |
| `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS` | Default `120` |

With `FORGE_AUTOCAD_ROOT` unset, the server discovers the highest installed
`C:\Program Files\Autodesk\AutoCAD <4-digit year>` that contains `accoreconsole.exe`.

## Safety

- Deterministic capability gate: the five free-text executors are `Unsafe` and need `FORGE_ENABLE_UNSAFE_OPS=true` plus per-call `unsafeAcknowledged=true`; otherwise `unsafe_not_acknowledged`
- No text denylist and no keyword filtering anywhere — argument text is never inspected
- Dry-run, backup before writes, JSONL audit (`server-`/`plugin-` daily files, secrets redacted)
- Overwrite acknowledgement for SaveAs / PDF / publish / pack
- Publish preflight can refuse to plot
- Publish receipts verify existence, non-zero length, and the exact `%PDF-` header; page count is reported as `null` / `not_available`
- If a plugin call times out, a write may still have committed — read-back before retry

See [docs/safety.md](docs/safety.md) and [SECURITY.md](SECURITY.md).

## Versioning

Product SemVer **0.3.0** (Unreleased) ≠ roadmap phase ≠ MCP protocol. Last tagged TP: **0.2.1**. See [docs/versioning.md](docs/versioning.md).

## License

MIT — [LICENSE](LICENSE). Autodesk APIs/DLLs are **not** redistributed.
