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
