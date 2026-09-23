# 03 — Forge extension map (R&D)

> **Track status:** SIDE R&D under `docs/research/taluy/`.  
> **Not product scope.** Does **not** change `docs/capability-matrix.md`, `docs/roadmap.md`, or any `src/` surface until an explicit MVP smoke gate.  
> **Product positioning:** 765T-Forge competes on **reliable publish + agent safety**, not geometry tool count. Taluy (rải taluy / mái dốc đắp–đào) must not dilute that claim.

Related pack docs (siblings own these paths):

| Doc | Owner intent |
|-----|----------------|
| `00-charter.md` | Why / non-goals / Vietnamese AEC framing |
| `01-autocad-primitives.md` | Vanilla AutoCAD entity choices |
| `02-domain-model.md` | Baseline / slope / daylight vocabulary + I/O |
| `04-civil-vs-vanilla.md` | Civil corridor vs full AutoCAD 2026 primitives |
| `05-api-mvp.md` | API sketch / MVP call shapes (Main) |

---

## 1. How tools plug in today (load-bearing triad)

Pipe-routed tools always cross three synchronized sites. Missing any site is a defect; missing registry fails closed as destructive/unknown.

```
Agent
  → ForgeMcpTools.[McpServerTool]          // MCP annotations the agent sees
  → ForgeToolRunner.InvokeAsync            // server SafetyPolicy + audit start
  → ForgePipeClient → named pipe
  → NamedPipePluginServer
  → PluginCommandProcessor.Process         // token + plugin SafetyPolicy + backup + switch
  → AutoCAD API (main thread, doc lock)
```

### 1.1 Three mandatory edits (pipe tools)

| # | Site | File | What to add |
|---|------|------|-------------|
| 1 | MCP surface | `src/Forge.Server/ForgeMcpTools.cs` | Method + `[McpServerTool(Name=…, ReadOnly=…, Destructive=…, Idempotent=…, OpenWorld=…)]` + `Description` → `runner.InvokeAsync("forge_taluy_…", args, dryRun, …)` |
| 2 | Shared registry | `src/Forge.Shared/ToolMetadata.cs` → `ForgeToolRegistry` | `ToolMetadata` with same name and flags (`ReadOnly` / `Destructive` / `Idempotent` / `OpenWorld` / `RequiresBackup` / `RequiresAutoCad` / `Unsafe`) |
| 3 | Plugin dispatch | `src/Forge.Plugin/PluginCommandProcessor*.cs` | `case` arm in `Process` switch: `"forge_taluy_…" => Taluy*(command)` |

Guards already in-tree (must stay green if/when code lands):

- `ToolMetadataTests` — MCP names ↔ registry; annotation vs metadata flag parity  
- `PluginDispatchSyncTests` — every non–server-only registry name has a switch arm  
- `SafetyPolicyTests` — deny codes stay stable for open-world / wildcard patterns  

### 1.2 Helper factories in registry (reuse, do not invent parallel semantics)

| Factory | ReadOnly | Destructive | OpenWorld | RequiresBackup | Typical use |
|---------|----------|-------------|-----------|----------------|-------------|
| `Read(...)` | true | false | false | false | Inspect / validate / preview compute-only |
| `Write(...)` | false | false | false | **true** | Scoped model writes with backup |
| `Destructive(...)` | false | true | **true** | true | Executors / broad-risk paths |

Taluy MVP should prefer **`Read` + `Write`**, never `Destructive`/`OpenWorld`/`Unsafe`, so the capability gate allows typed calls and no wildcard behaviour is reachable at all.

### 1.3 Request lifecycle details Taluy inherits for free

From `PluginCommandProcessor.Process` (do not reimplement):

