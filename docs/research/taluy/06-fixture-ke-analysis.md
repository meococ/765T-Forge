# 06 — Fixture analysis: `taluy ke - 1/2.dwg`

> **SME (2026-07-29): fork C** + pitch **0.4 m flush** + units **must drape on green corridor solid** (`SOLIDS - Corridor - (3) - Top_*`). See [05](05-api-mvp.md), [07](07-path-c-plan.md).

> **Status:** lab inventory (AccoreConsole 2026) · R&D only · not product capability  
> **Sources:** `tests/taluy ke - 1.dwg`, `tests/taluy ke - 2.dwg`  
> **Probed:** 2026-07-29 via `tests/taluy-inventory.scr`, `tests/taluy-probe-ke.scr`

## 1. What the screenshot is

The attached 3D view (hatched rectangular deck, elliptical openings, vertical walls, magenta construction lines) matches **`taluy ke - 1.dwg`**: extracted **Civil corridor solids**, not a hand-built vanilla taluy mesh.

| Clue | Evidence |
|------|----------|
| Solid body with holes | `3DSOLID` + `BODY` on corridor solid layers |
| Layer naming | `SOLIDS - Corridor - (3) - Top_Datum`, `... - Curb` |
| Low Z range | EXT Z ≈ 0.004 … 5.04 m (deck thickness / local elev band) |
| No block array of “kè” units | DWG1 has **zero** `ke` / `LOAI *` inserts |

## 2. File roles

| File | Size | Role |
|------|------|------|
| **`taluy ke - 1.dwg`** | ~10.3 MB | **Clean solid extract** — corridor Top_Datum + Curb as 3DSOLID/BODY. Good visual reference for *target look* of a kè/platform solid. |
| **`taluy ke - 2.dwg`** | ~3.6 MB | **Full Civil site context** — corridor LINE mesh, TIN/topo layers, VN design layers, and **arrayed kè unit blocks** (`LOAI 1/2/3`, `ke`, `BLOCK GIA CO`). |

Both: `INSUNITS = 6` (meters). Project XY ≈ **581k E, 1.477M N** (VN projected CRS-style).

## 3. Inventory summary

### 3.1 `taluy ke - 1.dwg`

| Metric | Value |
|--------|------:|
| Total entities | **4** |
| `3DSOLID` | 2 |
| `BODY` | 2 |
| Layers | 4 (0 + 3 corridor solid layers) |
| Blocks of interest | none |
| EXTMIN | 581054.19, 1476653.42, 0.0039 |
| EXTMAX | 581543.15, 1477207.26, 5.0415 |

Handles (stable for lab notes):

| Handle | Type | Layer |
|--------|------|-------|
| `7D` | 3DSOLID | `SOLIDS - Corridor - (3) -` |
| `7B` | 3DSOLID | `SOLIDS - Corridor - (3) - Curb` |
| `7F`, `78` | BODY | `SOLIDS - Corridor - (3) - Top_Datum` |

### 3.2 `taluy ke - 2.dwg`

| Metric | Value |
|--------|------:|
| Total entities | **15 587** |
| `LINE` | 15 152 (almost all on corridor solid layer) |
| `INSERT` | 430 |
| `LWPOLYLINE` | 1 |
| `REGION` | 3 |
| `BODY` | 1 |
| Layers | 232 (Civil C-*/V-* + VN: `TK1`, `CHU`, `VIA HE`, `@@ THUAN - TK`, …) |
| EXTMIN | 580753.30, 1476654.71, **-21.32** |
| EXTMAX | 581545.14, 1477376.99, **4.82** |

#### Block inserts (kè-related)

| Block | Inserts | Adjacent spacing (plan) |
|-------|--------:|-------------------------|
| `LOAI 2` | **341** | dmin ≈ **0.40 m**, davg ≈ **0.42 m** |
| `LOAI 3` | 16 | ≈ **0.40 m** |
| `LOAI 1` | 12 | ≈ **0.40 m** |
| `BLOCK GIA CO` | 60 | (composite; nests LOAI inserts) |
| `ke` | **1** | assembly / detail package |

**Placement pattern (sample):**

- Healthy cluster: `LOAI 3` @ ~`(581067, 1477203, 0)`, rot ≈ **48.9°**, `sx = -1` (mirrored side).
- Suspect cluster: many `LOAI 2` @ ~`(499489, -1493127, **202128**)` — wrong CRS/Z; treat as **junk or bad paste**, do not use for baseline math.

#### Block definition character

| Block | Composition (entity types) | 3D solid? |
|-------|----------------------------|-----------|
| **`ke`** | 1209 ents: `LINE` 393, nested `INSERT` 658, `LWPOLYLINE` 138, `HATCH` 20 on `@@ THUAN - TK` / `VIA HE` / `@@ THUAN - PHU` | **No** — 2D kè detail / hatch package |
| **`LOAI 1`** | **exactly one `3DSOLID`** | **Yes — unit solid “kè loại 1”** |
| **`LOAI 2`** | **exactly one `3DSOLID`** | **Yes — unit solid “kè loại 2”** (341 arrayed) |
| **`LOAI 3`** | **exactly one `3DSOLID`** | **Yes — unit solid “kè loại 3”** |
| **`OB1`** | 2× `LWPOLYLINE` | outline helper |
| **`BLOCK GIA CO`** | 300 nested `INSERT` on `A-BLDG` | composite “gia cố” (bundles LOAI units) |
| Corridor solids | `REGION`/`BODY` + massive `LINE` tessellation on `SOLIDS - Corridor - (3) - Top_Datum` | Civil extract / proxy mesh |

