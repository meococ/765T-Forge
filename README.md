# 765T-Forge

<!-- mcp-name: io.github.meococ/765t-forge -->

English | [Tiếng Việt](README.vi.md)

All-C# MCP server + AutoCAD plugin for **metro/AEC issue-set drawing production** — inspect → fix → fill → gate → publish → verify, through typed tools instead of command strings.

Not affiliated with Autodesk. Requires a licensed AutoCAD seat. See [NOTICE.md](NOTICE.md).

## What it is

- **125 typed tools** behind toolset filters. The default session exposes ~19 core/discovery tools (~3.7k tokens of `tools/list`) — agents widen at runtime with `forge_toolset_search`/`forge_toolset_enable`, or set `FORGE_TOOLSETS=all` to list the full catalog up front.
- **Real production path:** configurable plot (device/paper/CTB·STB/area/scale) and real DSD multi-layout publish with PDF bookmarks + verify.
- **A quality gate, not just draw tools:** `forge_qa_preflight` can refuse to publish on xref, titleblock, `####` field or standards-pack failures; `forge_qa_native_objects` flags exploded dimensions/hatches/blocks in reused sources; `forge_qa_annotation_clash` finds text overlaps and linework through text.
- **Authoring surface:** `forge_draw_*` (lines to NURBS splines, dimensions, hatches, tables, MLeaders, walls, column grids), `forge_entity_*` editing by explicit handle, blocks/attributes, layer/layout/style management.
- **MCP core v2:** structured output on every tool, progress notifications, elicitation for destructive confirmations, MCP tasks, live `forge://` drawing resources, argument completions, real cancellation.
- **Safety model:** denylist on open-world executors, dual safety evaluation (server + plugin), dry-run on every write, pre-write DWG backups, JSONL audit, named-pipe ACL + required token, unsafe ops off by default, `retryable`+`hint` failure metadata that stops blind retry loops.

Measured on this codebase: an outcome eval of 32 runs (fresh agent sessions, MCP-only) over 8 **synthetic** drafting tasks passed every v2 check. Synthetic tasks measure plumbing and discipline — they are not a promise about your real drawings.

## Tested versions

| AutoCAD | Status |
|---------|--------|
| 2025 | **Live-tested** — the full tool surface was exercised against a running 2025 host |
| 2026 | **Built, not live-tested** — compiles against the 2026 managed references; unproven in a live session |

## Requirements

- Windows + .NET 8 SDK
- AutoCAD 2025 (live-tested) or 2026 (build supported) for the plugin / live tools
- `FORGE_AUTOCAD_TOKEN` (or `MCP_AUTOCAD_TOKEN`) — **required**, no production default secret

## Quick install

### 1. Build and install the plugin (machine with AutoCAD)

```powershell
.\scripts\build-plugin.ps1 -Configuration Release -Year 2025
.\scripts\install-plugin.ps1 -Configuration Release -Year 2025 -SkipBuild
```

Default install dir: `%LOCALAPPDATA%\765T-Forge\plugin\<Year>\Forge.Plugin.dll`.

Then:

1. Add the install dir to AutoCAD **Trusted Locations** (`TRUSTEDPATHS`).
2. Set `FORGE_AUTOCAD_TOKEN` (and optional `FORGE_PIPE_NAME`) for the **AutoCAD process** — must match the MCP server.
3. `NETLOAD` the installed DLL, then run `MCP_STATUS` in AutoCAD.
4. From the MCP client, call `forge_system_health` — the `pipe` field must match your configured pipe name.

**Autoloader alternative:** copy `plugin-bundle/765T-Forge.bundle` into `%APPDATA%\Autodesk\ApplicationPlugins` (per-user; preferred) or `%PROGRAMFILES%\Autodesk\ApplicationPlugins` (machine-wide, admin). Avoid `%PROGRAMDATA%` on 2026 — Autodesk tightened ProgramData auto-load. Build/bundle per year: 2025 = Series R25.0, 2026 = R25.1.

