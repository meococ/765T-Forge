# 765T-Forge Agent Skill

Use this skill when an agent drives AutoCAD through **765T-Forge** for metro/AEC drawing-production (issue-set inspect → fix → fill → gate → publish → verify).

## Hard rules

- Prefer **typed tools** (`forge_<group>_<action>`) before generic executors.
- Always call `forge_system_health` first (and again after AutoCAD restart / NETLOAD). Confirm `Data.pipe` matches `FORGE_PIPE_NAME`.
- Use `dryRun=true` before write, publish, script, or executor calls.
- **Never invent drawing numbers** — call `forge_registry_load` / `forge_registry_lookup` when a register exists; otherwise read from the drawing or ask the human.
- Load `forge_pack_load` for project standards when available; preflight merges pack rules.
- If a tool returns a safety denial, do **not** retry via another executor; switch to a typed scoped tool or ask for a narrower target.
- Treat `forge_exec_dotnet` as disabled unless the human explicitly enabled unsafe ops for this session (`FORGE_ENABLE_UNSAFE_OPS` + `unsafeAcknowledged`).
- Never use executors for broad selections such as `ERASE ALL` or `ERASE *` (harmless `ZOOM *` / layer filters are OK).
- If `forge_exec_command` / `forge_exec_lisp` returns `queued=true` or `completed=false`, **do not** chain writes — read back first.
- AccoreConsole scripts: use `forge_run_script` / `forge_batch_run` (server-side safety only — ADR 0002); Accore **Ok ≠ PDF/plot verification** — never treat as issue-set PDF SoT.
- On plugin timeout, call **`forge_qa_readback_after_timeout`** — never retry the write blind.
- Prefer `forge_issue_set_validate` when an IssueSetContract / sheet inventory exists; do not invent the sheet set.
- `force=true` and ceremony booleans (`dryRunDone`, `issueAcknowledged`) are **attested** ops accepts — not human-presence or CDE proof (ADR 0004). `force=true` also requires `FORGE_ALLOW_FORCE_PUBLISH=true` or the tool returns `force_not_allowed`. Prefer ceremony optional `dryRunAuditId` / `preflightAuditId` / `receiptAuditId` after real runs.

## Profiles

Call `forge_system_tool_profile` (`core` | `plot` | `qa`) to shrink the tool surface for the current task. MCP resources: `forge://profiles/{name}`.

## Exclusive AEC gates (0.2.1+)

Prefer these before publish when the project pack/register exists: `forge_qa_plot_fingerprint`, `forge_qa_dependency_closure`, `forge_qa_dual_source`, `forge_qa_modal_trap`, `forge_xref_closure`, `forge_xref_pin_save` / `_verify`, `forge_publish_ceremony_check`, `forge_cde_gate_evaluate`, `forge_transmittal_seal`. Ceremony/CDE tools are **attested / CAD-side** — not ISO 19650 issuance. Details: [references/tool-cheatsheet.md](references/tool-cheatsheet.md).

## Inspect before write / publish

1. `forge_doc_list_layouts`
2. `forge_xref_list` or `forge_qa_check_xrefs`
3. `forge_layer_list` / `forge_layer_state_list`
4. `forge_block_list_attributes`
5. **`forge_qa_preflight`** before any real publish; do not force past a failed gate unless the human explicitly requests `force`.

Prefer **`forge_recipe_issue_set`** for full issue-set runs.

## Metro hot path

1. Health + `forge_registry_load` / `forge_pack_load` when project files exist.
2. List layouts; choose the target sheet set.
3. Check xrefs; `forge_xref_normalize_relative` / repath / reload.
4. Restore layer state (`forge_layer_state_restore`) — dry-run first.
5. Fill titleblock via `forge_block_campaign` / `forge_block_set_attr` (registry values only).
6. Page setup apply (prefer sync import/apply).
7. Preflight → plot/publish with overwrite ack when needed.
8. Verify again (`forge_qa_*`).

### Publish honesty

- Prefer `forge_system_capabilities` before choosing device/paper/CTB·STB.
- Require `overwriteAcknowledged=true` when replacing an existing PDF/pack folder.
- **Do not claim PDFs were published** unless `verification.passed` / receipt PDF probe passed and the output file exists. See `docs/capability-matrix.md`.
- After publish, keep the **PublishReceipt** artifact path for audit / `forge_issue_set_diff`.

## Safety expectations

Every write should produce an audit record and, when a DWG path is available, a backup. On plugin timeout, call `forge_qa_readback_after_timeout` before any retry. Use `forge_audit_summarize` for a redacted session overview.

## Capability awareness

Trust [docs/capability-matrix.md](../../docs/capability-matrix.md). Prompts: `issue_set_runbook`, `safety_first`.
