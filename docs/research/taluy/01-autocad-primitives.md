# 01 — AutoCAD 2026 vanilla primitives for taluy (R&D)

> **Track:** side R&D under `docs/research/taluy/` — **not** product scope.  
> **Target host:** full AutoCAD 2026 (.NET 8 plugin via `AcDbMgd` / `AcCoreMgd` / `AcMgd`).  
> **Explicit non-claim:** Civil 3D assemblies, corridors, assemblies, feature lines, surfaces (TinSurface), and any “geometry megakit” are **out of product scope** ([capability-matrix.md](../../capability-matrix.md), [roadmap.md](../../roadmap.md)). This note only inventories vanilla entities an R&D path could use later.

## 1. Repo geometry baseline (as of this research)

| Observation | Evidence |
|---|---|
| **No typed geometry create/modify tools** shipped | `ToolMetadata` groups are system/doc/xref/layer/block/layout/plot/qa/exec — no `entity` / `geometry` / `taluy` group |
| Plugin uses `Autodesk.AutoCAD.DatabaseServices` heavily | Transactions, `BlockTable`/`BlockTableRecord`, layers, layouts, viewports, xrefs, attributes — **not** mesh/solid construction |
| `forge_exec_dotnet` is the only in-process geometry escape hatch | Imports: `ApplicationServices`, `DatabaseServices`, `EditorInput`, **`Geometry`** (`PluginCommandProcessor.ExecDotNet`) |
| Aspirational entity CRUD exists only in archive brief | `docs/archive/build-brief.md` §5.4 lists `forge_entity_create_*` (line/polyline/…/3D solid/mesh) — **not implemented** |
| Product kill-list | Roadmap: “Clash 3D / Civil 3D / … / generative geometry / … / geometry megakit” |

**Implication for R&D:** any taluy prototype must either (a) run behind dual-gated `forge_exec_dotnet` experiments, or (b) land later as a new `forge_<group>_<action>` triad (ToolMetadata + MCP + plugin switch) without claiming product geometry megakit.

### Assemblies already referenced by Forge.Plugin

| DLL | Role for taluy |
|---|---|
| `AcDbMgd.dll` | Entity types (`Polyline3d`, `Face`, `SubDMesh`, `Solid3d`, …), `Transaction`, symbol tables |
| `AcCoreMgd.dll` | Core runtime / application services used by host |
| `AcMgd.dll` | Editor, document UI hooks (selection of baseline, etc.) |

Namespaces of interest (vanilla, no Civil):

- `Autodesk.AutoCAD.DatabaseServices` — entities + DB ops  
- `Autodesk.AutoCAD.Geometry` — `Point3d`, `Vector3d`, `Plane`, `Matrix3d`, `Curve3d`, `NurbSurface` helpers  
- `Autodesk.AutoCAD.BoundaryRepresentation` (`AcDbMgd` BRep) — inspect `Solid3d` faces/edges after loft/extrude (validation, section extraction)  
- `Autodesk.AutoCAD.DatabaseServices.Filters` / spatial filters — optional query of catch geometry  
- **Not available without Civil:** `Autodesk.Civil.*`, corridor/assembly/feature-line APIs

---

## 2. Shared vocabulary → CAD primitives

| Domain term | Vanilla CAD meaning |
|---|---|
| **Baseline** | Open (typical) or closed chain of 3D points — prefer `Polyline3d` (or lightweight `Polyline` with elevated vertices only if planar intent is forced — **avoid** for true 3D baselines) |
| **Station / sample** | Parameter `t` along baseline (chord-length or segment arc-length); yields origin point + tangent + lateral frame |
| **Slope** | Cut and/or fill ratio as **H:V** (e.g. 1.5:1) or percent; converts to a lateral offset vector in the station normal × world-up frame |
| **Daylight target** | (1) constant elevation plane, (2) existing mesh/surface samples, or (3) explicit catch/toe point list |
| **Toe / catch line** | Result 3D polyline where slope face meets target |
| **Taluy face** | Ruled strip between baseline edge and catch line (possibly multi-panel per station bay) |
| **Output package** | Faces/mesh/solid + layers + optional station markers (points/lines/text) |