**Screenshot ↔ data:** the hatched deck with elliptical voids is almost certainly a **`LOAI *` unit solid** (or the corridor Top_Datum/Curb solid in DWG1). Unit path for automation = **array INSERT of LOAI solids** along an edge at ~0.4 m, not only ruled slope mesh.

## 4. Domain mapping → Taluy R&D vocabulary

| R&D term ([02](02-domain-model.md)) | In these DWGs |
|-------------------------------------|---------------|
| Baseline | **Not explicit** as a single `Polyline3d`. Proxy candidates: corridor edge feature lines (Civil), or chain of `LOAI *` insert origins at Z≈0 with spacing 0.4 m |
| Daylight / EG | TIN/topo layers present in DWG2 (`C-TINN*`, `C-TOPO*`) — **Civil surfaces**, outside vanilla MVP |
| Slope face (taluy skin) | Corridor **Top_Datum** solid/BODY (DWG1) is the closest “finished envelope” |
| Curb / edge structure | Layer `... - Curb` 3DSOLID |
| Unit kè / gia cố | `LOAI 1|2|3` (each 1× 3DSOLID), `BLOCK GIA CO`, block `ke` (2D detail) |
| Output target (visual) | Match DWG1 solid readability; optional array of LOAI like DWG2 |

**Important naming note:** In site language *“taluy kè”* here is closer to **retaining / edge structure + corridor solid + unit blocks**, not only bare H:V earth slope. R&D MVP (plane daylight skin) is a **subset**; full kè package = skin **and/or** unit solid array.

## 5. Implications for engine design

### 5.1 What vanilla MVP can learn from these files

1. **Coordinate frame:** meters, large VN grid — ε and string formatting must keep ≥3–4 decimal mm safely.  
2. **Array pitch:** ~**0.4 m** along edge for LOAI units — candidate default `unitSpacingM` if we add a *place units* tool later (post-MVP).  
3. **Side mirror:** `sx = -1` on LOAI 3 = left/right flip; API `side` + optional `mirrorUnit` needed for block stamping.  
4. **Rotation:** bearing ≈ 48.9° on sample chain — unit rot = baseline tangent.  
5. **Target visual:** closed solid/skin with openings (holes) — openings are **out of MVP** (boolean subtract later).  
6. **Unit primitive:** `LOAI *` = single `3DSOLID` block def — array path is standard `INSERT`, not mesh generation.

### 5.2 What these files do *not* give MVP for free

| Gap | Why |
|-----|-----|
| Clean baseline poly with Z | Must extract/export from Civil feature line or digitize |
| Cut vs fill H:V params | Encoded inside Civil assembly/corridor, not as Forge args |
| EG TIN daylight in vanilla | Would need surface sampling engine (K2 kill risk if mandatory) |
| Associative rebuild | Civil corridor rebuild ≠ regenerate-from-params |

### 5.3 Recommended fixture split for lab

| Id | Source | Use |
|----|--------|-----|
| **F-KE-SOLID** | DWG1 (as-is) | Visual golden / bbox / layer names |
| **F-KE-UNITS** | DWG2 subset: LOAI inserts with Z≈0 only | Spacing/rot/mirror stats |
| **F-KE-BASELINE** | *To create* | Export or draw one open 3D poly along deck edge from DWG1 | 
| **F-KE-PLANE** | *Synthetic* | Same baseline + fixedElevation daylight → compare skin to Top_Datum edge (coordination ε, not survey) |

Do **not** feed the Z≈202128 LOAI cluster into tests.

## 6. Path C tool stack (locked)

```text
L1  Slope / envelope skin     ← forge_taluy_generate          (R1 — C-L1)
L2  Corridor solid consume    ← read-only reference (DWG1); not generative
L3  Unit array (LOAI 1/2/3)   ← forge_taluy_place_units       (R1 — C-L3)
L4  Detail block ke / gia cố  ← optional later (block campaign)
L5  Civil corridor rebuild    ← R2 only, flagged, not default
```

Screenshot satisfaction for kè ≈ **L3** (+ L2 as visual golden). Earth slope coordination ≈ **L1**. Path C ships both in R1 research spike.
## 7. Open questions for Metro SME

1. Is the screenshot the **desired output** of automation, or only a Civil corridor extract for reference?  
2. Should agent **generate solids**, or **array LOAI blocks** along an edge, or both?  
3. Typical H:V behind the kè / platform edge on this line?  
4. Confirm LOAI pitch **0.4 m** is design intent (not just this sample).  
5. Discard rules for inserts with absurd Z (202128) — data cleanup before any batch place.

## 8. Next lab actions

1. In desktop AutoCAD: isolate DWG1 solids → `MASSPROP` / bbox note; export edge as `Polyline3d` → `tests/fixtures/taluy/F-KE-BASELINE.dwg`.  
2. Filter DWG2 inserts: `Z < 100` and XY near 581k/1.477M → CSV of origin/rot/name for unit-array fixture.  
3. Keep Civil corridor rebuild **out** of R1.  
4. Update [05-api-mvp.md](05-api-mvp.md) only if SME chooses L3 unit-array in MVP (currently slope skin only).

## Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.1 | 2026-07-29 | Accore inventory + probe of both ke DWGs |
| 0.2 | 2026-07-29 | SME fork **C** locked; path C stack |
