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
    return bz, math.sqrt(best)


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
    M = [
        [sxx, sxy, sx, sxz],
        [sxy, syy, sy, syz],
        [sx, sy, float(n), sz],
    ]
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


def plane_z(plane, x, y):
    a, b, c = plane
    return a * x + b * y + c


stations = []
with open("tests/fixtures/taluy/_stations_tmp.csv", encoding="utf-8") as f:
    for r in csv.DictReader(f):
        stations.append((float(r["x"]), float(r["y"]), float(r["z"]), float(r["rot"])))

LA = (0.0, -4.263)
LB = (9.478, -4.263)
LC = (0.0, 4.678)
LD = (9.478, 4.678)
locals_pts = [LA, LB, LC, LD]


def Rz(lx, ly, ang, ox, oy):
    a = math.radians(ang)
    c, s = math.cos(a), math.sin(a)
    return ox + lx * c - ly * s, oy + lx * s + ly * c


def vsub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def vdot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def vcross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def vnorm(a):
    L = math.sqrt(vdot(a, a)) or 1.0
    return (a[0] / L, a[1] / L, a[2] / L)


def rigid_from_3(src, dst):
    sc = tuple(sum(p[i] for p in src) / 3 for i in range(3))
    dc = tuple(sum(p[i] for p in dst) / 3 for i in range(3))
    ns = vnorm(vcross(vsub(src[1], src[0]), vsub(src[2], src[0])))
    xs = vnorm(vsub(src[1], src[0]))
    ys = vnorm(vcross(ns, xs))
    nd = vnorm(vcross(vsub(dst[1], dst[0]), vsub(dst[2], dst[0])))
    xd = vnorm(vsub(dst[1], dst[0]))
    yd = vnorm(vcross(nd, xd))
    Rs = [[xs[0], ys[0], ns[0]], [xs[1], ys[1], ns[1]], [xs[2], ys[2], ns[2]]]
    Rd = [[xd[0], yd[0], nd[0]], [xd[1], yd[1], nd[1]], [xd[2], yd[2], nd[2]]]
    R = [[0.0] * 3 for _ in range(3)]
    for i in range(3):
        for j in range(3):
            R[i][j] = sum(Rd[i][k] * Rs[j][k] for k in range(3))
    Rsc = (
        sum(R[0][k] * sc[k] for k in range(3)),
        sum(R[1][k] * sc[k] for k in range(3)),
        sum(R[2][k] * sc[k] for k in range(3)),
    )
    t = vsub(dc, Rsc)
    return R, t


def apply_Rt(R, t, p):
    return (
        R[0][0] * p[0] + R[0][1] * p[1] + R[0][2] * p[2] + t[0],
        R[1][0] * p[0] + R[1][1] * p[1] + R[1][2] * p[2] + t[1],
        R[2][0] * p[0] + R[2][1] * p[1] + R[2][2] * p[2] + t[2],
    )