1. Named-pipe **token** check.  
2. **Plugin-side** `SafetyPolicy.Evaluate` (dual with server — ADR 0002).  
3. Audit JSONL write (`Source = "plugin"`).  
4. `Unsafe` tools also need `FORGE_ENABLE_UNSAFE_OPS` (Taluy must **not** be `Unsafe`).  
5. If `!ReadOnly && RequiresBackup && !DryRun` → `BackupPlanner.TryBackup` before dispatch.  
6. Non–read-only, non–dry-run → `WithUndoMark` (UNDO group).  
7. Result stamps `AuditId`; backup path merged into `Data` when present.

Dry-run helper pattern already used everywhere:

```csharp
return DryRun(command, new { /* planned metrics only */ });
// → ForgeResult.Success(..., new { dryRun = true, data = ... })
```

---

## 2. Proposed tool surface: `forge_taluy_*`

Naming: `forge_<group>_<action>` with group **`taluy`**. All tools are **pipe-routed**, `RequiresAutoCad: true`, model-space geometry only.

Shared vocabulary (see `02-domain-model.md`):

- **Baseline** — 3D polyline / feature-like chain (points with Z), by handle or explicit coords  
- **Daylight target** — elevation plane, offset surface, or catch points  
- **Slope** — H:V or percent; cut vs fill may differ  
- **Output** — 3D faces / mesh / solid / polylines + layers + optional section markers  

### 2.1 Tool table (Path C MVP)

**SME fork C (2026-07-29):** C-L1 skin + C-L3 unit array. See [05-api-mvp.md](05-api-mvp.md), [07-path-c-plan.md](07-path-c-plan.md).

| Tool | MCP / registry flags | RequiresBackup | Role | Notes |
|------|----------------------|----------------|------|-------|
| `forge_taluy_validate` | ReadOnly, !Destructive, Idempotent, !OpenWorld | no | Schema + geometry + **support** sanity | No DB write. |
| `forge_taluy_bind_surface` | ReadOnly, Idempotent | no | Resolve green `SOLIDS - Corridor -*Top*` solid → handle | Read-only pick/filter. |
| `forge_taluy_preview` | ReadOnly, Idempotent | no | **C-L1** plan | **No write.** |
| `forge_taluy_generate` | Write + backup | **yes** | **C-L1** skin commit | Prefer same support. |
| `forge_taluy_place_units` | Write + backup | **yes** | **C-L3** LOAI array **draped** on support | `drape=true` required on commit; flush pitch 0.4. |
| `forge_taluy_explode_to_mesh` | Write + backup | **yes** | Skin → mesh | Explicit handles. |
| `forge_taluy_list` | ReadOnly | no | List groups | |
| `forge_taluy_erase_group` | Write + backup, !OpenWorld | **yes** | Erase one group | Never `*`. |
| `forge_taluy_health` | ReadOnly | no | RD flag + caps | |

### 2.2 Suggested MCP method sketches (illustrative — not shipped)

