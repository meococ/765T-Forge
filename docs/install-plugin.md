# Install the AutoCAD plugin

The **default verified host is AutoCAD 2026**. `scripts/build-plugin.ps1` builds that year unless you pass `-Year` or `-AllYears`.

2017–2025 are build targets when `AUTOCAD_<year>_ROOT` (or `C:\Program Files\Autodesk\AutoCAD <year>`) contains that year's `AcCoreMgd.dll`, `AcDbMgd.dll`, and `AcMgd.dll`. They are **not smoke-tested**. Plugin binaries are **not interchangeable** across years. A `net48` DLL does not load in AutoCAD 2017.

Autodesk’s managed .NET compatibility table lists **AutoCAD 2017 (R21.0) and 2018 (R22.0) as .NET Framework 4.6**, 2019–2020 as 4.7, and 2021–2024 as 4.8. 2017–2018 therefore target `net46`. `System.Text.Json` 8.0.5 returns NU1202 for `net46` (and warns that `net461` is unsupported), so that TFM serializes with Newtonsoft.Json. `net462` is newer than the documented CLR and is not used. `forge_exec_dotnet` returns `autocad_version_unsupported` on 2017–2018 because Roslyn scripting is `netstandard2.0`.

| Years | TFM | Env var |
|-------|-----|---------|
| 2017–2018 | `net46` (documented CLR 4.6; Newtonsoft.Json; not smoke-tested) | `AUTOCAD_2017_ROOT`, `AUTOCAD_2018_ROOT` |
| 2019–2020 | `net47` | `AUTOCAD_2019_ROOT`, `AUTOCAD_2020_ROOT` |
| 2021–2024 | `net48` | `AUTOCAD_<year>_ROOT` |
| 2025–2026 | `net8.0-windows` | `AUTOCAD_2025_ROOT`, `AUTOCAD_2026_ROOT` |

`-AllYears` builds every year whose root exists and prints the skipped years. It does not require every year to be installed.

## Quick path (recommended, AutoCAD 2026)

```powershell
.\scripts\build-plugin.ps1 -Configuration Release
.\scripts\install-plugin.ps1 -Configuration Release -SkipBuild
```

Other year:

```powershell
$env:AUTOCAD_2024_ROOT = "C:\Program Files\Autodesk\AutoCAD 2024"
.\scripts\build-plugin.ps1 -Year 2024 -Configuration Release
.\scripts\install-plugin.ps1 -Year 2024 -Configuration Release -SkipBuild
```

Default NETLOAD directory: `%LOCALAPPDATA%\765T-Forge\plugin\autocad-<year>\Forge.Plugin.dll`.

Output of a default build: `src\Forge.Plugin\bin\Release\autocad-2026\`.

## TRUSTEDPATHS + NETLOAD

1. Add the year folder to AutoCAD **Trusted Locations**.
2. Ensure `FORGE_AUTOCAD_TOKEN` (and optional `FORGE_PIPE_NAME`) are set for the **AutoCAD process**, matching the MCP server.
3. `NETLOAD` the DLL built for **that** year, then run `MCP_STATUS`.
4. From the agent: `forge_system_health` — `pipe` in the payload must match your configured pipe name.
5. If the DLL year does not match the running release, tools return `autocad_version_unsupported` instead of succeeding. `forge_exec_dotnet` returns that code on AutoCAD 2017–2018. `Publisher.PublishDsd` and command-line undo marks are available on every catalog year; 2026 calls `PublishDsd` directly.

## Autoloader bundle (optional)

Copy [`plugin-bundle/765T-Forge.bundle`](../plugin-bundle/765T-Forge.bundle) into an AutoCAD `ApplicationPlugins` folder. Prefer (in order):

1. **`%APPDATA%\Autodesk\ApplicationPlugins`** — per-user; works reliably on AutoCAD 2026
2. **`%PROGRAMFILES%\Autodesk\ApplicationPlugins`** — machine-wide (requires admin)
3. `%PROGRAMDATA%\Autodesk\ApplicationPlugins` — **not recommended** on AutoCAD 2026; Autodesk tightened ProgramData auto-load and bundles here often do not load

`install-plugin.ps1` writes the same layout under `%LOCALAPPDATA%\765T-Forge\plugin\765T-Forge.bundle\`. Each built year has its own component:

`Contents/Windows/<year>/Forge.Plugin.dll`

The repository `PackageContents.xml` lists every year. The copy written by install and by `pack-release.ps1` keeps a component only when that year’s `Forge.Plugin.dll` is in the bundle. Years that were not built are omitted. `SeriesMin` = `SeriesMax` for that R-series only (2026 is R25.1). Autoloader loads one component. Token env vars are still required.

## Releases

CI does **not** build the plugin (no AutoCAD on GitHub-hosted runners). Download **`765T-Forge.Plugin.zip`** from the GitHub Release (maintainer-attached) in addition to the server zip. The plugin zip is the bundle layout and contains only the years that were installed on the packing machine. A release without the plugin zip is **incomplete**. See [CONTRIBUTING.md](../CONTRIBUTING.md) release checklist.