results = []
for ox, oy, z0, rot in stations:
    corners_xy = [Rz(lx, ly, rot, ox, oy) for lx, ly in locals_pts]
    xs = [p[0] for p in corners_xy]
    ys = [p[1] for p in corners_xy]
    minx, maxx = min(xs) - 2, max(xs) + 2
    miny, maxy = min(ys) - 2, max(ys) + 2
    pts = []
    for ix in range(int(minx // cell), int(maxx // cell) + 1):
        for iy in range(int(miny // cell), int(maxy // cell) + 1):
            for g in grid.get((ix, iy), []):
                if minx <= g[0] <= maxx and miny <= g[1] <= maxy:
                    pts.append(g)
    plane = fit_plane(pts) if len(pts) >= 8 else None

    def z_at(x, y):
        if plane:
            return plane_z(plane, x, y)
        return nearest_z(x, y, z0)[0]

    def wflat(lx, ly):
        x, y = Rz(lx, ly, rot, ox, oy)
        return (x, y, 0.0)

    s1, s2, s3 = wflat(*LA), wflat(*LB), wflat(*LC)
    d1 = (s1[0], s1[1], z_at(s1[0], s1[1]))
    d2 = (s2[0], s2[1], z_at(s2[0], s2[1]))
    d3 = (s3[0], s3[1], z_at(s3[0], s3[1]))
    R, t = rigid_from_3([s1, s2, s3], [d1, d2, d3])
    origin = apply_Rt(R, t, (ox, oy, 0.0))
    za = (
        R[0][2],
        R[1][2],
        R[2][2],
    )
    za = vnorm(za)
    # flip if pointing down
    if za[2] < 0:
        za = (-za[0], -za[1], -za[2])
    dz = max(d1[2], d2[2], d3[2]) - min(d1[2], d2[2], d3[2])
    results.append(
        dict(origin=origin, za=za, rot=rot, dz=dz, nfit=len(pts), d1=d1, d2=d2, d3=d3)
    )

print("n", len(results), "dz med", sorted(r["dz"] for r in results)[len(results) // 2])
angs = [math.degrees(math.acos(max(-1, min(1, r["za"][2])))) for r in results]
print("tilt deg min/med/max", min(angs), sorted(angs)[len(angs) // 2], max(angs))

lines = [
    "FILEDIA 0",
    "CMDECHO 0",
    "OSMODE 0",
    "ATTREQ 0",
    "(progn",
    '(princ "\\nTALUY_TILT2_BEGIN")',
    "(setq er 0)",
    "(foreach bn '(\"BLOCK GIA CO\" \"LOAI 1\" \"LOAI 2\" \"LOAI 3\" \"ke\")",
    '  (setq ss (ssget "_X" (list (cons 0 "INSERT") (cons 2 bn))))',
    "  (if ss (progn (setq i 0 n (sslength ss)) (while (< i n) (entdel (ssname ss i)) (setq er (1+ er) i (1+ i)))))",
    ")",
    '(princ (strcat "\\nTALUY_ERASED=" (itoa er)))',
    '(command "_.-LAYER" "_M" "TALUY-GIACO" "_C" "4" "TALUY-GIACO" "")',
    "(setq placed 0 tilted 0)",
]
for r in results:
    o = r["origin"]
    za = r["za"]
    rot = r["rot"]
    lines.append(
        f'(command "_.-INSERT" "BLOCK GIA CO" "{o[0]:.6f},{o[1]:.6f},{o[2]:.6f}" "1" "1" "{rot:.6f}")'
    )
    lines.append("(setq e (entlast) ed (entget e))")
    lines.append('(if ed (entmod (subst (cons 8 "TALUY-GIACO") (assoc 8 ed) ed)))')
    # axis = cross((0,0,1), za)
    cx, cy, cz = -za[1], za[0], 0.0
    cl = math.sqrt(cx * cx + cy * cy + cz * cz)
    ang = math.degrees(math.acos(max(-1.0, min(1.0, za[2]))))
    if cl > 1e-8 and ang > 0.05:
        lines.append(
            f'(command "_.ROTATE3D" e "" "{o[0]:.6f},{o[1]:.6f},{o[2]:.6f}" '
            f'"{o[0] + cx:.6f},{o[1] + cy:.6f},{o[2] + cz:.6f}" "{ang:.6f}")'
        )
        lines.append("(setq tilted (1+ tilted))")
    lines.append("(setq placed (1+ placed))")

lines += [
    '(princ (strcat "\\nTALUY_PLACED=" (itoa placed)))',
    '(princ (strcat "\\nTALUY_TILTED=" (itoa tilted)))',
    '(command "_.SAVEAS" "" "C:/Users/ADMIN/Downloads/05. 765T-Forge/tests/fixtures/taluy/F-C-WORKING-giaco.dwg")',
    '(princ "\\nTALUY_TILT2_END")',
    "(princ))",
    "QUIT",
    "Y",
]
Path("tests/fixtures/taluy/place_giaco_tilt2.scr").write_text("\n".join(lines), encoding="utf-8")
print("wrote scr", len(lines))
