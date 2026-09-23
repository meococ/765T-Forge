import csv
import math
from pathlib import Path

p = Path(__file__).resolve().parent
ch = list(csv.DictReader(open(p / "giaco_children.csv", encoding="utf-8")))
counts = {}
for c in ch:
    counts[c["name"]] = counts.get(c["name"], 0) + 1
xs = [float(c["x"]) for c in ch]
ys = [float(c["y"]) for c in ch]
xmin, xmax, ymin, ymax = min(xs), max(xs), min(ys), max(ys)
loai2 = [c for c in ch if c["name"] == "LOAI 2"]
steps = []
for a, b in zip(loai2, loai2[1:]):
    d = math.hypot(float(b["x"]) - float(a["x"]), float(b["y"]) - float(a["y"]))
    if 0.05 < d < 0.6:
        steps.append(d)
steps.sort()
# along child rotation axis 310.9961
ang = math.radians(float(ch[0]["rot_deg"]))
ux, uy = math.cos(ang), math.sin(ang)
px, py = -uy, ux
ss = [float(c["x"]) * ux + float(c["y"]) * uy for c in ch]
tt = [float(c["x"]) * px + float(c["y"]) * py for c in ch]
ins = [
    r
    for r in csv.DictReader(open(p / "giaco_inserts.csv", encoding="utf-8"))
    if 580000 < float(r["x"]) < 582000
    and 1476000 < float(r["y"]) < 1478000
    and -5 < float(r["z"]) < 50
]
ins = sorted(ins, key=lambda r: float(r["x"]))
pitches = [
    math.hypot(float(b["x"]) - float(a["x"]), float(b["y"]) - float(a["y"]))
    for a, b in zip(ins, ins[1:])
]
pitches.sort()
til = list(csv.DictReader(open(p / "giaco_tilted_readback.csv", encoding="utf-8")))
nz = sorted(float(r["nz"]) for r in til)
flush_n = sum(1 for _ in open(p / "giaco_flush_plan.csv", encoding="utf-8")) - 1
placed_n = sum(1 for _ in open(p / "giaco_placed.csv", encoding="utf-8")) - 1
# LOAI edge chains
for name in ("LOAI 1", "LOAI 3"):
    arr = [c for c in ch if c["name"] == name]
    ds = []
    for a, b in zip(arr, arr[1:]):
        d = math.hypot(float(b["x"]) - float(a["x"]), float(b["y"]) - float(a["y"]))
        if 0.05 < d < 0.6:
            ds.append(d)
    ds.sort()
    print(name, "n", len(arr), "step_med", ds[len(ds) // 2] if ds else None, "step_n", len(ds))

print("children", len(ch), counts)
print("bbox", xmin, xmax, ymin, ymax, "dx", xmax - xmin, "dy", ymax - ymin)
print(
    "along_rot",
    min(ss),
    max(ss),
    "len",
    max(ss) - min(ss),
    "across",
    min(tt),
    max(tt),
    "len",
    max(tt) - min(tt),
)
print(
    "loai2_step_med",
    steps[len(steps) // 2],
    "min",
    steps[0],
    "max",
    steps[-1],
    "n",
    len(steps),
)
print(
    "first_step",
    math.hypot(
        float(loai2[1]["x"]) - float(loai2[0]["x"]),
        float(loai2[1]["y"]) - float(loai2[0]["y"]),
    ),
)
print(
    "orig_n",
    len(ins),
    "pitch_med",
    pitches[len(pitches) // 2],
    "min",
    pitches[0],
    "max",
    pitches[-1],
    "path",
    sum(pitches),
)
print("flush_plan", flush_n, "placed", placed_n)
print(
    "tilted",
    len(til),
    "nz_med",
    nz[len(nz) // 2],
    "nz_min",
    nz[0],
    "nz_max",
    nz[-1],
    "tilt_med_deg",
    math.degrees(math.acos(max(-1.0, min(1.0, nz[len(nz) // 2])))),
)
print("mirror", sum(1 for c in ch if float(c["sx"]) < 0))
print("child_rot", ch[0]["rot_deg"])
# pitch formula check: panel extent along path
# original path direction from first two clean inserts
if len(ins) >= 2:
    tx = float(ins[1]["x"]) - float(ins[0]["x"])
    ty = float(ins[1]["y"]) - float(ins[0]["y"])
    tl = math.hypot(tx, ty) or 1.0
    tx, ty = tx / tl, ty / tl
    # project child bbox corners
    corners = [(xmin, ymin), (xmax, ymin), (xmax, ymax), (xmin, ymax)]
    projs = [cx * tx + cy * ty for cx, cy in corners]
    print("panel_extent_along_sample_tangent", max(projs) - min(projs))
print("PITCH_FLUSH", 6.090009782147593)
