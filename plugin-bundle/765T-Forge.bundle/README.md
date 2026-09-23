# Autoloader bundle skeleton

Ships two components via `PackageContents.xml`:

| Folder | Target | Series |
| --- | --- | --- |
| `Contents/2017/` | AutoCAD 2017-2024 (.NET Framework 4.6.2 build) | R21.0 - R24.3 |
| `Contents/2025/` | AutoCAD 2025-2027 (.NET 8 build) | R25.0 - R26.0 |

1. Build/install the plugin (`scripts/install-plugin.ps1 -AllSeries`).
2. Copy each series' `Forge.Plugin.dll` (+ deps except Autodesk `Ac*.dll`) into `Contents/2017/` and
   `Contents/2025/` next to this package, or let `scripts/build-plugin.ps1 -AllSeries -OutDir <dir>`
   lay them out for you.
3. Copy the whole `765T-Forge.bundle` folder into an ApplicationPlugins folder (preferred:
   `%APPDATA%\Autodesk\ApplicationPlugins`). Avoid `%PROGRAMDATA%` as primary — see
   [docs/install/plugin.md](../../docs/install/plugin.md).
4. Restart AutoCAD; confirm `MCP_STATUS` and matching `FORGE_AUTOCAD_TOKEN`.

`Contents/2017` requires AutoCAD 2017 reference assemblies at build time
(`FORGE_AUTOCAD_LEGACY_ROOT` / `AUTOCAD_2017_ROOT`); there is no fallback to a newer series.

**`Contents/2017` is prepared but not yet compiled against those references**, so AutoCAD
2017-2024 runtime support is not yet verified. Run
`scripts/verify-plugin-series.ps1 -Series 2017` on a machine with the AutoCAD 2017 (or
ObjectARX 2017 SDK) reference assemblies to close that gate; it exits non-zero naming the
exact missing path rather than skipping. Until it passes, a release must not claim
2017-2024 support.

This skeleton does not ship Autodesk binaries.