```csharp
// ForgeMcpTools.cs — only when FORGE_ENABLE_TALUY_RD gates allow registration (see §3)

[McpServerTool(Name = "forge_taluy_validate", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false, UseStructuredContent = true,
    OutputSchemaType = typeof(ForgeResult))]
[Description("R&D: validate taluy baseline/slope/daylight args without writing the drawing.")]
public static Task<ForgeResult> TaluyValidate(
    ForgeToolRunner runner,
    /* baseline, cutSlope, fillSlope, daylight, ... */
    CancellationToken cancellationToken = default)
    => runner.InvokeAsync("forge_taluy_validate", new { /* ... */ },
        cancellationToken: cancellationToken);

[McpServerTool(Name = "forge_taluy_preview", ReadOnly = true, Destructive = false,
    Idempotent = true, OpenWorld = false, UseStructuredContent = true,
    OutputSchemaType = typeof(ForgeResult))]
[Description("R&D: plan taluy faces/bbox/layers; never mutates model space.")]
public static Task<ForgeResult> TaluyPreview(...)
    => runner.InvokeAsync("forge_taluy_preview", new { /* ... */ },
        cancellationToken: cancellationToken);

[McpServerTool(Name = "forge_taluy_generate", ReadOnly = false, Destructive = false,
    Idempotent = false, OpenWorld = false, UseStructuredContent = true,
    OutputSchemaType = typeof(ForgeResult))]
[Description("R&D: generate taluy slope geometry; dryRun returns planned face count and bbox only.")]
public static Task<ForgeResult> TaluyGenerate(..., bool dryRun = false, ...)
    => runner.InvokeAsync("forge_taluy_generate", new { /* ... */ },
        dryRun, cancellationToken: cancellationToken);

[McpServerTool(Name = "forge_taluy_explode_to_mesh", ReadOnly = false, Destructive = false,
    Idempotent = false, OpenWorld = false, UseStructuredContent = true,
    OutputSchemaType = typeof(ForgeResult))]
[Description("R&D: explode a prior taluy result handle set to mesh/faces; explicit handles only.")]
public static Task<ForgeResult> TaluyExplodeToMesh(
    ForgeToolRunner runner, string[] handles, bool dryRun = false, ...)
    => runner.InvokeAsync("forge_taluy_explode_to_mesh", new { handles },
        dryRun, cancellationToken: cancellationToken);
```

Registry counterparts (illustrative):

```csharp
["forge_taluy_validate"] = Read("forge_taluy_validate", "taluy"),
["forge_taluy_preview"]  = Read("forge_taluy_preview", "taluy"),
["forge_taluy_generate"] = Write("forge_taluy_generate", "taluy", idempotent: false),
["forge_taluy_explode_to_mesh"] = Write("forge_taluy_explode_to_mesh", "taluy", idempotent: false),
```

Plugin switch (illustrative):

```csharp
"forge_taluy_validate" => TaluyValidate(command),
"forge_taluy_preview"  => TaluyPreview(command),
"forge_taluy_generate" => TaluyGenerate(command),
"forge_taluy_explode_to_mesh" => TaluyExplodeToMesh(command),
```

### 2.3 Profiles

Do **not** add taluy tools to shipped profiles `core` / `plot` / `qa` in `ToolProfiles.cs` during R&D. Optional later: profile name `taluy_rd` behind the same feature flag, discoverable only when enabled.

---

## 3. Feature flag: keep default product surface clean

### 3.1 Env var

| Variable | Default | Effect |
|----------|---------|--------|
| `FORGE_ENABLE_TALUY_RD` | **unset / false** | Taluy MCP methods **not registered** (or registered but hard-fail with `taluy_rd_disabled`). Registry entries either absent or gated so `system_tool_profile` / agent tool lists stay publish-centric. |

Mirrors existing dual-gate style for risky surface:

| Existing | Purpose |
|----------|---------|
| `FORGE_ENABLE_UNSAFE_OPS` | Gates `forge_exec_dotnet` |
| `FORGE_ALLOW_FORCE_PUBLISH` | Gates `force=true` on publish/recipe |

Taluy is **not** unsafe/exec; it is **optional geometry R&D**. Separate flag avoids overloading `FORGE_ENABLE_UNSAFE_OPS`.

### 3.2 Recommended enforcement layers (defense in depth)

1. **Server registration** — Prefer conditional MCP exposure so default agent sessions never see `forge_taluy_*` (clean tool list). Options when implementing:
   - Split `ForgeMcpToolsTaluyRd` type registered only when env true in `Program.cs`, **or**
   - Single `ForgeMcpTools` with methods present but runner rejects when flag false (weaker: tools still listed).  
   **R&D preference:** separate tool type + conditional `WithTools<>` so default surface stays identical to today.
