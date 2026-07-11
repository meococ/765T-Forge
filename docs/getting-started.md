# Getting started

**Platform:** Windows + AutoCAD **2026** only (Technical Preview). Not affiliated with Autodesk.

765T-Forge is a dual-process product: an MCP stdio **server** plus an in-process AutoCAD **plugin**. Both are required for live drawing tools.

## 1. Get artifacts

**Preferred:** download both zips from the [latest GitHub Release](https://github.com/765T/765T-Forge/releases):

- `765T-Forge.Server-win-x64.zip`
- `765T-Forge.Plugin.zip`

A release without the plugin zip is **incomplete**. Requires [.NET 8 runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (framework-dependent server).

**Or build from source:**

```powershell
.\scripts\build-server.ps1 -Configuration Release
.\scripts\build-plugin.ps1 -Configuration Release   # needs AutoCAD 2026
.\scripts\install-plugin.ps1 -Configuration Release -SkipBuild
```

## 2. Install the plugin (Autoloader preferred)

1. Unpack the plugin zip (or use `install-plugin.ps1`).
2. Copy [`plugin-bundle/765T-Forge.bundle`](../plugin-bundle/765T-Forge.bundle) to `%APPDATA%\Autodesk\ApplicationPlugins` and place `Forge.Plugin.dll` under `Contents\Windows\` (see bundle README).
3. Set a strong shared token for the **AutoCAD process**:

```powershell
[Environment]::SetEnvironmentVariable("FORGE_AUTOCAD_TOKEN", "replace-with-a-long-random-secret", "User")
```

4. Restart AutoCAD 2026; run `MCP_STATUS` (or `NETLOAD` if not using Autoloader).
5. Details: [install/plugin.md](install/plugin.md).

## 3. Connect an MCP host

Use the **same** `FORGE_AUTOCAD_TOKEN` and point `command` at `Forge.Server.exe` from the server zip (or your build output).

| Host | Guide | Example |
|------|-------|---------|
| Cursor | [install/cursor.md](install/cursor.md) | [examples/mcp.cursor.json](../examples/mcp.cursor.json) |
| Claude Desktop | [install/claude-desktop.md](install/claude-desktop.md) | [examples/mcp.claude-desktop.json](../examples/mcp.claude-desktop.json) |
| VS Code / Copilot | [install/vscode.md](install/vscode.md) | [examples/mcp.vscode.json](../examples/mcp.vscode.json) |

## 4. Verify

1. Call `forge_system_health` — expect `status=ok` and matching pipe name.
2. Prefer agent skill [skills/765t-forge/SKILL.md](../skills/765t-forge/SKILL.md).
3. Before publish: `forge_qa_preflight`. Prefer `forge_plot_to_pdf` for single-layout; DSD multi-layout `forge_plot_publish` is **partial** (see [capability-matrix.md](capability-matrix.md)).

## Troubleshooting

[troubleshooting.md](troubleshooting.md) — pipe down, token mismatch, TRUSTEDPATHS, locked DLL.
