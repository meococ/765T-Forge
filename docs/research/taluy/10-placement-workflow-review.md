# 10 — Placement workflow review (why user sees only green)

> **Role:** WorkflowReviewer lab note · **Not product** · no capability-matrix claims  
> **Date:** 2026-07-30  
> **Peer:** CadApiScout → `09-cad-api-block-giaco.md`  
> **Symptom:** After lab scripts erased old blocks and re-placed `BLOCK GIA CO`, user sees **only the green Top_Datum line strip**.

---

## 0. Executive contradiction → resolved

| Source | Observation |
|--------|-------------|
| **Accore / disk inventory** | `BLOCK GIA CO` **×112**, layer **`TALUY-GIACO`**, vis=0, scales=1, LOAI/ke **×0**, Z **3.545…4.619** |
| **Layer state (deep probe)** | `TALUY-GIACO` color **4**, **ON** — **not** Off/Frozen |
| **User UI** | Only green strip; Zoom Extents does not frame panels |
| **Lock** | `tests/taluy ke - 2.dwl` present (ADMIN); Accore often opened RO |

**Root cause (confirmed):** inserts **are on disk and on an ON layer**. Tilting via **DXF 210** (~`0.32, 0.28, 0.90`, med tilt ~26°) **corrupts drawing extents** (`EXTMIN` ≈ `-1.5e6, -9e5`, `EXTMAX` Z ≈ `713241`). **Zoom Extents** then frames garbage / green-only, so panels look “missing.”

**Recipe fix (Main in flight):** re-place with **210 = (0,0,1)** + **Z-drape only** → SAVEAS `taluy ke - 2-FIXED.dwg` → replace ke-2.

Block def is **not empty**: ~300 nested LOAI INSERTs inside `BLOCK GIA CO` (`giaco_children.csv`); each LOAI = 1× 3DSOLID.

---

## 1. On-disk state (2026-07-30)

| File | Bytes | MD5 | mtime | Role |
|------|------:|-----|-------|------|
| `tests/taluy ke - 2.dwg` | 3 678 776 | `c81052a0…` | 08:50:37 | User path — 112 tilted inserts (broken extents) |
| `tests/taluy ke - 2-FLUSH.dwg` | 3 678 776 | `c81052a0…` | 08:36:04 | Same payload |
| `tests/taluy_ke-2_FLUSHED.dwg` (+ fixtures) | 3 678 776 | `c81052a0…` | 08:41:30 | Same |
| `tests/fixtures/taluy/F-C-WORKING-giaco.dwg` | 3 678 776 | `c81052a0…` | 08:50:37 | Default SAVEAS of most SCRs — **same broken 210** |
| `tests/taluy ke - 2.bak.dwg` | 3 760 703 | `d5a2b4ba…` | 08:30:34 | Pre-lab (GIA~60 + LOAI cluster) |
| `tests/taluy ke - 2-src.dwg` | 3 760 703 | `d5a2b4ba…` | 08:36:01 | Source snapshot |

**Inventory logs (UTF-16 LE Accore):**

| Log | GIA CO | On `TALUY-GIACO` | Z | Notes |
|-----|-------:|-----------------:|---|-------|
| `taluy-giaco.log` (early, RO) | 60 | mixed | — | + LOAI1=12 LOAI2=341 LOAI3=16 ke=1; VLA nil |
| `taluy-verify` / `verify2` | 59 | 59 | 3.767…4.651 | post-tilt3 (pitch 11.65 era) |
| `taluy-now` / `ke2-counts` | **112** | **112** | **3.545…4.619** | post-flush; LOAI/ke=0 |
| `giaco_placed.csv` | 112 rows | all TALUY-GIACO | 3.545…4.619 | handles present |
| `giaco_tilted_readback.csv` | 112 | — | same | **nx,ny,nz** tilted; nz med **0.898** |

---

## 2. Workflow map (end-to-end)

