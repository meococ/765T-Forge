# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

765T-Forge is an **all-C# MCP (Model Context Protocol) stdio server that drives AutoCAD** for **metro/AEC issue-set drawing production** (inspect → fix → fill → preflight gate → publish → verify). An AI agent calls MCP tools; the server forwards them to an in-process AutoCAD plugin, which executes the Autodesk `.NET`/ObjectARX API.

**Product SemVer:** `0.3.0` Unreleased (see `CHANGELOG.md`; last tagged TP `0.2.1`). Honest shipped surface: [`docs/capability-matrix.md`](docs/capability-matrix.md). Roadmap map: [`docs/roadmap.md`](docs/roadmap.md). Start here: [`docs/getting-started.md`](docs/getting-started.md).

Host support: the plugin builds `net8.0-windows` by default for AutoCAD 2025–2026 (.NET 8) and can build `net462` for AutoCAD 2017–2024 with `-p:ForgeBuildLegacy=true` against AutoCAD 2017 reference assemblies. The legacy build has **not yet been compiled on a machine with those references**, so 2017–2024 runtime support is unverified. The gate is `scripts/verify-plugin-series.ps1 -Series 2017`.

The Vietnamese build brief (`docs/archive/build-brief.md`) is the **aspirational catalog / historical decisions** (tool catalog §5, safety §6, phases §7). It is **not** the shipped capability list — prefer the capability matrix for what works today. Positioning: compete on **reliable publish + agent safety**, not geometry tool count.

`docs/research/` and the loose R&D material under `tests/` (fixture DWGs, `.scr` scripts, `.log` runs) are untracked local research. Do not describe them as shipped product.

Typed "structured" tools cover the hot path; five free-text executors (`forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_batch_run`, `forge_exec_dotnet`) cover the long tail — all five are flagged `Unsafe` and refused unless unsafe ops are enabled and acknowledged per call.

## Build / test

Requires the .NET 8 SDK pinned in `global.json` (8.0.422, `rollForward: latestPatch`). Central package management (`Directory.Packages.props`) with lock files — versions are pinned, not floating.

```powershell
dotnet restore .\765T-Forge.ServerOnly.slnf --locked-mode
dotnet build   .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test    .\765T-Forge.ServerOnly.slnf --no-build
```

Full solution (needs AutoCAD 2025/2026 reference assemblies; see root resolution below):

```powershell
dotnet restore .\765T-Forge.sln --locked-mode
dotnet build   .\765T-Forge.sln --no-restore
```

Run one test class / one test:

```powershell
dotnet test .\765T-Forge.ServerOnly.slnf --filter "FullyQualifiedName~SafetyPolicyTests"
dotnet test .\765T-Forge.ServerOnly.slnf --filter "DisplayName~DotnetExecutorRequiresUnsafeAcknowledgement"
```

**Build model (both TFMs):**

- `Forge.Shared` targets **`netstandard2.0`** so the `net8.0` server and the `net462` plugin can both reference it.
- `Forge.Plugin` targets **`net8.0-windows`** by default. Adding **`-p:ForgeBuildLegacy=true`** builds `net462;net8.0-windows` and switches the lock file to `packages.legacy.lock.json`.
- The `net462` build **must** compile against AutoCAD 2017 reference assemblies so only 2017-era APIs are used. It has no fallback: if `FORGE_AUTOCAD_LEGACY_ROOT` (or the deprecated `AUTOCAD_2017_ROOT`) is unset, or the three DLLs are missing, the build fails with an explicit error (see `ForgeResolveAutoCadLegacyReferences` in the csproj).
- The modern build resolves references from `FORGE_AUTOCAD_MODERN_ROOT` → `AUTOCAD_2025_ROOT` → `FORGE_AUTOCAD_ROOT`, then by exact `Exists` checks for the installed AutoCAD 2025/2026 installs.
- `scripts/verify-plugin-series.ps1 -Series 2017,2025` asserts by exact `Test-Path` that each requested series' reference assemblies exist and then builds that series; it exits non-zero naming the exact missing path.
- Runtime root resolution (server) is separate: `FORGE_AUTOCAD_ROOT` → deprecated `AUTOCAD_2026_ROOT` → discovery of the highest installed `AutoCAD <4-digit year>` containing `accoreconsole.exe`, else empty.

**AutoCAD dependency gotcha:** `src/Forge.Plugin/Forge.Plugin.csproj` references AutoCAD managed DLLs from those roots. The **test project references only `Forge.Shared` and `Forge.Server`, never `Forge.Plugin`**, so all logic under test builds and runs without AutoCAD.

## Architecture — the two-process split

Three projects; the split is the thing to understand:

