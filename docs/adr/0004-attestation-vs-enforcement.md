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
