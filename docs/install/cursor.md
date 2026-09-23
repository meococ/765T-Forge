# Install — Cursor

Connect Cursor to 765T-Forge over MCP stdio.

## Prerequisites

1. Build or download `Forge.Server` (see [../getting-started.md](../getting-started.md) or `scripts/build-server.ps1`).
2. For live drawing tools: AutoCAD 2025/2026 running with `Forge.Plugin` loaded (`NETLOAD` / Autoloader), then `MCP_STATUS`. (The 2017–2024 plugin component is prepared but not yet verified.)
3. A **strong** shared token in the environment — **required**. Do not rely on any default secret.

## `mcp.json` example

Place this in your Cursor MCP config (user or project). Adjust the `command` path to your build output.

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

A checked-in template with placeholders lives at [examples/mcp.cursor.json](../../examples/mcp.cursor.json).

## Token requirement

| Variable | Required | Notes |
|----------|----------|-------|
| `FORGE_AUTOCAD_TOKEN` | **Yes** | Must match the token visible to the AutoCAD process hosting the plugin |
| `MCP_AUTOCAD_TOKEN` | Fallback only | Used if `FORGE_AUTOCAD_TOKEN` is unset — prefer setting `FORGE_AUTOCAD_TOKEN` explicitly |

Set the same token for AutoCAD (user or session environment) before `NETLOAD`, or the plugin will reject pipe calls.

## Agent skill

Point Cursor skills at [skills/765t-forge/SKILL.md](../../skills/765t-forge/SKILL.md) so agents prefer typed tools, health checks, dry-run, and preflight when available.

## Verify

1. Restart Cursor MCP / reload servers.
2. Call `forge_system_health`.
3. If the pipe is down, see [../troubleshooting.md](../troubleshooting.md).
