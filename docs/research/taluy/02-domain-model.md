# Taluy domain model (R&D)

> **Status:** Side R&D research only under `docs/research/taluy/`.  
> **Not shipped.** Product scope still excludes Civil 3D and geometry megakit  
> ([capability-matrix.md](../../capability-matrix.md), [roadmap.md](../../roadmap.md)).  
> Do not treat this document as a product claim or capability-matrix entry.
> **Path C (2026-07-29):** MVP also includes **unit array** (`LOAI *` INSERT along baseline) — see [05-api-mvp.md](05-api-mvp.md) C-L3 and [07-path-c-plan.md](07-path-c-plan.md). This file remains the slope-skin domain SoT; unit placement params live in 05.

This file defines the **domain vocabulary** and **MVP input/output contract** for
metro *rải taluy* (cut/fill slope generation) on vanilla AutoCAD 3D via a future
Forge MCP tool group. Implementation would follow the existing triad:
`ToolMetadata` + MCP server registration + plugin switch, named `forge_<group>_<action>`,
with dual `SafetyPolicy`, dry-run, backup, and audit — but **no `src/` changes** are
authorized by this research pack.

---

## 1. Shared vocabulary (canonical)

| Term (EN) | Term (VI) | Definition |
|-----------|-----------|------------|
| **Baseline** | **Mép kết cấu / đường gốc** | Ordered 3D chain (points with Z) that anchors the slope. Typically platform edge, structure edge, or alignment offset polyline. Not a Civil alignment object. |
| **Daylight target** | **Mục tiêu bắt mái** | Rule that ends the slope face: fixed elevation plane, relative Z offset, or max horizontal width. |
| **Slope** | **Tỉ lệ mái** | Inclination expressed as **H:V** (horizontal : vertical). Cut and fill may differ. |
| **Cut slope** | **Taluy đào** | Slope from baseline **down** into existing/higher ground (excavation face). |
| **Fill slope** | **Taluy đắp** | Slope from baseline **down** onto lower ground (embankment face). |
| **Crest** | **Đỉnh mái** | Upper edge of the slope face. For fill, often coincides with baseline; for cut, often the daylight/catch line uphill. |
| **Toe** | **Chân mái** | Lower edge of the slope face. For fill, often the daylight/catch line; for cut, often coincides with baseline. |
| **Crest elevation** | **Cao độ đỉnh** | Z at crest samples. |
| **Toe / bottom elevation** | **Cao độ đáy / chân** | Z at toe samples. |
| **Berm** | **Cấp / cơ** | Intermediate horizontal (or near-horizontal) bench inserted between slope segments. |
| **Crest ditch** | **Rãnh đỉnh** | Optional drainage channel along crest; **out of MVP geometry** (named only for glossary / future). |
| **Side** | **Phía** | Relative to baseline travel direction: `left`, `right`, or `both`. |
| **Station** | **Lý trình (nội bộ)** | Arc-length parameter *s* along the baseline from start (drawing units). Not a Civil station equation. |
| **Section / sample** | **Mặt cắt mẫu** | Planar cut normal to baseline tangent at station *s*, used to compute left/right daylight points. |
| **Face** | **Mặt mái** | Planar or triangulated surface patch between crest and toe (and berm edges). |
| **Output set** | **Tập thực thể kết quả** | Vanilla AutoCAD entities (3D poly, 3DFACE/mesh/solid) plus layers and handles returned to the agent. |

### 1.1 Slope ratio conventions

- Canonical storage: **`H:V`** as two positive numbers `horizontal`, `vertical`  
  (example: `1.5:1` → H=1.5, V=1 → angle from horizontal \(\theta = \atantwo(V, H)\)).
- Also accepted on input (normalized before compute):
  - string `"1.5:1"` or `"1.5/1"`
  - percent grade `percent` where \(H:V = 100:p\) for vertical rise *p* per 100 horizontal  
    (only when explicitly `unit: "percent"` — do not overload bare numbers).
- **m:n** in Vietnamese site language often means the same H:V pair; treat `m:n` ≡ `H:V`.
- Zero or negative H or V is invalid. Vertical face is **not** MVP (reject `H == 0`).

