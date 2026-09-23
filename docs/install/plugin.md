# Install the AutoCAD plugin

Requires a build of `Forge.Plugin` (two assemblies: `net462` for 2017–2024, `net8.0-windows` for
2025–2027) and AutoCAD reference assemblies to build it:

| Build | AutoCAD | Reference assemblies required |
|-------|---------|-------------------------------|
| `net8.0-windows` (default) | 2025, 2026 | AutoCAD 2025 or 2026 (`FORGE_AUTOCAD_MODERN_ROOT` → `AUTOCAD_2025_ROOT` → `FORGE_AUTOCAD_ROOT`) |
| `net8.0-windows` (artifact target) | 2027 | Not compiled against 2027: AutoCAD 2027 is .NET 10 and its assemblies cannot be referenced from a `net8.0` compilation (CS1705). Forge ships the `net8.0` assembly; it has not been runtime-tested on 2027 |
| `net462` (`-p:ForgeBuildLegacy=true`) | 2017–2024 | AutoCAD 2017 (`FORGE_AUTOCAD_LEGACY_ROOT` → deprecated `AUTOCAD_2017_ROOT`); no fallback |

**Verified vs prepared:** only the `net8.0-windows` component (AutoCAD 2025/2026) has been built and
run. The `net462` component is prepared but has **not** been compiled on a machine with AutoCAD 2017
reference assemblies, so 2017–2024 runtime support is **not yet verified**. The deterministic gate is
`scripts/verify-plugin-series.ps1 -Series 2017`; run it per series on a machine with those references.

## Quick path (recommended)

```powershell
.\scripts\build-plugin.ps1 -Configuration Release
.\scripts\install-plugin.ps1 -Configuration Release -SkipBuild
```

Default install directory: `%LOCALAPPDATA%\765T-Forge\plugin\Contents\2025\Forge.Plugin.dll` (modern)
and `%LOCALAPPDATA%\765T-Forge\plugin\Contents\2017\Forge.Plugin.dll` when built with `-AllSeries`.

## TRUSTEDPATHS + NETLOAD

1. Add the install directory to AutoCAD **Trusted Locations**.
2. Ensure `FORGE_AUTOCAD_TOKEN` (and optional `FORGE_PIPE_NAME`) are set for the **AutoCAD process**, matching the MCP server.
3. `NETLOAD` the installed DLL, then run `MCP_STATUS`.
4. From the agent: `forge_system_health` — `pipe` in the payload must match your configured pipe name.

## Autoloader bundle (optional)

Copy [`plugin-bundle/765T-Forge.bundle`](../../plugin-bundle/765T-Forge.bundle) into an AutoCAD `ApplicationPlugins` folder. Prefer (in order):

1. **`%APPDATA%\Autodesk\ApplicationPlugins`** — per-user; works reliably on AutoCAD 2025/2026
2. **`%PROGRAMFILES%\Autodesk\ApplicationPlugins`** — machine-wide (requires admin)
3. `%PROGRAMDATA%\Autodesk\ApplicationPlugins` — **not recommended**; Autodesk tightened ProgramData auto-load and bundles here often do not load

Place the built DLLs at the paths `PackageContents.xml` names (see the bundle README):

- `Contents/2017/Forge.Plugin.dll` — AutoCAD 2017–2024, `SeriesMin=R21.0 SeriesMax=R24.3`
- `Contents/2025/Forge.Plugin.dll` — AutoCAD 2025–2026, `SeriesMin=R25.0 SeriesMax=R26.0`

Autoloader avoids manual `NETLOAD` after AutoCAD restart; token env vars are still
required.

## Releases

CI does **not** build the plugin (no AutoCAD on GitHub-hosted runners). Download **`765T-Forge.Plugin.zip`** from the GitHub Release in addition to the server zip. A release without the plugin zip is **incomplete**. Until a `verify-plugin-series.ps1 -Series 2017` pass is recorded, treat the `Contents/2017` component as prepared-only. See [../getting-started.md](../getting-started.md) and [CONTRIBUTING.md](../../CONTRIBUTING.md) release checklist.
