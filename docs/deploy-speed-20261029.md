# Deploy — speed wave (branch `forge-speed-20261029`)

What ships:
- 5 new tools:
  - `forge_doc_import`: DXF/DWG into the active drawing in one call.
  - `forge_qa_annotation_clash`: read-only presentation QA.
  - `forge_standards_import`: layers and styles from a template.
  - `forge_block_insert_bulk`: N tags/blocks plus optional MLEADERs in one transaction.
  - `forge_plot_preset_apply`: one plot configuration on many layouts, saved as a page setup.
- `forge_draw_batch` is faster and has new keys:
  - `atomic` and `resultMode`;
  - `textStyle`, `backgroundMask`/`maskScale`, `leaderStyle`, and `contentBlock` + `attributes` for block MLEADERs.
- `forge_entity_get` reads back text style, mask and MLEADER block.

The registry now has 119 tools, 66 of them in the `authoring` profile.

This branch is built on `forge-reliability-20261029`. If that wave is not deployed yet, this deploy carries it as well; run its smoke (`docs/deploy-20261029.md` section 7) too. Server and plugin change together, so deploy both or neither. A new server talking to the old plugin answers `unknown tool` for the 5 new tools and ignores the new `draw_batch` keys.

Verified before deploy, with no live AutoCAD:
- `dotnet build/test .\765T-Forge.ServerOnly.slnf -c Release`: 0 warnings, 447/447 tests.
- The 2025 plugin builds cleanly to `artifacts\staging-speed-20261029\Forge.Plugin\2025` (Release, 0 errors, 0 CS warnings).
- Section 7 was replayed headless against that staged plugin (see section 9). The results listed there are the "Expected" values below. Rows marked **live-only** could not run headless.

Paths used below:

