# Parallel AutoCAD instances (one Forge pipe per instance)

The Forge plugin inside `acad.exe` serves **one** named pipe (`765T.Forge.AutoCAD`
by default, `FORGE_PIPE_NAME`), serialized to a single client. To let several
MCP peers work at once, run one extra AutoCAD per peer, each with its own pipe,
its own AutoCAD profile, and its own audit/backup/state folders:

```
scripts\launch-acad-instance.ps1 -Name <n> [-AcadExe <path>] [-Drawing <dwg>]
                                 [-ProductKey <ACAD-...:...>] [-RestoreProfilesDefault <profile>]
                                 [-DryRun]
scripts\stop-acad-instance.ps1   -Name <n> [-DryRun] [-RestoreOnly] [-WaitSeconds <s>]
```

`<n>` must match `^[A-Za-z0-9][A-Za-z0-9_-]{0,31}$`. The launcher refuses if the
pipe `765T.Forge.AutoCAD.<n>` already exists, if `instance.json` describes a
still-running instance, if another launch for the same name is in progress, or
if the `Profiles` default value already points at a `Forge-*` profile (see "The
pointer trap" below). If more than one `ACAD-*` product key exists under the
release it refuses until you pick one with `-ProductKey`.

## How

1. **Dry-run first** — prints the full plan (registry copy, retargets, pointer
   record/restore, env, command line, state path, MCP entry) and changes
   nothing:
   `powershell -NoProfile -File scripts\launch-acad-instance.ps1 -Name peer2 -DryRun`
2. **Launch** — same command without `-DryRun`. First run copies the current
   AutoCAD profile (`HKCU:\Software\Autodesk\AutoCAD\<release>\<product>\Profiles\<current>`)
   to a sibling `Forge-<n>` key, re-points its per-session write paths into
   `%LOCALAPPDATA%\765T-Forge\instances\<n>\profile\`, stamps the key
   `ForgeManaged=1`, then starts `acad.exe /nologo /p "Forge-<n>"` (+ optional
   drawing) with `FORGE_PIPE_NAME/FORGE_AUDIT_DIR/FORGE_BACKUP_DIR` set for that
   child only. It writes `%LOCALAPPDATA%\765T-Forge\instances\<n>\instance.json`
   (including the recorded `profilesDefault`) and prints it plus the MCP entry
   to register. A `Forge-<n>` key that already exists is only reused when it
   carries the marker; otherwise it is deleted and recopied, so a half-made
   copy can never be applied.
3. **Register** — merge the printed entry (or `examples/mcp/forge-instance.mcp.json.example`)
   into your host's `mcpServers` as `765t-forge-<n>`; the token is the same one
   the existing `765t-forge` entry uses. `AUTOCAD_2026_ROOT` is deliberately
   absent from the example: unset, the server auto-detects the newest real
   AutoCAD install (fixed-drive scan, so `D:\AutoCAD 2025` is found); set it
   only to override that detection.
4. **Stop** — `stop-acad-instance.ps1 -Name <n>` kills the process only if the
   recorded pid exists **and** its start time, exe path **and** command line
   (`/p "Forge-<n>"`) match `instance.json` exactly; otherwise it refuses and
   touches nothing. After the process has exited it also puts the recorded
   `Profiles` default value back if it was rewritten to a `Forge-*` value or
   emptied (a real, different profile name is left as-is). If you closed the
   instance window yourself, run it with `-RestoreOnly` to do just the pointer
   restore.

## The profile trap (why the copy exists)

AutoCAD rewrites per-session values in the **current** profile when it exits —
`Profiles\<p>\Editor Configuration\LogFilePath`, `General\PlotLogPath`,
`General\ProfileStorage` (the user's `.aws`), and similar log/temp paths. A
second instance on the user's profile would stomp those settings on exit. The
`Forge-<n>` copy + re-pointed paths keep those writes inside the instance dir;
the user's own profile key is only ever read, never written.

## The pointer trap (why the default value is watched)

The **default value** of `...\Profiles` is the current-profile pointer every
normal `acad.exe` start reads. On R19.1, starting `acad.exe /p B` rewrote it to
`B` at startup (Autodesk forums), so the user's *next* normal launch would open
on `Forge-<n>` instead of their own profile — the profile-hijack risk the copy
alone does not cover. Whether R25.0 still writes the pointer is **unverified**;
the guards below make it safe either way:

- At launch the pointer is recorded into `instance.json` as `profilesDefault`.
  If it already reads `Forge-*` (a leftover from an earlier run or crashed
  session) the launcher **refuses** — fix `...\Profiles` (Default) back to your
  profile name first, or pass `-RestoreProfilesDefault <name>` to declare the
  correct value and have the launch record and restore that instead.
- For ~10 s after `Start-Process` the launcher polls the pointer and writes the
  recorded value back, but only while the pointer is a `Forge-*` value or
  empty — the failure mode the restore exists to undo. A pointer that reads a
  real, different profile name is left as-is and reported once
  (`left as-is (now '<x>', recorded '<y>')`): that is your own profile switch,
  not a hijack.
- `stop-acad-instance.ps1` applies the same rule once more after the recorded
  pid exits — and `-RestoreOnly` covers the case where you closed the window
  yourself, so no stop happens but the pointer may still be wrong. Before any
  write, the recorded `profilesKey` must prove to be a real `...\Profiles` key
  under `HKCU:\Software\Autodesk\AutoCAD` and the recorded `profilesDefault`
  must not start with `Forge-` — a hand-edited `instance.json` cannot aim the
  write at another key or make the script install a Forge pointer. The forced
  `Stop-Process` also prevents the instance's own graceful-exit profile
  writes.
- The only write ever made to the pointer is the recorded original value —
  never anything else — and only ever over a `Forge-*` or empty pointer,
  never over a real profile name you chose yourself.

Residual risk: a write by AutoCAD after the watch window but before exit would
only be caught by the stop script's restore — the smoke steps below verify the
pointer at each stage. Converse edge: if you deliberately point your main
profile at one literally named `Forge-*` while an instance runs, the guard
treats it as a leftover and reverts it — don't name your own profiles
`Forge-*`.

## Isolated vs shared

- **Re-pointed into `%LOCALAPPDATA%\765T-Forge\instances\<n>\profile\`** (on the
  `Forge-<n>` copy only): `Editor Configuration\LogFilePath`, `SaveFilePath`
  (autosave `.sv$`), `XrefLoadPath`; `General\PlotLogPath`, `ProfileStorage`;
  `General Configuration\PlotSpoolerDirectory` — these six are written even
  when absent upstream; `Variables\XCOMPAREBAKPATH`, `ACTIVITYINSIGHTSPATH`
  only when already present; plus any other string value whose name looks like
  a log/temp/save/spool path and whose data looks like a filesystem path.
- **Isolated per instance:** pipe name, the `Forge-<n>` registry copy,
  `audit/`, `backups/`, `instance.json`, and every value above.
- **Shared:** everything else in the copied profile (resource dirs such as
  `PrinterConfigDir` are intentionally left shared), `FORGE_AUTOCAD_TOKEN`
  (inherited unchanged — both sides must resolve the same token), the
  ApplicationPlugins bundle (it autoloads via `LoadOnAutoCADStartup`
  regardless of `/p` profile), the AutoCAD install, and the licence seat —
  **UNVERIFIED:** a second GUI `acad.exe` may consume an additional licence
  seat on single-user licences; confirm in the smoke window. Two shared writes
  remain outside `Profiles`: the product-level `...\ACAD-8101:409\Loaded\*`
  records (same content as the existing instance writes, harmless) and the
  `Profiles` (Default) pointer itself, guarded above. Per-session paths not on
  the retarget list also stay shared — e.g. a crashed Forge instance can still
  leave stray files in the user's Temp if a path was not recognised.
- **Note:** the repo bundle manifest
  `plugin-bundle/765T-Forge.bundle/PackageContents.xml` declares
  `SeriesMin=R25.1` while the deployed bundle at
  `%APPDATA%\Autodesk\ApplicationPlugins\765T-Forge.bundle` covers `R25.0`–
  `R25.1`. AutoCAD 2025 (R25.0) autoloads the plugin only because the
  *deployed* manifest allows it — redeploying the repo copy as-is would stop
  the plugin loading in **both** instances.

## Live smoke steps (run in a granted window — AutoCAD may already be in use)

1. `reg query "HKCU\Software\Autodesk\AutoCAD\R25.0\ACAD-8101:409\Profiles" /ve`
   → `(Default)` must read your profile (here `<<Unnamed Profile>>`). If it
   already reads `Forge-*`, fix that first (see "The pointer trap").
2. `powershell -NoProfile -File scripts\launch-acad-instance.ps1 -Name smoke1 -DryRun`
   → expect a plan whose every `[WOULD-SET]` line targets `Profiles\Forge-smoke1`
   and whose pointer section records `<<Unnamed Profile>>`.
3. `...\launch-acad-instance.ps1 -Name smoke1` → second AutoCAD window opens on
   profile `Forge-smoke1`; watch the output for `[RESTORED]` lines. The default
   pipe `765T.Forge.AutoCAD` keeps working.
4. While it runs, repeat the `reg query ...\Profiles /ve` → it must still read
   `<<Unnamed Profile>>` (if it reads `Forge-smoke1`, the R25.0 pointer write
   is confirmed and the launcher's restore should already have corrected it).
5. Check `\\.\pipe\` lists `765T.Forge.AutoCAD.smoke1`
   (`[IO.Directory]::GetFiles('\\.\pipe\')`), and `instance.json` pid/start time
   match `Get-Process acad`.
6. Register `765t-forge-smoke1` (env from step 3 output) and call
   `forge_system_health` → it must answer on the new pipe while the original
   `765t-forge` server still answers on the old one.
7. `powershell -NoProfile -File scripts\stop-acad-instance.ps1 -Name smoke1`
   → stops only the recorded pid (requires the `/p "Forge-smoke1"` command
   line), then restores the pointer if needed. Repeat the `reg query /ve` →
   `<<Unnamed Profile>>`, and check the user's profile is untouched
   (`reg query "...\Profiles\<<Unnamed Profile>>\Editor Configuration"` →
   `LogFilePath` unchanged) while `instance.json`'s audit/backups dirs were
   used. If you close the window by hand instead, run the stop script with
   `-RestoreOnly` and repeat the `reg query /ve` check.
8. Re-run launch to confirm idempotent reuse of `Forge-smoke1` (`SKIP-COPY`
   via the `ForgeManaged` marker).
