# Taluy R&D pack — rải mái dốc / kè trên AutoCAD 3D

> **Side research track** for 765T-Forge. **Not shipped.**  
> Does **not** change product SoT: [capability-matrix.md](../../capability-matrix.md) · [roadmap.md](../../roadmap.md)  
> Civil 3D + geometry megakit remain **out of product scope**.

## One-line pitch

Agent-driven **cut/fill skins + kè unit arrays** on vanilla AutoCAD 2026, **units must drape on the green corridor solid** (`SOLIDS - Corridor - … Top_…`) — SME locked. Flag: `FORGE_ENABLE_TALUY_RD`. Publish/plot untouched.

## Virtual squad

| Role | Focus |
|------|--------|
| **Domain Lead** | Charter, gates, go/kill |
| **Geometry Engine** | Station frames, daylight, `SubDMesh` / `Face`, unit array |
| **Forge Integration** | `forge_taluy_*` triad, pipe DTOs, feature flag |
| **QA / Safety** | dryRun, backup, audit, fixtures ε |
| **Metro SME** | Vietnamese metro practice, acceptance |

## Document map

| Doc | Contents |
|-----|----------|
| [00-charter.md](00-charter.md) | Mission, squad, sprint, DoD — **fork C in MVP definition** |
| [01-autocad-primitives.md](01-autocad-primitives.md) | Vanilla entities · skin: Polyline3d + SubDMesh · units: INSERT 3DSOLID blocks |
| [02-domain-model.md](02-domain-model.md) | VI/EN glossary, JSON I/O, sampling, determinism |
| [03-forge-extension.md](03-forge-extension.md) | Tool triad, flag, layout B, **place_units** |
| [04-civil-vs-vanilla.md](04-civil-vs-vanilla.md) | R0–R1 vanilla only · R2 optional Civil |
| [05-api-mvp.md](05-api-mvp.md) | **Path C API** — skin + place_units |
| [06-fixture-ke-analysis.md](06-fixture-ke-analysis.md) | `tests/taluy ke - 1/2.dwg` probe · LOAI pitch ~0.4 m |
| [07-path-c-plan.md](07-path-c-plan.md) | Dual-track R1 plan, fixtures, spike prereqs |
| [08-support-solid-vs-linemesh.md](08-support-solid-vs-linemesh.md) | Green = BODY/solid **or** LINE mesh — drape both |

## Binding decisions

1. **Fork C** — both **C-L1** (H:V skin) and **C-L3** (LOAI unit array).  
2. **Host:** full AutoCAD 2026 — no Civil load dependency for R1.  
3. **Skin geometry:** ruled panels → `SubDMesh` (smooth 0); `Face` fallback; crest/toe `Polyline3d`.  
4. **Units:** `INSERT` `LOAI *`; pitch **0.4 m flush (sát)**; rot = tangent; optional mirror.  
5. **Support (bắt buộc):** drape lên mặt xanh — **`solidBody` (DWG1 BODY/3DSOLID) hoặc `lineMesh` (DWG2 ~15k LINE)**. Xem [08](08-support-solid-vs-linemesh.md).  
6. **Daylight MVP (skin):** prefer intersect/drape to same support solid; fallback fixed Z only if no solid.  
7. **Tools:** + `forge_taluy_bind_surface` (resolve solid handle/layer) before place/generate.  
8. **Product boundary:** no matrix/roadmap claims until post-G4 + smoke.

## Lab fixtures

| File | Finding |
|------|---------|
| `tests/taluy ke - 1.dwg` | **solidBody**: BODY/3DSOLID Top_Datum + Curb |
| `tests/taluy ke - 2.dwg` | **lineMesh**: Top_Datum ≈15k LINE green + LOAI @ Z often 0 |
| `tests/fixtures/taluy/` | Lab pack: COMBO, UNIT, LINEMESH, CSVs — see folder README |

## Sprint gates

```text
R0 Framing (docs)     ← 00–07; fork C locked
  G1 vocabulary + path C accepted
R1a Geometry kernel   → G2 skin fixtures + unit array demo
R1b Forge-shaped API  → G3 safety sign-off
R1 close              → G4 Go | Conditional | Kill
R2 (optional)         → Civil adapter, separate flag only
```

## Agent hot path (future)

```text
forge_system_health
→ forge_taluy_validate
→ forge_taluy_preview                 # C-L1 plan
→ forge_taluy_generate (dryRun=true)
→ forge_taluy_generate
→ forge_taluy_place_units (dryRun=true)  # C-L3
→ forge_taluy_place_units
→ forge_taluy_list / readback
```

## Explicit non-claims

- Forge **cannot** rải taluy/kè in any released build today.  
- Research only — not GA.  
- Publish/plot remains the product moat.

## Next actions

| When | Action |
|------|--------|
| **Now** | Export baseline from DWG1 edge; WBLOCK `LOAI 2` into clean fixture |
| R1a | Prototype skin kernel + unit array (lab script or gated plugin) |
| Product spike | `src/Forge.Plugin/Taluy/*` + triad behind `FORGE_ENABLE_TALUY_RD` |
| G4 | Go / Conditional / Kill memo |

## Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.1 | 2026-07-29 | R0 pack |
| 0.2 | 2026-07-29 | Fixture 06 + SME options A/B/C |
| 0.3 | 2026-07-29 | **Fork C locked** |
