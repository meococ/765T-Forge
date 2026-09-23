# 09 — CAD API: `BLOCK GIA CO` / LOAI structure & Accore placement

> **Track:** side R&D under `docs/research/taluy/` — **not** product scope.  
> **Host:** AutoCAD 2026 AccoreConsole (`W.164.0.0`) · lab scripts under `tests/` + `tests/fixtures/taluy/`.  
> **Probed:** 2026-07-30 · evidence from Accore SCR dumps + fixture CSVs.  
> **Companion:** [10-placement-workflow-review.md](10-placement-workflow-review.md) (workflow), [08-support-solid-vs-linemesh.md](08-support-solid-vs-linemesh.md) (green support).

---

## 0. Executive contradiction (why user sees only green)

| Source | What it reports |
|--------|-----------------|
| **Accore inventory** on `tests/taluy ke - 2.dwg` / `taluy_ke-2_FLUSHED.dwg` | **112×** `INSERT` `BLOCK GIA CO` on layer **`TALUY-GIACO`** (col=4, flags=0 ON), LOAI/ke MS inserts = **0**, IP Z ∈ **3.545…4.619**, scales **1,1,1**, vis60 nil/0 |
| **User UI / screenshot** | Only the green corridor strip (LINE mesh) — **no panels visible** |

**Root cause (confirmed):** placement scripts wrote **tilted DXF 210 extrusion normals** (~`0.32, 0.28, 0.90`, median tilt ~**26°**) onto each INSERT. AutoCAD’s OCS/extrusion math on nested 3DSOLID blocks then **corrupts drawing extents**:

| Drawing | EXTMIN | EXTMAX |
|---------|--------|--------|
| **bak (pre-flush, healthy)** `taluy ke - 2.bak.dwg` | `580753.30, 1476654.71, -21.32` | `581545.14, 1477376.99, 4.82` |
| **FLUSHED / current ke-2** | **`-1.50e6, -9.18e5, 0`** | `581543.15, 1477207.26, **713241.13**` |

→ **Zoom Extents** frames a multi-million-unit box; the green LINE mesh (~Z 0–5 m, XY ~581k) collapses to a thin strip in the view. Panels are **in the DB** but **off-camera / mis-bounded**, not erased and not on a frozen layer.

**Main fix direction (correct):** re-place with **world normal `210 = (0,0,1)`** + **Z drape only** (no OCS tilt), SAVEAS clean DWG, replace ke-2. Do **not** use tilted 210 for “flush to slope” on this block.

---

## 1. Probe inventory (evidence)

### 1.1 Drawings

| File | Role | GIA CO | LOAI MS | ke | Green LINEs | Notes |
|------|------|-------:|--------:|---:|------------:|-------|
| `tests/taluy ke - 2.bak.dwg` | Pre-script baseline | **60** | 12+341+16 | 1 | 15152 | `210≈(0,0,1)`, layer `0`, healthy extents |
| `tests/taluy ke - 2.dwg` (post lab) | Working seat (often locked by desktop) | **112** | 0 | 0 | 15152 | Tilted 210, broken extents |
| `tests/fixtures/taluy/taluy_ke-2_FLUSHED.dwg` | Flush SAVEAS copy | **112** | 0 | 0 | 15152 | Same broken extents / tilted 210 |
| `tests/fixtures/taluy/F-C-WORKING-giaco.dwg` | Lab working artifact | 112 (by design) | — | — | — | Same recipe family |
| `tests/taluy ke - 1.dwg` | Solid support golden | 0 | 0 | 0 | 0 | BODY/3DSOLID Top_Datum (no GIA CO) |

`INSUNITS = 6` (meters) on all probed ke-2 family files.

### 1.2 Accore commands used

```text
accoreconsole.exe /i "<dwg>" /s tests/taluy-inventory.scr /l en-US
accoreconsole.exe /i "<dwg>" /s tests/taluy-probe-giaco-full.scr /l en-US
```

