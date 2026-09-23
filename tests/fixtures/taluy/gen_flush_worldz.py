"""Place BLOCK GIA CO flush on path, Z from green plane, WORLD normal only (no DXF 210)."""
import csv
import math
from pathlib import Path

greens = []
with open("tests/fixtures/taluy/green_mids_all.csv", encoding="utf-8") as f:
    for r in csv.DictReader(f):
        greens.append((float(r["x"]), float(r["y"]), float(r["z"])))
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


raw = []
with open("tests/fixtures/taluy/giaco_inserts.csv", encoding="utf-8") as f:
    for r in csv.DictReader(f):
        x, y, z = float(r["x"]), float(r["y"]), float(r["z"])
        rot = float(r["rot_deg"])
        if 580000 < x < 582000 and 1476000 < y < 1478000 and -5 < z < 50:
            if rot > 180:
                rot -= 360
            raw.append((x, y, z, rot))
raw = sorted(raw, key=lambda p: p[0])

PITCH = 6.090009782147593
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

PAD = 0.2
corners_local = [
    (0.0 - PAD, -4.263468 - PAD),
    (9.478322 + PAD, -4.263468 - PAD),
    (9.478322 + PAD, 4.677838 + PAD),
    (0.0 - PAD, 4.677838 + PAD),
]


def Rz(lx, ly, ang, ox, oy):
    a = math.radians(ang)
    c, s = math.cos(a), math.sin(a)
    return ox + lx * c - ly * s, oy + lx * s + ly * c


results = []
for ox, oy, z0, rot in stations:
    cxy = [Rz(lx, ly, rot, ox, oy) for lx, ly in corners_local]
    xs = [p[0] for p in cxy]
    ys = [p[1] for p in cxy]
    minx, maxx, miny, maxy = min(xs) - 1, max(xs) + 1, min(ys) - 1, max(ys) + 1
    pts = []
    for ix in range(int(minx // cell), int(maxx // cell) + 1):
        for iy in range(int(miny // cell), int(maxy // cell) + 1):
            for g in grid.get((ix, iy), []):
                if minx <= g[0] <= maxx and miny <= g[1] <= maxy:
                    pts.append(g)
    pl = fit_plane(pts) if len(pts) >= 8 else None
    z_ip = plane_z(pl, ox, oy) if pl else nearest_z(ox, oy, z0)
    results.append((ox, oy, z_ip, rot))

print("n", len(results), "pitch", PITCH, "z", min(r[2] for r in results), max(r[2] for r in results))

out_dwg = "C:/Users/ADMIN/Downloads/05. 765T-Forge/tests/taluy ke - 2.dwg"
# write to NEW file then main can replace — also write FLUSH path
out_new = "C:/Users/ADMIN/Downloads/05. 765T-Forge/tests/taluy ke - 2-FIXED.dwg"

lines = [
    "FILEDIA 0",
    "CMDECHO 0",
    "OSMODE 0",
    "ATTREQ 0",
    "UCSWORLD",
    "(progn",
    '(princ "\\nTALUY_FIX_BEGIN")',
    # thaw/on layers
    '(command "_.-LAYER" "_ON" "TALUY-GIACO,SOLIDS - Corridor - (3) - Top_Datum" "_T" "TALUY-GIACO,SOLIDS - Corridor - (3) - Top_Datum" "_U" "TALUY-GIACO" "")',
    "(setq er 0)",
    "(foreach bn '(\"BLOCK GIA CO\" \"LOAI 1\" \"LOAI 2\" \"LOAI 3\" \"ke\")",
    '  (setq ss (ssget "_X" (list (cons 0 "INSERT") (cons 2 bn))))',
    "  (if ss (progn (setq i 0 n (sslength ss)) (while (< i n) (entdel (ssname ss i)) (setq er (1+ er) i (1+ i)))))",
    ")",
    '(princ (strcat "\\nTALUY_ERASED=" (itoa er)))',
    '(command "_.-LAYER" "_M" "TALUY-GIACO" "_C" "2" "TALUY-GIACO" "")',  # yellow visible
    "(setq placed 0)",
]
for x, y, z, rot in results:
    lines.append(
        f'(command "_.-INSERT" "BLOCK GIA CO" "{x:.6f},{y:.6f},{z:.6f}" "1" "1" "{rot:.6f}")'
    )
    lines.append("(setq e (entlast) ed (entget e))")
    # force world normal 210 = 0,0,1 and layer
    lines.append(
        "(if ed (progn"
        ' (setq ed (subst (cons 8 "TALUY-GIACO") (assoc 8 ed) ed))'
        " (if (assoc 210 ed)"
        "   (setq ed (subst (cons 210 (list 0.0 0.0 1.0)) (assoc 210 ed) ed))"
        "   (setq ed (append ed (list (cons 210 (list 0.0 0.0 1.0))))))"
        " (entmod ed) (entupd e) (setq placed (1+ placed))))"
    )

lines += [
    '(princ (strcat "\\nTALUY_PLACED=" (itoa placed)))',
    f'(princ (strcat "\\nTALUY_PITCH=" (rtos {PITCH:.6f} 2 4)))',
    # zoom to known corridor window not full broken extents
    '(command "_.ZOOM" "_W" "581050,1476650" "581550,1477220")',
    f'(command "_.SAVEAS" "" "{out_new}")',
    '(princ "\\nTALUY_FIX_END")',
    "(princ))",
    "QUIT",
    "Y",
]
Path("tests/fixtures/taluy/place_giaco_fix.scr").write_text("\n".join(lines), encoding="utf-8")
print("wrote place_giaco_fix.scr", len(results))