---

## 3. Candidate entity types — decision table

Scoring is for **MVP embankment/cut slope strips** along an open 3D baseline in vanilla AutoCAD (deterministic, editable enough for metro review, no Civil corridor).

| Entity | Namespace / type | Build pattern for taluy | Pros | Cons | MVP fit |
|---|---|---|---|---|---|
| **`Polyline3d`** | `DatabaseServices.Polyline3d` + `PolylineVertex3d` | Baseline, catch/toe, hinge lines, section traces | True 3D vertices; simple; layer-friendly; good SoT for inputs & derived edges | Not a surface; display is wire | **Input/edge SoT (required)** |
| **`Polyline` (LWPOLY)** | `DatabaseServices.Polyline` | 2D plan baselines only | Lightweight, bulge support | Elevation is per-vertex but entity is 2D-oriented; poor as general 3D baseline | Plan-only helper |
| **`Spline`** | `DatabaseServices.Spline` | Smoothed baseline fit | Smooth alignment approximation | Harder stationing; overkill for MVP | Later |
| **`Face`** (`AcDbFace`) | `DatabaseServices.Face` | One triangle/quad per bay (4 corners; optional edge visibility) | Dead-simple; no planarity solver beyond 3 pts; classic 3DFACE | Quads with non-coplanar corners warp/display poorly; no bulk normals/UVs; many entities; weak solid ops | **Viable fallback panel** |
| **`PolyFaceMesh`** | `DatabaseServices.PolyFaceMesh` + `PolyFaceMeshVertex` + `FaceRecord` | Indexed vertices + face records for whole slope | One DB object for many faces; older mesh interop | Clunky API; vertex/face record sequencing footguns; dated vs SubDMesh | Legacy only |
| **`SubDMesh`** | `DatabaseServices.SubDMesh` | `SetSubDMesh(vertices, faces, smoothLevel)` from station grid | **Best mesh SoT**: one entity, indexed faces, smoothing optional (keep 0 for faceted taluy), converts to solid/surface when closed/valid | Must build clean topology; self-intersection → convert fails; large coords sensitivity | **Primary surface rep (MVP)** |
| **`Mesh` / faceter** | `SubDMesh.GetObjectMesh`, `MeshFaceterData` | Tessellate solids back to mesh for QA | Good validation path | Not an authoring primitive | QA helper |
| **`Solid3d`** | `DatabaseServices.Solid3d` | `CreateLoftedSolid`, extrude/sweep region, boolean | Volume/cut-fill potential; sectionable; BRep | Needs closed profiles or careful loft guides; open “skin” slopes are surfaces not solids; heavier; failure-prone on skinny wedges | Volume phase / optional |
| **`LoftedSurface` / `SweptSurface` / `PlaneSurface`** | `DatabaseServices.*Surface` (NURBS surface entities) | Loft baseline→catch as surface | Smooth skin; can thicken later | Editing/station correlation harder; surface kernel failures; less “panelized” for construction docs | Advanced path |
| **`Region`** | `DatabaseServices.Region` | Cross-section profiles for extrude/sweep along path | Classic solid recipe (profile × path) | Taluy is **lateral** slope to daylight, not constant profile sweep; varying height breaks simple sweep | Only for constant bench sections |
| **`ExtrudedSurface` / `Solid3d.Extrude`** | Surface/solid | Vertical extrude of plan poly | Easy embankment **prism** if top=plan | Wrong model for slope-to-catch daylight | Special case only |
| **`Sweep` options** | `SweepOptions`, `Solid3d.CreateSweptSolid` | Bench/barrier along alignment | Good for constant gauge features | Not general cut/fill daylight | Ancillary |
| **`Line` / `Point` / `DBPoint`** | basics | Station ticks, slope ray debug | Cheap diagnostics | Not faces | Debug/markers |
| **`Hatch`** | 2D | Plan symbology of slope | Drawing convention | Not 3D geometry | Sheets only |
| **`BlockReference`** | block with nested mesh | Stamp repeated furniture | Reuse | Not the slope body | Markers/labels |

