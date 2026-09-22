# Troubleshooting

## Pipe down / health fails

**Symptoms:** `forge_system_health` fails; connection refused or timeout to the named pipe.

**Checks:**

1. AutoCAD 2026 is running.
2. Plugin loaded: `NETLOAD` → `Forge.Plugin.dll`, then run `MCP_STATUS`.
3. `FORGE_PIPE_NAME` matches on server and plugin (default `765T.Forge.AutoCAD`). Health reports the **configured** pipe name from the plugin environment — not a hardcoded default.
4. `FORGE_AUTOCAD_TOKEN` matches on both processes.
5. Only one plugin pipe instance is expected (`maxNumberOfServerInstances: 1`). Restart AutoCAD if a previous session left the pipe in a bad state.

## NETLOAD blocked or fails

- Place the DLL under a path listed in AutoCAD `TRUSTEDPATHS`, or adjust trust settings for your org policy.
- Prefer `.\scripts\install-plugin.ps1` after `.\scripts\build-plugin.ps1` — it copies into `%LOCALAPPDATA%\765T-Forge\plugin` and prints the checklist.
- Optional Autoloader bundle: `plugin-bundle/765T-Forge.bundle/` (see [install-plugin.md](install-plugin.md)).
- Build for `net8.0-windows` and AutoCAD 2026 managed references from `AUTOCAD_2026_ROOT`.
- Confirm you are loading the DLL you just built (install dir, `bin\Release\net8.0-windows\`, or `-OutDir`).

## Locked DLLs while AutoCAD is open

Windows locks `Forge.Plugin.dll` after `NETLOAD`. Full-solution rebuilds fail with file-lock errors.

**Workarounds:**

```powershell
# Build/test without the plugin project
dotnet build .\765T-Forge.ServerOnly.slnf
dotnet test  .\765T-Forge.ServerOnly.slnf --no-build

# Or build plugin to a temporary output
dotnet build .\src\Forge.Plugin\Forge.Plugin.csproj -p:OutDir=artifacts\Forge.Plugin\
```

Close AutoCAD before overwriting the NETLOAD’d path. See `scripts/build-plugin.ps1`.

## ServerOnly.slnf

Use `765T-Forge.ServerOnly.slnf` when:

- AutoCAD is not installed (CI, docs, server-only contributors)
- Plugin DLLs are locked
- You only changed `Forge.Shared`, `Forge.Server`, or tests

CI (`.github/workflows/ci-server.yml`) builds this filter on `windows-latest` with `--locked-mode`.

## Token missing / auth denied

**Symptoms:** Plugin rejects requests; auth failure in results.

**Fix:**

1. Set `FORGE_AUTOCAD_TOKEN` in the MCP host `env` block (required for install docs/examples).
2. Set the **same** value for the AutoCAD process (user env or launch script), then restart AutoCAD and `NETLOAD` again.
3. Do not leave production on a shared default secret.

Optional fallback variable: `MCP_AUTOCAD_TOKEN` — prefer explicit `FORGE_AUTOCAD_TOKEN`.

## AccoreConsole / script tools

- `forge_run_script` / `forge_batch_run` need `accoreconsole.exe` under `AUTOCAD_2026_ROOT`.
- Verify the root path if scripts fail to start.

## Timeout after a write

Assume the drawing may already have changed. Call QA/list tools before retrying. See [safety.md](safety.md).

## Plot / publish surprises

- `forge_plot_to_pdf` is **configurable** (device, paper, CTB/STB, area, orientation, scale). Unexpected paper usually means wrong args or missing `forge_system_capabilities` discovery — not a hardcoded Phase 0 path.
- `forge_plot_publish` uses real DSD + `Publisher.PublishDsd` with a preflight gate and overwrite acknowledgement. `Ok=true` means the publisher finished and the PDF probe passed. A failed preflight or probe is `Ok=false`. `force` is only when a human asks, and a bypass still returns `Ok=false` (`preflight_forced`).
- `forge_layout_page_setup_import` may still be **partial** (command-queued) — check [capability-matrix.md](capability-matrix.md). Prefer `forge_layout_page_setup_apply` for sync apply of an existing setup.
- Open-world `forge_exec_command` / `forge_exec_lisp`: `queued=true` or `completed=false` is `Ok=false`. Read back before chaining.

## Release artifacts incomplete

GitHub-hosted CI packs **server only** (no AutoCAD on runners). A usable install needs **both** `765T-Forge.Server-win-x64.zip` and `765T-Forge.Plugin.zip` (maintainer attaches plugin — see [CONTRIBUTING.md](../CONTRIBUTING.md) release checklist).