2. **`ForgeEnvironment`** — `bool EnableTaluyRd` from `FORGE_ENABLE_TALUY_RD`, same `FromProcess()` pattern as other flags (both server and plugin must agree).
3. **Plugin gate** — First lines of each `Taluy*` handler (and/or a single guard before switch arms): if `!_environment.EnableTaluyRd` → `ForgeResult.Failure(..., "taluy_rd_disabled", ...)`.
4. **Docs / skill** — Skill cheatsheet and capability-matrix **omit** taluy until MVP smoke; this research pack is the only in-repo mention.

### 3.3 What the flag is not

- Not a substitute for `SafetyPolicy`.  
- Not permission to call Civil 3D APIs.  
- Not a claim of shipped geometry capability in releases or README.

---

## 4. Package / folder layout options

| Option | Shape | Pros | Cons | R&D fit |
|--------|-------|------|------|---------|
| **A** | Cases + private methods inside existing `PluginCommandProcessor` partials | Matches current exclusive/QA pattern; zero project churn | Processor files already large; geometry math pollutes publish-centric types | Acceptable only for a **spike** (&lt;~100 LOC proof) |
| **B** | `Forge.Plugin/Taluy/*` helpers + thin switch arms | Clear boundary; still one NETLOAD DLL; easy to delete or `#if` later; mirrors mental model of “optional feature folder” | Still ships code in default plugin binary (must stay dark without flag) | **Recommended for R&D** |
| **C** | Future `src/Forge.Taluy` optional project (Shared contracts + Plugin hooks) | Cleanest product cut; can be solution-filter excluded | Needs csproj/sln/lockfile + load story; premature before MVP algorithm exists | **Post-MVP** if product wants optional geometry pack |

### 4.1 Recommendation for this R&D track: **B**

```
src/Forge.Plugin/
  Taluy/                          # R&D only; no public product claim
    TaluyArgs.cs                  # DTOs (or live in Forge.Shared later if dual-sided)
    TaluyPlanner.cs               # pure-ish plan: faces, bbox, counts (dry-run + preview)
    TaluyGenerator.cs             # ObjectARX writes: Face/Solid3d/PolyFaceMesh/etc.
    TaluyValidation.cs
  PluginCommandProcessor.Taluy.cs # partial: switch targets + flag gate + DryRun wiring
```

Shared pieces **only if** server must validate args before pipe (usually not required):

```
src/Forge.Shared/
  Taluy/                          # optional later
    TaluyContracts.cs             # JSON DTOs both sides agree on
```

Server:

```
src/Forge.Server/
  ForgeMcpTools.TaluyRd.cs        # [McpServerToolType] conditional registration
```

**Do not** start option C until:

1. Planner is deterministic and smoke-tested on metro sample baselines, and  
2. Product explicitly wants geometry as an optional SKU — still after capability-matrix update, not before.

**Avoid** long-term option A: publish/QA processor partials should not absorb slope math.

### 4.2 Solution / build notes (when code exists)

- Keep `765T-Forge.ServerOnly.slnf` free of AutoCAD-only taluy tests that need the plugin.  
- Unit-test **planner/validation** against plain nets in `Forge.Tests` if contracts live in Shared; plugin write paths stay manual/smoke.  
- No change to `capability-matrix.md` until MVP smoke criteria in §7 pass.

---

## 5. Safety contract for Taluy

Taluy writes create many entities. Safety must match existing Forge promises: dual policy, dry-run, backup, audit, no wildcards, scoped handles.

### 5.1 Dry-run (mandatory on writers)

| Tool | `dryRun=true` behavior |
|------|------------------------|
| `forge_taluy_generate` | Run **planner only**. Return `{ dryRun: true, data: { faceCount, vertexCount, bboxMin, bboxMax, cutFaceCount, fillFaceCount, targetLayers[], baselineHandle? } }`. **Zero** `Transaction` commits / entity `AppendEntity`. |
| `forge_taluy_explode_to_mesh` | Return planned mesh density / source handle set / bbox; no explode. |
| `forge_taluy_preview` / `_validate` | Always non-mutating; ignore or treat dryRun as N/A. |

