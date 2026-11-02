"""Semantic grader for the outcome eval. Reads answer.dxf via ezdxf (+ PDF for t7).

Each grader returns {check_name: {"pass": bool, "detail": str}}.
Failures get a taxonomy tag in detail: [missing_tool|unclear_desc|discovery|defaults|bug|too_many_calls|fake|agent_skip]
"""
import ezdxf, json, math, os, re, sys

def ents(doc, layer=None, types=None, space=None):
    out = []
    spaces = [doc.modelspace()] if space == "model" else [doc.modelspace()] + [l for l in doc.layouts if l.name != "Model"]
    for sp in spaces:
        for e in sp:
            if types and e.dxftype() not in types:
                continue
            if layer is not None and e.dxf.layer.upper() != layer.upper():
                continue
            out.append(e)
    return out

def dim_measures(doc):
    """(value, layer, has_override_text) for every DIMENSION entity."""
    out = []
    for e in ents(doc, types=["DIMENSION"]):
        m = getattr(e.dxf, "actual_measurement", None)
        if m is None:
            m = getattr(e.dxf, "measurement", None)
        if m is None:
            try:
                if getattr(e.dxf, "dimtype", 0) & 7 in (0, 1):  # linear/aligned
                    m = math.dist(tuple(e.dxf.defpoint2), tuple(e.dxf.defpoint3))
            except Exception:
                m = None
        txt = (e.dxf.text or "").strip()
        overridden = txt not in ("", "<>")
        out.append((m, e.dxf.layer, overridden))
    return out

def dim_scale_of(doc, e):
    """Effective plotted-text scale for a DIMENSION: style dimscale, else dimtxt."""
    try:
        st = doc.dimstyles.get(e.dxf.dimstyle)
        ds = getattr(st.dxf, "dimscale", None)
        dt = getattr(st.dxf, "dimtxt", None)
        return ds, dt
    except Exception:
        return None, None

def near(v, target, tol):
    return v is not None and abs(v - target) <= tol

def point_in_poly(px, py, poly):
    ins = False
    n = len(poly)
    for i in range(n):
        x1, y1 = poly[i][0], poly[i][1]
        x2, y2 = poly[(i + 1) % n][0], poly[(i + 1) % n][1]
        if (y1 > py) != (y2 > py):
            xint = x1 + (py - y1) * (x2 - x1) / (y2 - y1)
            if px < xint:
                ins = not ins
    return ins

def seg_dist(px, py, ax, ay, bx, by):
    dx, dy = bx - ax, by - ay
    L2 = dx * dx + dy * dy
    t = 0 if L2 == 0 else max(0, min(1, ((px - ax) * dx + (py - ay) * dy) / L2))
    cx, cy = ax + t * dx, ay + t * dy
    return math.hypot(px - cx, py - cy)

def text_of(e):
    if e.dxftype() == "MTEXT":
        return e.text
    if e.dxftype() == "TEXT":
        return e.dxf.text
    if e.dxftype() == "MULTILEADER":
        try:
            if e.context.mtext is not None:
                return e.context.mtext.default_content
        except Exception:
            pass
    return ""

# ---------------- T1 ----------------
def g_t1(doc, rundir):
    c = {}
    dims = dim_measures(doc)
    adims = [e for e in ents(doc, layer="A-DIMS")]
    real = [e for e in adims if e.dxftype() == "DIMENSION"]
    fakes = [e for e in adims if e.dxftype() in ("LINE", "TEXT", "SOLID", "LWPOLYLINE")]
    c["dims_on_A-DIMS"] = {"pass": len(real) >= 1, "detail": f"{len(real)} DIMENSION on A-DIMS (all dims: {len(dims)})"}
    c["real_dimension_objects"] = {"pass": len(dims) >= 3, "detail": f"{len(dims)} DIMENSION entities total"}
    vals = [m for m, _, _ in dims if m is not None]
    c["overall_measured"] = {"pass": any(near(v, 12000, 5) or near(v, 7200, 5) for v in vals),
                             "detail": f"measurements={sorted(round(v,1) for v in vals)}"}
    c["chain_or_opening_measured"] = {"pass": any(near(v, t, 5) for v in vals for t in (800, 900, 3600, 5400)),
                                    "detail": f"measurements={sorted(round(v,1) for v in vals)}"}
    scales = []
    for e in ents(doc, types=["DIMENSION"]):
        ds, dt = dim_scale_of(doc, e)
        if ds:
            scales.append(ds)
        elif dt:
            scales.append(round(dt / 2.5 * 100, 1))  # effective scale if direct dimtxt set
    c["dimscale_for_1to100"] = {"pass": any(80 <= s <= 120 for s in scales),
                                "detail": f"effective dimscales={scales}"}
    c["no_fake_geometry"] = {"pass": len(fakes) == 0,
                             "detail": f"{len(fakes)} non-DIMENSION entities on A-DIMS: {sorted(set(e.dxftype() for e in fakes))}"}
    return c

