# E01 — Health before work

## Setup
- AutoCAD 2026 running with plugin loaded; MCP server configured.

## Call
`forge_system_health`

## Expect
- `Ok=true`
- `Data.status=ok`
- `Data.pipe` equals configured `FORGE_PIPE_NAME`
