# Design note: remote transport (Streamable HTTP) — deferred

Status: **deferred** — Forge speaks stdio only today. This note records what a remote
transport would require, why it is riskier here than for a typical MCP server, and why it
is deliberately not shipped.

## What "remote transport" means for MCP

The MCP spec defines two transports: **stdio** (the client spawns the server as a child
process; all traffic is in-process pipes) and **Streamable HTTP** (a single HTTP endpoint
that accepts JSON-RPC POSTs and can upgrade to SSE for server→client messages;
`Mcp-Session-Id` headers bind replies to a session). Streamable HTTP is what remote and
multi-client deployments use — it is what daobataotie/CAD-MCP ships in v2.0 — and it is
the only realistic way to expose Forge to clients on other machines or to a shared
service.

## The risk: this server drives a CAD application

Forge is not a read-only data server. The tool surface includes entity creation,
document open/close/save, publish, and an opt-in `exec` toolset that evaluates
caller-supplied scripts inside AutoCAD. Whoever can send a `tools/call` to the transport
can drive the CAD seat: read drawing contents off the disk, write arbitrary geometry,
overwrite files AutoCAD can reach, and — via `exec` — run arbitrary code inside the
acad.exe process.

Over stdio this risk is contained by construction: only a local process the user started
can talk to the server, and Windows process boundaries do the authentication. Over
Streamable HTTP the same surface becomes a **network-reachable remote control for
AutoCAD** — every hardening item below becomes load-bearing instead of optional.

## What a remote deployment would need

1. **Bind to loopback only** (`127.0.0.1`, not `0.0.0.0`) until there is a real remote
   use case. A LAN-reachable unauthenticated AutoCAD-control endpoint is not acceptable
   at any maturity level; even loopback must be the explicit, logged default.
2. **Authentication on every request.** MCP's authorization guidance points at OAuth 2.0
   with **RFC 9728** (Protected Resource Metadata) for resource-server discovery —
   `/.well-known/oauth-protected-resource` advertising the issuer, bearer tokens
   validated per request. A simpler localhost-only variant (random per-session bearer
   written to a user-only file, mirroring how editors and language servers do it) is the
   likely first step; Forge already has token plumbing (`FORGE_AUTOCAD_TOKEN`) between
   server and plugin that proves the pattern.
3. **HTTPS or an explicit localhost exemption.** OAuth bearer tokens over plaintext HTTP
   are sniffable; the spec's localhost carve-out only applies when binding is genuinely
   loopback and the threat model accepts same-machine observers.
4. **Per-request origin/CSRF checks** — a browser on the same machine must not be able to
   POST to the endpoint (DNS-rebinding/localhost-CSRF is a real attack on
   loopback-bound dev servers).
5. **The `exec` toolset stays hard-off** unless the caller's token carries a scope that
   allows it — remote script evaluation is the worst-case exposure and needs its own
   authorization tier, not just a checkbox.
6. **Audit and rate limiting** — Forge's audit-record discipline (every verified call is
   recorded) maps well to a remote surface, but a network listener needs request-size
   caps, concurrency caps, and brute-force backoff that stdio never needed.

## Why it is deferred

- **No requesting use case.** Every Forge consumer today is a same-seat MCP client
  (Claude/Desktop-class hosts, the Paseo harness). The plugin↔server named pipe is
  already machine-local; a loopback HTTP hop adds latency and failure modes for zero
  new capability.
- **The blast radius is the CAD seat itself.** The failure mode is not "data leak" —
  it is "attacker drives AutoCAD". Shipping remote transport without items 1–6 fully
  implemented and reviewed would be shipping a vulnerability, not a feature.
- **Sequencing.** Toolset boundaries, caller cancellation, elicitation for destructive
  operations, and the `exec` opt-in shipped first (core v2) precisely so a remote
  transport has safe defaults to sit on. That groundwork is now in place; the transport
  itself is still future work.

If remote access becomes a requirement, the path is: loopback-only Streamable HTTP +
per-session bearer → OAuth/RFC 9728 when a second machine is in scope → re-review the
`exec` toolset policy under that model before enabling.