# ---------------- T2 ----------------
def g_t2(doc, rundir):
    c = {}
    rooms = json.load(open(os.path.join(os.path.dirname(__file__), "seeds", "t2-rooms.json")))
    expected = {}
    for name, spec in rooms.items():
        pts = spec["polygon"]
        a = 0.0
        for i in range(len(pts)):
            x1, y1 = pts[i]; x2, y2 = pts[(i + 1) % len(pts)]
            a += x1 * y2 - x2 * y1
        expected[name] = abs(a) / 2e6  # m2
    mts = [e for e in ents(doc, types=["MTEXT"])]
    anno = [e for e in ents(doc, layer="A-ANNO", types=["MTEXT"])]
    c["mtext_on_A-ANNO"] = {"pass": len(anno) >= 3, "detail": f"{len(anno)} MTEXT on A-ANNO (all mtext: {len(mts)})"}
    found_names, areas_ok, inside = set(), 0, 0
    for e in mts:
        t = e.text.upper()
        for name in expected:
            if name in t:
                found_names.add(name)
                nums = re.findall(r"(\d+(?:\.\d+)?)", e.text)
                if any(near(float(n), expected[name], expected[name] * 0.05 + 0.01) for n in nums):
                    areas_ok += 1
        ip = e.dxf.insert
        if any(point_in_poly(ip.x, ip.y, spec["polygon"]) for spec in rooms.values()):
            inside += 1
    c["all_room_names"] = {"pass": found_names == set(expected), "detail": f"found={sorted(found_names)}"}
    c["correct_areas"] = {"pass": areas_ok >= 3, "detail": f"{areas_ok}/3 labels carry correct area; expected={ {k: round(v,2) for k,v in expected.items()} }"}
    c["labels_inside_rooms"] = {"pass": inside >= 3, "detail": f"{inside}/3+ MTEXT inserts inside room polygons"}
    return c

# ---------------- T3 ----------------
def g_t3(doc, rundir):
    c = {}
    reg = json.load(open(os.path.join(os.path.dirname(__file__), "seeds", "t3-register.json")))
    target = reg["sheet"]
    lay_names = [l.name for l in doc.layouts]
    sheet_lay = None
    for l in doc.layouts:
        if l.name.upper() not in ("MODEL",):
            for e in l:
                if e.dxftype() == "INSERT" and e.dxf.name.upper() == "TBLK":
                    sheet_lay = l
    c["sheet_layout_created"] = {"pass": sheet_lay is not None, "detail": f"layouts={lay_names}; TBLK insert found in {sheet_lay.name if sheet_lay else 'none'}"}
    attrs = {}
    if sheet_lay:
        for e in sheet_lay:
            if e.dxftype() == "INSERT" and e.dxf.name.upper() == "TBLK":
                for a in e.attribs:
                    attrs[a.dxf.tag.upper()] = a.dxf.text
    filled = {k: bool(v.strip()) for k, v in attrs.items()}
    matches = []
    for k in ("SHEET_NO", "SHEET_NAME", "SCALE"):
        rv = target.get(k)
        if rv is not None:
            matches.append(attrs.get(k, "").strip().lower() == str(rv).strip().lower())
    c["titleblock_attrs_filled"] = {"pass": len(attrs) >= 4 and all(filled.get(k, False) for k in ("SHEET_NO", "SHEET_NAME", "SCALE", "DATE")),
                                    "detail": f"attrs={attrs}"}
    c["attrs_match_register"] = {"pass": len(matches) > 0 and all(matches), "detail": f"target={target}; attrs={attrs}"}
    vps = []
    if sheet_lay:
        vps = [e for e in sheet_lay if e.dxftype() == "VIEWPORT" and e.dxf.status == 0 or (e.dxftype() == "VIEWPORT" and getattr(e.dxf, 'id', 1) > 1)]
    scale_ok = any(near(getattr(v.dxf, "custom_scale", 0), 0.02, 0.003) for v in vps)
    c["viewport_1to50"] = {"pass": len(vps) >= 1 and scale_ok,
                           "detail": f"{len(vps)} viewports; scales={[round(getattr(v.dxf,'custom_scale',-1),4) for v in vps]}"}
    return c

