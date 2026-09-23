# 11 — Team integration fix (Main coordinator)

> 2026-07-30 · CadApiScout + WorkflowReviewer + Main

## User symptom
After “place taluy”, drawing shows **only green LINE strip** — blocks seem deleted, not replaced.

## Disk truth (Accore)
| Item | Value |
|------|--------|
| `BLOCK GIA CO` | **112** on `TALUY-GIACO` |
| LOAI/ke | 0 |
| Z | 3.545…4.619 |
| Layer | ON, color 2/4 |
| **Broken file** `taluy ke - 2.dwg` (pre-fix) | DXF **210 tilted** → EXTMIN≈-1.5e6, EXTMAX.Z≈713k |
| **Fixed file** `taluy ke - 2-FIXED.dwg` | 210=**(0,0,1)**, EXTMIN/MAX corridor-sane |

## Root cause (both agents agree)
1. **Primary:** `entmod` slope normal into DXF 210 corrupts OCS/extents → Zoom Extents hides panels.  
2. **Secondary:** SAVEAS often wrote fixture paths; GUI lock prevented overwrite of `taluy ke - 2.dwg`.  
3. **Pitch:** 11.65 m left gaps; correct flush pitch **6.09 m**.

## Ban list (Accore lab)
- ROTATE3D tilt after insert  
- ALIGN scripted  
- DXF 210 = slope normal  

## Approved recipe
```
erase old INSERTs (GIA CO / LOAI / ke)
stations @ pitch 6.09 m along path
Z = plane-fit / nearest green LINE mids
-INSERT BLOCK GIA CO scale 1 rot from path
entmod layer TALUY-GIACO + 210 = (0,0,1) only
SAVEAS new file → replace ke-2 when unlocked
verify: count=112, all 210 world, EXT* sane
```

## Deliverables for user
| File | Role |
|------|------|
| **`tests/taluy ke - 2-FIXED.dwg`** | Correct placement — **OPEN THIS** |
| **`tests/taluy_ke-2_OPEN_ME.dwg`** | Same bytes, easy name |
| `tests/taluy ke - 2.bak.dwg` | Original backup |
| `docs/research/taluy/09-cad-api-block-giaco.md` | API/block research |
| `docs/research/taluy/10-placement-workflow-review.md` | Workflow postmortem |

## When ke-2 unlocked
```
copy /Y "tests\taluy ke - 2-FIXED.dwg" "tests\taluy ke - 2.dwg"
```