**Releases:** GitHub-hosted CI packs the **server** zip only (no AutoCAD on runners). A usable install needs both `765T-Forge.Server-win-x64.zip` and `765T-Forge.Plugin.zip`.

### 2. Point your MCP client at Forge.Server.exe

The env block is identical across clients; only the config file and key name differ.

```json
{
  "command": "C:\\path\\to\\765T-Forge\\src\\Forge.Server\\bin\\Release\\net8.0\\Forge.Server.exe",
  "args": [],
  "env": {
    "FORGE_AUTOCAD_TOKEN": "replace-with-a-long-random-secret",
    "FORGE_PIPE_NAME": "765T.Forge.AutoCAD",
    "FORGE_TOOLSETS": "all",
    "FORGE_BACKUP_DIR": "%LOCALAPPDATA%\\765T-Forge\\backups",
    "FORGE_AUDIT_DIR": "%LOCALAPPDATA%\\765T-Forge\\audit"
  }
}
```

- **Claude Code** — drop it as `mcpServers."765t-forge"` in the project `.mcp.json`, or:
  `claude mcp add-json 765t-forge '{"command":"...","env":{...}}'`
- **Claude Desktop** — same `mcpServers` block in `%APPDATA%\Claude\claude_desktop_config.json`; fully restart the app.
- **Cursor** — same `mcpServers` block in the user or project `mcp.json`.
- **VS Code (Copilot)** — same shape but under the `servers` key in `.vscode/mcp.json`, plus `"type": "stdio"`. Put the token in an `inputs` `promptString` so it stays out of the checked-in file.

`FORGE_TOOLSETS=all` lists every tool immediately; omit it for the minimal default surface (clients that handle `tools/list_changed` widen on demand instead).

### `dnx` (after the NuGet package exists)

```powershell
dnx Forge765T.Server@<pinned-version> --yes
```

Pin a version; never `@latest`.

## Configuration

| Variable | Notes |
|---|---|
| `FORGE_AUTOCAD_TOKEN` / `MCP_AUTOCAD_TOKEN` | **Required** shared secret for the named pipe; must match on server and AutoCAD processes |
| `FORGE_DEV_ALLOW_DEFAULT_TOKEN` | Dev-only escape hatch (`true` → insecure token); never in production |
| `FORGE_PIPE_NAME` | Default `765T.Forge.AutoCAD` |
| `FORGE_BACKUP_DIR` / `FORGE_AUDIT_DIR` | Under `%LOCALAPPDATA%\765T-Forge\` by default |
| `AUTOCAD_2026_ROOT` | AutoCAD install root override (plugin references + accoreconsole) |
| `FORGE_ENABLE_UNSAFE_OPS` | Enables `forge_exec_dotnet` when also acknowledged per call |
| `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS` | Default `120`; drives the plugin watchdog (`plugin_request_timeout`) at timeout−5 s |
| `FORGE_PLUGIN_CONNECT_TIMEOUT_SECONDS` | Default `10` (clamp 1–120) |
| `FORGE_TOOLSETS` | Comma list or `all`; default loads core + discovery tools (~19 tools ≈3.7k tokens) |

## Tools

125 tools, grouped. The `forge://docs/capability-matrix` MCP resource always reflects the live registry (generated, not a hand-maintained list).

