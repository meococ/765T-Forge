# Safety

Forge is designed for AI agents that can mutate production DWGs. Safety here is a small set of exact gates, not a guess about command text.

## Capability gate (there is no text denylist)

`SafetyPolicy.Evaluate(command, unsafeOpsEnabled)` is deterministic:

1. **Read-only tools** — metadata `ReadOnly` → allowed.
2. **Unsafe tools** — metadata `Unsafe` in `ForgeToolRegistry` → allowed only when **both** gates are true, otherwise refused with code `unsafe_not_acknowledged`:
   - process-level `FORGE_ENABLE_UNSAFE_OPS=true`, read by `ForgeEnvironment.FromProcess()`; and
   - per-call `unsafeAcknowledged=true`.
3. **Everything else** → allowed.

The five `Unsafe` tools are the free-text executors:

- `forge_exec_command`
- `forge_exec_lisp`
- `forge_run_script`
- `forge_batch_run`
- `forge_exec_dotnet`

Unknown tool names are not inspected by the gate: `ForgeToolRegistry.Get` returns a `Destructive` metadata record for a name that is not registered, and the plugin's final dispatch arm returns `unknown_tool` — so an unregistered name fails at dispatch rather than executing anything.

**No argument text is inspected anywhere.** There is no regex denylist, no keyword blocklist, no "dangerous command" search over command strings, script files, or LISP. This is deliberate. Keyword matching cannot model AutoCAD command aliases, unique-prefix abbreviations, menu macros, or AutoLISP, so a text denylist produced false confidence about the commands it never recognized. The replacement gates the capability as a whole — it does not try to guess the text.

Typed tools' business values (titleblock notes, folder names, attribute text) are not scanned. That was true before and it is true now; the difference is that nothing else is scanned either.

## Dual evaluation (pipe path) vs headless

- **Named-pipe tools:** the gate runs on **both** the server (`ForgeToolRunner`) and the plugin (`PluginCommandProcessor`) using the same `SafetyPolicy` and each process's own `FORGE_ENABLE_UNSAFE_OPS`. The per-call `unsafeAcknowledged` flag is carried in the command over the pipe. See [adr/0002-dual-safety-evaluation.md](adr/0002-dual-safety-evaluation.md).
- **Headless AccoreConsole** (`forge_run_script`, `forge_batch_run`): these tools are handled by `HeadlessAccoreConsoleRunner` on the **server** and never enter the plugin. Safety for them is the server-side capability gate only. Do not describe them as dual-eval. No script-file contents are scanned.
- **Server-only tools** (`forge_batch_status`, `forge_system_tool_profile`, `forge_audit_summarize`, `forge_sheet_inventory_import`, `forge_issue_set_diff`) also never cross the pipe.

## Unsafe executor semantics (read this before enabling)

`forge_exec_dotnet` runs Roslyn C# inside AutoCAD with **full process trust, not sandboxed**. The timeout bounds how long the server waits for a result; a script already running inside AutoCAD cannot be forcibly aborted. On timeout the result code is **`exec_dotnet_timeout`**, and the snippet may still be executing. Roslyn assemblies ship with the plugin (`CopyLocalLockFileAssemblies`), so the executor can load at runtime; that is what makes it work, not what makes it safe.

Leave unsafe ops disabled unless a human explicitly enables them for a session they control.

## Deterministic gates and typed error codes

These are exact gates and exact validations. They either match or they do not; none of them infers intent.

| Gate | Behaviour | Code |
|------|-----------|------|
| Unsafe tool without both gates | Refused before dispatch | `unsafe_not_acknowledged` |
| Concurrent plugin execution | Second request refused while one is in flight; retry after it completes | `plugin_busy` |
| AutoCAD main thread does not run the command-context callback in time (for example a modal dialog is open) | Refused | `plugin_main_thread_timeout` |
| Pipe request frame over the exact 4,000,000-character cap | Refused | `frame_too_large` |
| Serialized response over the exact 4,000,000-character cap | Not sent | `response_too_large` |
| Wire command id not matching `^[A-Za-z0-9-]{1,64}$` | Refused | `invalid_command_id` |
| `forge_block_set_attr` / `forge_block_campaign` tag matches nothing | Failure (not a silent `updated=0` success) | `block_attribute_not_found` |
| Pack- or caller-supplied regex exceeding the 250 ms match timeout | Fails closed | `regex_timeout` |
| Paper units not readable from `PlotSettings.PlotPaperUnits` | Refused; units are never guessed from the paper name | `plot_units_unavailable` |
| DSD field containing `[`, `]`, `=`, CR/LF, or another control character | Refused | `illegal_dsd_character` |
| `force=true` while `FORGE_ALLOW_FORCE_PUBLISH` is false | Refused | `force_not_allowed` |
| Drawing number not in a loaded registry | Refused | `deny_unknown_drawing_no` |
| Tool with no plugin dispatch arm | Failure at dispatch | `unknown_tool` |
| Dotnet snippet did not return before the response timeout | Timeout reported; snippet may still run | `exec_dotnet_timeout` |

