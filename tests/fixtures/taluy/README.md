# Taluy Path C lab fixtures

## Working result (2026-07-29)

| File | Result |
|------|--------|
| **`F-C-WORKING-giaco.dwg`** | **112×** `BLOCK GIA CO` · `TALUY-GIACO` · pitch **6.09 m flush (sát)** · OCS tilt ~26° to green slope |
| `giaco_flush_plan.csv` / `giaco_tilted_readback.csv` | Plan + read-back |
| `gen_flush.py` + `place_giaco_flush.scr` | Regen pipeline |

**Unit:** `BLOCK GIA CO` panel. Old inserts erased (430).  
**Pitch:** was 11.65 m (hở); now **6.09 m** = panel size along path (sát).  
**Drape:** plane-fit green LINE mesh → extrusion normal.

> R&D only · not product capability · built 2026-07-29

## Support surface — important correction

| Drawing | What the “green” really is |
|---------|----------------------------|
| **`taluy ke - 1` / `F-C-GOLDEN-SOLID`** | Real **`BODY` / `3DSOLID`** on `SOLIDS - Corridor - (3) - Top_Datum` (+ Curb) |
| **`taluy ke - 2` / `F-C-GREEN-LINEMESH`** | Mostly **`LINE` mesh** (15 152 lines, color 3) on same layer name + 1 BODY + 3 REGION |

Screenshot of green “strip with yellow diamonds” on DWG2 = **line tessellation**, not a NURBS/TIN surface.  
Engine must support **two support kinds**:

1. `solidBody` — `3DSOLID` / `BODY` (DWG1)  
2. `lineMesh` — dense `LINE` set on Top_Datum (DWG2) → build/query as triangle or segment soup for drape

## Files

| File | Role |
|------|------|
| `F-C-GOLDEN-SOLID.dwg` | Copy of ke-1: solid/BODY support golden |
| `F-C-GREEN-LINEMESH.dwg` | Slimmed ke-2: ≤4000 Top_Datum lines + clean LOAI inserts + solidish leftovers |
| `F-C-UNIT-LOAI2.dwg` | WBLOCK: single `3DSOLID` unit (`LOAI 2` geometry) |
| `F-C-COMBO.dwg` | Golden solid + `TALUY-BASELINE` 3D poly (80 verts from line mids) + block def `LOAI 2` |
| `loai_clean_inserts.csv` | 346 LOAI inserts (sane XY/Z filter) from ke-2 |
| `green_lines_sample.csv` | 2000 sample green lines (endpoints + len) |
| `baseline_chain.csv` | 80-station chain used for COMBO baseline |

## Measured facts

- Green line Z sample ≈ **2.11 … 4.57 m** (full ke-2 lines **0.004 … 4.65 m**)  
- Clean LOAI inserts often sit at **Z = 0** in ke-2 → **not yet draped** (float below green) — matches need for drape engine  
- Insert pitch ~**0.35–0.40 m** on local clusters; default flush **0.4 m**  
- Unit DWG inventory: **1× 3DSOLID**, no extra junk  

## How to use

1. Open `F-C-COMBO.dwg` for solid-support spike.  
2. Open `F-C-GREEN-LINEMESH.dwg` for line-mesh drape spike.  
3. Block `LOAI 2` available in COMBO after rename from unit file.  
4. Baseline layer: `TALUY-BASELINE`.

## Regen notes

Scripts (repo `tests/`):

- `taluy-probe-green.scr` — classify support  
- `taluy-wblock-loai.scr` — unit extract  
- `taluy-export-csv.scr` — CSV dumps  
- `taluy-export-green-mesh.scr` — slim mesh DWG  
- `fixtures/taluy/make_combo.scr` — rebuild COMBO from golden + chain CSV  