```text
[gen_flush.py / gen_tilt3.py / gen_tilt2.py]
  green_mids_all.csv  → plane-fit / nearest-Z
  giaco_inserts or _stations_tmp → path + rot
        │
        ▼
 place_giaco_*.scr
  1. FILEDIA0 CMDECHO0 OSMODE0 ATTREQ0
  2. ERASE  ssget "_X" INSERT ∈ {BLOCK GIA CO, LOAI 1/2/3, ke}
  3. -LAYER _M TALUY-GIACO _C 4
  4. loop:
       -INSERT "BLOCK GIA CO" x,y,z  1 1 rot
       entmod 8=TALUY-GIACO
       entmod 210=(nx,ny,nz)     ◄── EXTENT BOMB when n ≠ world Z
  5. SAVEAS "" <path>            ◄── often F-C-WORKING, not user ke-2
  6. QUIT Y
```

| Stage | Behavior | Evidence |
|-------|----------|----------|
| **Erase** | Wipes named INSERTs only (green LINEs kept) | `TALUY_ERASED=430` every place/tilt/flush log |
| **Stationize** | Path from original GIA centers | flush pitch **6.0900** → **112**; early place pitch **11.6516** → **59** |
| **Drape** | Plane-fit green mids → Z (+ normal for tilt) | place.log `GREEN_SAMPLES=15152` `DRAPE_MISS=0`; plan nfit hundreds |
| **Insert** | `-INSERT` scale 1,1 | flush SCR: **112** `_.-INSERT` |
| **Normal** | `entmod` DXF **210** | flush/tilt3: nz≈0.88–0.99 — **breaks extents** |
| **SAVEAS** | Path varies | §3 |

Green support: `SOLIDS - Corridor - (3) - Top_Datum` LINE mesh ~15 152 (see `08-support-solid-vs-linemesh.md`). Untouched by erase → always visible → “only green” when panels are off-camera due to extents.

---

## 3. Timeline of lab runs

| # | Log | Family | Erased | Placed | SAVEAS (from SCR/log) | Notes |
|---|-----|--------|-------:|-------:|-----------------------|-------|
| 0 | `taluy-giaco.log` | probe | — | — | — | ke-2 locked RO; baseline 60+LOAI |
| 1 | `taluy-place.log` | place | 430 | 59 | `…/F-C-WORKING-giaco.dwg` | pitch 11.65; **no 210** |
| 2 | `taluy-place2.log` | place2 | 430 | 59 | F-C-WORKING | no 210 |
| 3 | `taluy-tilt.log` | tilt | 430 | 59 | F-C-WORKING | `Unknown command "N"` noise |
| 4 | `taluy-tilt2.log` | tilt2 | 430 | 59 | F-C-WORKING | rigid tilt; `TILTED=59` |
| 5 | `taluy-tilt3.log` | tilt3 | 430 | 59 | F-C-WORKING | **210 entmod** |
| 6 | `taluy-flush.log` | **flush** | 430 | **112** | **F-C-WORKING** | pitch 6.09 + **210** — “working” lab claim |
| 7 | `taluy-flush-ke2.log` | flush_ke2 | 430 | 112 | **`taluy ke - 2.dwg`** | opened **RO (locked)** then SAVEAS ke-2 |
| 8 | `taluy-flush-ke2b.log` | flush | 430 | 112 | **`taluy ke - 2-FLUSH.dwg`** | secondary path; same 210 payload |
| 9 | `taluy-now` / counts | verify | — | n/a | — | 112 confirmed on disk |

**Generator SAVEAS (bug):** `gen_flush.py` L207 / `place_giaco_flush.scr` L353 → always  
`C:/Users/ADMIN/Downloads/05. 765T-Forge/tests/fixtures/taluy/F-C-WORKING-giaco.dwg`  
unless a hand-edited ke2 SCR retargets it.

**210 write pattern** (`gen_flush.py` / SCR):

```lisp
(setq ed (subst (cons 210 (list nx ny nz)) (assoc 210 ed) ed))  ; or append
(entmod ed) (entupd e)
```

with `nx,ny,nz` from plane fit (example first station: `0.1455, 0.1449, 0.9787`; mid-corridor often `~0.32, 0.28, 0.90`).

---

## 4. Failure-mode matrix vs user report

