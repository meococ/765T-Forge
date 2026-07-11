# Autoloader bundle skeleton

Targets **AutoCAD 2026 (Series R25.1)** via `PackageContents.xml`.

1. Build/install the plugin (`scripts/install-plugin.ps1`).
2. Copy `Forge.Plugin.dll` (+ deps except Autodesk `Ac*.dll`) into `Contents/Windows/` next to this package.
3. Copy the whole `765T-Forge.bundle` folder into `%APPDATA%\Autodesk\ApplicationPlugins` (preferred on AutoCAD 2026). Avoid `%PROGRAMDATA%` as primary — see [docs/install/plugin.md](../../docs/install/plugin.md).
4. Restart AutoCAD; confirm `MCP_STATUS` and matching `FORGE_AUTOCAD_TOKEN`.

This skeleton does not ship Autodesk binaries.
