# 765T-Forge Agent Skill

Use this skill when an agent drives AutoCAD through **765T-Forge** for metro/AEC drawing-production (issue-set inspect → fix → fill → gate → publish → verify).

## Hard rules

- Prefer **typed tools** (`forge_<group>_<action>`) before generic executors.
- Always call `forge_system_health` first (and again after AutoCAD restart / NETLOAD). Confirm `Data.pipe` matches `FORGE_PIPE_NAME`.
- Use `dryRun=true` before write, publish, script, or executor calls.
- **Never invent drawing numbers** — call `forge_registry_load` / `forge_registry_lookup` when a register exists; otherwise read from the drawing or ask the human.
- Load `forge_pack_load` for project standards when available; preflight merges pack rules.
- If a tool returns a safety denial, do **not** retry via another executor; switch to a typed scoped tool or ask for a narrower target.
- The five free-text executors (`forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_batch_run`, `forge_exec_dotnet`) are disabled unless the human enabled unsafe ops for this session (`FORGE_ENABLE_UNSAFE_OPS=true` and `unsafeAcknowledged=true`). Otherwise they return `unsafe_not_acknowledged`.
- There is **no text filter**: nothing inspects the command, LISP, script, or C# you pass. Never send a whole-drawing selection (`ERASE ALL`, `*`) through an executor — the gate checks the capability, not the text, so nothing will catch it for you. Type the exact handles/names you intend.
- If `forge_exec_command` / `forge_exec_lisp` returns `queued=true` or `completed=false` (with `undoGrouped=false`), **do not** chain writes — read back first.
- AccoreConsole scripts: use `forge_run_script` / `forge_batch_run` (server-side capability gate only, no script scan — ADR 0002); Accore **Ok ≠ PDF/plot verification** — never treat as issue-set PDF SoT.
- On plugin timeout, call **`forge_qa_readback_after_timeout`** — never retry the write blind. On `plugin_busy`, wait for the in-flight call and retry.
- Prefer `forge_issue_set_validate` when an IssueSetContract / sheet inventory exists; do not invent the sheet set.
- `force=true` and ceremony booleans (`dryRunDone`, `issueAcknowledged`) are **attested** ops accepts — not human-presence or CDE proof (ADR 0004). `force=true` also requires `FORGE_ALLOW_FORCE_PUBLISH=true` or the tool returns `force_not_allowed`. Prefer ceremony optional `dryRunAuditId` / `preflightAuditId` / `receiptAuditId` after real runs.
- Do not describe a publish as page-count verified: receipts report `pageCount=null` / `pageCountSource="not_available"`. Verified fields are existence, non-zero length, and the exact `%PDF-` header.

## Error codes you will actually see

| Code | Meaning | What to do |
|------|---------|------------|
| `unsafe_not_acknowledged` | Executor blocked by the capability gate | Stop; use a typed tool, or ask the human to enable unsafe ops for this session |
| `plugin_busy` | Another plugin request is in flight (execution is serialized) | Wait for the first call to finish, then retry |
| `plugin_main_thread_timeout` | AutoCAD's main thread did not run the command callback in time (modal dialog?) | Close the dialog, then read back |
| `exec_dotnet_timeout` | Dotnet snippet did not return in time | The snippet may still run; read back before retrying |
| `invalid_command_id` | Wire id did not match `^[A-Za-z0-9-]{1,64}$` | Fix the caller |
| `frame_too_large` / `response_too_large` | Pipe frame exceeded the exact 4,000,000-character cap | Shrink the payload |
| `block_attribute_not_found` | Tag matched nothing | Check the tag name; it is a failure, not `updated=0` |
| `regex_timeout` | Pack/caller regex exceeded the 250 ms match timeout (fails closed) | Simplify the regex |
| `plot_units_unavailable` | Paper units unreadable from `PlotSettings.PlotPaperUnits` | Fix the page setup; Forge will not guess |
| `illegal_dsd_character` | DSD field contained `[`, `]`, `=`, CR/LF, or a control character | Remove it |
| `force_not_allowed` | `force=true` while `FORGE_ALLOW_FORCE_PUBLISH` is unset | Ask the human; do not set the env var yourself |

