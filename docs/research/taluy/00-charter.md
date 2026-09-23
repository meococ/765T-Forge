# Taluy R&D Charter

> **Status:** Side R&D track — **not** product scope.  
> **Location:** `docs/research/taluy/` only.  
> **Product SoT unchanged:** Civil 3D and geometry megakit remain **out of scope** in [capability-matrix.md](../../capability-matrix.md) and [roadmap.md](../../roadmap.md).  
> **Stack:** Forge.Server (MCP) → named pipe → Forge.Plugin (AutoCAD 2026 .NET 8) → ObjectARX / Autodesk.AutoCAD.*. Contracts in Forge.Shared.  
> **Safety:** Dual `SafetyPolicy`, dry-run, backup, audit — any future tools must follow `forge_<group>_<action>` + ToolMetadata + MCP + plugin switch triad.

---

## 1. Mission

Stand up an **evidence-backed research pack** for **rải taluy** (cut/fill embankment and slope daylighting) that can be driven by AI agents through 765T-Forge on **full AutoCAD 2026** (vanilla 3D primitives), without claiming shipped product capability.

**Mission outcomes (research):**

1. Shared vocabulary and I/O contracts for baseline → slope → daylight → drawable output.
2. A **vanilla AutoCAD** geometry path that is deterministic enough for Vietnamese metro AEC pilot sketches (platform edges, temporary cut/fill envelopes, coordination solids/meshes).
3. An honest Civil 3D vs vanilla decision with phase gates and kill criteria.
4. A Forge extension sketch (tool names, safety class, dry-run shape) that could be implemented later **without** expanding product GA claims until a separate product decision.

**MVP definition (R&D spike, not GA):** (1) slope extrusion from baseline + params, (2) array of unit kè solids (`LOAI *`) **flush @ 0.4 m** and **draped on green corridor solid** (`SOLIDS - Corridor - (3) - Top_*`) — SME 2026-07-29. **No** Civil corridor rebuild.

---

## 2. Non-mission

Explicitly **out** of this R&D charter (and still out of product scope unless product leadership reopens roadmap):

| Non-mission | Why |
|-------------|-----|
| Shipping Civil 3D tools in Forge GA | Product matrix/roadmap exclude Civil |
| Geometry megakit / full generative design | Roadmap “do not build yet” |
| National metro ontology or BIM Level-of-Info standard | Separate program |
| Clash 3D, SSM write, remote multi-tenant MCP | Product non-claims |
| Replacing Civil grading workflows on production seats | Licensing + domain depth |
| Guaranteeing survey-grade earthwork quantities | R&D accuracy gates may kill this |
| Editing `src/` as part of this pack | Research docs only until a product spike is approved |
| Claiming “Forge does taluy” in capability-matrix or marketing | Forbidden until product wave + smoke evidence |

---

## 3. Shared vocabulary (binding for this folder)

| Term | Meaning |
|------|---------|
| **Baseline** | 3D polyline or feature-like chain (ordered points with Z) — e.g. platform edge, toe line, crown |
| **Daylight target** | Elevation plane, offset surface sample, or explicit catch points where slope meets existing ground |
| **Slope** | Ratio H:V or percent; **cut** and **fill** may differ; side (left/right/both) relative to baseline direction |
| **Output** | 3D faces / mesh / solid / 3D polylines + layers + optional section markers — vanilla-first |
| **Taluy** | Mái dốc đắp/đào (fill/cut slope) along alignment or platform edges in Vietnamese metro AEC practice |

Domain detail: [02-domain-model.md](02-domain-model.md).

---

## 4. Success metrics

Metrics apply to the **R&D pack and lab spikes**, not product GA.

| ID | Metric | Target (R&D MVP) | Evidence |
|----|--------|------------------|----------|
| M1 | Deterministic geometry | Same inputs → same vertex set within ε (lab default ε ≤ 1 mm plan, ≤ 5 mm Z unless SME tightens) | Scripted fixture DWG + hash/compare of handles/coords |
| M2 | Vanilla-only path | End-to-end slope solid/mesh **without** Civil assemblies loaded | Lab seat: full AutoCAD 2026, Civil not required |
| M3 | Agent-safe shape | Proposed tools nameable as `forge_taluy_*`, dry-run + backup class defined | [03-forge-extension.md](03-forge-extension.md) |
| M4 | Metro SME acceptance | ≥1 platform-edge and ≥1 cut daylight scenario “useful for coordination” (not final earthworks bid) | SME sign-off note in lab log |
| M5 | Honesty | Zero language that implies product shipment of Civil/taluy | Review of this folder + no matrix edits |
| M6 | Decision clarity | Civil vs vanilla gates + kill criteria written and socialized | [04-civil-vs-vanilla.md](04-civil-vs-vanilla.md) |
| M7 | Timebox | 4-week sprint produces Go / Conditional / Kill recommendation | Sprint close memo (see §7) |

**Non-metrics (do not chase in R0–R1):** corridor targeting, quantity takeoff certification, IFC export, surface rebuild from LiDAR at survey LoD.