| Group | Examples |
|---|---|
| system / document | `forge_system_health`, `forge_system_version`, `forge_doc_open`/`_save`/`_close`/`_activate`, `forge_doc_list_open`, `forge_doc_new`, `forge_doc_import` (DXF/DWG→block/merge/xref), `forge_doc_export_dxf` |
| layer / layout / viewport | `forge_layer_list`, `forge_layer_create`, `forge_layer_delete` (dry-run default; refuses 0/current/xref-dependent/in-use), `forge_layer_state_*`, `forge_layout_create`, `forge_layout_page_setup_import`/`_apply`, `forge_viewport_add`/`_list`, `forge_viewport_set_layer_freeze` |
| draw / entity | `forge_draw_line…revcloud` (15 primitives incl. spline, hatch, dimension, table, leader, grid, wall), `forge_draw_batch` (≤1000 steps), `forge_entity_get`/`_list`, `forge_entity_move`/`copy`/`rotate`/`scale`/`mirror`/`offset`, `forge_entity_fillet`/`chamfer`/`trim`/`extend`/`join`/`explode`, `forge_entity_erase`/`set_props`/`array`/`intersect`/`snap`, `forge_undo` |
| block / style | `forge_block_list`/`_get_attr`/`_set_attr`/`_campaign`, `forge_block_create`/`_insert`/`_insert_bulk`, `forge_linetype_load`, `forge_style_text_create`, `forge_style_dim_create`, `forge_standards_import`, `forge_text_list` |
| plot / publish | `forge_plot_to_pdf` (configurable device/paper/CTB/area/scale, no viewer), `forge_plot_preset_apply`, `forge_plot_publish` (real DSD + bookmarks + preflight gate) |
| qa | `forge_qa_preflight`, `forge_qa_native_objects` (exploded dims/hatches/blocks), `forge_qa_annotation_clash`, `forge_qa_verify_titleblock`, `forge_qa_check_xrefs`, `forge_qa_audit_layers`, `forge_qa_readback`, `forge_qa_readback_after_timeout`, `forge_audit_summarize` |
| recipe / registry / standards | `forge_recipe_issue_set`, `forge_issue_set_validate`/`_diff`, `forge_sheet_inventory_import`, `forge_registry_load`/`_lookup`, `forge_pack_load`/`_status` |
| linework | `forge_linework_dump`/`_trace`/`_topology`/`_coverage`/`_segments`/`_compare`/`_transform` — CAD↔model comparison (Revit-side anchors) |
| conversion / batch | `forge_dgn_import` (DGN V8 → DWG, verified), `forge_pack_and_go`, `forge_batch_run`/`_status` (AccoreConsole queue + resume) |
| capture / measure | `forge_view_capture` (PNG of the AutoCAD window — visual read-back), `forge_measure_batch`, `forge_drawing_summary`, `forge_entity_search_text` |
| exec (open-world, denylisted) | `forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_exec_dotnet` (unsafe-gated) |
| meta (always on) | `forge_toolset_list`, `forge_toolset_search`, `forge_toolset_enable` |

Known partials: `forge_exec_lisp` synchronous eval is context-limited on the pipe lane (queued fallback works); `forge_layout_page_setup_import` prefers the sync PlotSettings copy — a queued `-PSETUPIN` fallback can't be scoped against proxy dialogs. Full SSM write, Civil 3D and remote multi-tenant serving are out of scope.

## Safety model (short version)

- Open-world executors are regex-denylisted (`ERASE ALL`, `PURGE`, broad `ALL`/`*` selection, …); typed tools skip the scan — unknown tools fail closed as destructive.
- Safety is evaluated on **both** server and plugin for pipe tools; headless `forge_run_script`/`forge_batch_run` are server-side only.
- Every write accepts `dryRun=true`. Writes that mutate the DWG copy it to `FORGE_BACKUP_DIR` first; every call lands in the `FORGE_AUDIT_DIR` JSONL audit.
- Destructive path overwrites need `overwriteAcknowledged=true`; `forge_doc_close` refuses unsaved changes unless told otherwise; `forge_layer_delete` is dry-run by default and refuses protected/in-use layers.
- If a plugin call times out, the write may already have committed — read back (`forge_qa_*`) instead of retrying; `plugin_request_timeout`/`plugin_request_stale` bound the window.
- Failures carry `retryable`+`hint`: validation and plugin exceptions are non-retryable — change inputs or report, don't repeat identical calls.
- `forge_exec_dotnet` needs `FORGE_ENABLE_UNSAFE_OPS=true` **and** per-call `unsafeAcknowledged=true`. There is no sandbox.