### Ruled-panel geometry (engine-level, entity-agnostic)

At each station bay `i → i+1`:

1. Sample baseline points `B_i`, `B_{i+1}` (and optional hinge offsets).  
2. Compute right/left lateral frames from tangent × world Z (handle vertical tangent singularity).  
3. Cast slope ray from `B_i` with cut/fill inclination until daylight target → `C_i`.  
4. Emit panel quad `(B_i, B_{i+1}, C_{i+1}, C_i)` — split to two triangles if non-planar.  
5. Join `C_*` into catch `Polyline3d`; join panels into `SubDMesh` (or discrete `Face`s).

This is pure `Geometry` math + entity write — **no Civil**.

---

## 4. Required input geometry

### 4.1 Baseline (required)

| Field | Recommendation |
|---|---|
| Type | `Polyline3d` (open for alignment edge / platform lip; closed only for bowls/pads) |
| Vertices | WCS `Point3d` with real Z (metro elevations) |
| Continuity | Reject zero-length segments; optionally weld duplicates within ε |
| Direction | Define chain direction = increasing station; side = right/left looking along chain |
| Optional | Parallel hinge offset polyline (top of slope setback) |

### 4.2 Sampling

| Mode | Use |
|---|---|
| **Vertex stations** | MVP default — one sample per baseline vertex (plus optional mid-span densify) |
| **Fixed step** | Every *N* meters chord length along 3D poly |
| **Adaptive** | Densify on high curvature / slope breaks (post-MVP) |

At each sample store: `Station`, `Point`, `Tangent`, `NormalHorizontal`, `SideVector`, `SlopeVectorCut`, `SlopeVectorFill`.

### 4.3 Slope parameters

| Param | Notes |
|---|---|
| `cutRatioHtoV` / `fillRatioHtoV` | Independent; Vietnamese practice often e.g. đào 1:1, đắp 1.5:1 (confirm per project standards pack later) |
| `slopePercent` | Alternate input; convert once at boundary |
| `maxSlopeLength` | Safety cap so unbounded daylight does not run forever |
| `side` | `Left` / `Right` / `Both` |

Conversion: ratio H:V means horizontal run H for vertical rise 1 → inclination vector in the lateral vertical plane.

### 4.4 Daylight target (one of)

| Target | Algorithm sketch | Vanilla entities involved |
|---|---|---|
| **Elevation plane** | Intersect slope ray with `Z = Z_const` (or `Plane`) | Math only; output catch pts |
| **Point list / breakline** | Project to nearest segment on target poly | Target `Polyline3d` |
| **Existing mesh** | Ray × triangle hit test on `SubDMesh` / `Face` set | Read mesh vertices/faces |
| **TIN-like faces in DWG** | Same as mesh | User-supplied faces on a layer |

**MVP target:** constant elevation plane **or** explicit catch polyline supplied by caller (skip general surface intersection).

### 4.5 Output set (suggested layers — research only)

| Layer (example) | Content |
|---|---|
| `TALUY_BASELINE` | Source or copied baseline |
| `TALUY_CATCH` | Toe/catch `Polyline3d` |
| `TALUY_FACE` | `SubDMesh` or `Face` panels |
| `TALUY_STATION` | Optional ticks / codes |
| `TALUY_SOLID` | Optional loft solid (non-MVP) |

---

## 5. API touchpoints (vanilla .NET)

### 5.1 Construction sketch — primary (`SubDMesh`)

```csharp
// Research sketch — not product code. Names illustrate API surface only.
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

// 1) Read baseline Polyline3d vertices (Transaction, OpenMode.ForRead)
// 2) Build List<Point3d> vertices and List<int> faceIndices (groups of 3 or 4)
// 3) Create mesh:
var mesh = new SubDMesh();
mesh.SetSubDMesh(new Point3dCollection(vertices), faceIndices, smoothLevel: 0);
mesh.Layer = "TALUY_FACE";
// 4) btr.AppendEntity(mesh); tr.AddNewlyCreatedDBObject(mesh, true);
```

