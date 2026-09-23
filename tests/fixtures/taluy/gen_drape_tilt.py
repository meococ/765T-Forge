"""Flush GIA CO + safe ROTATE3D onto slope normal. World insert then tilt about IP."""
import csv
import math
from pathlib import Path

greens = [
    (float(r["x"]), float(r["y"]), float(r["z"]))
    for r in csv.DictReader(open("tests/fixtures/taluy/green_mids_all.csv", encoding="utf-8"))
]
cell = 4.0
grid = {}
for g in greens:
    grid.setdefault((int(g[0] // cell), int(g[1] // cell)), []).append(g)


def fit_plane(pts):
    n = len(pts)
    if n < 3:
        return None
    sx = sy = sz = sxx = syy = sxy = sxz = syz = 0.0
    for x, y, z in pts:
        sx += x
        sy += y
        sz += z
        sxx += x * x
        syy += y * y
        sxy += x * y
        sxz += x * z
        syz += y * z
    M = [[sxx, sxy, sx, sxz], [sxy, syy, sy, syz], [sx, sy, float(n), sz]]
    for i in range(3):
        piv = max(range(i, 3), key=lambda r: abs(M[r][i]))
        M[i], M[piv] = M[piv], M[i]
        if abs(M[i][i]) < 1e-12:
            return None
        div = M[i][i]
        for j in range(i, 4):
            M[i][j] /= div
        for r in range(3):
            if r == i:
                continue
            f = M[r][i]
            for j in range(i, 4):
                M[r][j] -= f * M[i][j]
    return M[0][3], M[1][3], M[2][3]


def plane_z(pl, x, y):
    return pl[0] * x + pl[1] * y + pl[2]


def nearest_z(x, y, z0=0.0):
    best = 1e99
    bz = z0
    ix, iy = int(x // cell), int(y // cell)
    for dx in range(-5, 6):
        for dy in range(-5, 6):
            for g in grid.get((ix + dx, iy + dy), []):
                d = (g[0] - x) ** 2 + (g[1] - y) ** 2
                if d < best:
                    best = d
                    bz = g[2]
    return bz


def vnorm(v):
    L = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]) or 1.0
    return (v[0] / L, v[1] / L, v[2] / L)


raw = []
for r in csv.DictReader(open("tests/fixtures/taluy/giaco_inserts.csv", encoding="utf-8")):
    x, y, z = float(r["x"]), float(r["y"]), float(r["z"])
    rot = float(r["rot_deg"])
    if 580000 < x < 582000 and 1476000 < y < 1478000 and -5 < z < 50:
        if rot > 180:
            rot -= 360
        raw.append((x, y, z, rot))
raw = sorted(raw, key=lambda p: p[0])

# LOAI center-span along path ~5.69; use 5.40 so solids overlap slightly
PITCH = 5.40
segs = []
total = 0.0
for a, b in zip(raw, raw[1:]):
    d = math.hypot(b[0] - a[0], b[1] - a[1])
    segs.append((a, b, d, total))
    total += d

stations = []
s = 0.0
while s <= total + 1e-6:
    if s >= total:
        b = raw[-1]
        r = b[3] if b[3] >= 0 else b[3] + 360
        stations.append((b[0], b[1], b[2], r))
        break
    for a, b, d, t0 in segs:
        if t0 <= s <= t0 + d + 1e-9:
            u = 0.0 if d < 1e-9 else (s - t0) / d
            x = a[0] + u * (b[0] - a[0])
            y = a[1] + u * (b[1] - a[1])
            z = a[2] + u * (b[2] - a[2])
            ra, rb = a[3], b[3]
            dr = rb - ra
            if dr > 180:
                dr -= 360
            if dr < -180:
                dr += 360
            r = ra + u * dr
            if r < 0:
                r += 360
            if r >= 360:
                r -= 360
            stations.append((x, y, z, r))
            break
    s += PITCH

PAD = 0.5  # wider fit for slope under full panel
corners = [
    (0 - PAD, -4.263468 - PAD),
    (9.478322 + PAD, -4.263468 - PAD),
    (9.478322 + PAD, 4.677838 + PAD),
    (0 - PAD, 4.677838 + PAD),
]


def Rz(lx, ly, ang, ox, oy):
    a = math.radians(ang)
    c, s = math.cos(a), math.sin(a)
    return ox + lx * c - ly * s, oy + lx * s + ly * c


results = []
for ox, oy, z0, rot in stations:
    cxy = [Rz(lx, ly, rot, ox, oy) for lx, ly in corners]
    xs = [p[0] for p in cxy]
    ys = [p[1] for p in cxy]
    minx, maxx, miny, maxy = min(xs) - 1, max(xs) + 1, min(ys) - 1, max(ys) + 1
    pts = []
    for ix in range(int(minx // cell), int(maxx // cell) + 1):
        for iy in range(int(miny // cell), int(maxy // cell) + 1):
            for g in grid.get((ix, iy), []):
                if minx <= g[0] <= maxx and miny <= g[1] <= maxy:
                    pts.append(g)
    pl = fit_plane(pts) if len(pts) >= 12 else None
    if pl:
        a, b, c = pl
        n = vnorm((-a, -b, 1.0))
        if n[2] < 0:
            n = (-n[0], -n[1], -n[2])
        z_ip = plane_z(pl, ox, oy)
    else:
        n = (0.0, 0.0, 1.0)
        z_ip = nearest_z(ox, oy, z0)
    # axis = Z × n (rotate from world up toward slope normal)
    ax = -n[1]
    ay = n[0]
    az = 0.0
    al = math.sqrt(ax * ax + ay * ay + az * az)
    ang = math.degrees(math.acos(max(-1.0, min(1.0, n[2]))))
    results.append(dict(x=ox, y=oy, z=z_ip, rot=rot, n=n, ax=ax, ay=ay, az=az, al=al, ang=ang, nfit=len(pts)))

angs = [r["ang"] for r in results]
print(
    "n",
    len(results),
    "pitch",
    PITCH,
    "tilt deg med",
    sorted(angs)[len(angs) // 2],
    "max",
    max(angs),
    "nfit med",
    sorted(r["nfit"] for r in results)[len(results) // 2],
)

out = "C:/Users/ADMIN/Downloads/05. 765T-Forge/tests/taluy_ke-2_DRAPE.dwg"
lines = [
    "FILEDIA 0",
    "CMDECHO 0",
    "OSMODE 0",
    "ATTREQ 0",
    "(progn",
    '(princ "\\nTALUY_DRAPE_BEGIN")',
    "(setq er 0)",
    "(foreach bn '(\"BLOCK GIA CO\" \"LOAI 1\" \"LOAI 2\" \"LOAI 3\" \"ke\")",
    '  (setq ss (ssget "_X" (list (cons 0 "INSERT") (cons 2 bn))))',
    "  (if ss (progn (setq i 0 n (sslength ss)) (while (< i n) (entdel (ssname ss i)) (setq er (1+ er) i (1+ i)))))",
    ")",
    '(princ (strcat "\\nTALUY_ERASED=" (itoa er)))',
    '(command "_.-LAYER" "_M" "TALUY-GIACO" "_C" "2" "TALUY-GIACO" "")',
    "(setq placed 0 tilted 0)",
]
for r in results:
    x, y, z, rot = r["x"], r["y"], r["z"], r["rot"]
    lines.append(
        f'(command "_.-INSERT" "BLOCK GIA CO" "{x:.6f},{y:.6f},{z:.6f}" "1" "1" "{rot:.6f}")'
    )
    lines.append("(setq e (entlast) ed (entget e))")
    # force world 210 first (clean OCS)
    lines.append(
        "(if ed (progn"
        ' (setq ed (subst (cons 8 "TALUY-GIACO") (assoc 8 ed) ed))'
        " (if (assoc 210 ed)"
        "   (setq ed (subst (cons 210 (list 0.0 0.0 1.0)) (assoc 210 ed) ed))"
        "   (setq ed (append ed (list (cons 210 (list 0.0 0.0 1.0))))))"
        " (entmod ed) (entupd e)))"
    )
    if r["al"] > 1e-8 and r["ang"] > 0.25:
        # ROTATE3D: base pt, second pt on axis, angle degrees
        lines.append(
            f'(command "_.ROTATE3D" e "" "{x:.6f},{y:.6f},{z:.6f}" '
            f'"{x + r["ax"]:.6f},{y + r["ay"]:.6f},{z + r["az"]:.6f}" "{r["ang"]:.5f}")'
        )
        lines.append("(setq tilted (1+ tilted))")
    lines.append("(setq placed (1+ placed))")

lines += [
    '(princ (strcat "\\nTALUY_PLACED=" (itoa placed)))',
    '(princ (strcat "\\nTALUY_TILTED=" (itoa tilted)))',
    f'(princ (strcat "\\nTALUY_PITCH=" (rtos {PITCH:.4f} 2 4)))',
    # zoom window corridor before save
    '(command "_.ZOOM" "_W" "581050,1476650,0" "581550,1477220,10")',
    f'(command "_.SAVEAS" "" "{out}")',
    '(princ "\\nTALUY_DRAPE_END")',
    "(princ))",
    "QUIT",
    "Y",
]
Path("tests/fixtures/taluy/place_drape_tilt.scr").write_text("\n".join(lines), encoding="utf-8")
print("wrote", len(results), "->", out)