Key SCR markers: `TALUY_COUNT=`, `TALUY_EXTMIN/MAX=`, `TALUY_SAMPLE=…|210=…`, `TALUY_DEF_CHILD_INSERTS=`, `TALUY_UNIT_SOLIDS=`.

### 1.3 Original insert mix (bak)

| Block | MS inserts | Layer (sample) | 210 (sample) |
|-------|----------:|----------------|--------------|
| `LOAI 2` | **341** | various / A-BLDG | world Z |
| `LOAI 3` | 16 | | |
| `LOAI 1` | 12 | | |
| `BLOCK GIA CO` | **60** (59 clean + 1 junk CRS) | `0` | **`(0,0,1)`** |
| `ke` | 1 | 2D detail package | — |

Junk filter (must apply to any baseline rebuild): keep only  
`580000 < X < 582000` ∧ `1476000 < Y < 1478000` ∧ `-5 < Z < 50`.  
One bak GIA CO sits at ~`(641176, -1321418, 601218)` — discard.

Clean original GIA CO path length ≈ **678.3 m**, median adjacent pitch ≈ **11.65 m** (sparse / hở — not flush).

---

## 2. `BLOCK GIA CO` definition

### 2.1 Composition (block table walk)

| Metric | Value | Source |
|--------|------:|--------|
| Def exists | yes | `tblsearch "BLOCK"` |
| Insert base (DXF 10 of BLOCK) | **`(0, 0, 0)`** | Accore `TALUY_DEF_BASE` |
| Nested `INSERT` count | **300** | `giaco_children.csv`, Accore |
| Nested `LOAI 1` | **13** | |
| Nested `LOAI 2` | **272** | |
| Nested `LOAI 3` | **15** | |
| Nested other | 0 | |
| Child insert layer | **`A-BLDG`** | first child probe |
| Anonymous / explode required for place? | **No** | place as named INSERT |

CSV: `tests/fixtures/taluy/giaco_children.csv` (300 rows).

### 2.2 Local footprint (child insert origins)

| Axis | Min | Max | Span (m) |
|------|----:|----:|---------:|
| X | 0.0000 | 9.4783 | **9.4783** |
| Y | −4.2635 | 4.6778 | **8.9413** |
| Z | 0 | 0 | 0 (all child Z = 0) |

Used by `gen_flush.py` plane-fit pad:

```text
XMIN,XMAX = -0.2 … 9.478322+0.2
YMIN,YMAX = -4.263468-0.2 … 4.677838+0.2
```

### 2.3 Nested orientation

| Field | Typical value |
|-------|----------------|
| Child rot (DXF 50) | **310.9961°** (LOAI 1/2 body grid) |
| Edge LOAI 3 rot | **40.9961°** with `sx = -1` (mirrored cap row) |
| LOAI 1 | `sx = -1` along one edge |
| Scales | `|sx|=|sy|=|sz|=1` (sign flip only on sx for mirror) |
| Mirror child count | 27 / 300 |

### 2.4 Extent along child array direction

Projecting child origins onto unit vector of rot 310.9961°:

| Direction | Length (m) |
|-----------|----------:|
| Along child rot | ≈ **5.649** |
| Across (perp in XY) | ≈ **7.648** |

These are **insert-origin** spans, not full solid envelopes (each LOAI solid adds ~0.4 m unit body).

### 2.5 MS insert transform convention (healthy bak)

| DXF | Meaning | Healthy value |
|-----|---------|---------------|
| 10 | Insertion point (WCS) | path station XYZ (Z already near green) |
| 41/42/43 | Scale X/Y/Z | **1 / 1 / 1** |
| 50 | Rotation about OCS Z | path bearing (deg), often ~0° or ~359.7° or small yaw ~0–7° |
| 210 | Extrusion / OCS Z | **`(0,0,1)` only** |
| 8 | Layer | originally `0`; lab used `TALUY-GIACO` |
| 60 | Visibility | absent / 0 (visible) |

