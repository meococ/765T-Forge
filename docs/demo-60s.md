# Demo script (60s narrative)

Use this as a recording checklist for a GIF/video. Prefer **Release zips** (not a dirty tree) so the demo matches what GitHub users download. Do not commit customer DWGs.

## Storyboard

1. **0–10s** — AutoCAD + `MCP_STATUS` (version/pipe/token); Cursor calls `forge_system_health` (pipe OK).
2. **10–20s** — `forge_registry_load` + `forge_pack_load` (demo pack) + optional `forge_sheet_inventory_import` / `forge_issue_set_validate`.
3. **20–35s** — `forge_qa_preflight` fails on empty `DWG_NO` (show finding + SuggestedTool).
4. **35–50s** — Campaign dry-run from registry → write → preflight pass (foreground plot warning cleared).
5. **50–60s** — `forge_plot_publish` dry-run then publish; show **PublishReceipt** + open PDF.

## Commands (agent)

Prefer skill `skills/765t-forge/SKILL.md`. Profile hint: `forge_system_tool_profile` name=`plot`.

## Release artifact path

1. `.\scripts\pack-release.ps1 -Configuration Release` on a machine with AutoCAD (plugin zip).
2. Point MCP config at the published `Forge.Server.exe` from `artifacts/release/server/`.
3. Confirm `docs/` folder sits next to the exe before recording.
