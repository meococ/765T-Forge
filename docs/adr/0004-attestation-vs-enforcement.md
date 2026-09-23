# ADR 0004: Attestation vs enforcement (publish gates)

- **Status:** Accepted
- **Date:** 2026-07-14
- **Related:** ADR 0002 (dual safety), ADR 0003 (completion honesty), red-team Wave 2 (A5)

## Context

Forge exposes several “gate” tools and parameters that agents and humans may treat as security or compliance controls:

- `force=true` on publish / recipe paths bypasses failed preflight
- `forge_publish_ceremony_check` accepts caller-supplied `dryRunDone`, `issueAcknowledged`, blast-radius counters
- AccoreConsole (`forge_run_script` / `forge_batch_run`) returns success from process exit code
- PDF `verification.passed` proves probe/existence contracts, not nested content or visual correctness

Marketing and skill language that implies these are hard controls creates false confidence and wrong issue sets.

## Decision

1. **Classify controls:**
   - **Enforced:** shared `SafetyPolicy` denylist (pipe dual-eval; Accore server-only script scan), registry fail-closed when loaded, overwrite ack, backup-on-RequiresBackup, preflight block **unless** force, MCP/registry sync fail-closed for unknown tools.
   - **Attested (agent/human-claimed):** `force`, ceremony booleans, blast-radius usage counters, “I dry-ran,” “Issued” language without external CDE.
   - **Evidence-backed:** PublishReceipt + PDF probe (`verification.passed`), dual-source QA readings, pin verify — still not visual/nested-complete until roadmap items ship.

2. **Documentation must use the words *enforced* vs *attested* explicitly** in `docs/safety.md`, `SECURITY.md`, skill, and capability-matrix notes for ceremony/force/Accore.

3. **AccoreConsole remains non-SoT for issue-set PDFs** (reaffirm ADR 0002). Exit code success must never be described as publish verification.

4. **Future implementation direction:** prefer binding ceremony to server-observed AuditIds / receipt IDs (**optional evidence IDs landed in 0.3.0 Unreleased**); optional env gate for `force` (**`FORGE_ALLOW_FORCE_PUBLISH` landed**); project-root allowlist for Accore scripts (still future). Until ceremony is fully auto-bound, supervised-human process remains mandatory.

## Consequences

- Technical Preview positioning stays honest.
- Red-team kill criteria (no GA / no unattended / no CDE-complete claims) remain in force until CHANGELOG evidence.
- Implementers must not “simplify” docs back into “ceremony = safety.”

## Amendment 2026-09-22 — capability gate, measurement honesty, and prepared builds

1. **Enforced class updated.** The first enforced row is now the deterministic **capability gate** (`SafetyPolicy.Evaluate`): the five free-text executors are `Unsafe` and require `FORGE_ENABLE_UNSAFE_OPS=true` plus per-call `unsafeAcknowledged=true`, else `unsafe_not_acknowledged`. The regex text denylist and its `deny_*` codes were deleted (see ADR 0002, Amendment 2026-09-22). There is no text inspection, so no claim may describe Forge as filtering or blocking command strings.
2. **Measurement honesty.** A control that reports a number it did not verify is the same class of dishonesty as an attested boolean:
   - Publish receipts set `pageCount = null` with `pageCountSource = "not_available"`; the verified fields are existence, length > 0, and an exact `%PDF-` header. The previous `/Type /Page` byte scan was removed.
   - Paper units come from `PlotSettings.PlotPaperUnits`; when unreadable the tool returns `plot_units_unavailable` instead of inferring units from the paper name.
   - Pack/caller regexes fail closed on the 250 ms match timeout (`regex_timeout`).
   - DSD fields are validated against illegal characters (`[`, `]`, `=`, CR/LF, other control characters) and return `illegal_dsd_character`.
   - `forge_block_set_attr` / `forge_block_campaign` return `block_attribute_not_found` instead of a silent `updated=0` success.
3. **Prepared is not verified.** The AutoCAD 2017–2024 (`net462`) plugin assembly is prepared but not yet compiled against AutoCAD 2017 reference assemblies. No doc, release note, or capability claim may state that 2017–2024 runtime support is verified. The verification gate is `scripts/verify-plugin-series.ps1 -Series 2017`, run per series on a machine with those references.
4. Point 4's future direction remains: project-root allowlist for Accore scripts is still future; optional env gate for `force` and optional ceremony evidence IDs have landed.