# ---------------- T4 ----------------
def g_t4(doc, rundir):
    c = {}
    pls = [e for e in ents(doc, layer="M-DUCT", types=["LWPOLYLINE", "POLYLINE", "LINE"])]
    spans = []
    for p in pls:
        try:
            xs = [v[0] for v in p.get_points()] if p.dxftype() == "LWPOLYLINE" else [p.dxf.start.x, p.dxf.end.x]
            spans.append(max(xs) - min(xs))
        except Exception:
            pass
    c["duct_polyline_full_run"] = {"pass": any(s >= 17000 for s in spans), "detail": f"M-DUCT span(s)={spans}"}
    mls = [e for e in ents(doc, types=["MULTILEADER"])]
    tagged = [e for e in mls if "630" in text_of(e)]
    c["mleader_tags"] = {"pass": len(tagged) >= 2, "detail": f"{len(mls)} MULTILEADER, {len(tagged)} containing '630'"}
    mask = "ungraded"
    try:
        masks = [bool(e.context.mtext.has_bg_fill) for e in tagged if e.context.mtext is not None]
        if masks:
            mask = all(masks)
    except Exception:
        pass
    c["background_mask"] = {"pass": mask if mask != "ungraded" else False,
                            "detail": f"has_bg_fill={mask}" if mask != "ungraded" else "ungradable via ezdxf [grader]"}
    lines = [(e.dxf.start.x, e.dxf.start.y, e.dxf.end.x, e.dxf.end.y) for e in ents(doc, types=["LINE"])]
    crossed = 0
    for e in tagged:
        try:
            locs = [tuple(v)[:2] for v in e.context.leader_line_points] if e.context.leader_line_points else []
            tx, ty = locs[-1] if locs else (0, 0)
        except Exception:
            continue
        if any(seg_dist(tx, ty, *l) < 150 for l in lines):
            crossed += 1
    c["no_text_line_clash"] = {"pass": crossed == 0, "detail": f"{crossed} tag texts within 150mm of a line"}
    return c

# ---------------- T5 ----------------
def g_t5(doc, rundir):
    c = {}
    std = {"A-WALL", "A-DOOR", "A-DIMS", "A-TEXT"}
    old = {"WALL", "WALL-NEW", "WALLS", "DIM-2", "TEXT-XX", "MISC"}
    ms = list(doc.modelspace())
    bad = [e for e in ms if e.dxf.layer.upper() in old or e.dxf.layer.upper() == "0"]
    good = [e for e in ms if e.dxf.layer.upper() in std]
    c["all_entities_on_standard"] = {"pass": len(bad) == 0 and len(good) >= 10,
                                     "detail": f"{len(bad)} entities still on old layers: {sorted(set(e.dxf.layer for e in bad))}"}
    remaining = [l for l in doc.layers if l.dxf.name.upper() in old]
    c["old_layers_removed"] = {"pass": len(remaining) == 0,
                               "detail": f"remaining non-standard layer names: {[l.dxf.name for l in remaining]}"}
    return c

# ---------------- T6 ----------------
def g_t6(doc, rundir):
    c = {}
    dims = dim_measures(doc)
    vals = sorted(v for v, _, _ in dims if v is not None)
    need = [3600.0, 2400.0, 3600.0, 9600.0]
    got = list(vals)
    ok = True
    for n in need:
        hit = next((i for i, v in enumerate(got) if near(v, n, 2)), None)
        if hit is None:
            ok = False
            break
        got.pop(hit)
    c["real_dims_with_true_values"] = {"pass": ok and len(dims) >= 4, "detail": f"measurements={[round(v,1) for v in vals]}"}
    fake = [e for e in ents(doc, layer="A-DIMS") if e.dxftype() in ("LINE", "TEXT", "SOLID", "LWPOLYLINE")]
    c["fake_geometry_removed"] = {"pass": len(fake) == 0, "detail": f"{len(fake)} fake pieces left on A-DIMS"}
    c["dims_on_A-DIMS"] = {"pass": sum(1 for _, l, _ in dims if l.upper() == "A-DIMS") >= 4,
                           "detail": f"{sum(1 for _, l, _ in dims if l.upper()=='A-DIMS')} dims on A-DIMS"}
    return c

