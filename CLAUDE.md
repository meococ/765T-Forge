# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

765T-Forge is an **all-C# MCP (Model Context Protocol) stdio server that drives AutoCAD 2026** for **metro/AEC issue-set drawing production** (inspect → fix → fill → preflight gate → publish → verify). An AI agent calls MCP tools; the server forwards them to an in-process AutoCAD plugin, which executes the Autodesk `.NET`/ObjectARX API.

**Product SemVer:** `0.3.0` Unreleased (see `CHANGELOG.md`; last tagged TP `0.2.1`). Honest shipped surface: [`docs/capability-matrix.md`](docs/capability-matrix.md). Roadmap map: [`docs/roadmap.md`](docs/roadmap.md). Start here: [`docs/getting-started.md`](docs/getting-started.md).

The Vietnamese build brief (`docs/archive/build-brief.md`) is the **aspirational catalog / historical decisions** (tool catalog §5, safety §6, phases §7). It is **not** the shipped capability list — prefer the capability matrix for what works today. Positioning: compete on **reliable publish + agent safety**, not geometry tool count.

Typed "structured" tools cover the hot path; four generic executors (`forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_exec_dotnet`) cover the long tail — all behind a mandatory safety layer.

## Build / test

Requires the .NET 8 SDK pinned in `global.json` (8.0.422, `rollForward: latestPatch`). Central package management (`Directory.Packages.props`) with lock files — versions are pinned, not floating.

```powershell
dotnet restore .\765T-Forge.ServerOnly.slnf --locked-mode
dotnet build   .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test    .\765T-Forge.ServerOnly.slnf --no-build
```

Full solution (needs AutoCAD 2026 at `AUTOCAD_2026_ROOT`):

```powershell
dotnet restore .\765T-Forge.sln --locked-mode
dotnet build   .\765T-Forge.sln --no-restore
```

Run one test class / one test:

```powershell
dotnet test .\765T-Forge.ServerOnly.slnf --filter "FullyQualifiedName~SafetyPolicyTests"
dotnet test .\765T-Forge.ServerOnly.slnf --filter "DisplayName~DotnetExecutorRequiresUnsafeAcknowledgement"
```

**AutoCAD dependency gotcha:** `src/Forge.Plugin/Forge.Plugin.csproj` references AutoCAD managed DLLs via `$(AutoCadRoot)` from `AUTOCAD_2026_ROOT` (default `C:\Program Files\Autodesk\AutoCAD 2026`). The **test project references only `Forge.Shared` and `Forge.Server`, never `Forge.Plugin`**, so all logic under test builds and runs without AutoCAD.

## Architecture — the two-process split

Three projects; the split is the thing to understand:

- **`Forge.Server`** (`net8.0`, console exe) — the MCP server. Registered via the `ModelContextProtocol` SDK with `WithStdioServerTransport().WithTools<ForgeMcpTools>()` in `Program.cs`. Runs wherever the agent runs.
- **`Forge.Plugin`** (`net8.0-windows`) — loaded *inside* AutoCAD via `NETLOAD` (`IExtensionApplication`). Has the only references to the AutoCAD API. Exposes the `MCP_STATUS` command.
- **`Forge.Shared`** (`net8.0`) — referenced by **both** sides: the command/result envelope (`ForgeCommand`/`ForgeResult`), `SafetyPolicy`, `ForgeToolRegistry`/`ToolMetadata`, `FileAuditSink`, `BackupPlanner`, `ForgeEnvironment`, `ForgeJson`, drawing registry, standards packs. Anything both processes must agree on lives here.

They communicate over a **named pipe** (`765T.Forge.AutoCAD`), one JSON line each way. The plugin side (`NamedPipePluginServer`) creates the pipe with an ACL for the current Windows user + LocalSystem, `maxNumberOfServerInstances: 1` (calls are serialized). Token auth is checked as defense-in-depth on top of the ACL.

### Request lifecycle (follow it across files)

