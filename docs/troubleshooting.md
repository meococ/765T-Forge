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
| `deny_sysvar` | `forge_system_setvar` was asked to set a trust/startup variable (exact-name set) | Leave it unchanged; use a typed tool for drawing state |
| `plot_probe_failed` | `forge_plot_to_pdf` wrote a file but the PDF probe failed (missing/zero-length file, or the first 5 bytes are not `%PDF-`) | Inspect `data.pdfProbe`; do not treat the output as published |
| `autocad_host_mismatch` | The plugin could not read a parseable `ACADVER` (missing, empty, whitespace, or unparseable) | Run inside AutoCAD with the plugin build for that release; this is a refusal, not a pass |
| `autocad_version_unsupported` | `ACADVER` is outside the loaded plugin build's series, or the requested AccoreConsole year is outside 2017–2026 | Load the matching plugin build (`net462` = 2017–2024, `net8.0-windows` = 2025–2027) or request a supported year |
| `accoreconsole_not_found` | No year could be resolved, or `accoreconsole.exe` is missing at the resolved path | Set `FORGE_ACCORECONSOLE_YEAR` (and `AUTOCAD_<year>_ROOT` if the install is not in the default location); there is no newer-year fallback |
| `accoreconsole_script_error` | AccoreConsole exited 0 but its output contains `*Cancel*`, `Unknown command`, or `*Invalid*` | Fix the script; a run without these markers still does not prove the script succeeded |
| `undoWarnings[]` (`undo_group_open_failed` / `undo_group_close_failed`) | Undo grouping could not be opened/closed cleanly | Check `undoGrouped` and read back |

## AccoreConsole / script tools

- Year resolution is deterministic: job-level `autoCadYear`, then call-level `autoCadYear`, then `FORGE_ACCORECONSOLE_YEAR`, then the discovered default (`FORGE_AUTOCAD_ROOT` / the highest installed `C:\Program Files\Autodesk\AutoCAD <year>` containing `accoreconsole.exe`). A missing exe is `accoreconsole_not_found` naming the exact path and the env var to set; Forge never substitutes a newer AutoCAD year.
- Each batch job honours its own `timeoutSeconds` (clamped 5–3600, default 300).
- These are server-only paths: the capability gate runs on the server, and no script text is scanned. Do not expect a second plugin-side gate.
- AccoreConsole can exit 0 after a failed command. Forge fails the run with `accoreconsole_script_error` when the output contains the literal abort tokens `*Cancel*`, `Unknown command`, or `*Invalid*`. A run without them does **not** prove the script succeeded — verify the drawing with readback (`forge_qa_readback` / `forge_qa_readback_after_timeout`) before trusting the result.
- Verify the root path if scripts fail to start.

## Timeout after a write

Assume the drawing may already have changed. Call QA/list tools before retrying. See [safety.md](safety.md).

## Plot / publish surprises

- `forge_plot_to_pdf` is **configurable** (device, paper, CTB/STB, area, orientation, scale). Unexpected paper usually means wrong args or missing `forge_system_capabilities` discovery — not a hardcoded Phase 0 path. A paper whose units cannot be read yields `plot_units_unavailable` rather than a guess. When the output file fails the PDF probe, the tool returns `plot_probe_failed` (`Ok=false`) with the probe in `data` instead of reporting success.
- `forge_plot_publish` uses real DSD + `Publisher.PublishDsd` with optional preflight gate and overwrite acknowledgement. `Ok=true` with PDF verify means the publisher finished; if preflight fails, publish refuses unless `force=true`. The receipt's `pageCount` is always `null` with `pageCountSource=not_available` — Forge verifies existence, non-zero length, and the `%PDF-` header, not page count.
- `forge_layout_page_setup_import` is **implemented** (prefers sync `PlotSettings` copy from template DWG/DWT). If the sync path cannot run, it may fall back to queued `-PSETUPIN` with `queued=true` / `completed=false` — check [capability-matrix.md](capability-matrix.md). Prefer `forge_layout_page_setup_apply` for sync apply of an existing setup.
- Free-text `forge_exec_command` / `forge_exec_lisp`: if the result includes `queued=true` or `completed=false` (and `undoGrouped=false`), do **not** treat success as command finished — read back before chaining.

## Release artifacts incomplete

GitHub-hosted CI packs **server only** (no AutoCAD on runners). A usable install needs **both** `765T-Forge.Server-win-x64.zip` and `765T-Forge.Plugin.zip` (maintainer attaches plugin — see [CONTRIBUTING.md](../CONTRIBUTING.md) release checklist).
