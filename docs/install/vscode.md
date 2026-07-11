# Install — VS Code / Copilot MCP

Connect VS Code (GitHub Copilot MCP or compatible MCP extension) to 765T-Forge over stdio.

## Prerequisites

Same as Cursor: Windows, .NET 8, AutoCAD 2026 + plugin loaded, shared `FORGE_AUTOCAD_TOKEN`. See [../getting-started.md](../getting-started.md).

## Config

Add a server entry to your MCP configuration (workspace `.vscode/mcp.json` or user MCP settings, depending on the extension). Template: [examples/mcp.vscode.json](../../examples/mcp.vscode.json).

```json
{
  "servers": {
    "765t-forge": {
      "type": "stdio",
      "command": "C:\\path\\to\\Forge.Server.exe",
      "args": [],
      "env": {
        "FORGE_AUTOCAD_TOKEN": "replace-with-a-long-random-secret",
        "FORGE_PIPE_NAME": "765T.Forge.AutoCAD",
        "AUTOCAD_2026_ROOT": "C:\\Program Files\\Autodesk\\AutoCAD 2026"
      }
    }
  }
}
```

Exact JSON shape may vary by VS Code MCP host version — keep `command`, `args`, and `env` equivalent.

## Verify

1. Reload the MCP server list.
2. Call `forge_system_health`.
3. If the pipe is down, see [../troubleshooting.md](../troubleshooting.md).