### 1.2 Cut vs fill sense

At each sample, the algorithm classifies the needed face by comparing baseline Z
to the daylight target Z along the outward horizontal direction:

| Condition | Classification | Uses |
|-----------|----------------|------|
| Baseline must step **down** to meet target | **fill** (đắp) | `fillSlope` |
| Baseline must step **up** toward catch / target above | **cut** (đào) | `cutSlope` |
| Target Z equals baseline Z within tol | **degenerate** | no face (or zero-width marker only) |

MVP does **not** sample an existing TIN/surface. Classification is purely from
`target` + baseline Z (see §3). Optional future: surface-aware daylight.

### 1.3 Side / handedness

- Baseline points are ordered in **run direction**.
- `left` / `right` are relative to the **horizontal projection** of the run tangent
  (2D bearing). Outward normal in plan = rotate tangent by +90° (left) or −90° (right)
  in the XY plane (right-hand CAD coords, Z up).
- `both` = independent left and right extrusions sharing the same baseline and params
  (unless per-side overrides are supplied).

---

## 2. Conceptual model

```text
Baseline (3D chain)
    │
    ├─ sample at stations s0..sN
    │     ├─ frame: origin, tangent, left/right normal (plan)
    │     ├─ classify cut | fill | degenerate
    │     ├─ apply slope H:V (+ optional berms)
    │     └─ intersect daylight target → crest/toe points
    │
    ├─ longitudinal join → crest line, toe line, berm lines
    └─ loft / stitch → faces (mesh / 3DFACE set) + layers
```

**MVP = deterministic slope extrusion** from baseline + slope params in vanilla
AutoCAD. No Civil corridor, no assembly catalog, no subassembly logic.

---

## 3. MVP input contract

JSON-ish schema for a future tool such as `forge_taluy_generate` (name indicative
only). Envelope fields (`document`, `dryRun`, `auditId`, auth) remain on
`ForgeCommand` and are **not** repeated inside args.

### 3.1 Root args

```json
{
  "baseline": {
    "points": [
      { "x": 0.0, "y": 0.0, "z": 12.500 },
      { "x": 20.0, "y": 0.0, "z": 12.480 }
    ],
    "closed": false,
    "id": null,
    "handle": null
  },
  "side": "right",
  "cutSlope": { "h": 1.0, "v": 1.0 },
  "fillSlope": { "h": 1.5, "v": 1.0 },
  "target": {
    "type": "fixedElevation",
    "elevation": 10.000
  },
  "berms": [],
  "sampling": {
    "mode": "adaptive",
    "maxSpacing": 5.0,
    "minSpacing": 0.5,
    "addVertices": true,
    "addCorners": true,
    "cornerAngleDeg": 15.0
  },
  "output": {
    "entityMode": "facesAndOutlines",
    "meshType": "faces",
    "includeSectionMarkers": false,
    "estimateVolume": true,
    "layerPrefix": "TALUY",
    "layers": {
      "crest": "TALUY-CREST",
      "toe": "TALUY-TOE",
      "face": "TALUY-FACE",
      "berm": "TALUY-BERM",
      "section": "TALUY-SECT",
      "baselineRef": "TALUY-BASE"
    },
    "groupName": null
  },
  "tolerances": {
    "linear": 1e-4,
    "angularDeg": 1e-4,
    "z": 1e-4,
    "area": 1e-6,
    "volume": 1e-4
  },
  "units": {
    "linear": "drawing",
    "slope": "H:V"
  }
}
```

### 3.2 Field reference

#### `baseline` (required)

| Field | Type | Rules |
|-------|------|--------|
| `points` | `[{x,y,z}, …]` | ≥ 2 points. Finite numbers. Ordered along run. |
| `closed` | bool | Default `false`. If true, last→first segment included; start/end stations identified. |
| `id` | string? | Caller correlation id (echoed in result). |
| `handle` | string? | Optional existing entity handle to **read** points from (3D poly / 2D poly with Z). If both `handle` and `points` given, `points` win after read-validation warning in QA metrics. |

Coordinates are in **WCS drawing units**. No CRS transform in MVP.

#### `side` (required)

Enum: `"left"` | `"right"` | `"both"`.