- **`Forge.Server`** (`net8.0`, console exe) — the MCP server. Registered via the `ModelContextProtocol` SDK with `WithStdioServerTransport().WithTools<ForgeMcpTools>()` in `Program.cs`. Runs wherever the agent runs.
- **`Forge.Plugin`** (`net8.0-windows`, or `net462` on demand) — loaded *inside* AutoCAD via `NETLOAD` (`IExtensionApplication`). Has the only references to the AutoCAD API. Exposes the `MCP_STATUS` command.
- **`Forge.Shared`** (`netstandard2.0`) — referenced by **both** sides: the command/result envelope (`ForgeCommand`/`ForgeResult`), `SafetyPolicy`, `ForgeToolRegistry`/`ToolMetadata`, `FileAuditSink`, `BackupPlanner`, `ForgeEnvironment`, `ForgeJson`, drawing registry, standards packs. Anything both processes must agree on lives here.

They communicate over a **named pipe** (`765T.Forge.AutoCAD`), one JSON line each way. The plugin side (`NamedPipePluginServer`) creates the pipe with an ACL for the current Windows user + LocalSystem and up to 4 server instances so a busy plugin still accepts connections; execution is serialized by a semaphore, and a request that cannot acquire it returns `plugin_busy` instead of a misleading transport timeout. Token auth is checked as defense-in-depth on top of the ACL.

### Request lifecycle (follow it across files)

