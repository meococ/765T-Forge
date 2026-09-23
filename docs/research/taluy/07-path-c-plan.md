# 07 — Path C plan (skin + unit array + drape)

> **Status:** binding for R1 after SME locks (2026-07-29)  
> **Not shipped** · no `src/` until product opens spike  
> **Parents:** [00-charter.md](00-charter.md) · [05-api-mvp.md](05-api-mvp.md) · [06-fixture-ke-analysis.md](06-fixture-ke-analysis.md)

## SME locks

| Lock | Value |
|------|--------|
| Fork | **C** — skin + units |
| Pitch | **0.4 m flush (sát)** — not air gap |
| Support | Drape on green: **`solidBody` or `lineMesh`** ([08](08-support-solid-vs-linemesh.md)) |
## 1. Dual track + support

| Track | ID | Goal | Primary tool |
|-------|-----|------|----------------|
| Support | **C-S0** | Resolve green Top solid → handle | `forge_taluy_bind_surface` |
| Slope skin | **C-L1** | H:V faces; prefer same support | `forge_taluy_generate` |
| Kè units | **C-L3** | LOAI array **flush + draped** on green solid | `forge_taluy_place_units` |

```text
         green solid (Top_Datum…)     baseline (plan path)
                    \                   /
                     \                 /
                      v               v
              bind_surface      sample stations
                      \               /
                       \             /
                        v           v
              C-L3: INSERT LOAI at drape hit (sát 0.4 m)
              C-L1: skin rays / mesh (optional same support)
```

**Rule:** plan follow baseline; **contact Z = hit on green face**. Baseline-Z-only = **fail acceptance**.

## 2. R1 sequence

| Step | Track | Deliverable | Gate |
|------|-------|-------------|------|
| 1 | Shared | Fixture = baseline + LOAI def + **green Top solid** (DWG1) | — |
| 2 | C-S0 | `bind_surface` by layer pattern / handle | — |
| 3 | C-L3 | Place flush 0.4 m with **drape hits**; measure gap-to-surface | visual win |
| 4 | C-L1 | Skin; contact vs same solid when possible | G2 |
| 5 | Shared | dryRun + preview honesty | G2 |
| 6 | Shared | Forge triad + safety | G3 |
| 7 | SME | Units **touch** green face (no float/sink) | G4 |

## 3. Fixture pack

| Id | Content | Source |
|----|---------|--------|
| `F-C-GOLDEN-SOLID.dwg` | Green Top + Curb solids | `taluy ke - 1.dwg` |
| `F-C-UNIT.dwg` | Block `LOAI 2` only | WBLOCK DWG2 |
| `F-C-BASE.dwg` | Edge polyline in plan of solid | Digitize DWG1 |
| `F-C-COMBO.dwg` | solid + baseline + LOAI def | Merge above |

## 4. Acceptance

### C-S0 support

| Check | Pass |
|-------|------|
| Find solid | layer `SOLIDS - Corridor - (3) - Top*` or explicit handle |
| Ambiguous | `taluy_support_ambiguous` if >1 without handle |

### C-L3 units

| Check | Pass |
|-------|------|
| Pitch | 0.4 m flush |
| **Drape** | every insert within ε of solid (default **5 mm**); miss → `taluy_drape_miss` |
| Spacing | mean error ≤ 5 mm straight |
| Count | `floor(L/step)+1` |
| Rotation | tangent ≤ 0.1° |
| Mirror | `sx=-1` when set |
| replaceGroup | old inserts gone |

### C-L1 skin

| Check | Pass |
|-------|------|
| Determinism | M1 ε |
| dryRun | no entity delta |
| Prefer support | document if solid used vs plane fallback |

## 5. Spike prerequisites

1. Product ack research branch (not matrix implemented).  
2. `FORGE_ENABLE_TALUY_RD=true` lab only.  
3. Fixtures F-C-* on disk.  
4. Triad: MCP + ToolMetadata + `Plugin/Taluy/*`.  
5. Safety: no OpenWorld; scoped erase.  
6. Pitch 0.4 m — **done**.  
7. Drape-on-green — **done (requirement)**; engine still to build.

## 6. Non-goals (still)

- Civil corridor rebuild  
- Full survey EG TIN as primary (green solid is the support SoT for this project)  
- Boolean holes in LOAI  
- `BLOCK GIA CO` / full 2D `ke` package  
- Survey volumes  

## 7. Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.1 | 2026-07-29 | Path C after fork lock |
| 0.2 | 2026-07-29 | Pitch 0.4 m |
| 0.3 | 2026-07-29 | **Drape on green Top solid required** |