# ---------------- T7 ----------------
def g_t7(doc, rundir):
    c = {}
    pdf = os.path.join(rundir, "set.pdf")
    if not os.path.exists(pdf):
        return {"pdf_exists": {"pass": False, "detail": "set.pdf missing"}}
    b = open(pdf, "rb").read()
    pages = len(re.findall(rb"/Type\s*/Page(?![a-zA-Z])", b))
    titles = [t.decode("latin1", "replace") for t in re.findall(rb"/Title\s*\(([^)]*)\)", b)]
    for hx in re.findall(rb"/Title\s*<([0-9a-fA-F]+)>", b):
        try:
            raw = bytes.fromhex(hx.decode())
            titles.append(raw[2:].decode("utf-16-be") if raw[:2] == b"\xfe\xff" else raw.decode("latin1", "replace"))
        except Exception:
            pass
    sheet_titles = [t for t in titles if t.startswith("P")]
    c["pdf_exists"] = {"pass": True, "detail": f"{len(b)} bytes"}
    c["three_pages"] = {"pass": pages == 3, "detail": f"{pages} page objects"}
    c["bookmarks_per_sheet"] = {"pass": len(sheet_titles) >= 3, "detail": f"{len(sheet_titles)} sheet titles: {titles[:8]}"}
    return c

# ---------------- T8 ----------------
def g_t8(doc, rundir):
    c = {}
    ms = list(doc.modelspace())
    c["entities_imported"] = {"pass": len(ms) >= 5, "detail": f"{len(ms)} modelspace entities"}
    c["drawing_opens"] = {"pass": True, "detail": "dxf export read ok"}
    return c

# ==================== GRADER V2 — professional drafting bar ====================
# v2 keeps v1's checks for t5–t8 (they were adequate) but replaces t1–t4 with the
# team-peer bar: completeness, chain sums, collinearity, no overlaps, no dim lines
# through wall interiors, truthful override text, one dimstyle.

def dim_info(e):
    """(measure, style, layer, override_text, p1, p2, angle_deg, dimline_pt, text_pt)."""
    d = e.dxf
    m = getattr(d, "actual_measurement", None) or getattr(d, "measurement", None)
    p1 = tuple(d.defpoint2)[:2] if getattr(d, "defpoint2", None) is not None else None
    p2 = tuple(d.defpoint3)[:2] if getattr(d, "defpoint3", None) is not None else None
    txt = (d.text or "").strip()
    over = txt if txt not in ("", "<>") else None
    dl = tuple(d.defpoint)[:2] if getattr(d, "defpoint", None) is not None else None
    tp = tuple(d.text_midpoint)[:2] if getattr(d, "text_midpoint", None) is not None else None
    return (m, d.dimstyle, d.layer, over, p1, p2, getattr(d, "angle", 0), dl, tp)

def seg_intersect_interior(ax, ay, bx, by, cx, cy, dx, dy, tol=1.0):
    """Do segments ab-cd cross, with the hit strictly inside cd (not at its ends)?"""
    rx, ry = bx - ax, by - ay
    sx, sy = dx - cx, dy - cy
    den = rx * sy - ry * sx
    if abs(den) < 1e-9:
        return False
    t = ((cx - ax) * sy - (cy - ay) * sx) / den
    u = ((cx - ax) * ry - (cy - ay) * rx) / den
    eps = tol / max(1e-9, math.hypot(sx, sy))
    return 0 <= t <= 1 and eps < u < 1 - eps

def text_boxes_close(p1, p2, half=120.0):
    return p1 is not None and p2 is not None and abs(p1[0] - p2[0]) < half and abs(p1[1] - p2[1]) < half

