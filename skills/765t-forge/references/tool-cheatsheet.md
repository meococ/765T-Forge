# Tool cheatsheet (hot path)

| Goal | Tools |
|------|-------|
| Health | `forge_system_health`, `forge_system_version` |
| Profiles | `forge_system_tool_profile` (`core`/`plot`/`qa`) |
| Registry | `forge_registry_load`, `forge_registry_lookup` |
| Standards | `forge_pack_load`, `forge_pack_status` |
| Issue set | `forge_sheet_inventory_import`, `forge_issue_set_validate`, `forge_issue_set_diff` |
| Inspect | `forge_doc_list_layouts`, `forge_xref_list`, `forge_layer_list`, `forge_block_list_attributes`, `forge_viewport_list` |
| Gate | `forge_qa_preflight`, `forge_qa_plot_fingerprint`, `forge_qa_dependency_closure`, `forge_qa_dual_source`, `forge_qa_modal_trap`, `forge_qa_check_xrefs` |
| Timeout | `forge_qa_readback_after_timeout`, `forge_audit_summarize` |
| Xref pin | `forge_xref_closure`, `forge_xref_pin_save`, `forge_xref_pin_verify` |
| Ceremony / CDE | `forge_publish_ceremony_check` (optional AuditId evidence), `forge_cde_gate_evaluate`, `forge_transmittal_seal` — **attested** flags, not human/CDE proof (ADR 0004) |
| Publish | `forge_plot_to_pdf`, `forge_plot_publish` (receipt with `AuditId`; DSD partial + plot fallback; `pageCount=null` / `not_available`; force needs `FORGE_ALLOW_FORCE_PUBLISH`), `forge_recipe_issue_set` (steps stop at first failure) |
| Xref depth | `forge_xref_closure` / pin / `forge_qa_dependency_closure` (`maxDepth`, optional `failClosed`) |
| Batch / Accore | `forge_batch_run` (+ `resumeBatchId`), `forge_batch_status`, `forge_run_script` (AccoreConsole; server-only capability gate, no script scan — **not** PDF SoT) |
| Pack | `forge_pack_and_go` (plans, resolves names, stages, then moves; `renamedFrom` in manifest) |
| Exec (last resort) | `forge_exec_command` / `_lisp` — check `completed`; `forge_exec_dotnet` — all five executors are `Unsafe` and need `FORGE_ENABLE_UNSAFE_OPS` + `unsafeAcknowledged`, else `unsafe_not_acknowledged`; no text filter |

Error codes to branch on: `unsafe_not_acknowledged`, `plugin_busy`, `plugin_main_thread_timeout`, `exec_dotnet_timeout`, `invalid_command_id`, `frame_too_large` / `response_too_large`, `block_attribute_not_found`, `regex_timeout`, `plot_units_unavailable`, `illegal_dsd_character`, `force_not_allowed`, `undoWarnings[]` (`undo_group_open_failed` / `undo_group_close_failed`).

Honest status: [docs/capability-matrix.md](../../../docs/capability-matrix.md)