| # | Mode | Verdict | Evidence |
|---|------|---------|----------|
| **A** | **OCS 210 → broken extents / Zoom Extents misses panels** | **PRIMARY — CONFIRMED** | Deep probe: EXTMIN ~`-1.5e6,-9e5`, EXTMAX Z~`713241`; 210 tilted; layer ON; 112 inserts exist |
| **B** | Wrong SAVEAS path (F-C-WORKING vs ke-2) | **CONFIRMED secondary** | Default SCR SAVEAS → fixtures WORKING; user opens ke-2; compounded confusion |
| **C** | File lock / stale GUI buffer | **CONFIRMED secondary** | `.dwl` + RO opens; GUI does not auto-reload disk |
| **D** | Layer Off / Frozen / wrong color | **RULED OUT** | Deep probe: TALUY-GIACO color 4 **ON** |
| **E** | vis / scale wrong | **RULED OUT** | vis=0, scales=1 |
| **F** | XYZ off corridor | **RULED OUT** | XY 581075–581520 / 1476676–1477185; Z on green band |
| **G** | Empty block def / WBLOCK mishap | **RULED OUT** | 300 nested LOAI; solids in LOAI defs (CadApiScout) |
| **H** | Erase-only, place failed | **RULED OUT on disk** | `TALUY_PLACED=112` + verify |
| **I** | Nested LOAI layer off | **UNLIKELY primary** | Would hide solids but not explode EXTMIN to 1e6; check only if FIXED still blank |

---

## 5. Ranked root causes

### R1 — DXF 210 tilt corrupts extents (highest)

- Lab “drape normal” wrote non-world extrusion on each INSERT.
- AutoCAD OCS + nested 3DSOLID bbox math yields **absurd drawing extents**.
- User (and default) **Zoom Extents** → camera/frame on green mesh only; panels exist but **off-view**.
- Matches: inventory 112 + layer ON + green-only screenshot + EXTMIN/EXTMAX probe.

**Fix direction:** keep **210 = (0,0,1)**; apply slope later via true 3D transform if needed (not raw 210 on panel block), or accept plan-horizontal panels with **Z-only drape**.

### R2 — SAVEAS / path / lock skew

- Scripts celebrated success on **F-C-WORKING** while user stared at **ke-2**.
- Writes under **read-only** open (`flush-ke2.log`).
- Even after MD5-equal copies, **open editor stays stale**.

### R3 — Process gaps

| Gap | Detail |
|-----|--------|
| No extent gate | Never checked `EXTMIN`/`EXTMAX` after 210 entmod |
| Destructive erase every run | 430 deletes; green remains → “only green” if view broken |
| Pitch churn | 11.65→59 vs 6.09→112 without user-facing path labels |
| No post-SAVEAS verify on **user** path | verify often hit WORKING, not what GUI had open |

### R4 — Not root

- Failed INSERT count  
- Empty GIA CO def  
- Layer off  
- Green LINE mesh “is” the panels (it is support only; panels are separate INSERTs)

---

## 6. Concrete fix steps for Main

### 6.1 In-flight fix (align with Main plan)

```text
1. CLOSE GUI on ke-2* (discard stale buffer if unsure). Remove .dwl/.dwl2 if orphaned.
2. Regenerate SCR from flush plan BUT force 210 = (0,0,1) — strip plane-normal entmod
   (or gen_flush.py: write (0 0 1) only; keep draped Z + rot).
3. Accore:
     /i <unlocked work copy of bak or src or current>
     /s place_giaco_flush_worldZ.scr
   SAVEAS → tests/taluy ke - 2-FIXED.dwg
4. Verify SCR on FIXED:
     GIA=112, layer TALUY-GIACO, Z~3.5–4.6
     EXTMIN/EXTMAX finite and near corridor (~5.81e5, 1.477e6, z~0–10)
5. copy FIXED → tests/taluy ke - 2.dwg (and optional F-C-WORKING refresh)
6. User OPEN FIXED/ke-2 → ZOOM Extents → panels + green both in frame
```

### 6.2 Exact code/SCR changes

