# MCP tasks for long-running tools (core v2 item 4)

Status: **enabled** (2026-11). `tasks/list`, `tasks/get`, `tasks/result`, `tasks/cancel`
and task-augmented `tools/call` are on for every tool.

## Wiring

`ForgeMcpHost.ApplyServerOptions`:

- `options.TaskStore = new InMemoryMcpTaskStore()` — session-scoped tasks; a result only
  matters while this server lives, and `forge_batch_run` already has durable resume via
  `BatchResumeState`. The whole surface is MCPEXP001-gated (2025-11-25 experimental);
  suppressed at the call site with a comment, not project-wide.
- `options.Capabilities.Tasks` — `List`, `Cancel`, and `Requests.Tools.Call` advertised.
- Every tool emits `execution.taskSupport: "optional"` (SDK default) — sync `tools/call`
  is unchanged; clients that send `task` metadata get an `McpTask` back and poll
  `tasks/get` / fetch via `tasks/result`.

## Forge semantics — read before relying on detach

1. **Detaching ≠ AutoCAD is free.** A task returned while `-DGNIMPORT` still runs on the
   main thread — the plugin serializes work, so a second tool call queues behind it.
2. **`tasks/cancel` maps to our existing boundary.** It cancels the server-side wait —
   exactly like `notifications/cancelled` — it cannot stop an in-flight AutoCAD command;
   the plugin runs to completion or its request deadline fires.
3. **Progress composes.** A task-augmented call carrying `_meta.progressToken` still gets
   `notifications/progress` ticks from the same ForgeProgressScope plumbing.
4. **One pipe, one AutoCAD.** Progress lines carry `commandId` — concurrent tasks each
   see only their own ticks.

## Suggested eligible tools (all are "optional" — clients opt in per call)

`forge_plot_publish`, `forge_batch_run`, `forge_dgn_import`, `forge_plot_to_pdf`,
`forge_pack_and_go` — the same set that emits real progress.

## Testing without AutoCAD

- `tools/call` with `task` metadata on a server-native tool (`forge_toolset_list`) →
  `McpTask`; `tasks/get` → `tasks/result` carries the structured content.
- `tasks/cancel` mid-run on a stalled fake pipe → `cancelled`.
- Non-task clients: zero behavior change.