When an undo group cannot be opened or closed, the result carries `undoWarnings[]` with codes `undo_group_open_failed` / `undo_group_close_failed`. Results also expose **`undoGrouped`**: `true` for synchronous `Editor.Command` paths, `false` for queued `SendStringToExecute` fallbacks, because queued work runs after the undo group has closed.

## Dry-run

Write tools accept `dryRun=true` and return what they would do without changing the drawing. Use dry-run before publish, save, attribute campaigns, scripts, and any executor call.

## Backup

Before non-dry-run writes whose metadata sets `RequiresBackup`, Forge copies the active DWG to `FORGE_BACKUP_DIR` (default `%LOCALAPPDATA%\765T-Forge\backups`). Backups are timestamped and do not overwrite prior copies by default.

## Audit

Operations write JSONL audit records under `FORGE_AUDIT_DIR` (default `%LOCALAPPDATA%\765T-Forge\audit`). Records are written **one file per provenance**: `server-{yyyyMMdd}.jsonl` and `plugin-{yyyyMMdd}.jsonl`, so two processes cannot interleave partial lines. Readers enumerate `*.jsonl`.

Fields named exactly `hmacKey`, `token`, `authToken`, `secret`, `password`, or `apiKey` are replaced with `[redacted]` before an audit record is serialized. Treat audit logs as sensitive (paths, arguments). Prefer correlating by `AuditId`.

## No default token (production)

Do **not** run shared or production hosts on a documented default pipe secret. Set:

```text
FORGE_AUTOCAD_TOKEN=<long random value>
```

on both the MCP server process and the AutoCAD process. See [install/cursor.md](install/cursor.md) and [SECURITY.md](../SECURITY.md).

## Overwrite acknowledgement

Destructive path overwrites require explicit acknowledgement:

- `overwriteAcknowledged` on SaveAs / PDF plot / DSD publish / pack-and-go when the destination already exists

Silent clobber is refused with a typed error code.

## Publish preflight

`forge_qa_preflight` (and recipes that call it) can **block** publish when xrefs, required titleblock tags, unresolved `####` fields, or standards-pack rules fail. Pass `force=true` only with human approval **and** `FORGE_ALLOW_FORCE_PUBLISH=true` on server + AutoCAD (default deny → `force_not_allowed`).

## Step and transaction honesty

- `forge_recipe_issue_set` reports `steps[]` where each step's `status` is exactly `completed` or `failed`, and it stops at the first failure.
- `forge_block_campaign` commits in a single transaction.
- `forge_pack_and_go` plans all destinations before copying, resolves same-named xrefs deterministically (suffix derived from the parent folder, recorded as `renamedFrom` in the manifest), stages the whole pack, and only then moves it into place.
- `forge_system_capabilities` captures the previous plot device and restores it in a `finally`, reporting `currentConfig.previousDevice` / `restored` / `restoreError`.

## Publish receipts (what is verified)

A publish receipt no longer reports a PDF page count. `pageCount` is `null` with `pageCountSource = "not_available"` because the previous `/Type /Page` scan of binary PDF bytes was a guess. The verified fields are exactly:

1. the file exists,
2. its length is greater than 0, and
3. its first five bytes are exactly `%PDF-`.

## Drawing number registry

When a sheet register is loaded (`forge_registry_load`), attribute writes that claim a drawing number must match the registry — agents must not invent sheet numbers. See [standards-and-registry.md](standards-and-registry.md).

## Timeout / write races

If the plugin response times out, a write may already have committed. **Read back** (`forge_qa_*`, list tools) before retrying. Do not "fix" a timeout by re-sending the same write blindly.

Open-world executors that still queue AutoCAD commands expose `queued` / `completed` flags — `Ok=true` alone is not completion.

## Agent rules of thumb

Prefer typed tools → health first → load registry/pack when available → inspect → dry-run → write → verify → preflight → publish. Never invent drawing numbers. Full agent guidance: [skills/765t-forge/SKILL.md](../skills/765t-forge/SKILL.md).

## Attestation vs enforcement (read this)

| Mechanism | Class | Meaning |
|-----------|--------|---------|
| SafetyPolicy capability gate (unsafe executors require env + per-call ack) | **Enforced** | An unsafe tool cannot run without both gates; no evaluation of the text it will run |
| Registry drawing-number fail-closed (when loaded) | **Enforced** | |
| Overwrite acknowledgement | **Enforced** | |
| Preflight block | **Enforced until** `force=true` **and** `FORGE_ALLOW_FORCE_PUBLISH=true` | `force` is an **ops accept** — treat as human-only process, not agent convenience |
| `forge_publish_ceremony_check` flags | **Attested** | Caller can lie; does not prove dry-run/preflight/human |
| AccoreConsole exit code | **Not publish verification** | Never issue-set PDF SoT |
| `verification.passed` / PublishReceipt | **Evidence-backed probe** | Existence, non-zero length, exact `%PDF-` header — not page count, not nested visual truth |

Compromised or reckless agents with the pipe token are **out of scope** as a solved security problem (see [SECURITY.md](../SECURITY.md)). Forge reduces accident surface; it does not sandbox a hostile same-user agent. See [ADR 0004](adr/0004-attestation-vs-enforcement.md).
