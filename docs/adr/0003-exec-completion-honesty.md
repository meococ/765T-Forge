# ADR 0003: Executor completion honesty

- **Status:** Accepted
- **Date:** 2026-07-11

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
- Page-setup import remains `partial` until a sync Database API path replaces `-PSETUPIN` queueing.