def g2_t1(doc, rundir):
    c = {}
    infos = [dim_info(e) for e in ents(doc, types=["DIMENSION"])]
    adims = [i for i in infos if i[2].upper() == "A-DIMS"]
    vals = [i[0] for i in infos if i[0] is not None]
    n_fake = sum(1 for e in ents(doc, layer="A-DIMS") if e.dxftype() in ("LINE", "TEXT", "SOLID", "LWPOLYLINE"))
    c["all_real_dimensions"] = {"pass": len(adims) >= 5 and n_fake == 0,
                                "detail": f"{len(adims)} DIMENSION on A-DIMS, {n_fake} fake pieces"}
    # completeness: both overall axes (12000, 5400), chain pair ~3600, opening ~900
    c["overall_width_12000"] = {"pass": any(near(v, 12000, 5) for v in vals), "detail": f"vals={sorted(round(v,1) for v in vals)}"}
    c["overall_height_5400"] = {"pass": any(near(v, 5400, 5) for v in vals), "detail": "both plan axes must be dimensioned"}
    segs = sorted(v for v in vals if near(v, 3600, 5))
    c["chain_segments_present"] = {"pass": len(segs) >= 2, "detail": f"~3600 segments: {len(segs)}"}
    c["opening_900"] = {"pass": any(near(v, 900, 5) for v in vals), "detail": "door opening dim present"}
    # chain sums to its overall (3600+3600 = 7200 left-wing extent)
    chain = [i for i in infos if i[0] is not None and near(i[0], 3600, 5)]
    ssum = sum(i[0] for i in chain[:2]) if len(chain) >= 2 else 0
    c["chain_sums_to_7200"] = {"pass": abs(ssum - 7200) <= 1, "detail": f"sum of first two ~3600 segments = {ssum:.1f}"}
    # chain collinear at consistent offset: dimline Y of ~3600 dims equal within 2mm,
    # and projected spans contiguous (abut within 50mm)
    horiz = [i for i in chain if i[4] is not None and i[5] is not None and abs(i[4][1] - i[5][1]) < 50]
    ys = [i[7][1] for i in horiz if i[7] is not None]
    collinear = len(ys) >= 2 and (max(ys) - min(ys)) <= 2
    xs = sorted(min(i[4][0], i[5][0]) for i in horiz)
    gaps = len(xs) < 2 or all(abs(xs[k+1] - xs[k] - 3600) < 60 for k in range(len(xs)-1))
    c["chain_collinear_contiguous"] = {"pass": bool(collinear and gaps),
                                       "detail": f"dimline Ys={[round(y,1) for y in ys]}, x-starts={[round(x,1) for x in xs]}"}
    # no dim line through wall interiors
    walls = [(tuple(w.dxf.start)[:2], tuple(w.dxf.end)[:2]) for w in ents(doc, layer="A-WALL", types=["LINE"])]
    through = 0
    for i in infos:
        if i[7] is None or i[4] is None or i[5] is None:
            continue
        dx, dy = i[5][0] - i[4][0], i[5][1] - i[4][1]
        L = math.hypot(dx, dy)
        if L < 1:
            continue
        ux, uy = dx / L, dy / L
        q1 = (i[7][0], i[7][1])
        q2 = (i[7][0] + ux * i[0], i[7][1] + uy * i[0])
        for (w1, w2) in walls:
            if seg_intersect_interior(q1[0], q1[1], q2[0], q2[1], w1[0], w1[1], w2[0], w2[1]):
                through += 1
                break
    c["no_dim_through_wall"] = {"pass": through == 0, "detail": f"{through} dim lines cross a wall interior"}
    # no overlaps among annotation texts
    pts = [i[8] or i[7] for i in infos]
    for e in ents(doc, types=["TEXT", "MTEXT"]):
        ins = getattr(e.dxf, "insert", None)
        if ins is not None:
            pts.append(tuple(ins)[:2])
    overlaps = sum(1 for a in range(len(pts)) for b in range(a+1, len(pts))
                   if pts[a] and pts[b] and text_boxes_close(pts[a], pts[b]))
    c["no_annotation_overlap"] = {"pass": overlaps == 0, "detail": f"{overlaps} text/dim label pairs closer than 120mm"}
    # truthful override text
    liars = []
    for i in infos:
        if i[3]:
            nums = [float(n) for n in re.findall(r"\d+(?:\.\d+)?", i[3])]
            if "<>" not in i[3] and nums and i[0] is not None and not any(near(n, i[0], 1.5) for n in nums):
                liars.append((i[3], round(i[0], 1)))
            elif "<>" not in i[3] and not nums:
                liars.append((i[3], "non-numeric override"))
    c["no_lying_text"] = {"pass": len(liars) == 0, "detail": f"overrides contradicting measurement: {liars[:4]}"}
    # one consistent dimstyle at sheet scale (1:100 → dimscale≈100 or dimtxt*dimscale≈250)
    styles = set(i[1] for i in infos)
    eff = []
    for i in infos:
        ds, dt = dim_scale_of(doc, next(e for e in ents(doc, types=["DIMENSION"]) if e.dxf.dimstyle == i[1]))
        eff.append((ds, dt))
    scale_ok = any((s and 80 <= s <= 120) or (s and t and 80 <= s*t/2.5 <= 120) for s, t in eff)
    c["one_dimstyle_at_scale"] = {"pass": len(styles) == 1 and scale_ok,
                                  "detail": f"styles={sorted(styles)}, effective={eff[:4]}"}
    return c