| # | Change | Where |
|---|--------|--------|
| 1 | **Stop writing tilted 210** | `gen_flush.py` L195–200; `gen_tilt3.py` L144–150; `gen_tilt2.py` equivalent; all `place_giaco_flush*.scr` / `place_giaco_tilt*.scr` entmod blocks |
| 2 | Emit only | `(setq ed (subst (cons 210 (list 0.0 0.0 1.0)) …))` **or omit 210** (default world) |
| 3 | Keep | draped **Z**, **rot** (DXF 50), layer TALUY-GIACO, scale 1 |
| 4 | SAVEAS | `tests/taluy ke - 2-FIXED.dwg` (not silent overwrite of open ke-2) |
| 5 | Post-check LISP | print `EXTMIN`/`EXTMAX` via `(getvar "EXTMIN")` `(getvar "EXTMAX")`; fail if abs(x)>1e7 or abs(z)>1e5 |
| 6 | Lock preflight | abort if `taluy ke - 2.dwl` exists |
| 7 | Optional | `(command "_.ZOOM" "_E")` then SAVEAS so saved view is sane |

### 6.3 Suggested verify snippet (append to verify SCR)

```lisp
(princ (strcat "\nTALUY_EXTMIN=" (vl-princ-to-string (getvar "EXTMIN"))))
(princ (strcat "\nTALUY_EXTMAX=" (vl-princ-to-string (getvar "EXTMAX"))))
(setq ss (ssget "_X" '((0 . "INSERT")(2 . "BLOCK GIA CO"))) i 0 bad210 0)
(if ss (repeat (sslength ss)
  (setq n (cdr (assoc 210 (entget (ssname ss i)))))
  (if (and n (> (abs (- (caddr n) 1.0)) 1e-6)) (setq bad210 (1+ bad210)))
  (setq i (1+ i))))
(princ (strcat "\nTALUY_BAD210=" (itoa bad210)))
```

Pass: `BAD210=0`, extents near corridor.

### 6.4 Do not

- Re-apply plane-fit **210** on nested solid panels without a proven OCS recipe (CadApiScout `09`).  
- Erase+place while GUI holds ke-2.  
- Claim Forge MCP taluy tools did this — lab SCR/Python only.  
- Restore `.bak` over FIXED without re-verify (bak is pre-112 family).

---

## 7. Acceptance checks

| ID | Check | Pass |
|----|-------|------|
| A1 | Insert count | `BLOCK GIA CO=112`, LOAI*=0, ke=0 |
| A2 | Layer | all on `TALUY-GIACO`, layer ON |
| A3 | Z drape | Z ∈ ~3.5…4.7 |
| A4 | **210 world** | every insert 210 ≈ (0,0,1); `BAD210=0` |
| A5 | **Extents** | EXTMIN/EXTMAX within ~corridor ±1e3 plan, Z within ~±100 of green |
| A6 | Zoom Extents GUI | green strip **and** panels visible in one frame |
| A7 | Path | deliverable = `*-FIXED.dwg` / refreshed ke-2; MD5 documented |
| A8 | Green preserved | Top_Datum LINE count still ~15k |
| A9 | Lock | no write under `.dwl` |

**Status before FIXED regen:** A1–A3 pass on disk; **A4–A6 FAIL** (tilted 210 + bad extents + green-only Zoom E).  
**Status after Main FIXED (expected):** A1–A9 pass.

---

## 8. Root causes (summary)

1. **Primary:** `entmod` of **tilted DXF 210** on `BLOCK GIA CO` inserts destroyed **drawing extents**; Zoom Extents → green-only UI despite 112 valid inserts on ON layer.  
2. **Secondary:** SAVEAS defaulted to **F-C-WORKING**; user/GUI on **ke-2** with lock → path/session confusion.  
3. **Secondary:** no post-condition on EXTMIN/EXTMAX or 210.  
4. **Not root:** missing inserts, empty def, layer off, undraped Z, green erased.

---

## 9. Fix checklist for Main (short)

1. Regen place SCR: **210=(0,0,1)**, keep Z drape + rot + TALUY-GIACO.  
2. SAVEAS `tests/taluy ke - 2-FIXED.dwg`; verify counts + extents + BAD210=0.  
3. Replace ke-2 from FIXED after GUI closed; delete locks.  
4. User: OPEN → ZOOM E → confirm panels on green.  
5. Patch `gen_flush.py` / tilt gens so 210 tilt cannot ship again without extent gate.  
6. Defer true slope alignment to a method that does not nuke extents (see CadApiScout `09`).

---

## 10. Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.1 | 2026-07-30 | Initial workflow map + path/lock hypothesis |
| 0.2 | 2026-07-30 | **Primary = 210/extents** (Main deep probe + CadApiScout); layer-off ruled out; FIXED recipe |
