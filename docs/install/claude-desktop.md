# Install — Claude Desktop

Connect Claude Desktop to 765T-Forge over MCP stdio.

## Prerequisites

1. Build or download `Forge.Server` (see [../getting-started.md](../getting-started.md) or `scripts/build-server.ps1`).
2. For live drawing tools: AutoCAD 2025/2026 with `Forge.Plugin` loaded (`NETLOAD` / Autoloader), then `MCP_STATUS`. (The 2017–2024 plugin component is prepared but not yet verified.)
3. Set **`FORGE_AUTOCAD_TOKEN`** to a long random secret shared with the AutoCAD process. Do not use a default token.

## Claude Desktop config

Edit Claude Desktop’s MCP config (Windows typical path):

`%APPDATA%\Claude\claude_desktop_config.json`

```json
{
  "mcpServers": {
    "765t-forge": {
      "command": "C:\\path\\to\\765T-Forge\\src\\Forge.Server\\bin\\Debug\\net8.0\\Forge.Server.exe",
      "args": [],
      "env": {
        "FORGE_AUTOCAD_TOKEN": "replace-with-a-long-random-secret",
        "FORGE_PIPE_NAME": "765T.Forge.AutoCAD",
        "FORGE_BACKUP_DIR": "%LOCALAPPDATA%\\765T-Forge\\backups",
        "FORGE_AUDIT_DIR": "%LOCALAPPDATA%\\765T-Forge\\audit",
        "FORGE_AUTOCAD_ROOT": "C:\\Program Files\\Autodesk\\AutoCAD 2026",
        "FORGE_ENABLE_UNSAFE_OPS": "false"
      }
    }
  }
}
```

Template: [examples/mcp.claude-desktop.json](../../examples/mcp.claude-desktop.json).

## Token requirement

`FORGE_AUTOCAD_TOKEN` is **required** for any real install. The plugin and server must resolve the same value. Optional fallback: `MCP_AUTOCAD_TOKEN` — still set an explicit `FORGE_AUTOCAD_TOKEN` in production.

Ensure AutoCAD inherits the same environment (or set the variable for the Windows user before launching AutoCAD).

## After config change

1. Fully quit and restart Claude Desktop.
2. Confirm the `765t-forge` server appears connected.
3. Call `forge_system_health`.

## Skill

Use [skills/765t-forge/SKILL.md](../../skills/765t-forge/SKILL.md) for agent workflow rules (health first, dry-run, never invent drawing numbers, preflight before publish when available).

## Troubleshooting

See [../troubleshooting.md](../troubleshooting.md) for pipe down, missing token, NETLOAD, and locked DLL issues.
