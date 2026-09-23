# ADR 0001: Named pipe trust boundary

- **Status:** Accepted
- **Date:** 2026-07-11

## Context

Forge splits into an MCP server (agent host) and an AutoCAD in-process plugin. The agent process may be less trusted than the CAD session. Transport must stay local, authenticated, and hard to spoof from other users on the machine.

## Decision

Use a Windows **named pipe** (`FORGE_PIPE_NAME`, default `765T.Forge.AutoCAD`) with:

1. ACL limited to the current Windows user + LocalSystem
2. Shared secret token (`FORGE_AUTOCAD_TOKEN`) checked on every request
3. `maxNumberOfServerInstances: 1` (serialized calls)
4. Dual `SafetyPolicy` evaluation (server + plugin) and dual audit writes

Do **not** expose an unauthenticated HTTP listener on the plugin for MCP traffic.

## Consequences

- Strong local trust model; poor fit for remote multi-user CAD without a different design.
- Both processes must share env (pipe name + token); health must report the configured pipe name.
- CI can test Shared/Server without AutoCAD; plugin packaging needs a machine with AutoCAD refs.

## Amendment 2026-09-22 — concurrency and the capability gate

1. `maxNumberOfServerInstances` is now **4**, not 1. The extra instances let a second MCP call connect while the first request executes; execution itself is still strictly serialized by a semaphore. A request that cannot acquire the gate within the response timeout is answered with `plugin_busy` instead of being dropped as a misleading `plugin_unavailable` transport error. `plugin_main_thread_timeout` reports the distinct case where AutoCAD's main thread does not run the command-context callback in time (for example a modal dialog is open).
2. Pipe frames are capped at an exact 4,000,000 characters: oversized requests are refused with `frame_too_large` and oversized responses are not sent, reported as `response_too_large`.
3. The dual safety evaluation in point 4 is now the **deterministic capability gate** (see ADR 0002, Amendment 2026-09-22). The trust boundary itself — local pipe, ACL, token, dual evaluation, dual audit — is unchanged.
