# Install — VS Code (GitHub Copilot MCP)

Connect VS Code's Copilot agent mode to 765T-Forge over MCP stdio.

## Prerequisites

1. Build `Forge.Server` (see [CONTRIBUTING.md](../CONTRIBUTING.md) or `scripts/build-server.ps1`).
2. For live drawing tools: AutoCAD 2025 or 2026 with `Forge.Plugin` loaded (`NETLOAD`), then `MCP_STATUS`.
3. Set **`FORGE_AUTOCAD_TOKEN`** to a long random secret shared with the AutoCAD process.

## Workspace `.vscode/mcp.json`

VS Code uses a `servers` key (not `mcpServers`):

```json
{
  "servers": {
    "765t-forge": {
      "type": "stdio",
      "command": "C:\\path\\to\\765T-Forge\\src\\Forge.Server\\bin\\Release\\net8.0\\Forge.Server.exe",
      "args": [],
      "env": {
        "FORGE_AUTOCAD_TOKEN": "${input:forge-autocad-token}",
        "FORGE_PIPE_NAME": "765T.Forge.AutoCAD",
        "FORGE_TOOLSETS": "all",
        "FORGE_BACKUP_DIR": "${userHome}\\AppData\\Local\\765T-Forge\\backups",
        "FORGE_AUDIT_DIR": "${userHome}\\AppData\\Local\\765T-Forge\\audit"
      }
    }
  },
  "inputs": [
    {
      "id": "forge-autocad-token",
      "type": "promptString",
      "description": "Shared secret for the Forge AutoCAD pipe (must match the AutoCAD process environment)",
      "password": true
    }
  ]
}
```

The `inputs` block keeps the token out of the checked-in file — VS Code prompts for it
once per session.

## Toolsets

Default startup loads core + discovery tools only; `FORGE_TOOLSETS=all` lists the full
catalog up front (recommended here — Copilot handles `tools/list_changed`, but `all`
removes the extra round-trip).

## Future `dnx` install

Once `Forge765T.Server` is published to NuGet (prepared, **not** published — see
[packaging-nuget.md](packaging-nuget.md)):

```json
"command": "dnx",
"args": ["Forge765T.Server@<pinned-version>", "--yes"]
```

Pin a version; never `@latest`.

## Verify

1. Copilot chat → tools icon — `765t-forge` tools listed.
2. Call `forge_system_health`.
3. Pipe down / token issues: [troubleshooting.md](troubleshooting.md).

## Skill

Use [skills/765t-forge/SKILL.md](../skills/765t-forge/SKILL.md) for agent workflow rules.
