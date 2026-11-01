# Install — Claude Code

Connect Claude Code to 765T-Forge over MCP stdio.

## Prerequisites

1. Build `Forge.Server` (see [CONTRIBUTING.md](../CONTRIBUTING.md) or `scripts/build-server.ps1`).
2. For live drawing tools: AutoCAD 2025 or 2026 with `Forge.Plugin` loaded (`NETLOAD`), then `MCP_STATUS`.
3. Set **`FORGE_AUTOCAD_TOKEN`** to a long random secret shared with the AutoCAD process.

## Project `.mcp.json`

Drop this in the project root as `.mcp.json` (checked-in or local):

```json
{
  "mcpServers": {
    "765t-forge": {
      "command": "C:\\path\\to\\765T-Forge\\src\\Forge.Server\\bin\\Release\\net8.0\\Forge.Server.exe",
      "args": [],
      "env": {
        "FORGE_AUTOCAD_TOKEN": "replace-with-a-long-random-secret",
        "FORGE_PIPE_NAME": "765T.Forge.AutoCAD",
        "FORGE_TOOLSETS": "all",
        "FORGE_BACKUP_DIR": "%LOCALAPPDATA%\\765T-Forge\\backups",
        "FORGE_AUDIT_DIR": "%LOCALAPPDATA%\\765T-Forge\\audit"
      }
    }
  }
}
```

Or register from the CLI (user scope):

```powershell
claude mcp add-json 765t-forge '{\"command\":\"C:\\path\\to\\Forge.Server.exe\",\"env\":{\"FORGE_AUTOCAD_TOKEN\":\"replace-with-a-long-random-secret\",\"FORGE_TOOLSETS\":\"all\"}}'
```

## Toolsets

Default startup loads core + discovery tools only (~19 tools) — enough for
`forge_toolset_enable` to widen at runtime. Set `FORGE_TOOLSETS=all` (as above) if you
want the full catalog listed up front.

## Future `dnx` install

Once `Forge765T.Server` is published to NuGet (prepared, **not** published — see
[packaging-nuget.md](packaging-nuget.md)), the command becomes a one-liner:
`dnx Forge765T.Server@<pinned-version>` — pin a version, never `@latest`.

## Verify

1. `/mcp` in Claude Code — `765t-forge` should show connected.
2. Call `forge_system_health`.
3. Pipe down / token issues: [troubleshooting.md](troubleshooting.md).

## Skill

Use [skills/765t-forge/SKILL.md](../skills/765t-forge/SKILL.md) for agent workflow rules.