---

## 5. Virtual squad roles

R&D is staffed as a **virtual squad** (people may wear multiple hats).

| Role | Owns | Does not own |
|------|------|--------------|
| **Domain Lead** | Charter, phase gates, vocabulary, kill/go calls, liaison to product | Shipping code without product approval |
| **Geometry Engine** | Algorithms: offset, slope rays, daylight intersection, mesh/solid construction, numerical tolerances | MCP surface area, Civil product packaging |
| **Forge Integration** | Tool triad sketch, SafetyPolicy class, pipe DTOs, naming `forge_taluy_*`, dual-eval implications | Changing core publish/plot stack |
| **QA / Safety** | Dry-run contracts, backup triggers, audit fields, fixture protocol, ε and regression harness design | Domain slope standards |
| **Metro SME** | Vietnamese metro practice (đào/đắp, typical H:V, platform edges, temporary vs permanent taluy), acceptance of “good enough” | AutoCAD API design |

**RACI (research artifacts):**

| Artifact | Domain Lead | Geometry | Forge Int. | QA/Safety | Metro SME |
|----------|:-----------:|:--------:|:----------:|:---------:|:---------:|
| 00-charter | **A/R** | C | C | C | C |
| 01-primitives | C | **R** | C | C | C |
| 02-domain-model | **A** | **R** | C | C | **R** |
| 03-forge-extension | C | C | **A/R** | **R** | I |
| 04-civil-vs-vanilla | **A/R** | C | C | C | **R** |
| 05-api-mvp (planned) | **A** | R | **R** | R | C |
| Lab fixtures / ε | A | R | C | **R** | C |

R = responsible, A = accountable, C = consulted, I = informed.

---

## 6. Phase gates (research)

Aligned with [04-civil-vs-vanilla.md](04-civil-vs-vanilla.md):

| Phase | Name | Allowed stack | Exit criteria |
|-------|------|---------------|---------------|
| **R0** | Framing | Docs only | Charter + primitives + domain + Civil decision drafted |
| **R1** | Vanilla spike | Full AutoCAD 2026 + Forge research hooks (no product matrix change) | Path **C**: skin mesh/solid **and** LOAI unit array on fixtures; SME “coordination useful” |
| **R2** | Optional Civil adapter | **Only if** R1 passes **and** product allows experiment flag | Civil path behind **separate** capability/env flag; never default; no GA claim |
| **R-Kill** | Stop or pivot | — | See kill criteria in §8 and doc 04 |

**Hard rule:** R0–R1 are **vanilla only**. Civil API must not be a dependency of the R1 spike.

---

## 7. Four-week research sprint

Assume one continuous lab seat (AutoCAD 2026 full) and async SME review.

### Week 1 — Frame & contracts (R0)

**Outcomes:**

- [x] Charter (this doc), Civil vs vanilla decision (04)
- [x] Primitives catalog (01), domain model + JSON-ish I/O (02)
- [x] Forge extension sketch outline (03) — tool names, risk class
- [x] Fixture list: ke DWGs probed ([06-fixture-ke-analysis.md](06-fixture-ke-analysis.md)); synthetic T01–T10 still open
- [x] ε and coordinate system assumptions: meters (`INSUNITS=6`); lab ε ≤ 1 mm plan / ≤ 5 mm Z (M1); ke unit pitch ~0.4 m
- [x] **SME fork C** locked: skin H:V + array LOAI units ([05-api-mvp.md](05-api-mvp.md) path C)

**Gate G1:** Domain Lead + SME agree vocabulary and non-mission; no `src/` edits required to proceed.

### Week 2 — Vanilla geometry kernel (R1a)

**Outcomes:**

- [ ] Offline or in-CAD prototype: offset rays / H:V extrusion along baseline segments (**path C-L1**)
- [ ] Cut vs fill rule (Z of daylight vs baseline)
- [ ] Daylight-to-plane (constant elevation) working
- [ ] Output: 3D polyline catch line + `SubDMesh` or `Solid3d` loft/extrude path documented
- [ ] **Unit array prototype:** INSERT `LOAI 2` (or fixture block) along baseline @ pitch 0.4 m, tangent rot, optional mirror (**path C-L3**)
- [ ] Failure modes listed (vertical segments, hairpin, zero-length, slope flip, missing block def)

**Gate G2:** Geometry Engine demo on fixtures; QA records vertex compare method.

### Week 3 — Forge-shaped spike & safety (R1b)

**Outcomes:**

- [ ] Map prototype to proposed `forge_taluy_*` operations (preview vs commit)
- [ ] Dry-run payload shape (would-create entity counts, layer plan, bbox)
- [ ] Backup/audit expectations for commit path
- [ ] Layer/standards proposal (`TALUY-CUT`, `TALUY-FILL`, `TALUY-CATCH`, section markers)
- [ ] Optional: throwaway plugin branch **only if** product Lead pre-approves — still **not** matrix “implemented”