Related members to verify on seat AutoCAD 2026: `SetSubDMesh`, `Vertices`, `NumberOfFaces`, `ConvertToSolid` / solid conversion paths, `GetObjectMesh` (faceter inverse).

### 5.2 Fallback — `Face` quads/tris

```csharp
var face = new Face(p0, p1, p2, p3, true, true, true, true);
// or triangle: repeat last point / use 3-pt overload patterns per API
face.Layer = "TALUY_FACE";
```

### 5.3 Edges — `Polyline3d`

```csharp
var pl = new Polyline3d(Poly3dType.SimplePoly, new Point3dCollection(pts), closed: false);
// vertices are PolylineVertex3d owned by the polyline
```

### 5.4 Optional solid loft (post-MVP)

- Build closed profile `Region` at start/end **or** guide curves: baseline + catch + end caps.  
- `Solid3d.CreateLoftedSolid(crossSectionCurves, guideCurves, pathCurve, loftOptions)`.  
- Validate with `Autodesk.AutoCAD.BoundaryRepresentation.Brep`.

### 5.5 Math (`Autodesk.AutoCAD.Geometry`)

| Type | Use |
|---|---|
| `Point3d`, `Vector3d`, `Matrix3d` | Frames, offsets |
| `Plane` | Elevation daylight |
| `Line3d` / `Ray3d` | Slope ray |
| `PolylineCurve3d` / sample helpers | Parameterization if wrapping baseline curve |
| ε comparisons | `Tolerance.Global` or project metric tolerance (mm) |

### 5.6 Host integration points already in Forge

| Hook | Relevance |
|---|---|
| `PluginCommandProcessor` switch triad | Future `forge_taluy_*` would land here — **not now** |
| `forge_exec_dotnet` + `Geometry` import | R&D spikes without new tools |
| Dual `SafetyPolicy`, dry-run, backup, audit | Any write tool must honor metadata (`Write`/`Destructive`, backup) |
| Layers via `LayerTable` / `LayerTableRecord` | Ensure output layers exist (pattern already used in QA layer audits) |
| Transaction + `BlockTableRecord` model space append | Same pattern as all plugin DB writes |

### 5.7 What we will **not** call (unless gated Civil adapter exists later)

- `Autodesk.Civil.DatabaseServices.Alignment`, `FeatureLine`, `Corridor`, `TinSurface`, `SlopePattern`, daylight from corridor targets  
- Any COM Civil interop  

Those belong only in a clearly optional adapter doc (`04-civil-vs-vanilla.md`), not MVP.

---

## 6. Failure modes & mitigations

| Failure | Symptom | Mitigation |
|---|---|---|
| **Zero-length segment** | NaN tangent, explode stationing | Pre-weld vertices; reject δ < ε |
| **Vertical / near-vertical tangent** | Lateral frame undefined (T × Z ≈ 0) | Fallback to last good horizontal tangent or use Frenet with bank lock |
| **Open vs closed baseline** | Closed accidentally fills interior; open leaves end caps empty | Explicit `closed` flag; MVP = open only |
| **Non-planar quad** | `Face` shading artifacts; bad mesh normals | Always split bay into **two triangles** for mesh faces |
| **Self-intersecting catch** | Bow-tie panels, solid convert fail | Detect segment intersections in plan; reduce step; refuse with diagnostic |
| **Slope never hits target** | Ray goes up in fill when target above, etc. | Classify cut vs fill from baseline Z vs target; pick matching slope; enforce `maxSlopeLength` |
| **Mixed cut/fill along chain** | One side needs both slopes | Per-station sign(baselineZ − targetZ); allow transition stations |
| **Huge coordinates** | Mesh/solid numeric instability | Work in local ENU shifted origin; store transform |
| **Duplicate face indices / wrong winding** | Black faces, wrong normals | Consistent CCW when viewed from outward normal (upslope → downslope convention documented) |
| **Open skin as `Solid3d`** | API reject | Don’t solidify until capped; keep `SubDMesh` as skin SoT |
| **Daylight to noisy mesh** | Ray miss / multi-hit | Prefer plane/poly MVP; later use closest-hit with max distance |
| **Transaction / undo** | Partial write | Single transaction per generate; rely on plugin undo group patterns |
| **Layer locked / frozen** | Append throws | Pre-create layers; check lock like other write tools |
| **exec_dotnet only path** | No dry-run structure, unsafe gate | R&D OK; productization needs typed tool + dry-run plan payload |