`ForgeMcpTools.*` (MCP surface) → `ForgeToolRunner.InvokeAsync` (**server-side** safety eval + audit write, then routes) → for `forge_run_script` / `forge_batch_run`: `HeadlessAccoreConsoleRunner` (spawns `accoreconsole.exe`); everything else: `ForgePipeClient.SendAsync` → pipe → `NamedPipePluginServer.HandleRequestAsync` → `ExecuteOnMainThreadAsync` (marshals onto AutoCAD's command context, takes a document lock) → `PluginCommandProcessor.Process` (**token auth** + **plugin-side** safety eval + audit + pre-write backup, then a big `switch` on `command.Tool.ToLowerInvariant()`) → AutoCAD API.

Note the deliberate redundancy: **safety is evaluated twice** (server and plugin) using the same shared `SafetyPolicy`, and **audit is written on both sides** (server writes a start record + a completion record reusing the same `AuditId`; the plugin writes its own). Don't "simplify" one away without understanding it's the trust boundary — the server may run untrusted; the plugin is the last line of defense.

### The tool metadata is duplicated on purpose — keep it in sync

Two sources of truth describe each tool and **both must be updated together**:

1. `ForgeMcpTools.cs` — the `[McpServerTool(ReadOnly=…, Destructive=…, Idempotent=…, OpenWorld=…)]` attributes that the agent sees.
2. `ToolMetadata.cs` → `ForgeToolRegistry` — the `ToolMetadata` (`ReadOnly`/`Destructive`/`Idempotent`/`RequiresBackup`/`RequiresAutoCad`/`Unsafe`) that `SafetyPolicy` and `PluginCommandProcessor` enforce.

**Adding a tool = three edits** (pipe-routed tools): register it in `ForgeMcpTools` (MCP method), add its `ToolMetadata` in `ForgeToolRegistry`, and add a `case` in the `PluginCommandProcessor.Process` switch. A tool missing from the registry defaults to `Destructive("…", "unknown")` (fail-safe). `ToolMetadataTests` guards MCP↔registry sync; `PluginDispatchSyncTests` guards plugin arms.

**Server-only tools** (no plugin switch — handled in `ForgeToolRunner`): `forge_run_script`, `forge_batch_run`, `forge_batch_status`, `forge_system_tool_profile`, `forge_audit_summarize`, `forge_sheet_inventory_import`, `forge_issue_set_diff`. Also update `docs/capability-matrix.md`, skill/cheatsheet, and CHANGELOG.

Tool naming convention: `forge_<group>_<action>` (e.g. `forge_layer_state_restore`, `forge_plot_publish`).

## Safety model (the core promise — treat as load-bearing)

`SafetyPolicy` (`src/Forge.Shared/SafetyPolicy.cs`) is a regex denylist over the flattened string values of a command's args. It blocks `ERASE ALL`, `PURGE`, `OVERKILL`, `RECOVER`, `AUDIT`-with-fix, `SAVEAS/QSAVE/WBLOCK`-with-wildcard, and **any `forge_exec_*` command containing `ALL`/`*`**. Read-only tools short-circuit to allow. When changing safety, add tests to `SafetyPolicyTests` — denies must keep returning a `deny_*` code.

- **`forge_exec_dotnet` (Roslyn) is off by default** and needs **both** gates: env `FORGE_ENABLE_UNSAFE_OPS=true` *and* per-call `unsafeAcknowledged=true`. It has full assembly access and no sandbox — highest-risk path.
- **Backups**: `BackupPlanner.TryBackup` copies the active DWG to `FORGE_BACKUP_DIR` before any non-dry-run write whose metadata has `RequiresBackup`.
- **Dry-run**: writes accept `dryRun=true` and return what they *would* do without touching the drawing.
- **Read-back verification**: write tools return a `ForgeVerification`; `forge_qa_*` tools formalize this. Publish can be blocked by `forge_qa_preflight` unless `force=true` **and** `FORGE_ALLOW_FORCE_PUBLISH=true`.
- **`forge_exec_command` / `forge_exec_lisp`:** prefer sync `Editor.Command` when possible; when a path still queues, results must expose `queued=true` / `completed=false` honestly — never treat `Ok=true` alone as command finished.

## Configuration (environment variables)

Both processes call `ForgeEnvironment.FromProcess()` independently; they must resolve the **same** pipe name and token to talk. Defaults live in `ForgeConstants`.

- `FORGE_PIPE_NAME` (default `765T.Forge.AutoCAD`)
- `FORGE_AUTOCAD_TOKEN` (required) → falls back to `MCP_AUTOCAD_TOKEN`. No production default secret. For local/tests only: `FORGE_DEV_ALLOW_DEFAULT_TOKEN=true` uses `dev-only-insecure-token` and prints a warning.
- `FORGE_BACKUP_DIR`, `FORGE_AUDIT_DIR` (default under `%LOCALAPPDATA%\765T-Forge\`)
- `AUTOCAD_2026_ROOT` (default `C:\Program Files\Autodesk\AutoCAD 2026`) — used for plugin HintPath + `accoreconsole.exe`
- `FORGE_ENABLE_UNSAFE_OPS` (default `false`)
- `FORGE_ALLOW_FORCE_PUBLISH` (default `false`) — required for `force=true` on `forge_plot_publish` / `forge_recipe_issue_set` (ops accept; ADR 0004)
- `FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS` (default `120`)

## Running against AutoCAD

```powershell
.\scripts\build-plugin.ps1
.\scripts\install-plugin.ps1   # copies DLL + prints TRUSTEDPATHS / NETLOAD checklist
```

In AutoCAD 2026: `NETLOAD` → installed `Forge.Plugin.dll`, then `MCP_STATUS`. Put the DLL in a `TRUSTEDPATHS` location or AutoCAD will block the load. Agent-facing usage: `skills/765t-forge/SKILL.md` (health-check first, preflight before publish, dry-run before writes, never invent drawing numbers, never broad-select via executors).
