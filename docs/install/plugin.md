# Install the AutoCAD plugin

Requires AutoCAD 2026 and a build of `Forge.Plugin`.

## Quick path (recommended)

```powershell
.\scripts\build-plugin.ps1 -Configuration Release
.\scripts\install-plugin.ps1 -Configuration Release -SkipBuild
```

Default install directory: `%LOCALAPPDATA%\765T-Forge\plugin\Forge.Plugin.dll`.

## TRUSTEDPATHS + NETLOAD

1. Add the install directory to AutoCAD **Trusted Locations**.
2. Ensure `FORGE_AUTOCAD_TOKEN` (and optional `FORGE_PIPE_NAME`) are set for the **AutoCAD process**, matching the MCP server.
3. `NETLOAD` the installed DLL, then run `MCP_STATUS`.
4. From the agent: `forge_system_health` — `pipe` in the payload must match your configured pipe name.

## Autoloader bundle (optional)

Copy [`plugin-bundle/765T-Forge.bundle`](../../plugin-bundle/765T-Forge.bundle) into an AutoCAD `ApplicationPlugins` folder. Prefer (in order):

1. **`%APPDATA%\Autodesk\ApplicationPlugins`** — per-user; works reliably on AutoCAD 2026
2. **`%PROGRAMFILES%\Autodesk\ApplicationPlugins`** — machine-wide (requires admin)
3. `%PROGRAMDATA%\Autodesk\ApplicationPlugins` — **not recommended** on AutoCAD 2026; Autodesk tightened ProgramData auto-load and bundles here often do not load

Place `Forge.Plugin.dll` at `Contents/Windows/Forge.Plugin.dll` relative to the bundle (see the bundle README), or edit `PackageContents.xml` `ModuleName` to match your layout.

`PackageContents.xml` targets **Series R25.1** (AutoCAD 2026). Autoloader avoids manual `NETLOAD` after AutoCAD restart; token env vars are still required.

## Releases

CI does **not** build the plugin (no AutoCAD on GitHub-hosted runners). Download **`765T-Forge.Plugin.zip`** from the GitHub Release in addition to the server zip. A release without the plugin zip is **incomplete**. See [../getting-started.md](../getting-started.md) and [CONTRIBUTING.md](../../CONTRIBUTING.md) release checklist.