`undoWarnings[]` (`undo_group_open_failed` / `undo_group_close_failed`) means the undo group was not clean; check `undoGrouped` and read back.

## Profiles

Call `forge_system_tool_profile` (`core` | `plot` | `qa` | `linework`) to shrink the tool surface for the current task. MCP resources: `forge://profiles/{name}`.

## Exclusive AEC gates (0.2.1+)

Prefer these before publish when the project pack/register exists: `forge_qa_plot_fingerprint`, `forge_qa_dependency_closure`, `forge_qa_dual_source`, `forge_qa_modal_trap`, `forge_xref_closure`, `forge_xref_pin_save` / `_verify`, `forge_publish_ceremony_check`, `forge_cde_gate_evaluate`, `forge_transmittal_seal`. Ceremony/CDE tools are **attested / CAD-side** — not ISO 19650 issuance. Details: [references/tool-cheatsheet.md](references/tool-cheatsheet.md).

## Inspect before write / publish

1. `forge_doc_list_layouts`
2. `forge_xref_list` or `forge_qa_check_xrefs`
3. `forge_layer_list` / `forge_layer_state_list`
4. `forge_block_list_attributes`
5. **`forge_qa_preflight`** before any real publish; do not force past a failed gate unless the human explicitly requests `force`.

Prefer **`forge_recipe_issue_set`** for full issue-set runs. It reports `steps[]` with `status` exactly `completed` or `failed` and stops at the first failure — inspect the failing step, do not assume the rest ran.

## Linework (CAD ↔ Revit model QA)

- The `forge_linework_*` family is **server-side and AutoCAD-free** in this build: it reads a `pl_dump.txt` dump (`source=dumpFile` + `dumpPath`). `source=drawing` (live extraction) needs the AutoCAD plugin and is not dispatched here — it fails closed with `live_source_unavailable`.
- Layer names may be xref-prefixed (`XREF$0$Layer`). `layerFilter` is prefix-aware, but prefer **`layerSuffix`** (EndsWith on the bare layer name) when targeting a specific layer; `prefix` matching silently drops xref content.
- `forge_linework_transform` calibrates/persists/applies the CAD→Revit transform (anchor `pairs` → similarity or affine fit → `transformPath` file). Re-check `maxResidualM`; > 0.5 m means the anchors do not describe one transform.
- `forge_linework_compare` marks every CAD segment and every Revit pipe segment (`modelSegments`/`modelSegmentsPath`) as `matched` / `partial` / `missing_in_revit` / `extra_off_cad` with distance; `overlayPath` writes a color-coded SVG for human review.
- These tools write only caller-supplied artifact paths (`outputPath`, `overlayPath`, `transformPath`); they are **not** read-only and require no backup. Unit-tested only — do not claim a real drawing was QA'd from them.

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

- Prefer `forge_system_capabilities` before choosing device/paper/CTB·STB; it restores the previous plot device and reports `currentConfig.previousDevice` / `restored` / `restoreError`.
- Require `overwriteAcknowledged=true` when replacing an existing PDF/pack folder.
- **Do not claim PDFs were published** unless `verification.passed` / receipt PDF probe passed and the output file exists. Page count is never verified. See `docs/capability-matrix.md`.
- After publish, keep the **PublishReceipt** artifact path for audit / `forge_issue_set_diff`.

## Safety expectations

Every write should produce an audit record and, when a DWG path is available, a backup. On plugin timeout, call `forge_qa_readback_after_timeout` before any retry. Use `forge_audit_summarize` for a redacted session overview.

## Capability awareness

Trust [docs/capability-matrix.md](../../docs/capability-matrix.md). Prompts: `issue_set_runbook`, `safety_first`.
