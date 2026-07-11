# Tool cheatsheet (hot path)

| Goal | Tools |
|------|-------|
| Health | `forge_system_health`, `forge_system_version` |
| Profiles | `forge_system_tool_profile` (`core`/`plot`/`qa`) |
| Registry | `forge_registry_load`, `forge_registry_lookup` |
| Standards | `forge_pack_load`, `forge_pack_status` |
| Issue set | `forge_sheet_inventory_import`, `forge_issue_set_validate`, `forge_issue_set_diff` |
| Inspect | `forge_doc_list_layouts`, `forge_xref_list`, `forge_layer_list`, `forge_block_list_attributes`, `forge_viewport_list` |
| Gate | `forge_qa_preflight`, `forge_qa_check_xrefs`, `forge_qa_verify_titleblock` |
| Timeout | `forge_qa_readback_after_timeout`, `forge_audit_summarize` |
| Publish | `forge_plot_to_pdf`, `forge_plot_publish` (receipt), `forge_recipe_issue_set` |
| Batch | `forge_batch_run` (+ `resumeBatchId`), `forge_batch_status` |
| Exec (last resort) | `forge_exec_command` / `_lisp` — check `completed`; `forge_exec_dotnet` dual-gated |

Honest status: [docs/capability-matrix.md](../../docs/capability-matrix.md)
