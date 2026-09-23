# E01 — Health before work

## Setup
- AutoCAD 2025/2026 running with plugin loaded (the 2017–2024 component is prepared but not yet verified); MCP server configured.

## Call
`forge_system_health`

## Expect
- `Ok=true`
- `Data.status=ok`
- `Data.pipe` equals configured `FORGE_PIPE_NAME`