`ForgeMcpTools.*` (MCP surface) → `ForgeToolRunner.InvokeAsync` (**server-side** capability gate + audit write, then routes) → for `forge_run_script` / `forge_batch_run`: `HeadlessAccoreConsoleRunner` (spawns `accoreconsole.exe`); server-only tools handled inline; everything else: `ForgePipeClient.SendAsync` → pipe → `NamedPipePluginServer.HandleRequestAsync` → `ExecuteOnMainThreadAsync` (marshals onto AutoCAD's command context, takes a document lock) → `PluginCommandProcessor.Process` (**token auth** + **plugin-side** safety eval + audit + pre-write backup, then a big `switch` on `command.Tool.ToLowerInvariant()`) → AutoCAD API.

Note the deliberate redundancy: **safety is evaluated twice** (server and plugin) using the same shared `SafetyPolicy`, and **audit is written on both sides** (server writes a start record + a completion record reusing the same `AuditId`; the plugin writes its own). Don't "simplify" one away without understanding it's the trust boundary — the server may run untrusted; the plugin is the last line of defense.

### The tool metadata is duplicated on purpose — keep it in sync

Two sources of truth describe each tool and **both must be updated together**:

1. `ForgeMcpTools.cs` — the `[McpServerTool(ReadOnly=…, Destructive=…, Idempotent=…, OpenWorld=…)]` attributes that the agent sees.
2. `ToolMetadata.cs` → `ForgeToolRegistry` — the `ToolMetadata` (`ReadOnly`/`Destructive`/`Idempotent`/`RequiresBackup`/`RequiresAutoCad`/`Unsafe`) that `SafetyPolicy` and `PluginCommandProcessor` enforce.

**Adding a tool = three edits** (pipe-routed tools): register it in `ForgeMcpTools` (MCP method), add its `ToolMetadata` in `ForgeToolRegistry`, and add a `case` in the `PluginCommandProcessor.Process` switch. A tool missing from the registry defaults to `Destructive("…", "unknown")`, and a name with no plugin dispatch arm returns `unknown_tool` at execution. `ToolMetadataTests` guards MCP↔registry sync; `PluginDispatchSyncTests` guards plugin arms.

**Server-only tools** (no plugin switch — handled in `ForgeToolRunner`): `forge_run_script`, `forge_batch_run`, `forge_batch_status`, `forge_system_tool_profile`, `forge_audit_summarize`, `forge_sheet_inventory_import`, `forge_issue_set_diff`, and the seven `forge_linework_*` tools (`dump`/`trace`/`topology`/`coverage`/`segments`/`compare`/`transform`; `RequiresAutoCad=false`, no backup). The linework family reads a `pl_dump.txt` dump and does pure coordinate math; live-drawing extraction is not dispatched in this build, and the tools are writers (`ReadOnly=false`) because they create the caller's `outputPath`/`overlayPath`/`transformPath` artifacts. Also update `docs/capability-matrix.md`, skill/cheatsheet, and CHANGELOG.

Tool naming convention: `forge_<group>_<action>` (e.g. `forge_layer_state_restore`, `forge_plot_publish`).

## Safety model (the core promise — treat as load-bearing)

`SafetyPolicy` (`src/Forge.Shared/SafetyPolicy.cs`) is a **deterministic capability gate**, not a text filter. `Evaluate(command, unsafeOpsEnabled)`:

1. `ReadOnly` tools → allow.
2. Tools flagged `Unsafe` in `ForgeToolRegistry` → allow only when **both** `FORGE_ENABLE_UNSAFE_OPS=true` in the process environment **and** the per-call `unsafeAcknowledged=true`; otherwise deny with `unsafe_not_acknowledged`.
3. Everything else → allow. Unknown tool names fail closed as `Destructive("…", "unknown")`.

**Nothing inspects argument text.** The five `Unsafe` tools are `forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_batch_run`, `forge_exec_dotnet`. When changing safety, add tests to `SafetyPolicyTests`; denials keep returning a typed code (`unsafe_not_acknowledged`).

- **`forge_exec_dotnet` (Roslyn) is off by default** and needs **both** gates. It runs with full process trust inside AutoCAD and is **not sandboxed**; the timeout bounds how long the server waits, and a script already running cannot be forcibly aborted. On timeout the code is `exec_dotnet_timeout`. Roslyn assemblies ship next to the plugin (`CopyLocalLockFileAssemblies`).
- **Backups**: `BackupPlanner.TryBackup` copies the active DWG to `FORGE_BACKUP_DIR` before any non-dry-run write whose metadata has `RequiresBackup`.
- **Dry-run**: writes accept `dryRun=true` and return what they *would* do without touching the drawing.
- **Read-back verification**: write tools return a `ForgeVerification`; `forge_qa_*` tools formalize this. Publish can be blocked by `forge_qa_preflight` unless `force=true` **and** `FORGE_ALLOW_FORCE_PUBLISH=true`.
- **`forge_exec_command` / `forge_exec_lisp`:** prefer sync `Editor.Command` when possible; when a path still queues, results must expose `queued=true` / `completed=false` and `undoGrouped=false` honestly — never treat `Ok=true` alone as command finished.
- **Typed gates worth knowing:** `plugin_busy`, `plugin_main_thread_timeout`, `frame_too_large` / `response_too_large` (exact 4,000,000-character cap), `invalid_command_id` (`^[A-Za-z0-9-]{1,64}$`), `block_attribute_not_found`, `regex_timeout` (250 ms), `plot_units_unavailable`, `illegal_dsd_character`, `undoWarnings[]` (`undo_group_open_failed` / `undo_group_close_failed`).

## Configuration (environment variables)

Both processes call `ForgeEnvironment.FromProcess()` independently; they must resolve the **same** pipe name and token to talk. Defaults live in `ForgeConstants`.

- `FORGE_PIPE_NAME` (default `765T.Forge.AutoCAD`)
- `FORGE_AUTOCAD_TOKEN` (required) → falls back to `MCP_AUTOCAD_TOKEN`. No production default secret. For local/tests only: `FORGE_DEV_ALLOW_DEFAULT_TOKEN=true` uses `dev-only-insecure-token` and prints a warning.
- `FORGE_BACKUP_DIR`, `FORGE_AUDIT_DIR` (default under `%LOCALAPPDATA%\765T-Forge\`)
- `FORGE_AUTOCAD_ROOT` (runtime root for plugin discovery + `accoreconsole.exe`); deprecated alias `AUTOCAD_2026_ROOT`; falling back to discovery of the highest `C:\Program Files\Autodesk\AutoCAD <year>` with `accoreconsole.exe`
- `FORGE_ENABLE_UNSAFE_OPS` (default `false`)
- `FORGE_ALLOW_FORCE_PUBLISH` (default `false`) — required for `force=true` on `forge_plot_publish` / `forge_recipe_issue_set` (ops accept; ADR 0004)
- `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS` (default `120`)

Build-time roots (plugin csproj): `FORGE_AUTOCAD_MODERN_ROOT` → `AUTOCAD_2025_ROOT` → `FORGE_AUTOCAD_ROOT` for `net8.0-windows`; `FORGE_AUTOCAD_LEGACY_ROOT` → `AUTOCAD_2017_ROOT` for `net462`.

## Running against AutoCAD

```powershell
.\scripts\build-plugin.ps1
.\scripts\install-plugin.ps1   # copies DLL + prints TRUSTEDPATHS / NETLOAD checklist
```

In AutoCAD: `NETLOAD` → installed `Forge.Plugin.dll`, then `MCP_STATUS`. Put the DLL in a `TRUSTEDPATHS` location or AutoCAD will block the load. Agent-facing usage: `skills/765t-forge/SKILL.md` (health-check first, preflight before publish, dry-run before writes, never invent drawing numbers, never broad-select a whole drawing).