## Troubleshooting

- **Pipe down / health fails:** AutoCAD running? Plugin `NETLOAD`ed and `MCP_STATUS` OK? `FORGE_PIPE_NAME` and `FORGE_AUTOCAD_TOKEN` identical on both processes? Only one plugin pipe instance is expected — restart AutoCAD if a previous session wedged it.
- **NETLOAD blocked:** the DLL must sit under AutoCAD `TRUSTEDPATHS`; use `install-plugin.ps1` and follow its checklist.
- **Locked DLLs while AutoCAD is open:** Windows locks `Forge.Plugin.dll` after NETLOAD — build with `765T-Forge.ServerOnly.slnf`, or `-p:OutDir=` a scratch path, or close AutoCAD before overwriting.
- **Token missing / auth denied:** set `FORGE_AUTOCAD_TOKEN` in the MCP host env **and** for the AutoCAD process, restart AutoCAD, re-NETLOAD.
- **Timeout after a write:** assume it may have committed; `forge_qa_readback_after_timeout` before retrying.
- **Release artifacts:** CI packs server only — you need **both** zips for a working install.

## Build from source

```powershell
$env:FORGE_DEV_ALLOW_DEFAULT_TOKEN = "true"   # tests / local only
dotnet restore .\765T-Forge.ServerOnly.slnf
dotnet build   .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test    .\765T-Forge.ServerOnly.slnf --no-build
```

Full solution (needs AutoCAD references; `AUTOCAD_2026_ROOT` overrides the default root):

```powershell
dotnet build .\765T-Forge.sln
dotnet test  .\765T-Forge.sln
```

Scripts: `scripts\build-server.ps1`, `build-plugin.ps1`, `install-plugin.ps1`, `pack-release.ps1`, `deploy-server.ps1` (SHA-verified deploy folders), `launch-acad-instance.ps1`/`stop-acad-instance.ps1` (parallel-instance harness).

## Contributing

- Server/server-only changes: keep `dotnet test .\765T-Forge.ServerOnly.slnf` green; CI runs it on `windows-latest` with `--locked-mode`.
- Plugin changes: build per year via `scripts\build-plugin.ps1 -Year`; say what you NETLOAD-tested.
- Adding a tool needs three synchronized edits: the `[McpServerTool]` method in `src/Forge.Server/ForgeMcpTools.cs`, the `ForgeToolRegistry` entry in `src/Forge.Shared/ToolMetadata.cs`, and the dispatch case in `src/Forge.Plugin/PluginCommandProcessor.cs`. Unregistered tools fail closed as destructive.
- Prefer typed tools over open-world executors; keep dual safety evaluation intact; add `SafetyPolicyTests` for new deny codes.
- Never commit tokens, customer DWGs, or Autodesk binaries. Generated `*.log` stay out of git.

## Reporting vulnerabilities

Report privately — do not file public issues for security findings. Preferred: **GitHub Security Advisories** ("Report a vulnerability" on this repo). Fallback: the GitHub organization profile contact. Include affected component (`Forge.Server`, `Forge.Plugin`, pipe protocol), reproduction steps, impact. We aim to acknowledge within 7 days — give us a reasonable window before public disclosure.

Trust boundary notes: the pipe ACL limits to the current user + LocalSystem and the token is defense-in-depth — keep `FORGE_AUTOCAD_TOKEN` strong and unique; `forge_exec_dotnet` is the highest-risk path and stays double-gated.

## Versioning

Product SemVer **0.3.0** (git tags + GitHub Releases) ≠ roadmap phase ≠ MCP protocol version ≠ AutoCAD target year. Plugin binaries are not interchangeable across AutoCAD major years — build per year.

## License

MIT — [LICENSE](LICENSE). Third-party attributions: [NOTICE.md](NOTICE.md). Autodesk APIs/DLLs are **not** redistributed.