#### `cutSlope` / `fillSlope` (required)

```json
{ "h": 1.5, "v": 1.0 }
```

Optional string form at tool boundary: `"1.5:1"` → normalized to `{h,v}`.  
Both slopes required even if a run is pure fill or pure cut (unused side ignored per sample).

#### `target` (required)

Discriminated union on `type`:

| `type` | Fields | Meaning |
|--------|--------|---------|
| `fixedElevation` | `elevation: number` | Horizontal plane Z = elevation. Daylight where slope plane meets Z. |
| `offsetZ` | `deltaZ: number` | Plane Z = baselineZ(s) + deltaZ (constant offset from local baseline Z). Sign: negative = below baseline. |
| `maxWidth` | `width: number` (> 0) | Horizontal offset distance from baseline plan projection; Z follows slope from baseline for cut/fill chosen by comparing implied Z to… **MVP rule:** with `maxWidth` only, build face at fixed plan width using **fillSlope if deltaZ would be negative when projecting down-slope convention is ambiguous** — see deterministic rule below. |

**`maxWidth` deterministic rule (MVP):**

1. Compute outward plan point at distance `width`.
2. Compute Z_down = baselineZ − (width / H_fill) * V_fill (fill face dropping).
3. Compute Z_up = baselineZ + (width / H_cut) * V_cut (cut face rising).
4. Caller **must** also supply `maxWidth.prefer`: `"fill"` | `"cut"` | `"auto"`.
   - `"fill"` → use Z_down + fillSlope  
   - `"cut"` → use Z_up + cutSlope  
   - `"auto"` → use `fill` if `prefer` omitted default; **document default = `"fill"`** for metro embankment-first workflows, and echo `resolvedMode` in QA.

Recommended MVP target for first spike: **`fixedElevation` only**; support `offsetZ` and `maxWidth` in schema but mark `maxWidth` as “schema-stable, second spike.”

#### `berms` (optional)

Ordered from baseline outward (crest→toe direction along the face):

```json
{
  "berms": [
    {
      "afterSlopeHeight": 3.0,
      "width": 1.5,
      "slopeContinuity": "break",
      "cutSlope": null,
      "fillSlope": null
    }
  ]
}
```

| Field | Meaning |
|-------|---------|
| `afterSlopeHeight` | Vertical height of the slope segment **before** this berm, measured along Z from the previous outer edge (baseline or prior berm). > 0. |
| `width` | Horizontal berm width outward. > 0. |
| `slopeContinuity` | `"break"` (default): berm is level (ΔZ=0). `"grade"` reserved — **non-MVP**. |
| `cutSlope` / `fillSlope` | Optional override for the **next** slope segment below/beyond the berm; null = inherit root slopes. |

Empty array / omit = single-segment slope baseline→daylight.

#### `sampling` (optional; defaults shown in §3.1)

See §5 for rules. Tool must apply defaults when omitted.

#### `output` (optional)

| Field | Values / default | Notes |
|-------|------------------|-------|
| `entityMode` | `facesAndOutlines` (default), `outlinesOnly`, `facesOnly` | Outlines = crest/toe/berm 3D polylines. |
| `meshType` | `faces` (3DFACE/Mesh), `polyfaceMesh`, `solid` | MVP target: `faces`. `solid` optional later. |
| `includeSectionMarkers` | bool, default false | Short 3D lines or points at samples on face. |
| `estimateVolume` | bool, default false | Prismoidal / average-end estimate; not legal earthwork. |
| `layerPrefix` | string, default `TALUY` | Used when `layers` omitted. |
| `layers` | map | Full names; see §3.3. |
| `groupName` | string? | Optional anonymous group / app-dict tag for erase-by-run. |

#### `tolerances` (optional)

Defaults in §3.1. Used for degeneracy, snap, and determinism checks (§6).

### 3.3 Layer naming convention

Default pattern:

```text
{prefix}-{ROLE}
```

| Role key | Default layer | Contents |
|----------|---------------|----------|
| `crest` | `TALUY-CREST` | Crest 3D polyline(s) |
| `toe` | `TALUY-TOE` | Toe / daylight 3D polyline(s) |
| `face` | `TALUY-FACE` | 3DFACE / mesh entities |
| `berm` | `TALUY-BERM` | Berm edge polylines |
| `section` | `TALUY-SECT` | Optional section markers |
| `baselineRef` | `TALUY-BASE` | Optional copy or marker of baseline used |

