# Troubleshooting

## Pipe down / health fails

**Symptoms:** `forge_system_health` fails; connection refused or timeout to the named pipe.

**Checks:**

1. AutoCAD is running (2025/2026 with the `net8.0-windows` plugin component; 2017–2024 requires the `net462` component, which is prepared but not yet runtime-verified).
2. Plugin loaded: `NETLOAD` → `Forge.Plugin.dll`, then run `MCP_STATUS`.
3. `FORGE_PIPE_NAME` matches on server and plugin (default `765T.Forge.AutoCAD`). Health reports the **configured** pipe name from the plugin environment — not a hardcoded default.
4. `FORGE_AUTOCAD_TOKEN` matches on both processes.
5. The pipe accepts up to 4 concurrent connections but executes one request at a time. If another call is in flight you get `plugin_busy` — retry after it completes. If AutoCAD's main thread is blocked (for example a modal dialog), you get `plugin_main_thread_timeout`.

## NETLOAD blocked or fails

- Place the DLL under a path listed in AutoCAD `TRUSTEDPATHS`, or adjust trust settings for your org policy.
- Prefer `.\scripts\install-plugin.ps1` after `.\scripts\build-plugin.ps1` — it copies into `%LOCALAPPDATA%\765T-Forge\plugin` and prints the checklist.
- Optional Autoloader bundle: `plugin-bundle/765T-Forge.bundle/` (see [install/plugin.md](install/plugin.md)).
- Build for the component you are loading: `net8.0-windows` (AutoCAD 2025/2026) or `net462` (`-p:ForgeBuildLegacy=true`, AutoCAD 2017 reference assemblies).
- Reference roots: `FORGE_AUTOCAD_MODERN_ROOT` → `AUTOCAD_2025_ROOT` → `FORGE_AUTOCAD_ROOT`, or `FORGE_AUTOCAD_LEGACY_ROOT` → `AUTOCAD_2017_ROOT` for `net462`. A missing root or DLL fails the build with the exact path; there is no silent fallback.
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

## Legacy (`net462`) build fails

**Symptoms:** `dotnet build -p:ForgeBuildLegacy=true` errors about `FORGE_AUTOCAD_LEGACY_ROOT`.

**Fix:** point `FORGE_AUTOCAD_LEGACY_ROOT` (or the deprecated `AUTOCAD_2017_ROOT`) at an AutoCAD 2017 install directory or the ObjectARX 2017 SDK `inc` folder that contains `AcCoreMgd.dll`, `AcDbMgd.dll`, `AcMgd.dll`. The legacy build deliberately refuses to substitute a newer series, because compiling against newer assemblies can emit API calls that do not exist on AutoCAD 2017.

If you cannot provide those references, do not claim 2017–2024 support: the component is prepared but unverified until `scripts/verify-plugin-series.ps1 -Series 2017` passes.

## Token missing / auth denied

**Symptoms:** Plugin rejects requests; auth failure in results.

**Fix:**

1. Set `FORGE_AUTOCAD_TOKEN` in the MCP host `env` block (required for install docs/examples).
2. Set the **same** value for the AutoCAD process (user env or launch script), then restart AutoCAD and `NETLOAD` again.
3. Do not leave production on a shared default secret.

Optional fallback variable: `MCP_AUTOCAD_TOKEN` — prefer explicit `FORGE_AUTOCAD_TOKEN`.

## Unsafe executor refused

**Symptoms:** `Error.Code=unsafe_not_acknowledged`.

**Meaning:** the tool is one of the five free-text executors (`forge_exec_command`, `forge_exec_lisp`, `forge_run_script`, `forge_batch_run`, `forge_exec_dotnet`) and the capability gate is closed.

**Fix:** only if a human intends it for this session, set `FORGE_ENABLE_UNSAFE_OPS=true` in the **server** environment and pass `unsafeAcknowledged=true` on the call. There is no text filter to satisfy — the gate is on the capability, not on the command text.

## Other typed errors you will actually see

| Code | Meaning | Action |
|------|---------|--------|
| `plugin_busy` | Another plugin request is in flight | Retry after it completes |
| `plugin_main_thread_timeout` | AutoCAD's main thread did not run the command-context callback in time (modal dialog open?) | Close the dialog, then retry |
| `frame_too_large` / `response_too_large` | Pipe frame exceeded the exact 4,000,000-character cap | Shrink the payload/result |
| `invalid_command_id` | Wire command id did not match `^[A-Za-z0-9-]{1,64}$` | Fix the caller's id |
| `block_attribute_not_found` | `forge_block_set_attr` / `forge_block_campaign` tag matched nothing | Check the tag name; this is a failure, not `updated=0` |
| `regex_timeout` | A pack- or caller-supplied regex exceeded the 250 ms match timeout | Simplify the regex; the check fails closed |
| `plot_units_unavailable` | Paper units not readable from `PlotSettings.PlotPaperUnits` | Fix the page setup; Forge will not guess units from the paper name |
| `illegal_dsd_character` | DSD field contained `[`, `]`, `=`, CR/LF, or another control character | Remove the character |
| `exec_dotnet_timeout` | Dotnet snippet did not return in time | The snippet may still be running inside AutoCAD; read back before retrying |
| `undoWarnings[]` (`undo_group_open_failed` / `undo_group_close_failed`) | Undo grouping could not be opened/closed cleanly | Check `undoGrouped` and read back |

## AccoreConsole / script tools

- `forge_run_script` / `forge_batch_run` need `accoreconsole.exe` under `FORGE_AUTOCAD_ROOT` (deprecated alias `AUTOCAD_2026_ROOT`; with neither set, Forge discovers the highest installed `C:\Program Files\Autodesk\AutoCAD <year>` containing `accoreconsole.exe`).
- These are server-only paths: the capability gate runs on the server, and no script text is scanned. Do not expect a second plugin-side gate.
- Verify the root path if scripts fail to start.

## Timeout after a write

Assume the drawing may already have changed. Call QA/list tools before retrying. See [safety.md](safety.md).

## Plot / publish surprises

- `forge_plot_to_pdf` is **configurable** (device, paper, CTB/STB, area, orientation, scale). Unexpected paper usually means wrong args or missing `forge_system_capabilities` discovery — not a hardcoded Phase 0 path. A paper whose units cannot be read yields `plot_units_unavailable` rather than a guess.
- `forge_plot_publish` uses real DSD + `Publisher.PublishDsd` with optional preflight gate and overwrite acknowledgement. `Ok=true` with PDF verify means the publisher finished; if preflight fails, publish refuses unless `force=true`. The receipt's `pageCount` is always `null` with `pageCountSource=not_available` — Forge verifies existence, non-zero length, and the `%PDF-` header, not page count.
- `forge_layout_page_setup_import` is **implemented** (prefers sync `PlotSettings` copy from template DWG/DWT). If the sync path cannot run, it may fall back to queued `-PSETUPIN` with `queued=true` / `completed=false` — check [capability-matrix.md](capability-matrix.md). Prefer `forge_layout_page_setup_apply` for sync apply of an existing setup.
- Free-text `forge_exec_command` / `forge_exec_lisp`: if the result includes `queued=true` or `completed=false` (and `undoGrouped=false`), do **not** treat success as command finished — read back before chaining.

## Release artifacts incomplete

GitHub-hosted CI packs **server only** (no AutoCAD on runners). A usable install needs **both** `765T-Forge.Server-win-x64.zip` and `765T-Forge.Plugin.zip` (maintainer attaches plugin — see [CONTRIBUTING.md](../CONTRIBUTING.md) release checklist).
