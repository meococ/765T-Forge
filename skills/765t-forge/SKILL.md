# 765T-Forge Agent Skill

Use this skill when an agent drives AutoCAD through **765T-Forge** for metro/AEC drawing-production (issue-set inspect → fix → fill → gate → publish → verify).

## Hard rules

- Prefer **typed tools** (`forge_<group>_<action>`) before generic executors.
- Always call `forge_system_health` first (and again after AutoCAD restart / NETLOAD). Confirm `Data.pipe` matches `FORGE_PIPE_NAME`.
- NETLOAD the `Forge.Plugin.dll` built for the running AutoCAD year (`autocad-<year>`). A 2026 DLL does not load in 2017–2025. `autocad_version_unsupported` means the DLL year does not match the host, or the feature is not available on that year (`forge_exec_dotnet` on 2017–2018).
- Use `dryRun=true` before write, publish, script, or executor calls.
- **Never invent drawing numbers** — call `forge_registry_load` / `forge_registry_lookup` when a register exists; otherwise read from the drawing or ask the human.
- Load `forge_pack_load` for project standards when available; preflight merges pack rules.
- If a tool returns a safety denial, do **not** retry via another executor; switch to a typed scoped tool or ask for a narrower target.
- Treat `forge_exec_dotnet` as disabled unless the human explicitly enabled unsafe ops for this session (`FORGE_ENABLE_UNSAFE_OPS` + `unsafeAcknowledged`).
- Never use executors for broad selections such as `ERASE ALL` or `ERASE *` (harmless `ZOOM *` / layer filters are OK).
- If `forge_exec_command` / `forge_exec_lisp` returns `Ok=false` with `queued=true` or `completed=false`, **do not** chain writes — read back first.
- `dryRun=true` with `Ok=true` and `data.dryRun=true` is only a plan. Call again with `dryRun=false` to perform the work.
- `passed=false` is `Ok=false`. The report stays in `data`. Do not treat that call as success.
- `forge_block_set_attr` needs `handle` or `blockName`. Omitting both is rejected.
- On plugin timeout, call **`forge_qa_readback_after_timeout`** — never retry the write blind.
- Prefer `forge_issue_set_validate` when an IssueSetContract / sheet inventory exists; do not invent the sheet set.

## Profiles

Call `forge_system_tool_profile` (`core` | `plot` | `qa`) to shrink the tool surface for the current task. MCP resources: `forge://profiles/{name}`.

## Inspect before write / publish

1. `forge_doc_list_layouts`
2. `forge_xref_list` or `forge_qa_check_xrefs`
3. `forge_layer_list` / `forge_layer_state_list`
4. `forge_block_list_attributes`
5. **`forge_qa_preflight`** before any real publish. A failed gate is `Ok=false`. Use `force` only when a human asks in this session; a bypass still returns `Ok=false` with `preflight_forced`.

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

## Linework (CAD ↔ Revit model QA)

- `forge_linework_dump` / `_segments` extract linework from the live drawing (`source=drawing`, model space + xref contents transformed to host space) or a `pl_dump.txt` file (`source=dumpFile` — runs server-side, no AutoCAD needed).
- Layer names may be xref-prefixed (`XREF$0$Layer`). `layerFilter` is prefix-aware, but prefer **`layerSuffix`** (EndsWith on the bare layer name) when targeting a specific layer; never assume `prefix` matching is safe — it silently drops xref content.
- `forge_linework_transform` calibrates/persists/applies the CAD→Revit transform (anchor `pairs` → similarity or affine fit → `transformPath` file). Re-check `maxResidualM`; > 0.5 m means the anchors do not describe one transform.
- `forge_linework_compare` marks every CAD segment and every Revit pipe segment (`modelSegments`/`modelSegmentsPath`) as `matched` / `partial` / `missing_in_revit` / `extra_off_cad` with distance; `overlayPath` writes a color-coded SVG for human review.

## Capability awareness

Trust [docs/capability-matrix.md](../../docs/capability-matrix.md). Prompts: `issue_set_runbook`, `safety_first`.