Rules:

- Prefix: uppercase ASCII + digits + `-` / `_` only in MVP (Vietnamese UI names can map later via standards pack — not required here).
- Side disambiguation when `side: both`: append `-L` / `-R` to layer **or** to group — pick **entity XData/app name side tag** + same layers by default to avoid layer explosion; QA echoes `side` per handle list.
- Layers are created if missing (color/linetype: research default — continuous; color by role reserved for pack).
- Never silently write to `0` or `Defpoints` for faces.

### 3.4 Validation (fail closed)

Reject with structured `ForgeError.code` (indicative):

| Code | When |
|------|------|
| `taluy_baseline_too_short` | < 2 points or total plan length < `linear` tol |
| `taluy_baseline_nonfinite` | NaN/Inf coordinate |
| `taluy_slope_invalid` | h≤0 or v≤0 |
| `taluy_target_invalid` | missing fields / width≤0 |
| `taluy_side_invalid` | not left\|right\|both |
| `taluy_berm_order_invalid` | heights/widths non-positive or unordered nonsense |
| `taluy_no_daylight` | slope never meets target within max search (e.g. fill toward higher plane) |
| `taluy_degenerate_run` | all samples degenerate |
| `taluy_handle_unresolved` | handle not a supported curve |

Dry-run runs the **same validation + geometry compute** but commits no entities; returns planned counts and sample metrics.

---

## 4. MVP output contract

Success payload lives in `ForgeResult.Data` (plus optional `Verification` read-back).

### 4.1 Result shape

```json
{
  "okGeometry": true,
  "runId": "a1b2c3d4e5f6",
  "dryRun": false,
  "baseline": {
    "id": null,
    "handleIn": null,
    "pointCount": 12,
    "length3d": 153.042,
    "lengthPlan": 152.900
  },
  "sides": [
    {
      "side": "right",
      "resolvedModeSummary": { "fill": 40, "cut": 2, "degenerate": 0 },
      "handles": {
        "crest": ["1A2B"],
        "toe": ["1A2C"],
        "berms": ["1A2D"],
        "faces": ["1A2E", "1A2F"],
        "sections": [],
        "baselineRef": []
      },
      "polylines": {
        "crest": { "pointCount": 42, "length3d": 153.1 },
        "toe": { "pointCount": 42, "length3d": 158.4 },
        "berms": [{ "index": 0, "pointCount": 42, "length3d": 155.0 }]
      },
      "faces": {
        "count": 82,
        "triangleCount": 82,
        "area": 420.15
      },
      "volume": {
        "estimated": true,
        "method": "averageEndArea",
        "signedCubic": 312.4,
        "fillCubic": 320.1,
        "cutCubic": 7.7,
        "unit": "drawing^3"
      },
      "qa": {
        "sampleCount": 42,
        "maxGapPlan": 5.0,
        "maxCrestToeWidth": 6.12,
        "minCrestToeWidth": 0.0,
        "maxZResidualToTarget": 1e-5,
        "selfIntersectionSuspected": false,
        "determinismHash": "sha256:…",
        "warnings": []
      }
    }
  ],
  "layersEnsured": ["TALUY-CREST", "TALUY-TOE", "TALUY-FACE"],
  "tolerancesApplied": {
    "linear": 1e-4,
    "angularDeg": 1e-4,
    "z": 1e-4,
    "area": 1e-6,
    "volume": 1e-4
  },
  "schemaVersion": 1
}
```

### 4.2 Output field meanings

| Field | Required | Meaning |
|-------|----------|---------|
| `runId` | yes | Stable id for this generation (audit correlation; may match/pair `AuditId`). |
| `handles.*` | yes (empty if dry-run) | AutoCAD entity handles created (hex strings). Dry-run: empty arrays + `plannedCounts`. |
| `polylines.crest` / `.toe` | yes | Toe line = daylight catch line; crest line = upper edge. |
| `faces.count` | yes | Number of face entities (or mesh density summary). |
| `volume` | optional | Only if `estimateVolume: true`. **Indicative**, not bill-of-quantities. |
| `qa` | yes | Metrics for agent gates and determinism. |
| `qa.determinismHash` | yes | Hash of quantized geometry (§6). |
| `schemaVersion` | yes | Bump on breaking result shape changes. |