Preview and generate-dryRun **should share one planner** so agents can trust count parity.

### 5.2 Backup

- Metadata: `RequiresBackup = true` on `generate` and `explode_to_mesh`.  
- Existing processor path copies active DWG to `FORGE_BACKUP_DIR` when `!DryRun`.  
- Do not bypass `BackupPlanner` with ad-hoc copies.

### 5.3 Deny wildcards / broad selection

Typed taluy tools must be **`OpenWorld = false`** so SafetyPolicy allows without scanning free text — **and** handlers must still refuse unsafe inputs:

| Input pattern | Response |
|---------------|----------|
| Baseline selection `"*"` / `"ALL"` / empty handle list meaning “whole drawing” | Typed failure by exact validation (there is no text denylist; the product gate is the capability gate) |
| Explode without explicit handles | Fail closed |
| Layer name filters with destructive “delete other taluy” implied | Not offered as API |
| Shelling out to `forge_exec_command` with ERASE/EXPLODE ALL for cleanup | Blocked by existing executor policy — **do not document workarounds** |

Implementation rule: **handles and numeric geometry only** in public args. No raw command strings inside taluy tools.

### 5.4 Dual SafetyPolicy + audit

- No server-only shortcut (unlike `forge_run_script`). Taluy always crosses the pipe → **dual eval**.  
- Both sides write audit; writers include dry-run flag and high-level counts (avoid logging full coordinate dumps if huge — summarize).  
- Prefer returning `ForgeVerification`-style read-back on generate: created handles + layer + faceCount.

### 5.5 Undo

Non–dry-run writers already wrap `WithUndoMark`. Keep taluy generation inside that single UNDO group so one Ctrl+Z reverts a generate when AutoCAD allows.

### 5.6 Resource / abuse bounds (R&D policy)

Planner should refuse pathological inputs before write:

- Max baseline vertices / max planned faces (configurable constants).  
- Non-finite coordinates.  
- Zero-length segments.  

Return structured `taluy_input_rejected` rather than hanging the AutoCAD main thread.

---

## 6. Publish path boundary

Taluy produces **model-space geometry** (and maybe helper layers). Publish remains the **paper-space / plot / issue-set** path.

```
[Model space]                         [Issue-set / paper path]
 baseline + taluy generate            layouts, page setups, CTB/STB
        │                                    │
        ▼                                    ▼
  3D faces / mesh / polys              forge_qa_preflight
  layers e.g. TALUY-*                        │
        │                                    ▼
        │                            forge_plot_publish / recipe_issue_set
        │                                    │
        └──── viewport may *show* taluy ─────┘
              but taluy tools never call publish
```

### 6.1 Explicit non-coupling

| Concern | Taluy | Publish |
|---------|-------|---------|
| Primary tools | `forge_taluy_*` | `forge_plot_*`, `forge_recipe_issue_set`, `forge_qa_preflight`, ceremony/CDE |
| Space | Model | Paper layouts + plot devices |
| Success artifact | Entity handles + planner metrics | PDF + PublishReceipt + seals |
| Feature flag | `FORGE_ENABLE_TALUY_RD` | `FORGE_ALLOW_FORCE_PUBLISH` (force only) |
| Preflight | Optional geometry self-check only | Titleblock, xref, pack, modal traps, issue-set contract |
| Ceremony evidence | **Not** a dry-run substitute for publish ceremony | Still requires plot/recipe dry-run evidence tools as today |

### 6.2 Interaction rules for agents (when enabled)

1. Generating taluy **does not** imply drawings are issue-ready.  
2. Do not add taluy steps into `forge_recipe_issue_set`.  
3. If a sheet must show slopes, that is a **viewport / layer state** concern using existing `forge_viewport_*` / `forge_layer_state_*` — still separate tools.  
4. Pack-and-go / transmittal may **include** DWGs that contain taluy solids the same as any other model content; no special taluy seal type in R&D.  
5. Failures in taluy must not change publish deny codes or force-publish gating.