def g2_t2(doc, rundir):
    c = {}
    rooms = json.load(open(os.path.join(os.path.dirname(__file__), "seeds", "t2-rooms.json")))
    expected = {}
    for name, spec in rooms.items():
        pts = spec["polygon"]
        a = sum(pts[i][0]*pts[(i+1) % len(pts)][1] - pts[(i+1) % len(pts)][0]*pts[i][1] for i in range(len(pts)))
        expected[name] = abs(a) / 2e6
    mts = [e for e in ents(doc, types=["MTEXT"])]
    name_in_own, area_in_own, areas_right = 0, 0, 0
    inserts = []
    for e in mts:
        t = e.text.upper()
        ip = e.dxf.insert
        inserts.append((ip.x, ip.y))
        for name, spec in rooms.items():
            if name in t and point_in_poly(ip.x, ip.y, spec["polygon"]):
                name_in_own += 1
            if any(near(float(n), expected[name], expected[name]*0.05+0.01) for n in re.findall(r"(\d+(?:\.\d+)?)", e.text)):
                if point_in_poly(ip.x, ip.y, spec["polygon"]) and (name in t or "M2" in t or "²" in t):
                    areas_right += 1
                    area_in_own += 1
                break
    c["room_name_inside_own_room"] = {"pass": name_in_own >= 3, "detail": f"{name_in_own}/3 name labels inside their own room polygon"}
    c["room_area_inside_own_room"] = {"pass": area_in_own >= 3, "detail": f"{area_in_own}/3 area labels inside their own room polygon"}
    c["correct_areas"] = {"pass": areas_right >= 3, "detail": f"{areas_right}/3 correct areas; expected={ {k: round(v,2) for k,v in expected.items()} }"}
    overlaps = sum(1 for a in range(len(inserts)) for b in range(a+1, len(inserts))
                   if abs(inserts[a][0]-inserts[b][0]) < 800 and abs(inserts[a][1]-inserts[b][1]) < 400)
    c["no_annotation_overlap"] = {"pass": overlaps == 0, "detail": f"{overlaps} overlapping label pairs"}
    return c

def g2_t3(doc, rundir):
    c = {}
    reg = json.load(open(os.path.join(os.path.dirname(__file__), "seeds", "t3-register.json")))
    target = reg["sheet"]
    sheet_lay = next((l for l in doc.layouts if l.name == "A1-SHEET"), None)
    c["a1_sheet_layout"] = {"pass": sheet_lay is not None,
                            "detail": f"layouts={[l.name for l in doc.layouts]}"}
    tblks = [e for e in sheet_lay if e.dxftype() == "INSERT" and e.dxf.name.upper() == "TBLK"] if sheet_lay else []
    ms_tblks = [e for e in doc.modelspace() if e.dxftype() == "INSERT" and e.dxf.name.upper() == "TBLK"]
    c["tblk_on_sheet_layout"] = {"pass": len(tblks) >= 1 and len(ms_tblks) == 0,
                                 "detail": f"{len(tblks)} TBLK on A1-SHEET, {len(ms_tblks)} in modelspace"}
    attrs = {}
    for t in tblks:
        for a in t.attribs:
            attrs[a.dxf.tag.upper()] = a.dxf.text
    exact = all(attrs.get(k, "").strip() == str(target.get(k, "")).strip() for k in ("SHEET_NO", "SHEET_NAME", "SCALE", "DATE"))
    c["attrs_exact_register"] = {"pass": len(attrs) >= 4 and exact,
                                 "detail": f"attrs={attrs} vs target={target}"}
    vps = [e for e in sheet_lay if e.dxftype() == "VIEWPORT"] if sheet_lay else []
    scales = [getattr(v.dxf, "custom_scale", None) for v in vps]
    c["viewport_exact_1to50"] = {"pass": any(s is not None and abs(s - 0.02) <= 0.001 for s in scales),
                                 "detail": f"viewports={len(vps)}, custom_scales={scales}"}
    return c