**Rotation convention:** `-INSERT` rotation is **degrees CCW in the OCS XY plane** (world XY when 210=(0,0,1)). Matches plan tangent of the ke path. Do **not** encode slope pitch into DXF 50.

---

## 3. `LOAI 1` / `LOAI 2` / `LOAI 3` units

| Block | Def contents | Unit step (m) | Role in GIA CO |
|-------|--------------|--------------:|----------------|
| `LOAI 1` | **exactly 1× `3DSOLID`** | ≈ **0.3999** | Edge / special row (often mirrored) |
| `LOAI 2` | **exactly 1× `3DSOLID`** | ≈ **0.3999** | Main fill grid (272 inside panel) |
| `LOAI 3` | **exactly 1× `3DSOLID`** | ≈ **0.3999** | Cap / alternate edge (often mirrored) |

Evidence:

- Accore: `TALUY_UNIT_SOLIDS=LOAI 1=1` (same pattern for 2/3; script hiccup after first unit does not change the known 06/fixture result).
- Fixture `F-C-UNIT-LOAI2.dwg`: single solid WBLOCK.
- Pair spacing on clean LOAI 2 MS cluster (doc 06): dmin ≈ **0.40 m**, davg ≈ **0.42 m**.
- Child chain med step inside GIA CO: **0.399916 m** (256 consecutive LOAI2 steps).

**Design pitch for unit array (path C-L3):** **0.4 m flush (sát)**.  
**Design pitch for panel array (`BLOCK GIA CO`):** **panel extent along path**, not 0.4 m (see §5).

---

## 4. Green support (LINE mesh) — drape facts

Layer: `SOLIDS - Corridor - (3) - Top_Datum`

| Kind | Count (ke-2) | Notes |
|------|-------------:|-------|
| `LINE` | **15 152** | color often 3/green in UI; layer col 161 in table |
| `BODY` | 1 | residual, not primary |
| `REGION` | 3 | residual |

Z sample (mids): ~**0.004 … 4.65** m (doc 08 / green CSVs).  
Drape data: `tests/fixtures/taluy/green_mids_all.csv` (segment midpoints), `green_lines_sample.csv`.

**Support kind for ke-2 = `lineMesh`** (not TIN, not reliable single solid). See [08](08-support-solid-vs-linemesh.md).

### 4.1 Drape algorithms that work offline

Implemented in `tests/fixtures/taluy/gen_flush.py`:

1. **Spatial hash** of green mids (`cell = 4.0` m).  
2. For each station IP + rotated local footprint corners → collect mids in padded bbox.  
3. If ≥8 points: **least-squares plane** `z = a x + b y + c`.  
4. Else: **nearest mid Z** within grid neighborhood.  
5. IP elevation: `z_ip = plane_z(pl, ox, oy)` (or nearest).  
6. *(Failed experiment)* plane normal `n ∝ (-a,-b,1)` → wrote DXF 210 — **do not ship** (extents bomb).

**Acceptance drape gap:** |IP.Z − support.Z| ≤ **5 mm** at station (SME). Plane fit also yields footprint Δz (`dz`) for QA only — not for 210.

---

## 5. Flush pitch formula (panel sat)

### 5.1 Wrong vs right pitch

| Policy | Pitch | Result |
|--------|------:|--------|
| Original GIA CO chain | median **11.65 m** | Gaps (hở) between panels |
| LOAI unit pitch | **0.4 m** | Correct for bare `LOAI *`, **wrong** for whole GIA CO panel |
| **Flush panel** | **`P = L_panel_along_tangent`** | **6.0900 m** measured lab |

Lab constant (`gen_flush.py`):

```text
PITCH = 6.090009782147593   # meters, flush sát
```

With clean path length ≈ 678.3 m → `floor(L/P)+1` ≈ **112** stations (matches placed count).

### 5.2 Formula for Main