### 6.3 Xref patterns

Metro practice often keeps alignment/topography in xrefs. R&D generate should default to **active DB model space** only; reading baseline geometry from a resolved xref is a later design (clone into host vs. write into xref). Until specified, require baseline in the **host** drawing or explicit deep-clone — do not silently edit unloaded xrefs.

---

## 7. Scope boundary vs product SemVer

| Item | Status |
|------|--------|
| This document + sibling research markdown | R&D pack only |
| `docs/capability-matrix.md` | **Unchanged** — Civil 3D / geometry megakit remain out of scope / deferred |
| `docs/roadmap.md` “Do not build yet” | Geometry megakit / Civil 3D still listed; taluy R&D is **not** a 0.3.0 work item |
| Product SemVer **0.3.0** | Publish determinism + trust hardening only |
| Claiming “Forge does taluy/slopes” in README, skill, or releases | **Forbidden** until MVP smoke + matrix row |

### 7.1 MVP definition (research → possible later implementation)

**MVP** = deterministic slope extrusion from baseline + slope params in **vanilla AutoCAD 2026** (full AutoCAD, not Civil corridor):

1. `validate` + `preview` agree on face counts for fixture baselines.  
2. `generate` with `dryRun=true` matches preview metrics; no entities added.  
3. `generate` without dry-run creates expected entities on agreed layers; backup path present; undo group works.  
4. Flag off → tools absent or `taluy_rd_disabled`; default agent tool list unchanged.  
5. No publish API changes; matrix still silent on taluy.

Only after that smoke evidence may a **separate** product decision add matrix rows and changelog — not implied by this folder.

### 7.2 Kill list (extension-specific)

- No Civil 3D assemblies or corridor APIs in default plugin load.  
- No registering taluy under `plot` / `qa` profiles by default.  
- No `OpenWorld` taluy executors.  
- No silent scope creep into “geometry megakit” (booleans, clash, grading networks, TIN platforms) under the taluy flag.  
- No edits to capability-matrix “for completeness” before smoke.

---

## 8. Implementation checklist (future — not authorized by this doc alone)

When product explicitly green-lights an R&D code spike:

1. Add `ForgeEnvironment.EnableTaluyRd` + `FORGE_ENABLE_TALUY_RD`.  
2. Option **B** folders under `Forge.Plugin/Taluy/` + `PluginCommandProcessor.Taluy.cs`.  
3. Triad: MCP (conditional type) + `ForgeToolRegistry` + switch arms for the four tools in §2.  
4. Planner shared by preview and generate-dryRun; backup on real generate/explode.  
5. Tests: registry/dispatch sync; planner unit tests; SafetyPolicy unchanged deny codes still pass.  
6. Manual AutoCAD smoke on a private metro-ish baseline DWG.  
7. **Still no** capability-matrix / roadmap claim until smoke notes land and humans accept product scope change.

---

## 9. Summary

| Topic | Decision |
|-------|----------|
| Integration | Standard triad: `ForgeMcpTools` + `ForgeToolRegistry`/`ToolMetadata` + `PluginCommandProcessor` case |
| Names | `forge_taluy_validate`, `_preview`, `_generate`, `_explode_to_mesh` |
| Flags | Read tools read-only; writers `Write` + `RequiresBackup`; never OpenWorld/Unsafe for MVP |
| Feature flag | `FORGE_ENABLE_TALUY_RD=true` (default off) — keeps default MCP surface publish-safe |
| Layout | **B** — `Forge.Plugin/Taluy/*` + thin partial; defer **C** optional project |
| Safety | dryRun → face count + bbox only; backup on generate; explicit handles; dual policy + audit |
| Publish | Orthogonal; model geometry only; no recipe/ceremony coupling |
| Product | R&D track only; **not** 0.3.0; matrix unchanged until MVP smoke |

*End of extension map.*
