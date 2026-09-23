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


stations = []
with open("tests/fixtures/taluy/_stations_tmp.csv", encoding="utf-8") as f:
    for r in csv.DictReader(f):
        stations.append((float(r["x"]), float(r["y"]), float(r["z"]), float(r["rot"])))

LA, LB, LC, LD = (0.0, -4.263), (9.478, -4.263), (0.0, 4.678), (9.478, 4.678)


def Rz(lx, ly, ang, ox, oy):
    a = math.radians(ang)
    c, s = math.cos(a), math.sin(a)
    return ox + lx * c - ly * s, oy + lx * s + ly * c


def vnorm(a):
    L = math.sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]) or 1.0
    return (a[0] / L, a[1] / L, a[2] / L)


results = []
for ox, oy, z0, rot in stations:
    corners = [Rz(lx, ly, rot, ox, oy) for lx, ly in (LA, LB, LC, LD)]
    xs = [p[0] for p in corners]
    ys = [p[1] for p in corners]
    minx, maxx, miny, maxy = min(xs) - 2, max(xs) + 2, min(ys) - 2, max(ys) + 2
    pts = []
    for ix in range(int(minx // cell), int(maxx // cell) + 1):
        for iy in range(int(miny // cell), int(maxy // cell) + 1):
            for g in grid.get((ix, iy), []):
                if minx <= g[0] <= maxx and miny <= g[1] <= maxy:
                    pts.append(g)
    pl = fit_plane(pts) if len(pts) >= 8 else None
    if pl:
        # plane z=ax+by+c => normal ~ (-a,-b,1)
        a, b, c = pl
        n = vnorm((-a, -b, 1.0))
        if n[2] < 0:
            n = (-n[0], -n[1], -n[2])
        z_ip = plane_z(pl, ox, oy)
        zs = [plane_z(pl, x, y) for x, y in corners]
        dz = max(zs) - min(zs)
    else:
        n = (0.0, 0.0, 1.0)
        z_ip = nearest_z(ox, oy, z0)
        dz = 0.0
    results.append(dict(x=ox, y=oy, z=z_ip, rot=rot, n=n, dz=dz, nfit=len(pts)))

print(
    "n",
    len(results),
    "dz med",
    sorted(r["dz"] for r in results)[len(results) // 2],
    "nz med",
    sorted(r["n"][2] for r in results)[len(results) // 2],
)

lines = [
    "FILEDIA 0",
    "CMDECHO 0",
    "OSMODE 0",
    "ATTREQ 0",
    "(progn",
    '(princ "\\nTALUY_TILT3_BEGIN")',
    "(setq er 0)",
    "(foreach bn '(\"BLOCK GIA CO\" \"LOAI 1\" \"LOAI 2\" \"LOAI 3\" \"ke\")",
    '  (setq ss (ssget "_X" (list (cons 0 "INSERT") (cons 2 bn))))',
    "  (if ss (progn (setq i 0 n (sslength ss)) (while (< i n) (entdel (ssname ss i)) (setq er (1+ er) i (1+ i)))))",
    ")",
    '(princ (strcat "\\nTALUY_ERASED=" (itoa er)))',
    '(command "_.-LAYER" "_M" "TALUY-GIACO" "_C" "4" "TALUY-GIACO" "")',
    "(setq placed 0)",
]
for r in results:
    x, y, z, rot = r["x"], r["y"], r["z"], r["rot"]
    nx, ny, nz = r["n"]
    lines.append(
        f'(command "_.-INSERT" "BLOCK GIA CO" "{x:.6f},{y:.6f},{z:.6f}" "1" "1" "{rot:.6f}")'
    )
    lines.append("(setq e (entlast) ed (entget e))")
    lines.append(
        f'(if ed (progn'
        f' (setq ed (subst (cons 8 "TALUY-GIACO") (assoc 8 ed) ed))'
        f' (if (assoc 210 ed)'
        f'   (setq ed (subst (cons 210 (list {nx:.7f} {ny:.7f} {nz:.7f})) (assoc 210 ed) ed))'
        f'   (setq ed (append ed (list (cons 210 (list {nx:.7f} {ny:.7f} {nz:.7f}))))))'
        f' (entmod ed) (entupd e) (setq placed (1+ placed))))'
    )

lines += [
    '(princ (strcat "\\nTALUY_PLACED=" (itoa placed)))',
    '(command "_.SAVEAS" "" "C:/Users/ADMIN/Downloads/05. 765T-Forge/tests/fixtures/taluy/F-C-WORKING-giaco.dwg")',
    '(princ "\\nTALUY_TILT3_END")',
    "(princ))",
    "QUIT",
    "Y",
]
Path("tests/fixtures/taluy/place_giaco_tilt3.scr").write_text("\n".join(lines), encoding="utf-8")
with open("tests/fixtures/taluy/giaco_tilt3_plan.csv", "w", encoding="utf-8", newline="") as f:
    w = csv.DictWriter(f, fieldnames=["x", "y", "z", "rot", "nx", "ny", "nz", "dz", "nfit"])
    w.writeheader()
    for r in results:
        w.writerow(
            dict(
                x=r["x"],
                y=r["y"],
                z=r["z"],
                rot=r["rot"],
                nx=r["n"][0],
                ny=r["n"][1],
                nz=r["n"][2],
                dz=r["dz"],
                nfit=r["nfit"],
            )
        )
print("scr ok")