**Gate G3:** Forge Integration + QA/Safety sign that the design does not bypass dual safety evaluation or the capability gate (typed tools only; no free-text executors).

### Week 4 — SME acceptance, metrics, go/kill (R1 close)

**Outcomes:**

- [ ] Metro SME review of 2 scenarios (platform edge fill skin; LOAI unit array on ke edge)
- [ ] M1–M6 scored (+ unit spacing/count check vs DWG2 stats)
- [ ] [05-api-mvp.md](05-api-mvp.md) path C tools frozen enough for spike
- [ ] Sprint close: **Go** / **Conditional** / **Kill**
- [ ] If Conditional/Go and Civil interest remains: R2 charter addendum only — still behind flag

**Gate G4:** Domain Lead publishes close memo under `docs/research/taluy/` (e.g. `06-sprint-close.md` when written).

---

## 8. Definition of Done — R&D MVP spike

The R&D MVP spike is **done** when **all** of the following hold. This is **not** product GA.

1. **Docs complete:** `00`–`06` exist; `05-api-mvp.md` covers **path C** (skin + `place_units`).
2. **Vanilla path proven:** (L1) baseline + slope + daylight plane → catch + mesh/solid; **and** (L3) baseline + block name + pitch → INSERT array — both without Civil API.
3. **Determinism:** Fixture rerun meets M1 ε; unit count/spacing within tol of pitch policy; methods documented.
4. **Agent contract sketched:** preview + commit for skin **and** place_units, with dry-run and safety class.
5. **SME note:** Written acceptance or explicit rejection with reasons (accuracy, layering, editability).
6. **Honesty:** No capability-matrix/roadmap edits claiming taluy/Civil; research folder states side-track status on every major doc.
7. **Decision recorded:** Go / Conditional / Kill with reference to kill criteria below.
8. **No orphan code requirement:** Prefer docs + lab scripts; if code was spiked, it lives on an explicit research branch and is not described as shipped.

### Kill criteria (vanilla cannot meet metro need)

Escalate to **Kill** or hard pivot if any of these are true after Week 4 (or earlier if obvious):

| ID | Criterion |
|----|-----------|
| K1 | Catch line / slope face error vs SME reference **systematically** exceeds coordination tolerance (default: >50 mm plan or >100 mm Z on pilot lengths ≤ 50 m) with no stable mitigation |
| K2 | Non-plane daylight (real EG surface) is mandatory for the only valuable use case, and vanilla sampling cannot be specified without a surface engine Forge will not own |
| K3 | Edit loop (tweak slope / re-daylight) is unusable for agents (non-associative explode-only) **and** SME rejects one-shot regenerate-from-params |
| K4 | Performance: baseline ≤ 500 vertices cannot generate preview in interactive bound (lab default 5 s) after obvious algorithmic fixes |
| K5 | Safety/integration: no design can commit geometry without violating dual-eval / backup / audit model |
| K6 | Legal/seat: lab cannot run full AutoCAD 2026 (only LT/Civil-only seats) — blocked, not a geometry kill |

Civil **does not** automatically rescue a Kill: R2 is optional and product-gated. A Kill may end as “use external Civil manually; Forge stays plot/QA.”

---

## 9. Document index

| Doc | Purpose | Owner role |
|-----|---------|------------|
| [00-charter.md](00-charter.md) | Mission, squad, sprint, DoD | Domain Lead |
| [01-autocad-primitives.md](01-autocad-primitives.md) | Vanilla entity types & APIs for taluy | Geometry Engine |
| [02-domain-model.md](02-domain-model.md) | Glossary, I/O schemas, rules | Domain Lead + SME |
| [03-forge-extension.md](03-forge-extension.md) | MCP/plugin triad sketch, safety | Forge Integration |
| [04-civil-vs-vanilla.md](04-civil-vs-vanilla.md) | Civil vs vanilla decision & gates | Domain Lead |
| [05-api-mvp.md](05-api-mvp.md) | API MVP path **C** (skin + place_units) | Forge Integration + Domain Lead |
| [06-fixture-ke-analysis.md](06-fixture-ke-analysis.md) | ke DWG lab probe; SME fork C evidence | Domain Lead + Geometry |

---

## 10. Operating rules

1. **Docs-only default.** No `src/` changes from this charter unless product leadership opens a named spike.
2. **Vanilla first.** Civil references are comparison and R2-optional only.
3. **Safety is load-bearing.** Research designs assume dry-run, backup, audit, dual policy — not a bypass via `forge_exec_dotnet` as the long-term path.
4. **Naming.** Future tools: `forge_taluy_<action>` (e.g. `preview`, `commit`, `section_mark`) — group stable even if actions evolve.
5. **Language.** Prefer “research”, “spike”, “proposed”; never “shipped”, “supported”, or “GA” for taluy.
6. **Coordination.** Parallel authors own their files; cross-links stay relative within `docs/research/taluy/`.

---

## 11. Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.2 | 2026-07-29 | SME fork **C** locked (skin + LOAI unit array); fixtures 06 linked |