def g2_t4(doc, rundir):
    c = {}
    pls = [e for e in ents(doc, layer="M-DUCT", types=["LWPOLYLINE", "POLYLINE", "LINE"])]
    spans = []
    for p in pls:
        try:
            xs = [v[0] for v in p.get_points()] if p.dxftype() == "LWPOLYLINE" else [p.dxf.start.x, p.dxf.end.x]
            spans.append(max(xs) - min(xs))
        except Exception:
            pass
    c["duct_polyline_full_run"] = {"pass": any(s >= 17000 for s in spans), "detail": f"M-DUCT span(s)={spans}"}
    mls = [e for e in ents(doc, types=["MULTILEADER"])]
    tagged = [e for e in mls if "630" in text_of(e)]
    value_ok = [e for e in tagged if "630" in text_of(e) and "400" in text_of(e)]
    c["tag_values_correct"] = {"pass": len(value_ok) >= 2,
                               "detail": f"{len(mls)} MLEADER; {len(tagged)} with 630; {len(value_ok)} with full 630x400"}
    masks = []
    for e in tagged:
        try:
            if e.context.mtext is not None:
                masks.append(bool(e.context.mtext.has_bg_fill))
        except Exception:
            pass
    lines = [(e.dxf.start.x, e.dxf.start.y, e.dxf.end.x, e.dxf.end.y) for e in ents(doc, types=["LINE"])]
    crossed = 0
    for e in tagged:
        try:
            locs = [tuple(v)[:2] for v in e.context.leader_line_points] if e.context.leader_line_points else []
            tx, ty = locs[-1] if locs else (0, 0)
        except Exception:
            continue
        if any(seg_dist(tx, ty, *l) < 150 for l in lines):
            crossed += 1
    c["no_crossed_or_masked"] = {"pass": crossed == 0 or (masks and all(masks)),
                                 "detail": f"{crossed} tag texts near a line; bg_fill={masks}"}
    return c

GRADERS_V2 = {"t1": g2_t1, "t2": g2_t2, "t3": g2_t3, "t4": g2_t4,
              "t5": g_t5, "t6": g_t6, "t7": g_t7, "t8": g_t8}

GRADERS = {"t1": g_t1, "t2": g_t2, "t3": g_t3, "t4": g_t4,
           "t5": g_t5, "t6": g_t6, "t7": g_t7, "t8": g_t8}

def grade(rundir, task_id, version=1):
    """Grade a finished run dir. Requires answer.dxf (exported by the runner) or set.pdf (t7)."""
    g = (GRADERS_V2 if version == 2 else GRADERS)[task_id]
    dxf = os.path.join(rundir, "answer.dxf")
    dwg = os.path.join(rundir, "answer.dwg")
    if task_id != "t7" and not os.path.exists(dwg) and not os.path.exists(dxf):
        return {"answer_saved": {"pass": False, "detail": "answer.dwg missing — agent never saved [agent_skip]"}}
    if not os.path.exists(dxf) and os.path.exists(dwg) and task_id != "t7":
        return {"answer_saved": {"pass": False, "detail": "answer.dwg exists but no dxf export [grader]"}}
    doc = ezdxf.readfile(dxf) if os.path.exists(dxf) else None
    if doc is None and task_id != "t7":
        return {"answer_saved": {"pass": False, "detail": "no readable dxf [grader]"}}
    if task_id == "t7" and doc is None and not os.path.exists(os.path.join(rundir, "set.pdf")):
        return {"pdf_exists": {"pass": False, "detail": "set.pdf missing"}}
    try:
        res = g(doc, rundir)
    except Exception as e:
        return {"grader_error": {"pass": False, "detail": f"{type(e).__name__}: {e} [grader]"}}
    if os.path.exists(dwg) or os.path.exists(dxf):
        res = {"answer_saved": {"pass": True, "detail": ""}, **res}
    return res

if __name__ == "__main__":
    rundir, task = sys.argv[1], sys.argv[2]
    ver = int(sys.argv[3]) if len(sys.argv) > 3 else 1
    print(json.dumps(grade(rundir, task, ver), indent=1))
