# 05 — Taluy MVP API surface (Path C)

> **Status:** research draft · **Not shipped** · does **not** change `docs/capability-matrix.md`  
> **SME fork C locked 2026-07-29:** **C-L1** slope skin + **C-L3** unit array  
> **Depends on:** [00-charter.md](00-charter.md), [01](01-autocad-primitives.md), [02](02-domain-model.md), [03](03-forge-extension.md), [06](06-fixture-ke-analysis.md), [07](07-path-c-plan.md)
> **SME (2026-07-29):** LOAI units **must drape on green corridor solid** (`SOLIDS - Corridor - (3) - Top_*`). Baseline-Z-only is **not** acceptance.

## Design goals

1. **C-L1:** Deterministic cut/fill slope skins from a 3D baseline (vanilla AutoCAD 2026).  
2. **C-L3:** Deterministic array of unit kè solids (`LOAI *` blocks) along the same baseline.  
3. Agent-safe: `dryRun` first, backup on write, read-back handles, no wildcard selects.  
4. Thin MCP surface; math/placement in plugin helpers.  
5. Feature-flagged: `FORGE_ENABLE_TALUY_RD=true`.

## Canonical tool table

| Tool | Track | RO/W | Backup | Idempotent | Purpose |
|------|-------|------|--------|------------|---------|
| `forge_taluy_validate` | shared | R | — | yes | Schema + preflight (skin/units/support) |
| `forge_taluy_bind_surface` | shared | R | — | yes | Resolve green corridor solid → handle + bbox |
| `forge_taluy_preview` | C-L1 | R | — | yes | Skin plan (**no write**) |
| `forge_taluy_generate` | C-L1 | W | yes | no* | Commit skin; prefer same support solid |
| `forge_taluy_place_units` | C-L3 | W | yes | no* | Array LOAI **draped** on support solid |
| `forge_taluy_explode_to_mesh` | C-L1 | W | yes | no | Skin → mesh (explicit handles) |
| `forge_taluy_list` | shared | R | — | yes | List groups |
| `forge_taluy_erase_group` | shared | W | yes | yes | Erase one group |
| `forge_taluy_health` | shared | R | — | yes | Flag + caps |
\*Update: same `groupId` + `replaceGroup=true`.

**Skin representation default:** `SubDMesh` (`smoothLevel=0`); fallback `Face`; edges `Polyline3d`.  
**Unit representation:** `INSERT` of block def containing one `3DSOLID` (as in LOAI 1/2/3).

---

## Shared baseline args

```json
{
  "baseline": {
    "source": "points|handle|layer",
    "points": [{"x": 0, "y": 0, "z": 10}],
    "handle": null,
    "layer": null,
    "sampleSpacingM": 5.0
  },
  "groupId": "taluy-platform-A-R1",
  "toleranceM": 0.001,
  "dryRun": true,
  "replaceGroup": false,
  "failClosed": true
}
```

**Host model only** for `handle` sources in MVP (no xref baseline).

---

## C-L1 — Skin (`preview` / `generate`)

### Args (extends shared)

```json
{
  "side": "left|right|both",
  "cutSlopeHV": 1.0,
  "fillSlopeHV": 1.5,
  "target": {
    "type": "fixedElevation|offsetZ|maxWidth",
    "elevation": 0.0,
    "offsetZ": null,
    "maxWidthM": null
  },
  "berms": [
    {"widthM": 2.0, "afterDropM": 3.0, "slopeHV": null}
  ],
  "layers": {
    "prefix": "TALUY",
    "faces": "TALUY-FACES",
    "crest": "TALUY-CREST",
    "toe": "TALUY-TOE",
    "guide": "TALUY-GUIDE"
  },
  "representation": "subdmesh|faces"
}
```

Slope may accept `"1.5:1"` or `{horizontal, vertical}` — normalize to H:V.

### Result