| What | Path |
|---|---|
| Repo | `D:\765T-Forge` |
| Live server (current MCP config) | `D:\765T-Forge\src\Forge.Server\bin\Debug\net8.0\Forge.Server.exe` |
| Release server (already built and tested) | `D:\765T-Forge\src\Forge.Server\bin\Release\net8.0\Forge.Server.exe` |
| Staged plugin (AutoCAD 2025, Release) | `D:\765T-Forge\artifacts\staging-speed-20261029\Forge.Plugin\2025\` |
| Deployed bundle | `%APPDATA%\Autodesk\ApplicationPlugins\765T-Forge.bundle\` |
| Rollback copies (created in step 3) | `D:\765T-Forge\artifacts\rollback-speed-20261029\` |
| Smoke scratch folder (written as `<S>` below) | `D:\765T-Forge\artifacts\smoke-speed-20261029\` |
| Metric template | `C:\Users\toila\AppData\Local\Autodesk\AutoCAD 2025\R25.0\enu\Template\acadiso.dwt` |

## 1. Stop, in this order

Same order as `docs/deploy-20261029.md` section 1:
1. Let the live agent finish its current step and save its drawings.
2. Close every MCP client session that has `765t-forge` loaded. `Get-Process Forge.Server` must print nothing.
3. Close AutoCAD 2025 normally. `Get-Process acad, accoreconsole` must print nothing.

## 2. Build or locate the binaries

```powershell
cd D:\765T-Forge
git status                 # clean, on forge-speed-20261029
git log --oneline -8       # the speed-wave commits sit on top of the reliability-wave commits
```

The plugin is already staged. To rebuild it (Release, staging output only; this never writes to the bundle):
```powershell
$env:AUTOCAD_2026_ROOT = 'D:\AutoCAD 2025'
.\scripts\build-plugin.ps1 -Year 2025 -Configuration Release -OutDir D:\765T-Forge\artifacts\staging-speed-20261029\Forge.Plugin\2025
Get-ChildItem D:\765T-Forge\artifacts\staging-speed-20261029\Forge.Plugin\2025   # Forge.Plugin.dll, Forge.Shared.dll (+ .pdb, .deps.json)
```

## 3. Take rollback copies

```powershell
$rb = 'D:\765T-Forge\artifacts\rollback-speed-20261029'
New-Item -ItemType Directory -Force $rb | Out-Null
Copy-Item D:\765T-Forge\src\Forge.Server\bin\Debug\net8.0 "$rb\server-debug-net8.0" -Recurse
Copy-Item "$env:APPDATA\Autodesk\ApplicationPlugins\765T-Forge.bundle" "$rb\765T-Forge.bundle" -Recurse
```

## 4. Server: pick ONE option

**Option A — rebuild Debug, so the MCP config stays unchanged (recommended).**
```powershell
cd D:\765T-Forge
dotnet build .\765T-Forge.ServerOnly.slnf -c Debug
dotnet test  .\765T-Forge.ServerOnly.slnf -c Debug --no-build     # expect 447 passed
```

**Option B — point the MCP config at the Release build.** This works exactly as in `docs/deploy-20261029.md` section 4:
- change `command` to `...\bin\Release\net8.0\Forge.Server.exe`;
- keep `env` unchanged.

## 5. Plugin: copy the staged build into the bundle

Copy **every** staged file into the folder that `PackageContents.xml` `ModuleName` points to.

`Forge.Shared.dll` is mandatory. The plugin uses these new Shared types:
- `WildcardSet`
- `DrawingImport`
- `AnnotationClash`
- `StandardsImport`
- `BlockBulkInsert`
- `PlotPreset`

If only `Forge.Plugin.dll` is copied, the new tools fail with `TypeLoadException` or `MissingMethodException`.
```powershell
$src = 'D:\765T-Forge\artifacts\staging-speed-20261029\Forge.Plugin\2025'
$dst = "$env:APPDATA\Autodesk\ApplicationPlugins\765T-Forge.bundle\Contents\Windows"   # = ModuleName folder
Copy-Item "$src\*" $dst -Force
foreach ($f in 'Forge.Plugin.dll','Forge.Shared.dll') { (Get-FileHash "$src\$f").Hash -eq (Get-FileHash "$dst\$f").Hash }   # both True
```

## 6. Restart, in this order

1. Start AutoCAD 2025 and open any drawing. Run `MCP_STATUS`; the pipe name must match `FORGE_PIPE_NAME`.
2. Start a **new** MCP client session; the tool list is cached per session. The list must include:
   - `forge_doc_import`
   - `forge_qa_annotation_clash`
   - `forge_standards_import`
   - `forge_block_insert_bulk`
   - `forge_plot_preset_apply`
3. `forge_system_health`: `ok=true`, plugin answering.
4. Hand the client-job drawings back **only after** section 7 passes. Every smoke step uses scratch drawings.

## 7. Smoke checklist

```powershell
New-Item -ItemType Directory -Force D:\765T-Forge\artifacts\smoke-speed-20261029 | Out-Null
```

### 7.0 Scratch drawing, layers, styles

| # | Call | Expected |
|---|---|---|
| a | `forge_doc_new templatePath="<metric template>"` | ok; new drawing active |
| b | `forge_layer_create` for `A-ANNO` (2), `A-WALL` (7), `A-DOOR` (3), `M-DUCT` (5), `M-TAG` (1) as `name`/`colorIndex` | ok ×5 |
| c | `forge_style_text_create name="SMOKE-ROMANS" fontName="romans.shx"` | ok, `created=true` |
| d | `forge_style_dim_create name="SMOKE-100" dimScale=100 textStyle="SMOKE-ROMANS"` | ok, `created=true` |

### 7.1 `forge_draw_batch` — atomic + summary (the clash scene)

Make one call with `atomic=true resultMode="summary"` and these 13 steps (all text on `A-ANNO` unless noted):

```json
[
 {"tool":"text","position":[0,-2000],"text":"SUPPLY AIR","height":250,"layer":"A-ANNO"},
 {"tool":"text","position":[300,-1900],"text":"RETURN AIR","height":250,"layer":"A-ANNO"},
 {"tool":"line","start":[-500,1000],"end":[3000,1000],"layer":"A-WALL"},
 {"tool":"text","position":[0,900],"text":"WALL TAG","height":250,"layer":"A-ANNO"},
 {"tool":"arc","center":[5000,0],"radius":900,"startAngleDeg":0,"endAngleDeg":90,"layer":"A-DOOR"},
 {"tool":"line","start":[5000,0],"end":[5000,900],"layer":"A-DOOR"},
 {"tool":"text","position":[5150,150],"text":"D1","height":200,"layer":"A-ANNO"},
 {"tool":"line","start":[8000,-500],"end":[8000,500],"layer":"M-DUCT"},
 {"tool":"text","position":[7500,0],"text":"DUCT 600x300","height":250,"layer":"A-ANNO"},
 {"tool":"mtext","position":[11000,0],"text":"MASKED NOTE","height":250,"backgroundMask":true,"layer":"A-ANNO"},
 {"tool":"line","start":[11500,-500],"end":[11500,500],"layer":"M-DUCT"},
 {"tool":"text","position":[14000,0],"text":"tiny","height":50,"layer":"A-ANNO"},
 {"tool":"text","position":[17000,0],"text":"CLEAN","height":250,"layer":"A-ANNO"}
]
```

Expected:
- `created=13`, `failed=0`, `atomic=true`, `resultMode="summary"`, `results=[]`.
- `countsByType={"Arc":1,"DBText":7,"Line":4,"MText":1}`.
- `elapsedMs` and `entitiesPerSecond` are present.
- The whole envelope is well under 1 KB.

### 7.2 `forge_qa_annotation_clash` — the known errors are found

| Call | Expected |
|---|---|
| `window=[[-2000,-3000],[20000,3000]] plotScale=100` | `passed=false`; `summary.findings=5`; `byCheck={"text_overlap":1,"wall_straddle":1,"door_swing":1,"text_crossing":1,"min_height":1}`; `texts=8`; `maskedCrossingsSkipped=1` (the masked MTEXT); messages below |
| same with `checks=["min_height"] minTextHeightMm=0.4` | `passed=true`, 0 findings |
| same with `maxFindings=2` | 2 findings returned, `findingsTruncated=true`; `summary.findings=5` and `summary.byCheck` still count all 5 |

The five messages, each with handles, layers and a world `at` point:
- `TEXT 'SUPPLY AIR' overlaps TEXT 'RETURN AIR' (52% of the smaller box).`
- `TEXT 'WALL TAG' straddles wall Line on A-WALL.`
- `Line on M-DUCT runs through TEXT 'DUCT 600x300'.`
- `TEXT 'D1' sits inside the door swing Arc on A-DOOR.`
- `TEXT 'tiny' plots 0.5 mm high (minimum 2 mm).`

`CLEAN` and `MASKED NOTE` must not be reported.

### 7.3 Tag block (used by 7.4 and 7.9)

| # | Call | Expected |
|---|---|---|
| a | `forge_draw_circle center=[0,-6000] radius=150 layer="M-TAG"` | ok; note the handle |
| b | `forge_block_create name="TAGBUB" basePoint=[0,-6000] handles=[<circle>] attributeDefs=[{"tag":"ID","prompt":"Tag","height":100,"position":[-100,-6050]}]` | ok; `consumed=1`, `attributeTags=["ID"]` |

### 7.4 `forge_draw_batch` — the new annotation keys

Make one call, non-atomic, with the default `resultMode`:

```json
[
 {"tool":"mtext","position":[0,-10000],"text":"MASKED MTEXT","height":250,"backgroundMask":true,"textStyle":"SMOKE-ROMANS","layer":"A-ANNO"},
 {"tool":"leader","points":[[3000,-10000],[3600,-9400]],"text":"MASKED LEADER","textHeight":200,"backgroundMask":true,"layer":"A-ANNO"},
 {"tool":"leader","points":[[6000,-10000],[6600,-9400]],"contentBlock":"TAGBUB","attributes":{"ID":"VAV-7"},"layer":"A-ANNO"},
 {"tool":"dimension","kind":"aligned","p1":[0,-12000],"p2":[3000,-11000],"dimLinePoint":[1300,-10800],"dimStyle":"SMOKE-100","layer":"A-ANNO"},
 {"tool":"dimension","kind":"linear","p1":[4000,-12000],"p2":[7000,-11000],"dimLinePoint":[5500,-10000],"rotationDeg":30,"dimStyle":"SMOKE-100","layer":"A-ANNO"},
 {"tool":"hatch","vertices":[[8000,-12000],[10000,-12000],[10000,-10500],[8000,-10500]],"pattern":"SOLID","layer":"A-ANNO"},
 {"tool":"hatch","vertices":[[11000,-12000],[13000,-12000],[13000,-10500],[11000,-10500]],"pattern":"ANSI31","scale":20,"layer":"A-ANNO"}
]
```

Expected:
- `created=7`, `failed=0`.
- Result types, in step order:
  1. `MText`
  2. `MLeader`
  3. `MLeader`
  4. `AlignedDimension`
  5. `RotatedDimension`
  6. `Hatch`
  7. `Hatch`
- Step 7 (ANSI31 hatch) is no longer live-only. The `eInvalidInput` seen headless was a Forge bug (pattern angle set before the pattern; fix-smoke-20261029 D2), not a headless limitation; it now succeeds headless and live, with the pattern, scale and first pattern line read back. A failure of step 7 is a FAIL.

Read-back with `forge_entity_get handle=<result handle>`:

| Step | Expected `kind` |
|---|---|
| mtext | `textStyle="SMOKE-ROMANS"`, `backgroundMask=true`, `textHeight=250` |
| masked leader | `contentType="MTextContent"`, `text="MASKED LEADER"`, `backgroundMask=true` |
| block leader | `contentType="BlockContent"`, `contentBlock="TAGBUB"`. The tag bubble sits at (6600,-9400) and shows `VAV-7` |
| aligned dim | `dimStyleName="SMOKE-100"`, `measurement≈3162.28` |
| rotated dim | `dimStyleName="SMOKE-100"`, `measurement≈3098.08` (30° projection) |

Negative checks:

| Call | Expected |
|---|---|
| a leader step with `"contentBlock":"TAGBUB","attributes":{"NOPE":"x"}` | ok; the step carries the warning `No non-constant attribute definition for tag(s): NOPE` |
| `atomic=true` with `[{"tool":"line","start":[0,-14000],"end":[100,-14000]},{"tool":"text","position":[0,-14500],"text":"X","height":100,"textStyle":"NO-SUCH-STYLE"}]` | `ok=false`, `batch_invalid`; step 1 error `text_style_not_found`; **nothing** is drawn (`forge_entity_list window=[[-10,-14010],[110,-13990]]` returns 0) |
| a single mtext step with `"backgroundMask":true,"maskScale":9` | `batch_all_failed`; the step error is `invalid_args` ("maskScale must be between 1 and 5 …") |

### 7.5 QA respects masks

| # | Call | Expected |
|---|---|---|
| a | `forge_draw_batch` `[{"tool":"line","start":[500,-10500],"end":[500,-9700],"layer":"A-ANNO"},{"tool":"line","start":[3900,-9600],"end":[3900,-9000],"layer":"A-ANNO"}]` | `created=2`. The lines run through the masked MTEXT and the masked leader text |
| b | `forge_qa_annotation_clash window=[[-1000,-10600],[5000,-8800]] checks=["text_crossing"]` | `passed=true`, 0 findings, `maskedCrossingsSkipped>=2` (headless: 3) |
| c | same with `reportMaskedCrossings=true` | `passed=false`; the masked crossings come back as `text_crossing` findings with `masked=true` (headless: 3) |

### 7.6 Save the scene (the source file for 7.7–7.9) with a page setup

| # | Call | Expected |
|---|---|---|
| a | `forge_plot_preset_apply layouts=["Layout1"] paperSize="ISO_full_bleed_A3_(420.00_x_297.00_MM)" plotStyle="monochrome.ctb" plotArea="extents" scale="fit" setupName="SMOKE-A3"` | ok; `applied=1`; `setupName="SMOKE-A3"`, `setupReplaced=false`; layout row `paperMm=[420,297]`, `fit=true`, `centered=true`, `plotStyle="monochrome.ctb"`. **Live-only:** `viewerSuppression.state="clone"` (or `"already_off"`) and device `765T-Forge DWG To PDF (no viewer).pc3`. The headless run used `noViewer=false`, because the clone is written to the user's real Plotters folder |
| b | `forge_doc_save path="<S>\scene.dwg"` | ok |
| c | `forge_doc_export_dxf outputPath="<S>\scene.dxf"` | ok |

### 7.7 `forge_standards_import` (into a second, fresh drawing)

| # | Call | Expected |
|---|---|---|
| a | `forge_doc_new templatePath="<metric template>"`, then `forge_layer_create name="A-ANNO" colorIndex=2` | ok |
| b | `forge_standards_import templatePath="<S>\scene.dwg" dryRun=true` | dry run; nothing is written. The plan matches (c) |
| c | same without `dryRun` | `added=8`, `overwritten=0`, `skipped=13`. Per kind, see below. Note: "Existing names were kept…" |
| d | `templatePath=… include=["textStyles","dimStyles"] filter="SMOKE-*" onConflict="overwrite"` | `added=0`, `overwritten=2`: `SMOKE-ROMANS` and `SMOKE-100` (`filteredOut` 2 and 3) |
| e | `forge_layout_page_setup_apply setupName="SMOKE-A3" layout="Layout2"` | ok; the imported page setup is usable (`applied="SMOKE-A3"`) |

Per kind in (c):

| Kind | added | skippedExisting | skippedProtected |
|---|---|---|---|
| textStyles | `SMOKE-ROMANS` | | |
| dimStyles | `SMOKE-100` | | |
| layers | `A-DOOR`, `A-WALL`, `Defpoints`, `M-DUCT`, `M-TAG` | `A-ANNO` | `0` |
| pageSetups | `SMOKE-A3` | | |
| linetypes | | | `ByBlock`, `ByLayer`, `Continuous` |

### 7.8 `forge_doc_import` (same second drawing)

Every call uses `path="<S>\scene.dxf"`.

The counts below were taken headless before fix-smoke-20261029, when the ANSI31 hatch of 7.4 was not created. With that hatch (D2) the source has 2 more entities on `A-ANNO`: one `HATCH` and its `LWPOLYLINE` boundary. Expect 24 entities (`HATCH` 2, `LWPOLYLINE` 2), `created=24`, `A-ANNO` 19 and `X-ANNO` 19.

| # | Call | Expected |
|---|---|---|
| a | `blockName="SCENE" dryRun=true` | `source.entities=22`, `source.units="millimeters"`, `effectiveScale=1`, `blockExists=false`. `countsByType`: `ARC` 1, `DIMENSION` 2, `HATCH` 1, `LINE` 6, `LWPOLYLINE` 1, `MTEXT` 2, `MULTILEADER` 2, `TEXT` 7 |
| b | `blockName="SCENE" position=[0,-50000]` | ok; `created=22`; `blockHandle` and `insertHandle` set; `countsByLayer={"A-ANNO":17,"A-DOOR":2,"A-WALL":1,"M-DUCT":2}`; `extents≈[-500,-62000,0,18165,-48846,0]`; `timingsMs.total` in tens of ms; warning that the 4 existing layers keep this drawing's colours |
| c | same again | `ok=false`, `block_exists` |
| d | `blockName="SCENE" replaceExisting=true insertReference=false` | ok; `replaced=true`, `existingReferences=1`; warnings name the kept nested block `TAGBUB` and suggest ATTSYNC |
| e | `mode="merge" position=[0,-100000] layerMap={"A-*":"X-*"} layerPrefix="S-"` | ok; `created=22`; `countsByLayer={"X-ANNO":17,"X-DOOR":2,"X-WALL":1,"S-M-DUCT":2}`; `layersCreated` adds `S-M-TAG`. The `*` in the target takes what the key's `*` matched |
| f | `mode="xref" blockName="XR_SCENE" xrefSavePath="<S>\scene-xref.dwg" overwriteAcknowledged=true position=[0,-150000]` | ok; `created=0`; `xrefPath` ends `scene-xref.dwg`; `layersCreated` are `XR_SCENE|…`; warning "DXF saved as DWG for the xref" |
| g | `forge_undo` | Honest refusal in this sequence (fix-smoke D3, headless `docB-trace`/`xv3`): after step d (`replaceExisting`), AutoCAD's U of the xref attach also restores the nested `TAGBUB` circle and attribute definition, which the attach never reported. Expected: `ok=false`, `undo_target_not_reverted`, `foreign` names that `AcDbCircle` and `AcDbAttributeDefinition`, `redoCount=1`, `restored=true`; XR_SCENE is still attached (`forge_xref_list`) and nothing else changed. `undone=true` here would need that U to be attributed to the write, which Forge cannot prove |
| g2 | on a fresh drawing: `blockName="SCENE" position=[0,-50000]`, then the call of f, then `forge_undo` | `undone=true` with `verification.passed=true`; `forge_xref_list` is empty and the XR_SCENE INSERT is gone (headless `xv1`) |
| h | `path="<S>\nope.dxf"` / `path="<S>\scene.txt"` (need not exist) / `path="<S>\scene.dxf" mode="xref"` without `xrefSavePath` | `import_source_not_found` / `unsupported_format` / `xref_requires_dwg` |

### 7.9 `forge_block_insert_bulk`

| # | Call | Expected |
|---|---|---|
| a | `libraryPath="<S>\scene.dwg" blockName="TAGBUB" layer="M-TAG" leaderLayer="A-ANNO" leaderTextHeight=150 items=[…100 items…]` | ok; `created=100`, `leaders=20`, `blocks=["TAGBUB"]`; `items[0]` has both `handle` and `leader`; `elapsedMs` in tens of ms. The ID values read back with `forge_block_get_attr` |
| b | `blockName="TAGBUB" items=[{"position":[0,0]},{"position":[1000,0],"layer":"NO-SUCH-LAYER"}]` | `ok=false`, `layer_not_found`, `errors[0].index=1`; **nothing** is inserted |
| c | `blockName="NOPE" items=[{"position":[0,0]}]` | `block_not_found` ("Pass libraryPath …") |

The 100 items in (a): item `k` (0–99) has
- `"position":[(k%10)*1000, -200000+(k/10)*1000]` (`k/10` is integer division);
- `"attributes":{"ID":"SD-k"}`;
- for every 5th item (`k%5==0`), `"leader":{"points":[[x+400,y+400]],"text":"SA k","backgroundMask":true}`.

### 7.10 `forge_plot_preset_apply`

| # | Call | Expected |
|---|---|---|
| a | `layouts=["*"] paperSize="ISO_full_bleed_A1_(841.00_x_594.00_MM)" plotStyle="monochrome.ctb" plotArea="extents" scale="fit" setupName="SMOKE-A3" dryRun=true` | dry run; `layouts=["Layout1","Layout2"]`; `viewerSuppression.state="would_clone"` (or `"already_off"`); nothing is written, not even the PC3 clone |
| b | same without `dryRun` | `applied=2` (Layout1, Layout2; Model is not included by `*`); `setupReplaced=true` (SMOKE-A3 came in with 7.7); each row `paperMm=[841,594]`, `fit=true`, `centered=true`, `plotStyle="monochrome.ctb"`; **live-only:** the device is the no-viewer clone |
| c | `layouts=["Model"] paperSize="ISO_full_bleed_A1_(841.00_x_594.00_MM)" plotArea="window" window=[[-2000,-3000],[20000,3000]] scale="1:100"` | `applied=1`; `fit=false`, `customScale=[1,100]` |
| d | `layouts=["NoSuchLayout"]` | `layout_not_found`; `available` lists the layouts |
| e | **live-only:** `forge_plot_to_pdf layout="Layout1" plotArea="layout" outputPath="<S>\preset-layout1.pdf"` | ok; **no viewer window**; `pdfSheetMm≈[841,594]`, i.e. the preset's sheet |

### 7.11 Scale check (one drawing, real sizes)

Expected times below are headless; over the pipe, expect them plus JSON transfer.

| # | Call | Expected |
|---|---|---|
| a | `forge_doc_import path="D:\765T-Forge\artifacts\tmp\bench\fixtures\big.dxf" blockName="HVAC_BASE" position=[0,-400000]` | `created=8900`; `countsByType`: `ARC` 1250, `DIMENSION` 50, `INSERT` 100, `LINE` 2500, `LWPOLYLINE` 2500, `MTEXT` 1250, `TEXT` 1250. `timingsMs.total` ≈ 0.2 s headless (read 65, insert 122). The whole call took ≈ 1.4 s, including the pre-write safety backup |
| b | `forge_qa_annotation_clash plotScale=100 maxFindings=20` | block-aware: `blockTraversal.insertsVisited≥200`; 20 findings, `findingsTruncated=true`; `elapsedMs` 172 ms headless for 2,796 texts and 23,068 segments |
| c | `forge_draw_batch atomic=true resultMode="summary"` with 1000 generated steps (400 lines, 200 closed polylines, 200 text, 100 mtext, 100 circles) | `created=1000`; envelope ≈ 0.7 KB; `entitiesPerSecond` reported (headless in-plugin: 91–99k/s) |

The fixture in (a) is gitignored local test data. Regenerate it with `python artifacts\tmp\bench\make_fixtures.py <folder>` (needs `ezdxf`).

### 7.12 Hand-off

Close the scratch drawings without saving, then hand AutoCAD back. The agent must start a new MCP session (6.2).

## 8. Rollback

1. Stop everything in the order of section 1.
2. Restore the plugin:
   ```powershell
   Copy-Item D:\765T-Forge\artifacts\rollback-speed-20261029\765T-Forge.bundle\* "$env:APPDATA\Autodesk\ApplicationPlugins\765T-Forge.bundle\" -Recurse -Force
   ```
3. Restore the server:
   - Option A: copy `rollback-speed-20261029\server-debug-net8.0\*` back to `src\Forge.Server\bin\Debug\net8.0\`.
   - Option B: point `command` back to the Debug exe.
4. Restart in the order of section 6.

Drawings made with the new tools stay valid DWG; nothing in them depends on the plugin.

## 9. What was pre-verified headless (2026-10-29)

**Harness.** A throwaway harness (`artifacts\tmp\bench\`, gitignored) runs isolated `accoreconsole.exe` (`/isolate`). It NETLOADs the staged plugin and drives `PluginCommandProcessor.Process` in process, so policy, audit, backup and the undo mark are all exercised.

Each run:
- removes every real token from the child environment;
- uses a unique pipe name;
- keeps backups and audit inside the run folder.

To rerun:
```powershell
.\artifacts\tmp\bench\run-bench.ps1 -Name <run> -PluginDir <staged plugin dir> -Plan <plan.json>
```
Plans `plan-doc-A.json` / `plan-doc-B.json` replay sections 7.0–7.11, and `plan-doc-C.json` replays the negative and variant rows. Never put `noViewer=true` or `forge_plot_to_pdf` in a plan: both write a PC3 into the real Plotters folder, even under `/isolate`.

**Results (all on the staged Release plugin):**
- 7.1, 7.2, 7.3, 7.5, 7.7, 7.8 a–f and h, 7.9 a–c, 7.10 a–d and 7.11 a–b matched the "Expected" values above.
- 7.4 created 6 of 7: the patterned hatch hits a pre-existing headless limitation.
- `forge_undo` (7.8 g) returned `undone=true`.

**`forge_draw_batch` throughput.** 1000 mixed steps (400 lines, 200 closed polylines, 200 text, 100 mtext, 100 circles), measured in process, warm:

| Build | Harness ms per call | In-plugin `elapsedMs` | `entitiesPerSecond` | Envelope |
|---|---|---|---|---|
| reliability wave (before) | 18.6–22.4 | — | — | 166 KB |
| speed wave, default | 15.3–17.7 | 10.3–11.8 | 85–97k | 87 KB |
| speed wave, `atomic=true resultMode=summary` | 14.7–16.7 | 10.1–11.0 | 91–99k | 0.7 KB |

The in-plugin time is about 25–35% lower, and the payload sent back over the pipe is 2× smaller by default and about 235× smaller in summary mode. Over the live pipe, the payload saving is expected to dominate.

**Not verifiable headless; check live:**
- patterned HATCH (7.4);
- the no-viewer clone used by `forge_plot_preset_apply` and the PDF it plots (7.6 a, 7.10 b/e);
- `leaderStyle` with a non-Standard MLEADER style;
- MLEADER/table styles in `forge_standards_import`;
- the MCP round trip and payload size over the pipe;
- QA door/wall detection on real client drawings. It uses layer and block name patterns: default door layers `*DOOR*`, wall layers `*WALL*`. Override with `doorLayers` / `doorBlocks` / `wallLayers`.
