# 08 — Support: solid BODY vs green LINE mesh

> SME wants units **dính mặt xanh**. Lab shows “mặt xanh” is **not one geometry type**.

## Evidence (Accore 2026-07-29)

### DWG1 / `F-C-GOLDEN-SOLID.dwg`

| Layer | Types |
|-------|--------|
| `SOLIDS - Corridor - (3) - Top_Datum` | **BODY ×2** |
| `SOLIDS - Corridor - (3) - Curb` | **3DSOLID ×1** |
| `SOLIDS - Corridor - (3) -` | **3DSOLID ×1** |

→ True solid support. Drape = project onto `Solid3d`/`Body` (BRep / ray).

### DWG2 / production-looking green strip

| Layer | Types |
|-------|--------|
| `SOLIDS - Corridor - (3) - Top_Datum` | **LINE ×15 152** + BODY ×1 + REGION ×3 |

Sample LINE color **3 (green)**. Yellow “diamonds” in UI = shaded/line pattern on that mesh, not separate solids.

→ Support for drape on this seat = **line mesh / segment soup** (optionally plus leftover BODY).

### LOAI vs green Z

| Source | Z |
|--------|---|
| Green lines | ~0.004 … 4.65 m |
| Clean LOAI inserts (many) | **0.0** |

→ Current kè blocks are often **not** on the green face yet — drape is mandatory for “dính”.

## Engine contract (update)

```text
support.kind = solidBody | lineMesh | auto

auto:
  if count(3DSOLID|BODY on pattern) >= 1 and LINE count < threshold → solidBody
  else if LINE count high on pattern → lineMesh
  else → taluy_support_missing
```

### solidBody drape

- Input: handle or layer pattern  
- Query: closest point / downward ray on BRep  
- Fail: `taluy_drape_miss`

### lineMesh drape

- Input: layer pattern (default Top_Datum)  
- Build spatial index of segments (and optional triangles if we pair edges later)  
- For station XY: find nearby segments, interpolate Z (and optional slope from segment)  
- MVP acceptable: **vertical snap to nearest segment Z** within plan radius R (e.g. 0.5–1.0 m)  
- Better: local plane fit of K nearest segments  
- Fail: no segment within R → `taluy_drape_miss`

## bind_surface result shape

```json
{
  "kind": "solidBody|lineMesh",
  "handles": ["..."],
  "layer": "SOLIDS - Corridor - (3) - Top_Datum",
  "entityCounts": {"LINE": 15152, "BODY": 1, "3DSOLID": 0},
  "zRange": [0.004, 4.65],
  "bbox": {}
}
```

## Fixtures

See [`tests/fixtures/taluy/README.md`](../../tests/fixtures/taluy/README.md).

| Fixture | kind |
|---------|------|
| `F-C-COMBO.dwg` / golden | `solidBody` |
| `F-C-GREEN-LINEMESH.dwg` | `lineMesh` (+ residual BODY) |

## Acceptance addition

| Check | Pass |
|-------|------|
| Auto-detect kind | COMBO → solidBody; LINEMESH → lineMesh |
| Drape LOAI on COMBO | maxGapToSurface ≤ 5 mm |
| Drape LOAI on LINEMESH | maxGapToSurface ≤ 5 mm (to mesh Z) |
| No drape | rejected on commit |

## Revision

| Rev | Date | Notes |
|-----|------|-------|
| 0.1 | 2026-07-29 | Line-mesh reality check + fixtures landed |