Dry-run extra:

```json
{
  "plannedCounts": {
    "crestPolylines": 1,
    "toePolylines": 1,
    "bermPolylines": 0,
    "faces": 82,
    "sections": 0
  }
}
```

### 4.3 Verification (plugin)

When not dry-run, `ForgeVerification` SHOULD read back:

- handle existence  
- layer names  
- crest/toe point counts within tol of planned  
- optional bbox  

`Verification.Passed` proves entity commit contracts, **not** geotechnical correctness.

---

## 5. Station sampling rules

All distances in drawing linear units. Parameter *s* = 3D or plan length — **MVP uses plan (XY) chain length** for spacing so vertical noise does not densify samples unevenly. Report both `lengthPlan` and `length3d`.

### 5.1 Modes

| `sampling.mode` | Behavior |
|-----------------|----------|
| `fixed` | Samples every `maxSpacing` along plan length; always include start/end. |
| `adaptive` (default) | Start from vertices + corners; densify so adjacent sample plan gap ≤ `maxSpacing`; never closer than `minSpacing` except at forced vertices. |

### 5.2 Mandatory sample loci

1. **Start** and **end** of baseline (and closure point if `closed`).  
2. **Every baseline vertex** if `addVertices: true` (default).  
3. **Plan bearing change** ≥ `cornerAngleDeg` (default 15°) if `addCorners: true`, even on densified segments (for chorded curves approximated by many verts, vertex rule dominates).  
4. **Uniform densify**: on each plan segment, insert interior parameters so gap ≤ `maxSpacing`.  
5. If a densify point would fall within `minSpacing` of an existing sample, **skip** densify point (keep forced vertices).

### 5.3 Frame at sample *s*

1. Interpolate point \(P(s)\) on baseline (linear segments in 3D between vertices).  
2. Plan tangent \(\hat{t}\) from XY segment direction (average at vertices: bisect adjacent in-plan unit tangents).  
3. Outward normal \(\hat{n}\) = rotate \(\hat{t}\) by +90° (left) or −90° (right) in XY; z=0.  
4. Build slope in the vertical plane spanned by \(\hat{n}\) and \(\hat{z}\).

### 5.4 Daylight solve (single segment, no berm)

For fill toward `fixedElevation` \(Z_t < P_z\):

\[
\Delta Z = P_z - Z_t,\quad w = H/V \cdot \Delta Z,\quad T = P + w\cdot\hat{n} + (Z_t - P_z)\hat{z}
\]

For cut toward \(Z_t > P_z\):

\[
\Delta Z = Z_t - P_z,\quad w = H/V \cdot \Delta Z,\quad T = P + w\cdot\hat{n} + (Z_t - P_z)\hat{z}
\]

With berms: walk outward alternating slope-vertical budget and berm width until target Z is reached; if vertical budget exhausted before target, fail `taluy_no_daylight` or extend final slope (MVP: **extend final slope** and warn `berm_height_short_extended`).

### 5.5 Longitudinal topology

- Crest polyline = ordered crest points at samples (for pure fill, crest ≈ baseline side edge).  
- Toe polyline = ordered daylight points.  
- Faces = quad strip between consecutive samples, split to two triangles on stable diagonal rule (§6).  
- Gaps: if baseline has plan cusp > 135° interior, allow **split run** into separate face strips; QA flag `cusp_split`.

### 5.6 Defaults (normative for determinism)

```text
mode            = adaptive
maxSpacing      = 5.0
minSpacing      = 0.5
addVertices     = true
addCorners      = true
cornerAngleDeg  = 15.0
```

---

## 6. Determinism requirements

**Same input → same geometry within tolerances**, across process restarts, independent of viewport, UCS, or pick order.

