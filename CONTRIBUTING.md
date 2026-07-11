# Contributing to 765T-Forge

Thanks for helping improve Forge. This project is an all-C# MCP server plus an AutoCAD 2026 plugin for metro/AEC drawing-production workflows.

## Prerequisites

- .NET SDK matching `global.json` (8.0.422, `rollForward: latestPatch`)
- Windows (plugin and AccoreConsole paths are Windows-oriented)
- AutoCAD 2026 **only if** you build or run `Forge.Plugin`

Set `AUTOCAD_2026_ROOT` when AutoCAD is not installed at the default path:

```powershell
$env:AUTOCAD_2026_ROOT = "C:\Program Files\Autodesk\AutoCAD 2026"
```

Plugin projects resolve Autodesk managed assemblies from that root. Do not commit Autodesk DLLs.

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

# Full solution (requires AutoCAD at AUTOCAD_2026_ROOT)
dotnet restore .\765T-Forge.sln
dotnet build   .\765T-Forge.sln --no-restore
dotnet test    .\765T-Forge.sln --no-build
```

Helper scripts:

- `scripts/build-server.ps1` — restore/build/test ServerOnly
- `scripts/build-plugin.ps1` — build plugin (optional alternate `OutDir` when DLLs are locked)
- `scripts/install-plugin.ps1` — copy plugin DLL to local install dir + NETLOAD checklist
- `scripts/pack-release.ps1` — pack server publish output; plugin when available

## Release checklist (maintainers)

A GitHub Release for `v*` is **incomplete** with server zip alone.

1. Ensure [CHANGELOG.md](CHANGELOG.md) section for the version is accurate; move notes from Unreleased.
2. Confirm [docs/smoke-lab.md](docs/smoke-lab.md) has a maintainer pass for this build (or document deferral).
3. Tag `vX.Y.Z` — CI packs `765T-Forge.Server-win-x64.zip` via `pack-release.ps1 -SkipPlugin`.
4. On a machine with AutoCAD 2026: `.\scripts\pack-release.ps1 -Configuration Release` (no `-SkipPlugin`).
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

A tool missing from the registry fails closed as destructive/unknown. Update `ToolMetadataTests` and `PluginDispatchSyncTests` when hot-path tools or MCP annotation expectations change.

Also update:

- [docs/capability-matrix.md](docs/capability-matrix.md) — status (`implemented` / `partial` / `planned`)
- [skills/765t-forge/SKILL.md](skills/765t-forge/SKILL.md) — if agent workflow changes
- [CHANGELOG.md](CHANGELOG.md) — under Unreleased

## Safety expectations

- Prefer typed tools over open-world executors
- Keep denylist + dual evaluation (server and plugin) intact
- Add `SafetyPolicyTests` for new deny codes
- Document timeout/write-race behavior when changing transport or command queuing

## Skill updates

Agent guidance lives in `skills/765t-forge/SKILL.md`. Keep the skill aligned with:

- Real tool names and availability
- Dry-run / preflight / health-first rules
- Denylist and unsafe-ops gates

## Pull requests

- Prefer ServerOnly-green CI for docs and server/shared/test changes
- Call out AutoCAD-only verification you ran (NETLOAD, plot, publish)
- Use the PR template; link capability-matrix updates when tools change

## Secrets and hygiene

- Never commit tokens, customer DWGs, or Autodesk binaries
- Do not document a default shared pipe token for production installs
- Generated logs (`*.log`, including `plot.log`) stay out of git