```json
{
  "ok": true,
  "track": "skin",
  "groupId": "taluy-platform-A-R1",
  "mode": "preview|commit",
  "side": "right",
  "stationCount": 42,
  "faceCount": 82,
  "representation": "subdmesh",
  "bbox": {"min": [0, 0, 0], "max": [100, 20, 12]},
  "handles": {"mesh": [], "crest": [], "toe": [], "guides": []},
  "metrics": {
    "cutFaceCount": 40,
    "fillFaceCount": 42,
    "approxSurfaceAreaM2": 1234.5,
    "approxVolumeM3": null
  },
  "warnings": [],
  "verification": {"passed": true, "readBackFaceCount": 82},
  "auditId": "..."
}
```

### Skin algorithm (engine)

Per side, stations \(i\):

1. Tangent bearing; outward plan normal ±90°.  
2. Classify cut/fill from baseline Z vs target (see [02](02-domain-model.md) §1.2).  
3. Run = \(|\Delta z| \times H/V\); berms optional.  
4. Stitch quads → triangles → `SubDMesh`; emit crest/toe `Polyline3d`.

---

## C-L3 — Units (`place_units`)

### Args (extends shared)

```json
{
  "blockName": "LOAI 2",
  "side": "left|right|center",
  "pitchM": 0.4,
  "packing": "flush",
  "gapM": 0.0,
  "offsetM": 0.0,
  "startOffsetM": 0.0,
  "endOffsetM": 0.0,
  "mirror": false,
  "align": "tangent",
  "rotationOffsetDeg": 0.0,
  "scale": {"x": 1.0, "y": 1.0, "z": 1.0},
  "layer": "TALUY-UNITS",
  "maxCount": 5000,
  "support": {
    "mode": "auto|solidHandle|solidLayer|lineMeshLayer",
    "kind": "auto|solidBody|lineMesh",
    "handle": null,
    "layerPattern": "SOLIDS - Corridor - (3) - Top*",
    "drape": true,
    "alignNormal": false,
    "lineMeshRadiusM": 1.0
  }
}
```

| Field | Rules |
|-------|--------|
| `blockName` | Must exist; prefer single-3DSOLID (`LOAI 1\|2\|3`) |
| `pitchM` | Default **0.4** — insert-to-insert **flush/sát** (not air gap) |
| `packing` | `flush` (default) \| `gap` + `gapM` |
| `support.drape` | **true required on commit** (SME): IP projected onto support solid |
| `support.handle` / `layerPattern` | Green corridor top; else `taluy_support_missing` |
| `support.alignNormal` | MVP default **false** (Z-up); tilt later |
| `mirror` | `scale.x = -|sx|` when true |
| `maxCount` | hard cap |

**Flush / “sát”:** `pitchM` ≈ unit length along run; DWG2 ≈ 0.40 m.

**Drape / “dính mặt xanh” (binding):**

```text
support = 3DSOLID|BODY on layer SOLIDS - Corridor - (3) - Top*
for each station s_i (plan along baseline):
  hit = project/raycast (s_i.xy) onto support
  if miss → taluy_drape_miss (failClosed default)
  INSERT at hit.xyz   // contact on green face
  rot = baseline tangent; normal tilt only if alignNormal
```

Plan path from baseline; **contact Z from green solid**, not baseline Z alone.

**Count policy:**

```text
step = pitchM + (packing==gap ? gapM : 0)
n = floor(length / step) + 1
s_i = startOffset + i * step
```

### Result

```json
{
  "ok": true,
  "track": "units",
  "groupId": "taluy-platform-A-R1-units",
  "mode": "preview|commit",
  "blockName": "LOAI 2",
  "pitchM": 0.4,
  "packing": "flush",
  "count": 120,
  "countPolicy": "floor(L/step)+1",
  "baselineLengthM": 47.6,
  "supportHandle": "7F",
  "supportLayer": "SOLIDS - Corridor - (3) - Top_Datum",
  "drape": true,
  "mirror": false,
  "bbox": {"min": [], "max": []},
  "handles": {"inserts": []},
  "metrics": {
    "spacingMinM": 0.399,
    "spacingMaxM": 0.401,
    "spacingAvgM": 0.4,
    "drapeHitCount": 120,
    "drapeMissCount": 0,
    "maxGapToSurfaceM": 0.001
  },
  "warnings": [],
  "verification": {"passed": true, "readBackInsertCount": 120},
  "auditId": "..."
}
```