```text
# panel local footprint from def (child origins ± pad or true solid bbox)
foot = bbox2d(blockDef "BLOCK GIA CO")   # lab: x∈[0,9.478], y∈[-4.263,4.678]

# path tangent at station s (unit, plan)
t̂ = normalize(path'(s))   # XY

# corners in world after yaw θ (DXF 50), still Z-up
corners_w = [ Rz(θ)·p + IP_xy  for p in foot.corners ]

# extent of panel along path tangent (plan)
L_along = max(c·t̂ for c in corners_w) - min(c·t̂ for c in corners_w)

# optional: add one LOAI step if using insert-origin bbox only
# L_along += 0.4   # only if footprint was insert centers, not solid hull

P_flush = L_along     # lab measured 6.09 m on this def + path family
stations at arc length 0, P, 2P, … ≤ pathLen
```

**Rule:** pitch = **panel size projected on path tangent**, not LOAI 0.4 m, when placing `BLOCK GIA CO`.

---

## 6. Placement APIs in AccoreConsole 2026

VLA is often **nil / unreliable** in Accore (`vlax-ename->vla-object` bbox failed in early probes). Prefer **command + `entget`/`entmod`/`entupd`**.

### 6.1 ✅ Best recipe (recommended)

```lisp
;; pre: FILEDIA 0, CMDECHO 0, OSMODE 0, ATTREQ 0
;; erase old kè inserts by name (not wildcards across unrelated blocks)
(foreach bn '("BLOCK GIA CO" "LOAI 1" "LOAI 2" "LOAI 3" "ke")
  (setq ss (ssget "_X" (list (cons 0 "INSERT") (cons 2 bn))))
  (if ss (progn
    (setq i 0 n (sslength ss))
    (while (< i n) (entdel (ssname ss i)) (setq i (1+ i))))))

(command "_.-LAYER" "_M" "TALUY-GIACO" "_C" "4" "TALUY-GIACO" "")

;; for each station (x y z_draped rot_deg):
(command "_.-INSERT" "BLOCK GIA CO"
         (strcat (rtos x 2 6) "," (rtos y 2 6) "," (rtos z 2 6))
         "1" "1" (rtos rot 2 6))
(setq e (entlast) ed (entget e))
(if ed (progn
  (setq ed (subst (cons 8 "TALUY-GIACO") (assoc 8 ed) ed))
  ;; CRITICAL: force world normal — never slope normal
  (if (assoc 210 ed)
    (setq ed (subst (cons 210 (list 0.0 0.0 1.0)) (assoc 210 ed) ed))
    (setq ed (append ed (list (cons 210 (list 0.0 0.0 1.0))))))
  (entmod ed) (entupd e)))
```

`-INSERT` sequence in Accore (uniform scale block, no attrs):

```text
_.-INSERT
BLOCK GIA CO
x,y,z
1          ; X scale
1          ; Y scale (or blank if uniform prompt differs — lab used explicit 1 1)
rot_deg
```

Then **always** `entmod` layer + **`210=(0,0,1)`**.

### 6.2 ✅ `entmod` layer + 210 — what works / what doesn’t

| Operation | Accore result |
|-----------|---------------|
| `entmod` DXF **8** layer | ✅ reliable |
| `entmod` DXF **210** = `(0,0,1)` | ✅ safe, keeps extents sane |
| `entmod` DXF **210** = slope normal | ⚠️ entity accepts write-back, **extents explode**, Zoom Extents useless |
| `entupd` after entmod | ✅ use it |

Read-back proof (FLUSHED): all 112 have 210 present; avg `n ≈ (0.288, 0.247, 0.918)` — this is the **bad** tilted set. Bak samples: `210=(0,0,1)`.

### 6.3 ❌ `ROTATE3D` after INSERT

Tried in `gen_tilt2.py` / `place_giaco_tilt2.scr`:

```lisp
(command "_.ROTATE3D" e "" "ox,oy,oz" "ox+cx,oy+cy,oz" ang)
```

**Pitfalls:**

