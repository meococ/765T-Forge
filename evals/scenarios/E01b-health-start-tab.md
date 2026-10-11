# E01b — Health on the Start tab (no open drawing)

## Setup
- AutoCAD 2025 or 2026 running with plugin loaded, sitting on the Start tab (no document open); MCP server configured.

## Call
`forge_system_health`, `forge_doc_list_open`, then one document tool (e.g. `forge_layer_list`)

## Expect
- `forge_system_health` `Ok=true`, `Data.status=ok`, activeDocument null, `Data.pipe` equals configured `FORGE_PIPE_NAME`
- `forge_doc_list_open` `Ok=true` with an empty open-document list
- `forge_layer_list` fails with error code `no_command_context`
- After opening a drawing (`forge_doc_open`), the same `forge_layer_list` call succeeds (`Ok=true`)
