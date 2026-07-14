# ADR 0003: Executor completion honesty

- **Status:** Accepted (amended)
- **Date:** 2026-07-11
- **Amended:** 2026-07-14 (docs hygiene + red team Wave 2)

## Context

`Document.SendStringToExecute` queues AutoCAD commands asynchronously. Returning `Ok=true` before the command finishes makes dry-run/backup/verify **theater** for open-world executors and misleads agents into chaining writes on stale state.

## Decision

1. Prefer **synchronous** dispatch via `Editor.Command` for `forge_exec_command` / `forge_exec_lisp` when the payload can be expressed as command tokens.
2. When a path must still queue, the result **must** include `queued=true` and `completed=false` (never imply finished).
3. Typed hot-path tools must not rely on queued success for publish/QA contracts.
4. Agents and skills must treat open-world exec as last resort and always read back.

## Consequences

- Some LISP/command strings that need interactive prompts still cannot be fully sync-verified.
- Completion tokens / wait APIs may evolve; honesty flags are the minimum contract now.
- Page-setup import prefers a sync Database/`PlotSettings` path; when it must still queue `-PSETUPIN`, results expose `queued=true` / `completed=false` (matrix: **implemented** with fallback honesty — not a forever-`partial` tool).
- Dry-run on open-world / Accore paths must not be described as full behavioral simulation. Agents must re-read state before write; ceremony `dryRunDone` is **attested**, not proven (see ADR 0004).
- `Ok=true` with `completed=false` / Accore exit 0 / DSD `fallback=plot_to_pdf` are distinct honesty signals — skills must branch on them, not collapse to “success.”