- Axis from `cross((0,0,1), n_slope)` + angle `acos(nz)` is fragile at Accore prompts.
- Observed lab symptom: **coordinates explode** / orientation not equivalent to a clean OCS write.
- Depends on pick semantics and UCS; worse than `entmod 210` for batch.
- **Do not use** for production recipe.

### 6.4 ❌ `ALIGN` script

Command-line `ALIGN` / scripted multi-point align hit **`Unknown command "N"`** (Yes/No prompt token mismatch in Accore script stream). Unreliable for headless batch. **Do not use**.

### 6.5 Explode / anonymous blocks

| Approach | Verdict |
|----------|---------|
| Place named `BLOCK GIA CO` | ✅ preferred — keeps 300 LOAI solids editable as one panel |
| Explode to LOAI / 3DSOLID | ❌ not required; balloons entity count ×300; loses panel identity |
| Anonymous `*U` blocks | present in DWG from Civil OE junk — **ignore**; do not target |
| Redefine block with pre-tilted solid | possible later, **not** needed for Z-drape MVP |

### 6.6 .NET (plugin path — future, not lab SCR)

When/if `forge_taluy_place_units` lands:

```csharp
var br = new BlockReference(ip, btrId) {
  ScaleFactors = new Scale3d(1,1,1),
  Rotation = rotRad,           // about BlockTransform Z
  Normal = Vector3d.ZAxis,     // MUST stay world Z for this block family
  Layer = "TALUY-GIACO"
};
// Do NOT set Normal = slopeNormal for GIA CO / LOAI panel blocks
```

`BlockReference.Normal` ≡ DXF 210. Same extents lesson applies.

---

## 7. Accore do / don’t

### DO

1. Use **`_.-INSERT`** with explicit `"x,y,z"`, scales `1` `1`, rot degrees.  
2. **`entmod` layer** to `TALUY-GIACO` (create layer first).  
3. Force **`210 = (0,0,1)`** after every insert.  
4. **Drape Z** from LINE mesh (plane fit or nearest mid) onto IP.  
5. Pitch panels at **`P ≈ panel extent along tangent`** (~**6.09 m** for this def).  
6. Erase prior kè inserts by **explicit block names** before re-place.  
7. Filter junk CRS before building path from existing inserts.  
8. After write: assert **EXTMIN/EXTMAX** stay near site box (~581k / 1.477M / Z 0–10), not 1e6-scale.  
9. `FILEDIA 0`, `ATTREQ 0`, `OSMODE 0`; end with controlled `SAVEAS` + `QUIT Y`.  
10. Prefer **bak / FIXED** copies when desktop holds a lock on ke-2.

### DON’T

1. **Don’t** set DXF **210** to slope / plane normal on `BLOCK GIA CO` or LOAI inserts.  
2. **Don’t** batch **`ROTATE3D`** to “tilt onto green”.  
3. **Don’t** script **`ALIGN`** with interactive Y/N tokens in Accore.  
4. **Don’t** rely on **VLA** `GetBoundingBox` in Accore for measurement.  
5. **Don’t** use LOAI **0.4 m** pitch when placing whole **GIA CO** panels.  
6. **Don’t** explode panels just to drape.  
7. **Don’t** treat “only green visible” as “inserts missing” without checking insert count + **extents**.  
8. **Don’t** Zoom Extents as sole visual QA on a drawing with corrupted extents — use known XY window or `ZOOM C` on a station.  
9. **Don’t** leave junk insert at Z~6e5 in the set (destroys Z range stats).  
10. **Don’t** claim Forge MCP taluy tools are shipped — lab SCR/Python only.

---

## 8. Recommended single recipe for Main

