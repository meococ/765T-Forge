# 765T-Forge

[![CI Server](https://github.com/765T/765T-Forge/actions/workflows/ci-server.yml/badge.svg)](https://github.com/765T/765T-Forge/actions/workflows/ci-server.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Release](https://img.shields.io/github/v/release/765T/765T-Forge?include_prereleases)](https://github.com/765T/765T-Forge/releases)

All-C# MCP server and AutoCAD 2026 plugin for **metro/AEC issue-set drawing production** — inspect → fix → fill → gate → publish → verify.

**Technical Preview** — Windows + AutoCAD **2026** only. Not affiliated with Autodesk. See [NOTICE.md](NOTICE.md).

**Download:** [Latest Release](https://github.com/765T/765T-Forge/releases) — you need **both** `765T-Forge.Server-win-x64.zip` and `765T-Forge.Plugin.zip`.

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
- `src/Forge.Plugin` — AutoCAD 2026 .NET plugin (named pipe)
- `src/Forge.Shared` — envelope, safety, audit, backup, QA report
- `tests/Forge.Tests` — unit tests (no AutoCAD required)
- `skills/765t-forge` — companion agent skill
- `examples/` — Cursor / Claude Desktop / VS Code MCP configs

## Requirements

- Windows + .NET 8 (SDK to build; runtime for Release zip)
- AutoCAD 2026 (for plugin / live tools)
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

```powershell
$env:FORGE_DEV_ALLOW_DEFAULT_TOKEN = "true"   # tests / local only
dotnet restore .\765T-Forge.ServerOnly.slnf
dotnet build .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test .\765T-Forge.ServerOnly.slnf --no-build
```

Full solution (needs AutoCAD at `AUTOCAD_2026_ROOT`, default `C:\Program Files\Autodesk\AutoCAD 2026`):

```powershell
dotnet build .\765T-Forge.sln
```

If AutoCAD has `NETLOAD`ed the plugin, use `765T-Forge.ServerOnly.slnf` or `-p:OutDir=artifacts\Forge.Plugin\`.

## Configuration

| Variable | Notes |
|---|---|
| `FORGE_AUTOCAD_TOKEN` / `MCP_AUTOCAD_TOKEN` | **Required** shared secret for named pipe |
| `FORGE_DEV_ALLOW_DEFAULT_TOKEN` | Dev-only escape hatch (`true` → insecure token) |
| `FORGE_PIPE_NAME` | Default `765T.Forge.AutoCAD` |
| `FORGE_BACKUP_DIR` / `FORGE_AUDIT_DIR` | Under `%LOCALAPPDATA%\765T-Forge\` by default |
| `AUTOCAD_2026_ROOT` | AutoCAD install root (HintPath + accoreconsole) |
| `FORGE_ENABLE_UNSAFE_OPS` | Enables `forge_exec_dotnet` when also acknowledged |
| `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS` | Default `120` |

## Safety

- Denylist on open-world executors; typed tools use structured validation
- Dry-run, backup before writes, JSONL audit
- Overwrite acknowledgement for SaveAs / PDF / publish / pack
- Publish preflight can refuse to plot
- If a plugin call times out, a write may still have committed — read-back before retry

See [docs/safety.md](docs/safety.md) and [SECURITY.md](SECURITY.md).

## Versioning

Product SemVer **0.3.0** (Unreleased) ≠ roadmap phase ≠ MCP protocol. Last tagged TP: **0.2.1**. See [docs/versioning.md](docs/versioning.md).

## License

MIT — [LICENSE](LICENSE). Autodesk APIs/DLLs are **not** redistributed.
