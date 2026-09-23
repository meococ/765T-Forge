# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.3.x | Unreleased / pre-release (see [CHANGELOG.md](CHANGELOG.md)) |
| 0.2.x | Yes (Technical Preview) |
| 0.1.x | No (end-of-life) |
| < 0.1 | No |

## Reporting a vulnerability

**Report privately.** Do not open a public GitHub issue for security findings.

Preferred channels (in order):

1. **GitHub Security Advisories** — use **Report a vulnerability** on this repository when enabled.
2. Email **security@765t.dev** with subject `[765T-Forge security]`.
3. If the org email is unavailable, contact maintainers via the GitHub organization profile with the same subject line.

Include:

- Affected component (`Forge.Server`, `Forge.Plugin`, pipe protocol, docs/examples)
- Reproduction steps and impact
- Whether a fix is already known

We aim to acknowledge reports within **7 days** and to share a remediation plan or mitigation when available. Please give us a reasonable window before public disclosure.

## Trust boundary (what to review)

765T-Forge sits next to a live CAD session. Treat the following as security-sensitive:

### Named pipe token

- Server and plugin must share the same token (`FORGE_AUTOCAD_TOKEN`, with fallback `MCP_AUTOCAD_TOKEN`).
- **Do not use a default or shared secret in production.** Set a strong, unique token in the MCP host environment and ensure AutoCAD’s process environment matches.
- Pipe ACL limits connections to the current Windows user and LocalSystem; token checks are defense-in-depth, not a substitute for OS isolation.

### Capability gate (no text denylist)

- `SafetyPolicy.Evaluate` is a deterministic gate on tool capability, not on argument text. There is **no** regex denylist, keyword blocklist, or command-text scan anywhere.
- The five free-text executors (`forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_batch_run`, `forge_exec_dotnet`) are flagged `Unsafe` and are refused with `unsafe_not_acknowledged` unless **both** `FORGE_ENABLE_UNSAFE_OPS=true` in the process environment **and** per-call `unsafeAcknowledged=true` are present.
- Read-only tools are allowed. An unregistered name gets destructive metadata and fails at plugin dispatch with `unknown_tool`.
- Typed tools are **not** scanned for business text (titleblock notes, folder names). That class of risk is covered by typed gates (registry fail-closed, preflight, overwrite acknowledgement), not by text matching.
- Safety is evaluated on **both** server and plugin for named-pipe tools. Do not remove either side without a deliberate trust-model change.
- **Exception:** `forge_run_script` / `forge_batch_run` run AccoreConsole on the server and are evaluated **server-side only** (see ADR 0002). No script-file contents are scanned; the server-side capability gate is the control.
- Rationale for removing the old denylist: keyword matching cannot model AutoCAD command aliases, unique-prefix abbreviations, menu macros, or AutoLISP, so it produced false confidence. See [ADR 0002](docs/adr/0002-dual-safety-evaluation.md), Amendment 2026-09-22.

### Attested gates (not a security boundary)

`force=true` (also requires `FORGE_ALLOW_FORCE_PUBLISH=true`), `forge_publish_ceremony_check` booleans, and blast-radius counters are **not** authentication or human-presence proofs. Optional ceremony AuditIds (`dryRunAuditId` / `preflightAuditId` / `receiptAuditId`) are enforced when supplied. Do not describe attested booleans as security controls in advisories or customer assurances. See [ADR 0004](docs/adr/0004-attestation-vs-enforcement.md).

### AccoreConsole

Headless scripts are evaluated **server-side only**. Success is process-level (exit code). Do not equate AccoreConsole completion with desktop plot verification.

### Unsafe operations

- The five executors listed above are off by default. They require **both** `FORGE_ENABLE_UNSAFE_OPS=true` and per-call `unsafeAcknowledged=true`.
- `forge_exec_dotnet` runs Roslyn C# with **full process trust inside AutoCAD and is not sandboxed**. Its timeout bounds how long the server waits; a snippet already running cannot be forcibly aborted, and a timeout reports `exec_dotnet_timeout` while the snippet may still execute. Treat it as the highest-risk path.
- The gate is a **capability gate, not a content filter**: once enabled and acknowledged, the text is executed as given. **This is not a security boundary** for a compromised agent that already holds the pipe token.

### Timeout / write races

- If a plugin call times out (`FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS`), a write may **already have committed** inside AutoCAD.
- Before retrying a write, perform a read-back / QA call (`forge_qa_*`, list tools) to avoid double application.
- Concurrent plugin calls are serialized; a second call while one is in flight returns `plugin_busy` and should be retried after the first completes. If AutoCAD's main thread does not run the command-context callback in time (for example a modal dialog is open), the call returns `plugin_main_thread_timeout`.
- Open-world executors must expose `queued` / `completed` / `undoGrouped` honestly when AutoCAD still queues work.

### Backups and audit

- Non-dry-run writes that require backup copy the active DWG under `FORGE_BACKUP_DIR`.
- Audit JSONL is written under `FORGE_AUDIT_DIR` as one file per provenance (`server-{yyyyMMdd}.jsonl`, `plugin-{yyyyMMdd}.jsonl`) so two processes cannot interleave partial lines. Treat audit logs as sensitive operational data (paths, tool args).
- Fields named exactly `hmacKey`, `token`, `authToken`, `secret`, `password`, or `apiKey` are replaced with `[redacted]` in audit records.

## Out of scope for this policy

- Misconfiguration of AutoCAD `TRUSTEDPATHS` or loading untrusted NETLOAD DLLs
- Compromised host OS / agent process that already has the user’s token and pipe access