```text
INPUTS
  dwg: tests/taluy ke - 2.bak.dwg  (or FIXED once written)
  block: "BLOCK GIA CO"            (def already in DWG)
  support: LINE on "SOLIDS - Corridor - (3) - Top_Datum"
  path: clean chain of stations (from bak GIA CO XY + resample)

OFFLINE (Python)
  1. Load green_mids → grid
  2. Build path polyline from clean IPs; resample at P = 6.090009782147593 m
     (or recompute L_along from def footprint × tangent)
  3. For each station:
       rot = path yaw (deg, world Z)
       z   = plane_fit(footprint@rot) or nearest_mid_z
       emit row (x,y,z,rot) with n forced (0,0,1)
  4. Write place_*.scr: erase → layer → loop -INSERT + entmod 8 + 210=(0,0,1)

ACCORE
  5. accoreconsole /i bak /s place_*.scr
  6. SAVEAS "tests/taluy ke - 2-FIXED.dwg" (or replace ke-2 when unlocked)

VERIFY
  7. COUNT BLOCK GIA CO == N_stations (~112 for full chain @ 6.09 m)
  8. All 210 == (0,0,1); scales 1; layer TALUY-GIACO ON
  9. EXTMIN/MAX within site envelope (no |coord| > 1e6; Zmax < 100)
 10. |z_ip - green_z| median ≤ 5 mm
 11. Desktop: ZOOM to station 581200,1477040 → panels visible on green strip
```

**Slope “flush” visual without OCS tilt:** Z drape already puts the panel **on** the mesh; remaining air gap is LOAI solid thickness / base-point offset inside the unit solid — address later with a constant Z bias from unit MASSPROP if SME requires contact faces, **not** with 210 tilt.

---

## 9. Acceptance checks

| # | Check | Pass criterion |
|---|-------|----------------|
| A1 | Insert count | `BLOCK GIA CO` count = planned stations; LOAI/ke MS optional 0 after erase |
| A2 | Layer | all new inserts on `TALUY-GIACO`, layer ON, color 4 |
| A3 | Normal | every insert `210 = (0,0,1)` (±1e-9) |
| A4 | Extents | EXTMIN/MAX inside ~`[580k..582k] × [1.476e6..1.478e6] × [-30..30]` |
| A5 | Pitch | adjacent plan spacing median ≈ **6.09 m** ± 20 mm (panel mode) |
| A6 | Drape | median \|ΔZ\| to green ≤ **5 mm** |
| A7 | Visual | Zoom window on path shows panels **and** green mesh together |
| A8 | Def integrity | `BLOCK GIA CO` still 300 nested LOAI; each LOAI def 1× 3DSOLID |
| A9 | No junk | no insert with \|Z\|>100 or XY outside site filter |

---

## 10. Root causes

1. **Primary (user “only green”):** tilted **DXF 210** OCS normals on 112 GIA CO inserts → **catastrophic extents** → Zoom Extents shows only green LINE strip. Inserts exist (count/layer/scale OK).  
2. **Secondary (placement intent):** scripts erased LOAI/ke MS arrays and re-placed **panel** blocks — correct unit choice for “gia cố” panels, but used **slope 210** to fake flush.  
3. **Pitch history:** original panels ~11.65 m (gappy); flush lab correctly moved to **~6.09 m** panel pitch; LOAI **0.4 m** remains the **unit** pitch inside the panel, not the panel array pitch.  
4. **Support reality:** ke-2 green is **LINE mesh** (~15k), not a single TIN/solid — drape must be segment/plane query.  
5. **API footguns:** `ROTATE3D` / `ALIGN` / VLA bbox unsuitable for Accore batch; `-INSERT` + `entmod` is the reliable path **if 210 stays world Z**.

---

## 11. Concrete fix steps for Main

