# Tool cheatsheet (hot path)

| Goal | Tools |
|------|-------|
| Health | `forge_system_health`, `forge_system_version` (answers on the Start tab; other tools need an open drawing — `no_active_document`; drawings open but none active — `no_command_context`) |
| Profiles | `forge_system_tool_profile` (`core`/`plot`/`qa`) |
| Registry | `forge_registry_load`, `forge_registry_lookup` |
| Standards | `forge_pack_load`, `forge_pack_status` |
| Issue set | `forge_sheet_inventory_import`, `forge_issue_set_validate`, `forge_issue_set_diff` |
| Inspect | `forge_doc_list_layouts`, `forge_xref_list`, `forge_layer_list`, `forge_block_list_attributes`, `forge_viewport_list` |
| Ground | `forge_entity_get` (one handle), `forge_entity_list` (window or layer — no select-all), `forge_block_list`, `forge_layer_list` |
| Draw | `forge_layer_create` first, then `forge_draw_line/_polyline/_circle/_arc/_text/_mtext/_hatch/_dimension` — many shapes per call: `forge_draw_batch` (≤1000 steps; `atomic=true`, `resultMode=summary` for big batches); CSV point lists: `forge_draw_polyline_from_csv` |
| Bring in / set up | `forge_doc_import` (DXF/DWG → block / merge / xref in one call), `forge_standards_import` (styles, layers, page setups from a template), `forge_block_insert_bulk` (≤2000 INSERTs + tags) |
| Modify | `forge_entity_move/_copy/_rotate/_scale/_mirror/_array/_offset/_erase/_set_props` by handles; `forge_undo` reverts the last Forge write, verified (`undone=true` only after readback; refuses if the drawing changed since) |
| See / measure | `forge_view_capture` (PNG — read it to visually verify), `forge_measure_batch` (engine length/area/distance) |
| Sheets | `forge_layout_create`, `forge_viewport_add`, `forge_layer_set`, `forge_doc_new`, `forge_doc_close`, `forge_doc_export_dxf` |
| Gate | `forge_qa_annotation_clash` (text overlap / crossing / door swing / wall / plotted height — before PNG review), `forge_qa_preflight`, `forge_qa_check_xrefs`, `forge_qa_verify_titleblock` |
| Timeout | `forge_qa_readback_after_timeout`, `forge_audit_summarize` — `plugin_request_timeout` → readback; `plugin_request_stale` = nothing ran |
| Publish | `forge_plot_preset_apply` (one plot setup on many layouts), `forge_plot_to_pdf`, `forge_plot_publish` (receipt), `forge_recipe_issue_set` |
| Batch | `forge_batch_run` (+ `resumeBatchId`), `forge_batch_status` |
| Convert | `forge_dgn_import` (DGN **V8** → DWG; dry-run first, V7 refused, overwrite ack) |
| Exec (last resort) | `forge_exec_command` / `_lisp` — check `completed`; `forge_exec_dotnet` dual-gated |

Honest status: [docs/capability-matrix.md](../../docs/capability-matrix.md)
