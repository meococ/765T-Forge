# Autoloader bundle skeleton

One `Component` per AutoCAD release, `SeriesMin` = `SeriesMax` for that R-series only. The default verified host is **AutoCAD 2026 (R25.1)**. 2017–2025 are build targets when that year's `AUTOCAD_<year>_ROOT` is set. Binaries are not interchangeable, and 2017–2025 are not smoke-tested by this repo. This skeleton lists every year; `install-plugin.ps1` and `pack-release.ps1` rewrite `PackageContents.xml` so a bundle contains only years whose `Forge.Plugin.dll` was copied.

1. Build/install the plugin for the year you run (`scripts/build-plugin.ps1 -Year 2026` or `-AllYears`).
2. Each built year's `Forge.Plugin.dll` (+ deps except Autodesk `Ac*.dll`) belongs in `Contents/Windows/<year>/`. Years that were not built are omitted.
3. Copy the whole `765T-Forge.bundle` folder into `%APPDATA%\Autodesk\ApplicationPlugins` (preferred on AutoCAD 2026). Avoid `%PROGRAMDATA%` as primary — see [docs/install-plugin.md](../../docs/install-plugin.md).
4. Restart AutoCAD; confirm `MCP_STATUS` and matching `FORGE_AUTOCAD_TOKEN`.

A net48 DLL does not load in AutoCAD 2017. 2017–2018 are built as `net46` (documented .NET Framework 4.6). JSON on that TFM uses Newtonsoft.Json because System.Text.Json 8 cannot target `net46`. `forge_exec_dotnet` returns `autocad_version_unsupported` on those years. This skeleton does not ship Autodesk binaries. Smoke lab is AutoCAD 2026 only.