1. Stop writing slope vectors into DXF **210**. Treat prior flush/tilt SCR generators (`gen_flush.py` 210 branch, `gen_tilt3.py`) as **buggy for display** even though entity counts look green in Accore.  
2. Regenerate plan CSV with same stations/Z/rot but **`nx,ny,nz = 0,0,1`**.  
3. Run Accore place SCR against **`taluy ke - 2.bak.dwg`** (healthy source) or a copy not locked by desktop.  
4. `SAVEAS` → `tests/taluy ke - 2-FIXED.dwg`, then replace `taluy ke - 2.dwg` when unlocked.  
5. Verify A1–A9; especially **EXTMAX.Z < 100** and desktop zoom on a known station.  
6. Optional QA: `ZOOM C 581200,1477040,4 50` style center zoom in SCR to prove panels without Zoom Extents.  
7. Keep panel pitch **6.09 m** (or recompute `L_along`); do not revert to 11.65 m.  
8. Document in workflow review (doc 10): erase-all-kè then re-place is fine; **visibility failure ≠ empty def**.  
9. Long-term plugin: `BlockReference.Normal = ZAxis` always for GIA CO/LOAI; expose `alignNormal=false` as default (already in 05-api-mvp sketch).  
10. Do **not** mark product matrix; leave artifacts under `docs/research/taluy/` + `tests/fixtures/taluy/`.

---

## 12. Evidence index

| Artifact | Path |
|----------|------|
| Children dump | `tests/fixtures/taluy/giaco_children.csv` |
| Original inserts | `tests/fixtures/taluy/giaco_inserts.csv` |
| Flush plan | `tests/fixtures/taluy/giaco_flush_plan.csv` |
| Placed readback | `tests/fixtures/taluy/giaco_placed.csv` |
| Tilted 210 readback | `tests/fixtures/taluy/giaco_tilted_readback.csv` |
| Green mids | `tests/fixtures/taluy/green_mids_all.csv` |
| Flush generator | `tests/fixtures/taluy/gen_flush.py` |
| Tilt3 (210) generator | `tests/fixtures/taluy/gen_tilt3.py` |
| Tilt2 (ROTATE3D) generator | `tests/fixtures/taluy/gen_tilt2.py` |
| Full probe SCR | `tests/taluy-probe-giaco-full.scr` |
| Inventory SCR | `tests/taluy-inventory.scr` |
| Stats helper | `tests/fixtures/taluy/_stats_giaco.py` |
| Fixture README | `tests/fixtures/taluy/README.md` |

### Key measured constants (lab)

```text
LOAI_STEP_M           = 0.399916 ≈ 0.4
GIA_CO_CHILD_N        = 300 = 13 + 272 + 15
GIA_CO_FOOTPRINT_XY   = [0, 9.4783] × [-4.2635, 4.6778]
GIA_CO_BASE           = (0,0,0)
GIA_CO_PITCH_FLUSH_M  = 6.090009782147593
GIA_CO_PITCH_ORIG_MED = 11.6516
PATH_LEN_CLEAN_M      ≈ 678.31
STATIONS_FLUSH        = 112
TILT_MED_DEG_BAD      ≈ 26.06   # from nz_med 0.89833 — do not repeat
```

---

## Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.1 | 2026-07-30 | Accore probes bak+FLUSHED; children stats; 210 extents root cause; recommended Z-drape + world normal recipe |

---

## End matter (contract)

### Root causes

1. Tilted DXF 210 on GIA CO inserts destroys extents → UI Zoom Extents shows only green mesh despite 112 valid inserts.  
2. Flush scripts used slope normals as “drape tilt”; entity count succeeded, visual failed.  
3. Panel pitch must be ~6.09 m (footprint along tangent), not LOAI 0.4 m.  
4. ke-2 support is LINE mesh; Z drape offline is fine — OCS tilt is not.

### Concrete fix steps for Main

1. Re-place from bak with `-INSERT` + layer entmod + **`210=(0,0,1)`** + draped Z.  
2. SAVEAS FIXED; verify extents + desktop window zoom.  
3. Freeze pitch 6.09 m; ban ROTATE3D/ALIGN/tilted-210 in Accore lab path.

### Acceptance checks

Counts 112 (or planned N), all `210=(0,0,1)`, sane EXT*, median drape ≤5 mm, panels visible with green in a local ZOOM (not only Zoom Extents).
