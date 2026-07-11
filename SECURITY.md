# Security Policy

## Supported versions

| Version | Supported |
|---------|-----------|
| 0.2.x | Yes |
| 0.1.x | Yes (preview; upgrade recommended) |
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

### Command denylist

- `SafetyPolicy` scans open-world executor payloads (`forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_exec_dotnet`) for destructive patterns (for example `ERASE ALL`, `PURGE`, `OVERKILL`, broad `ALL`/`*` selection).
- Typed tools are **not** scanned for business text that happens to contain command words (titleblock notes, folder names). Do not assume denylist covers every destructive path.
- Safety is evaluated on **both** server and plugin for named-pipe tools. Do not remove either side without a deliberate trust-model change.
- **Exception:** `forge_run_script` / `forge_batch_run` run AccoreConsole on the server and are evaluated **server-side only** (see ADR 0002).

### Unsafe operations

- `forge_exec_dotnet` is off by default. It requires **both** `FORGE_ENABLE_UNSAFE_OPS=true` and per-call `unsafeAcknowledged=true`.
- Dotnet snippets run with full assembly access inside AutoCAD — treat them as the highest-risk path. **This is not a security boundary** for a compromised agent that already holds the pipe token.

### Timeout / write races

- If a plugin call times out (`FORGE_PLUGIN_RESPONSE_TIMEOUT_SECONDS`), a write may **already have committed** inside AutoCAD.
- Before retrying a write, perform a read-back / QA call (`forge_qa_*`, list tools) to avoid double application.
- Open-world executors must expose `queued` / `completed` honestly when AutoCAD still queues work.

### Backups and audit

- Non-dry-run writes that require backup copy the active DWG under `FORGE_BACKUP_DIR`.
- Audit JSONL is written under `FORGE_AUDIT_DIR`. Treat audit logs as sensitive operational data (paths, tool args).

## Out of scope for this policy

- Misconfiguration of AutoCAD `TRUSTEDPATHS` or loading untrusted NETLOAD DLLs
- Compromised host OS / agent process that already has the user’s token and pipe access
