# Contributing to 765T-Forge

Thanks for helping improve Forge. This project is an all-C# MCP server plus an AutoCAD plugin for metro/AEC drawing-production workflows.

## Prerequisites

- .NET SDK matching `global.json` (9.0.315, `rollForward: latestFeature`). The projects still target `net8.0` / `net8.0-windows` / `netstandard2.0` / `net462`; the newer SDK is needed for the Roslyn 5.x analyzers.
- Windows (plugin and AccoreConsole paths are Windows-oriented)
- AutoCAD reference assemblies **only if** you build `Forge.Plugin`:
  - `net8.0-windows` (default): AutoCAD 2025 or 2026
  - `net462` (`-p:ForgeBuildLegacy=true`): AutoCAD 2017 reference assemblies — no fallback to a newer series

Build roots are explicit; plugin projects never guess a path. Set the root you need:

```powershell
$env:FORGE_AUTOCAD_MODERN_ROOT = "C:\Program Files\Autodesk\AutoCAD 2026"
$env:FORGE_AUTOCAD_LEGACY_ROOT = "C:\Program Files\Autodesk\AutoCAD 2017"   # legacy TFM only
```

`FORGE_AUTOCAD_ROOT` remains the fallback modern root and the server-side runtime root; `AUTOCAD_2026_ROOT` and `AUTOCAD_2017_ROOT` are deprecated aliases that are still honored. Do not commit Autodesk DLLs.

The `net462` target has not yet been compiled on a machine with AutoCAD 2017 reference assemblies. If you have one, run `.\scripts\verify-plugin-series.ps1 -Series 2017` and record the result — that gate is what turns the prepared legacy build into a verified one.

## ServerOnly vs full solution

| Goal | Use |
|------|-----|
| CI, unit tests, MCP server work without AutoCAD | `765T-Forge.ServerOnly.slnf` |
| Full product including plugin | `765T-Forge.sln` |

```powershell
# Recommended while AutoCAD has NETLOAD'd the plugin (DLL lock)
dotnet restore .\765T-Forge.ServerOnly.slnf
dotnet build   .\765T-Forge.ServerOnly.slnf --no-restore
dotnet test    .\765T-Forge.ServerOnly.slnf --no-build

# Full solution (requires AutoCAD 2025/2026 reference assemblies)
dotnet restore .\765T-Forge.sln
dotnet build   .\765T-Forge.sln --no-restore
dotnet test    .\765T-Forge.sln --no-build
```

Helper scripts:

- `scripts/build-server.ps1` — restore/build/test ServerOnly
- `scripts/build-plugin.ps1` — build plugin (optional alternate `OutDir` when DLLs are locked; `-AllSeries` stages bundle folders)
- `scripts/verify-plugin-series.ps1 -Series 2017,2025` — deterministic gate: asserts the reference assemblies for each requested series exist, then builds that series; exits non-zero with the exact missing path
- `scripts/install-plugin.ps1` — copy plugin DLL to local install dir + NETLOAD checklist
- `scripts/pack-release.ps1` — pack server publish output; plugin when available

## Release checklist (maintainers)

A GitHub Release for `v*` is **incomplete** with server zip alone.

1. Ensure [CHANGELOG.md](CHANGELOG.md) section for the version is accurate; move notes from Unreleased.
2. Confirm [docs/smoke-lab.md](docs/smoke-lab.md) has a maintainer pass for this build (or document deferral).
3. Tag `vX.Y.Z` — CI packs `765T-Forge.Server-win-x64.zip` via `pack-release.ps1 -SkipPlugin`.
4. On a machine with a matching AutoCAD install: `.\scripts\pack-release.ps1 -Configuration Release` (no `-SkipPlugin`). Run `.\scripts\verify-plugin-series.ps1` for each series you claim before attaching the plugin zip.
5. Attach **`765T-Forge.Plugin.zip`** to the same GitHub Release (Autodesk `Ac*.dll` must stay excluded).
6. Verify the server zip contains `docs/capability-matrix.md` and `docs/safety.md` (embedded for `forge://` resources).
7. Verify release notes state: server alone is insufficient; plugin zip required; smoke-lab status.
8. Confirm `SECURITY.md` contact channels still valid.
9. Do **not** mark the GitHub Release as the “complete product” until steps 5–7 are done.

## Code of conduct

Participation is governed by [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md).

## How to add a tool

Tool naming: `forge_<group>_<action>` (example: `forge_layer_state_restore`).

Adding a tool requires **three** synchronized edits:

1. **MCP surface** — method + `[McpServerTool(...)]` in `src/Forge.Server/ForgeMcpTools.cs`
2. **Registry** — `ToolMetadata` entry in `src/Forge.Shared/ToolMetadata.cs` (`ForgeToolRegistry`)
3. **Plugin dispatch** — `case` in `src/Forge.Plugin/PluginCommandProcessor.Process`

A tool missing from the registry gets `Destructive("…", "unknown")` metadata, and an unregistered name with no plugin dispatch arm returns `unknown_tool` at execution. Update `ToolMetadataTests` and `PluginDispatchSyncTests` when hot-path tools or MCP annotation expectations change.

Also update:

- [docs/capability-matrix.md](docs/capability-matrix.md) — status (`implemented` / `partial` / `planned`)
- [skills/765t-forge/SKILL.md](skills/765t-forge/SKILL.md) — if agent workflow changes
- [CHANGELOG.md](CHANGELOG.md) — under Unreleased

## Safety expectations

- Prefer typed tools over free-text executors
- Keep the deterministic capability gate and dual evaluation (server and plugin) intact; do not reintroduce keyword or regex filtering of command text
- Keep the five executors flagged `Unsafe` in `ForgeToolRegistry`; add `SafetyPolicyTests` when gate behaviour changes
- Document timeout/write-race behavior when changing transport or command queuing

## Skill updates

Agent guidance lives in `skills/765t-forge/SKILL.md`. Keep the skill aligned with:

- Real tool names and availability
- Dry-run / preflight / health-first rules
- The capability gate, `unsafe_not_acknowledged`, and the other typed error codes

## Pull requests

- Prefer ServerOnly-green CI for docs and server/shared/test changes
- Call out AutoCAD-only verification you ran (NETLOAD, plot, publish)
- Use the PR template; link capability-matrix updates when tools change

## Secrets and hygiene

- Never commit tokens, customer DWGs, or Autodesk binaries
- Do not document a default shared pipe token for production installs
- Generated logs (`*.log`, including `plot.log`) stay out of git