Preview/dryRun: `handles` empty; report `count` + spacing stats from pure math.

---

## Validation / error codes

| Code | When |
|------|------|
| `taluy_rd_disabled` | Flag off |
| `taluy_baseline_too_short` | < 2 points or length too small |
| `taluy_baseline_degenerate` | zero-length / NaN segment |
| `taluy_baseline_not_host` | handle not in host MS |
| `taluy_support_missing` | no green corridor solid for layer/handle |
| `taluy_support_ambiguous` | multiple solids match pattern; need handle |
| `taluy_drape_miss` | station cannot project onto support (failClosed) |
| `taluy_slope_invalid` | H:V ≤ 0 |
| `taluy_slope_vertical_unsupported` | H == 0 |
| `taluy_sample_invalid` | bad skin sample spacing |
| `taluy_no_catch` | maxWidth miss |
| `taluy_toe_self_intersect` | toe 2D self-cross |
| `taluy_too_large` | skin vert/face cap |
| `taluy_block_missing` | `blockName` not in drawing |
| `taluy_pitch_invalid` | pitch ≤ 0 or absurd |
| `taluy_too_many_units` | count > maxCount |
| `taluy_target_required` | explode/erase without group/handles |
| `taluy_group_not_found` | replace/erase unknown groupId |

---

## Agent workflow (path C)

```text
forge_system_health
→ forge_taluy_health
→ forge_taluy_bind_surface          # lock green Top_* solid handle
→ forge_taluy_validate
→ forge_taluy_preview               # C-L1 (optional)
→ forge_taluy_generate (dryRun)
→ forge_taluy_generate
→ forge_taluy_place_units (dryRun)  # C-L3 drape on same support
→ forge_taluy_place_units
→ forge_taluy_list
```

On timeout: `forge_qa_readback_after_timeout` — never blind retry writes.

GroupId convention: skin `…-skin`, units `…-units` (or single parent + child ids in xdata).

---

## Engine placement (future spike)

```text
src/Forge.Plugin/Taluy/
  TaluyBaseline.cs          # sample stations, frames
  TaluySkinGenerator.cs     # C-L1
  TaluyUnitPlacer.cs        # C-L3
  TaluyValidator.cs
  TaluyArgs.cs
  TaluyGroupXData.cs        # app "765T_TALUY"
PluginCommandProcessor.Taluy.cs
```

---

## Test fixtures

| Id | Track | Case |
|----|-------|------|
| T01 | L1 | Straight fill, fixed Z |
| T02 | L1 | Straight cut |
| T03 | L1 | Cut→fill transition |
| T04 | L1 | Arc-ish baseline |
| T05 | L1 | One berm |
| T06 | L1 | both sides asymmetric HV |
| T07 | L1 | replaceGroup skin |
| T08 | L1 | dryRun no entity delta |
| T09 | L3 | LOAI 2 @ 0.4 m straight |
| T10 | L3 | mirror true / side right |
| T11 | L3 | missing block → fail-closed |
| T12 | L3 | replaceGroup units |
| T13 | L3 | dryRun no insert delta |
| T14 | L3 | pitch vs count policy on known length |
| T15 | shared | flag off → taluy_rd_disabled |

---

## Out of MVP

- Civil corridor / feature-line / grading  
- EG TIN daylight  
- Elliptical openings boolean  
- `BLOCK GIA CO` / full `ke` 2D package  
- Survey-grade volumes  
- `forge_taluy_publish` (use existing plot path)

---

## Open questions (non-blocking)

1. Unit Z: always baseline Z vs optional fixed elevation.  
2. XData vs extension dictionary for groupId.  
3. Whether `place_units` may target anonymous blocks exploded from LOAI (no — named def only).  

**Resolved:** `pitchM` default **0.4 m** — SME confirmed 2026-07-29.

## Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.1 | 2026-07-29 | Skin-only API |
| 0.2 | 2026-07-29 | **Path C:** + `place_units`, dual track, T09–T15 |
| 0.3 | 2026-07-29 | Pitch **0.4 m** SME-confirmed |
