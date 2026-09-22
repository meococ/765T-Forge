# 765T-Forge

All-C# MCP server and AutoCAD 2026 plugin for **metro/AEC issue-set drawing production** — inspect → fix → fill → gate → publish → verify.

Not affiliated with Autodesk. The default verified host is a licensed AutoCAD 2026 seat. Plugin projects also build for AutoCAD 2017–2025 when that year's install is present; those builds are not smoke-tested, and binaries are not interchangeable across years. See [NOTICE.md](NOTICE.md).

## Positioning

Forge competes on **reliable sheet-set publish**, not geometry tool count:

- Configurable plot (device / paper / CTB·STB)
- Real DSD multi-layout publish with PDF verify
- Publish readiness gate (`forge_qa_preflight`)
- Titleblock campaigns + issue-set recipe
- Audit, backup, dry-run, named-pipe ACL + required token

Honest status: [docs/capability-matrix.md](docs/capability-matrix.md) · Roadmap: [docs/roadmap.md](docs/roadmap.md) · Smoke lab: [docs/smoke-lab.md](docs/smoke-lab.md)

The Vietnamese [docs/history/build-brief.md](docs/history/build-brief.md) is an **aspirational catalog / decision log** — not the shipped tool list. Prefer the capability matrix for what works today.

**Release note:** GitHub Actions packs the **server** zip on tag; maintainers must also attach **`765T-Forge.Plugin.zip`** (AutoCAD required to build). Server alone is not a usable install.

## Shape

- `src/Forge.Server` — MCP stdio server (`ModelContextProtocol` 1.3.0)
- `src/Forge.Plugin` — AutoCAD 2026 .NET plugin (named pipe)
- `src/Forge.Shared` — envelope, safety, audit, backup, QA report
- `tests/Forge.Tests` — unit tests (no AutoCAD required)
- `skills/765t-forge` — companion agent skill
- `examples/` — Cursor / Claude Desktop MCP configs

## Requirements

- Windows + .NET 8 SDK
- AutoCAD 2026 for the verified plugin / live tools. Other years: set `AUTOCAD_<year>_ROOT` and build with `-p:AutoCadYear=<year>` (2017–2025 are not smoke-tested)
- `FORGE_AUTOCAD_TOKEN` (or `MCP_AUTOCAD_TOKEN`) **required** — no production default secret

## Quick install (Cursor)

1. Build server: `.\scripts\build-server.ps1`
2. Build plugin (with AutoCAD installed): `.\scripts\build-plugin.ps1`
3. Install plugin copy + checklist: `.\scripts\install-plugin.ps1`
4. `NETLOAD` the installed `Forge.Plugin.dll`, run `MCP_STATUS`
5. Copy [examples/mcp.cursor.json](examples/mcp.cursor.json) into your MCP config and set the token + exe path

Details: [docs/install-cursor.md](docs/install-cursor.md) · [docs/install-claude-desktop.md](docs/install-claude-desktop.md) · [docs/install-plugin.md](docs/install-plugin.md)

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
| `AUTOCAD_<year>_ROOT` | That year's AutoCAD install (plugin HintPath). `AUTOCAD_2026_ROOT` is the default verified host and the accoreconsole path |
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

Product SemVer **0.2.0** ≠ roadmap phase ≠ MCP protocol. See [docs/versioning.md](docs/versioning.md).

## License

MIT — [LICENSE](LICENSE). Autodesk APIs/DLLs are **not** redistributed.
