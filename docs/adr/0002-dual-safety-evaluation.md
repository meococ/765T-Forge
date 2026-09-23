# ADR 0002: Dual safety evaluation

- **Status:** Accepted (amended)
- **Date:** 2026-07-11
- **Amended:** 2026-07-11

## Context

The MCP server may run in an agent environment that is easier to compromise or misconfigure than AutoCAD. A single denylist on the server alone is not a trust boundary for the **named-pipe / in-process plugin** path.

`forge_run_script` and `forge_batch_run` execute via AccoreConsole on the **server process** and never enter `PluginCommandProcessor`. Claiming “dual eval on every tool” would be dishonest.

## Decision

1. Evaluate `SafetyPolicy` on **both** server (`ForgeToolRunner`) and plugin (`PluginCommandProcessor`) for every tool that crosses the named pipe, using the same shared implementation.
2. Fail closed for unknown tools. Gate `forge_exec_dotnet` with env **and** per-call acknowledgement on both sides.
3. For **headless AccoreConsole** tools (`forge_run_script`, `forge_batch_run`): evaluate `SafetyPolicy` on the **server only** (including full script file contents before spawn). Document this as an intentional exception — not dual-eval. Do not pretend the plugin is a second gate for those tools.

## Consequences

- Redundant checks on the pipe path are intentional — do not “simplify” one away.
- Typed tools are not scanned for business text that happens to contain command words; domain constraints (registry, preflight) cover that class of risk.
- Changing deny patterns requires tests in `SafetyPolicyTests` and docs in `docs/safety.md` / `SECURITY.md`.
- Open-world `*` denylist is **scoped** to broad selection idioms (not every asterisk) so production AccoreConsole scripts remain usable.
- SECURITY.md and skill docs must describe the AccoreConsole exception explicitly.

## Amendment 2026-07-14 (red team) — AccoreConsole operational limits

In addition to server-only evaluation:

1. AccoreConsole **Ok** means process exit code 0 (and script passed denylist scan), **not** PDF/plot verification.
2. Accore dry-run is a **plan** (paths / wouldRun), not a rehearsal of mutation or plot output.
3. `forge_batch_run` uses `OpenWorld=false` on MCP args by design; safety depends on **per-script file scan** at execution — document this so agents do not assume job JSON is denylisted.
4. Skill, cheatsheet, and `docs/safety.md` must name `forge_run_script` / `forge_batch_run` and the dual-eval exception; omission is a safety defect.

## Amendment 2026-09-22 — the text denylist was deleted

**What changed.** The regex command denylist in `SafetyPolicy` has been **removed entirely**, and
with it the nine `deny_*` command error codes it produced. The server-side control is now a
deterministic **capability gate**: read-only tools are allowed; the five free-text executors (`forge_exec_command`,
`forge_exec_lisp`, `forge_run_script`, `forge_batch_run`, `forge_exec_dotnet`) are flagged `Unsafe`
and require **both** `FORGE_ENABLE_UNSAFE_OPS=true` and per-call `unsafeAcknowledged=true`, otherwise
they are refused with `unsafe_not_acknowledged`. No argument text is inspected anywhere.

**Why.** Keyword matching cannot model AutoCAD command aliases, unique-prefix abbreviations, menu
macros, or AutoLISP. A denylist that misses the aliases a user actually types produced false
confidence — worse than no filter, because it was documented as a control. The replacement gates
the capability itself rather than trying to guess the text that will exercise it.

**Effect on the decisions above.** Dual evaluation is unchanged in shape: both the server and the
plugin call the same shared `SafetyPolicy` and both must independently have unsafe ops enabled. Point
2's "gate `forge_exec_dotnet` with env and per-call acknowledgement on both sides" is now the gate for
all five executors. The AccoreConsole exception in point 3 is unchanged, except that the server-side
evaluation is the capability gate, not a script-file scan.

**Effect on consequences.** The consequence "Changing deny patterns requires tests in
`SafetyPolicyTests`" is superseded: changes to the gate (which tools are `Unsafe`, or the two
conditions) must be covered in `SafetyPolicyTests`, and `docs/safety.md` / `SECURITY.md` must stay in
sync.