| Requirement | Rule |
|-------------|------|
| Pure function | Geometry depends only on args + baseline coordinates (+ tol). No wall clock, random, or handle-order side effects in compute. |
| Quantization | Before hash and before write, round XYZ to `linear` / `z` tol grids (decimal places derived from tol). |
| Triangle diagonal | For quad (P_i, P_{i+1}, T_{i+1}, T_i), split along the diagonal with **smaller** quantized midpoint key; tie → diagonal involving lower station index toward toe. |
| Entity create order | Crest → berms (outerizing) → toe → faces station-ascending → sections. Side order: left then right when `both`. |
| Handle lists | Result handle arrays follow create order (not database iteration order). |
| `determinismHash` | SHA-256 over canonical JSON of quantized crest/toe/berm points + face vertex indices + slopes + target + sampling defaults resolved. |
| Float mode | IEC 60559 doubles; no fast-math. |
| UCS / view | All compute in WCS; ignore current UCS. |
| Idempotent rewrite | Optional future `replaceRunId`: erase prior group then write; MVP may omit and require caller erase. |

**Tolerance equality:** two runs match if hash equal **or** pairwise point deltas ≤ tol (hash is preferred gate).

**Non-goals for determinism:** identical handle strings across runs (AutoCAD assigns handles); identical `runId` (new each call unless caller supplies).

---

## 7. Non-goals (MVP)

Explicitly **out of scope** for this R&D MVP (and still out of product geometry megakit claims):

| Non-goal | Notes |
|----------|--------|
| Civil 3D corridor / assembly / subassembly | No `Alignment`, `Corridor`, `Baseline` Civil objects |
| Material layers / pavement structures | Single homogeneous face only |
| Drainage networks | No rãnh đỉnh modeling, pipes, inlets |
| Existing ground TIN daylight | No surface intersect; targets are analytic only |
| Superelevation / spiral station equations | Plan tangent from polyline only |
| Multiple baselines / junctions / regions | One baseline chain per call |
| Geotechnical stability / factor of safety | Geometry only |
| BOQ / legal earthwork volumes | Volume is optional estimate |
| Live link / parametric update reactor | One-shot generate |
| Mesh simplification / LOD | Fixed sampling |
| CRS / geographic transforms | Drawing WCS only |
| Vietnamese national CAD standard pack enforcement | Layer defaults only; packs later |
| Generative / ML slope design | Deterministic extrusion only |
| Claiming shipped Forge capability | Research docs only until product intentionally expands matrix |

---

## 8. Mapping to future Forge tools (indicative)

Not an implementation commitment — guidance for `03-forge-extension.md` / `05-api-mvp.md`:

| Tool (proposed) | Role | Safety |
|-----------------|------|--------|
| `forge_taluy_validate` | Schema + daylight feasibility | readOnly |
| `forge_taluy_preview` | Compute only / dry geometry JSON | readOnly |
| `forge_taluy_generate` | Write entities (`DryRun` supported) | write + backup |
| `forge_taluy_erase` | Delete by `runId` / handles | destructive |

Group name: `taluy`. Every tool: `ToolMetadata` + MCP + plugin switch triad; dual safety on pipe path.

---

## 9. Worked micro-example

**Input (abbrev.):** baseline two points along X at Z=12.5; `side: right`; `fillSlope: 1.5:1`; `target: fixedElevation 10.0`.

- ΔZ = 2.5 → width = 1.5 × 2.5 = 3.75  
- Toe point at first station: \((0,\ -3.75,\ 10.0)\) if run is +X and right = −Y  
- One quad strip between two stations → 2 triangles  
- Layers: `TALUY-CREST`, `TALUY-TOE`, `TALUY-FACE`  
- Volume estimate (prism): average end area × plan length  

---

## 10. Document control

| Item | Value |
|------|--------|
| Path | `docs/research/taluy/02-domain-model.md` |
| Schema version | 1 |
| Product impact | **None** — research only |
| Related (siblings) | `00-charter.md`, `01-autocad-primitives.md`, `03-forge-extension.md`, `04-civil-vs-vanilla.md`, `05-api-mvp.md` |

When product scope **intentionally** absorbs taluy, promote a slim contract into
Forge.Shared and the capability matrix in a dedicated SemVer wave — do not silently
expand megakit scope from this file alone.
