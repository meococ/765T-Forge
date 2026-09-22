# Safety

Forge is designed for AI agents that can mutate production DWGs. Safety is mandatory, not optional.

## Denylist scope

`SafetyPolicy` applies a regex denylist to **open-world** executor payloads:

- `forge_exec_command`
- `forge_exec_lisp`
- `forge_run_script`
- `forge_exec_dotnet`

Blocked patterns include (non-exhaustive): `ERASE ALL` and the `E` / `_.E` alias with `ALL`, `DELETE ALL`, `PURGE`, `OVERKILL`, `RECOVER`, `AUDIT` with fix, layer delete commands, `-LAYER` + `D`/`Delete` + `*`/`ALL`, `ERASE` by fence/window/crossing (`F`/`W`/`C`/`WP`/`CP` and the long keywords), `ssget` `"_X"` / `"_A"` combined with erase/delete/`command`, `ssget` `"C"`/`"W"`/`"F"`/`"WP"`/`"CP"` combined with erase/delete/`command`, `strcat`/`eval` combined with `ALL` or `ssget`, `(eval (read`, `ARX` + `Load`/`L` and `(arxload`, command-anchored `NETLOAD` / `APPLOAD` / `ARXLOAD` / `(load` / `SCRIPT` / `SHELL` / `SH` (not a bare `\bSH\b`), **scoped** broad `ALL`/`*` **selection** idioms (not every asterisk — e.g. `ZOOM *` and layer filters are allowed), and `SAVE` / `QSAVE` / `SAVEAS` / `WBLOCK` on executors even when `*` is not next to the command.

**Out of band for the denylist:** assembling a command across separate executor calls (one call `strcat`, a later call `eval`). The same-payload `strcat`/`eval` + `ALL`/`ssget` rule stays. A regex that denies every `strcat` is rejected because it blocks harmless concatenation.

**Out of denylist scope:** typed tools’ structured business values (titleblock notes, folder names, attribute text). A note containing the word `PURGE` is not scanned the same way as an executor command string.

Read-only tools short-circuit to allow. Unknown tools fail closed as destructive in the registry.

## Dual evaluation (pipe path) vs headless

- **Named-pipe tools:** Safety runs on **both** the server and the plugin (same `SafetyPolicy`). See [adr/0002-dual-safety-evaluation.md](adr/0002-dual-safety-evaluation.md).
- **Headless AccoreConsole** (`forge_run_script`, `forge_batch_run`): Safety runs on the **server only** (script file contents are scanned before spawn). These tools never enter the plugin — do not treat them as dual-eval.

## Dry-run

Write tools accept `dryRun=true` and return what they would do without changing the drawing. Use dry-run before publish, save, attribute campaigns, scripts, and any executor call.

## Backup

Before non-dry-run writes whose metadata sets `RequiresBackup`, Forge copies the active DWG to `FORGE_BACKUP_DIR` (default `%LOCALAPPDATA%\765T-Forge\backups`). Backups are timestamped and do not overwrite prior copies by default.

## Audit

Operations write JSONL audit records under `FORGE_AUDIT_DIR` (default `%LOCALAPPDATA%\765T-Forge\audit`). The server writes start/completion records; the plugin writes its own. Treat audit logs as sensitive (paths and arguments). Prefer correlating by `AuditId`.

## No default token (production)

Do **not** run shared or production hosts on a documented default pipe secret. Set:

```text
FORGE_AUTOCAD_TOKEN=<long random value>
```

on both the MCP server process and the AutoCAD process. See [install-cursor.md](install-cursor.md) and [SECURITY.md](../SECURITY.md).

## Overwrite acknowledgement

Destructive path overwrites require explicit acknowledgement:

- `overwriteAcknowledged` on SaveAs / PDF plot / DSD publish / pack-and-go when the destination already exists

Silent clobber is refused with a typed error code.

## Publish preflight

`forge_qa_preflight` (and recipes that call it) **block** publish when xrefs, required titleblock tags, unresolved `####` fields, or standards-pack rules fail (`Ok=false`, report kept in `data`). `force` is only when a human asks in the session. A bypass can still write the file, but the result stays `Ok=false` with `preflight_forced` and `data.preflightBypassed=true`.

## Drawing number registry

When a sheet register is loaded (`forge_registry_load`), attribute writes that claim a drawing number must match the registry — agents must not invent sheet numbers. See [standards-and-registry.md](standards-and-registry.md).

## Unsafe ops

`forge_exec_dotnet` requires:

1. `FORGE_ENABLE_UNSAFE_OPS=true`
2. Per-call `unsafeAcknowledged=true`

Leave unsafe ops disabled unless a human explicitly enables them for the session. There is **no** sandbox — full assembly access inside AutoCAD.

## Timeout / write races

If the plugin response times out, a write may already have committed. **Read back** (`forge_qa_*`, list tools) before retrying. Do not “fix” a timeout by re-sending the same write blindly.

Open-world executors that still queue AutoCAD commands return `Ok=false` with `queued=true` and `completed=false`. That call is not finished.

## Agent rules of thumb

Prefer typed tools → health first → load registry/pack when available → inspect → dry-run → write → verify → preflight → publish. Never invent drawing numbers. Full agent guidance: [skills/765t-forge/SKILL.md](../skills/765t-forge/SKILL.md).