---

## 7. Recommendation

### 7.1 Primary MVP stack (choose **one** skin)

| Role | Entity | Why |
|---|---|---|
| Baseline in | `Polyline3d` | True 3D chain, matches metro edge-of-platform / road lip |
| Catch / toe out | `Polyline3d` | Editable edge, sectionable, plots as contour-like breakline |
| **Taluy skin** | **`SubDMesh` (smoothLevel = 0)** | Single entity, explicit tris from ruled bays, no Civil, good visual mass, upgrade path to solid |
| Station markers (opt.) | `Line` + `DBPoint` or short `Polyline3d` | Debug + drawing convention |
| Layers | Dedicated `TALUY_*` | Separable from publish titleblock flows |

**MVP algorithm:** vertex (or fixed-step) samples → per-station daylight to **plane or given catch poly** → triangle pair per bay → one `SubDMesh` + catch `Polyline3d`.

### 7.2 Fallback skin (choose **one**)

| Fallback | When | Notes |
|---|---|---|
| **`Face` triangles (3DFACE)** | `SubDMesh.SetSubDMesh` misbehaves on seat, or consumers demand explode-friendly legacy faces | One DB object per triangle; more clutter; simplest API surface; easy visual check |

Do **not** use `PolyFaceMesh` unless interop with a specific downstream tool demands it.

### 7.3 Explicitly deferred

| Deferred | Reason |
|---|---|
| `Solid3d` loft/boolean volumes | Needs caps, stable guides; cut/fill volumes are phase-2 |
| NURBS `LoftedSurface` | Overkill; weaker panel control for construction |
| `Region` sweep along path | Wrong for variable daylight |
| Civil surfaces/corridors | Product out of scope; optional future adapter only |
| Typed `forge_taluy_*` tools | Extension design lives in `03-forge-extension.md`; this doc only picks entities |

---

## 8. Risks (R&D honesty)

| Risk | Level | Comment |
|---|---|---|
| Scope creep into geometry megakit | High | Keep artifacts under `docs/research/taluy/`; no `src/` claims; matrix stays “out of scope” until a conscious product wave |
| Numeric robustness on long metro chains | Med | Local origin + ε policy required before any field pilot |
| Cut/fill transitions and benches | Med | MVP single-slope strips only; benches = chained baselines later |
| Expectation of Civil daylight fidelity | Med | Vanilla will not match corridor link behavior — document gaps in `04-civil-vs-vanilla.md` |
| `forge_exec_dotnet` as prototype vehicle | Med | Unsafe dual-gate; not auditable dry-run geometry plan unless typed |
| Mesh → solid conversion temptation | Low/Med | Treat conversion failure as non-blocking for skin MVP |

---

## 9. Acceptance checklist (this note)

- [x] Decision table of vanilla entities with tradeoffs  
- [x] Recommended **MVP** entity stack (`Polyline3d` + `SubDMesh`) and **fallback** (`Face`)  
- [x] Input geometry (baseline, stations, slope vectors, toe/catch, daylight targets)  
- [x] API namespaces/classes and Forge touchpoints (`exec_dotnet` Geometry import, AcDbMgd)  
- [x] Failure modes  
- [x] No `src/` changes; research-only path  

## 10. Pointers for sibling research docs

| Doc | Consume this as |
|---|---|
| `00-charter.md` | Scope boundary: vanilla primitives only for MVP |
| `02-domain-model.md` | Map Baseline/Slope/Daylight/Output fields to entities above |
| `03-forge-extension.md` | If tools ever exist: write ops creating `SubDMesh` + polys; safety Write+backup |
| `04-civil-vs-vanilla.md` | Why not TinSurface/corridor; parity gaps |
| `05-api-mvp.md` (Main) | Concrete parameter shapes against primary stack |

---

*End of R&D note. Not a ship commitment.*
