"""Generate one-row taluy block placements conforming to the ke-2 line-mesh support."""

from __future__ import annotations

import argparse
import bisect
import csv
import math
from collections import Counter, defaultdict
from pathlib import Path

UNIT_X = 0.399916
UNIT_Y = 0.449905
SUPPORT_LAYER = "SOLIDS - Corridor - (3) - Top_Datum"
OUTPUT_LAYER = "TALUY-GIACO"
SITE_FILTER = (580000.0, 582000.0, 1476000.0, 1478000.0, -5.0, 50.0)


def add(a, b):
    return tuple(a[i] + b[i] for i in range(3))


def sub(a, b):
    return tuple(a[i] - b[i] for i in range(3))


def mul(a, k):
    return tuple(v * k for v in a)


def dot(a, b):
    return sum(a[i] * b[i] for i in range(3))


def cross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def length(a):
    return math.sqrt(dot(a, a))


def normalize(a):
    n = length(a)
    if n <= 1e-12:
        raise ValueError("zero-length vector")
    return mul(a, 1.0 / n)


def lerp(a, b, u):
    return add(a, mul(sub(b, a), u))


def qpoint(p):
    return tuple(round(v, 5) for v in p)


def ocs_axes(normal):
    """AutoCAD Arbitrary Axis Algorithm for a normalized OCS normal."""
    nx, ny, _ = normal
    if abs(nx) < 1.0 / 64.0 and abs(ny) < 1.0 / 64.0:
        axis_x = normalize(cross((0.0, 1.0, 0.0), normal))
    else:
        axis_x = normalize(cross((0.0, 0.0, 1.0), normal))
    axis_y = normalize(cross(normal, axis_x))
    return axis_x, axis_y


def load_support(path):
    rows = list(csv.DictReader(path.open(encoding="utf-8")))
    raw_edges = []
    for row in rows:
        a = qpoint((float(row["x1"]), float(row["y1"]), float(row["z1"])))
        b = qpoint((float(row["x2"]), float(row["y2"]), float(row["z2"])))
        raw_edges.append(tuple(sorted((a, b))))

    unique_edges = Counter(raw_edges)
    rungs = [edge for edge in unique_edges if length(sub(edge[1], edge[0])) > 5.0]
    rails = [edge for edge in unique_edges if length(sub(edge[1], edge[0])) <= 5.0]

    node_to_rung = {}
    for index, edge in enumerate(rungs):
        for node in edge:
            if node in node_to_rung:
                raise ValueError(f"support node belongs to multiple cross-sections: {node}")
            node_to_rung[node] = index

    rung_graph = defaultdict(set)
    for a, b in rails:
        ia, ib = node_to_rung[a], node_to_rung[b]
        if ia != ib:
            rung_graph[ia].add(ib)
            rung_graph[ib].add(ia)

    starts = [index for index in range(len(rungs)) if len(rung_graph[index]) == 1]
    seen = set()
    chains = []
    for start in starts:
        if start in seen:
            continue
        chain = []
        previous = None
        current = start
        while current is not None and current not in seen:
            seen.add(current)
            chain.append(current)
            candidates = [n for n in rung_graph[current] if n != previous and n not in seen]
            previous, current = current, candidates[0] if candidates else None
        chains.append(chain)

    if len(seen) != len(rungs):
        raise ValueError(f"unordered support cross-sections: {len(rungs) - len(seen)}")

    ordered_chains = []
    for chain in chains:
        sections = []
        for index in chain:
            a, b = rungs[index]
            high, low = (a, b) if a[2] >= b[2] else (b, a)
            sections.append((high, low))
        if sections[0][0][0] > sections[-1][0][0]:
            sections.reverse()
        ordered_chains.append(sections)

    ordered_chains.sort(key=lambda sections: sum(high[0] for high, _ in sections) / len(sections))
    surface = [section for sections in ordered_chains for section in sections]
    return surface, unique_edges, rungs, rails


def load_design_domain(path):
    xmin, xmax, ymin, ymax, zmin, zmax = SITE_FILTER
    points = []
    for row in csv.DictReader(path.open(encoding="utf-8")):
        x, y, z = float(row["x"]), float(row["y"]), float(row["z"])
        if xmin < x < xmax and ymin < y < ymax and zmin < z < zmax:
            points.append((x, y, z))
    if len(points) < 2:
        raise ValueError("not enough clean original GIA CO stations")
    points.sort(key=lambda p: p[0])
    return points


def nearest_high_index(surface, point):
    return min(
        range(len(surface)),
        key=lambda index: math.hypot(surface[index][0][0] - point[0], surface[index][0][1] - point[1]),
    )


def sample_rows(surface, design_points):
    first = nearest_high_index(surface, design_points[0])
    last = nearest_high_index(surface, design_points[-1])
    if first > last:
        first, last = last, first
    cropped = surface[first : last + 1]

    cumulative = [0.0]
    for index in range(1, len(cropped)):
        cumulative.append(cumulative[-1] + length(sub(cropped[index][0], cropped[index - 1][0])))
    path_length = cumulative[-1]

    rows = []
    station = 0.0
    while station <= path_length + 1e-9:
        if station >= path_length:
            segment = len(cropped) - 2
            u = 1.0
        else:
            segment = max(0, min(len(cropped) - 2, bisect.bisect_right(cumulative, station) - 1))
            span = cumulative[segment + 1] - cumulative[segment]
            u = 0.0 if span <= 1e-12 else (station - cumulative[segment]) / span

        high = lerp(cropped[segment][0], cropped[segment + 1][0], u)
        low = lerp(cropped[segment][1], cropped[segment + 1][1], u)
        tangent = normalize(sub(cropped[segment + 1][0], cropped[segment][0]))
        cross_slope = normalize(sub(low, high))
        tangent = normalize(sub(tangent, mul(cross_slope, dot(tangent, cross_slope))))
        normal = normalize(cross(tangent, cross_slope))
        if normal[2] < 0.0:
            tangent = mul(tangent, -1.0)
            normal = mul(normal, -1.0)
        cross_slope = normalize(cross(normal, tangent))

        width = length(sub(low, high))
        unit_count = max(1, int(math.floor(width / UNIT_Y + 0.5)))
        row_width = unit_count * UNIT_Y
        residual = width - row_width
        base_wcs = add(high, mul(cross_slope, residual / 2.0))

        ocs_x, ocs_y = ocs_axes(normal)
        rotation = math.atan2(dot(tangent, ocs_y), dot(tangent, ocs_x))
        check_y = add(mul(ocs_x, -math.sin(rotation)), mul(ocs_y, math.cos(rotation)))
        if dot(check_y, cross_slope) < 0.999999:
            raise ValueError("computed OCS rotation does not reproduce cross-slope axis")
        base_ocs = (dot(base_wcs, ocs_x), dot(base_wcs, ocs_y), dot(base_wcs, normal))

        rows.append(
            {
                "station": station,
                "count": unit_count,
                "width": width,
                "row_width": row_width,
                "edge_residual": residual,
                "high": high,
                "low": low,
                "base_wcs": base_wcs,
                "base_ocs": base_ocs,
                "tangent": tangent,
                "cross_slope": cross_slope,
                "normal": normal,
                "rotation": rotation,
            }
        )
        station += UNIT_X

    return rows, first, last, path_length


def write_plan(path, rows):
    fields = [
        "station_m", "block", "unit_count", "support_width_m", "row_width_m", "edge_residual_m",
        "base_wcs_x", "base_wcs_y", "base_wcs_z", "base_ocs_x", "base_ocs_y", "base_ocs_z",
        "normal_x", "normal_y", "normal_z", "rotation_rad",
        "high_x", "high_y", "high_z", "low_x", "low_y", "low_z",
    ]
    with path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        for row in rows:
            writer.writerow(
                {
                    "station_m": f"{row['station']:.6f}",
                    "block": f"TALUY-ROW-{row['count']}",
                    "unit_count": row["count"],
                    "support_width_m": f"{row['width']:.6f}",
                    "row_width_m": f"{row['row_width']:.6f}",
                    "edge_residual_m": f"{row['edge_residual']:.6f}",
                    "base_wcs_x": f"{row['base_wcs'][0]:.8f}",
                    "base_wcs_y": f"{row['base_wcs'][1]:.8f}",
                    "base_wcs_z": f"{row['base_wcs'][2]:.8f}",
                    "base_ocs_x": f"{row['base_ocs'][0]:.8f}",
                    "base_ocs_y": f"{row['base_ocs'][1]:.8f}",
                    "base_ocs_z": f"{row['base_ocs'][2]:.8f}",
                    "normal_x": f"{row['normal'][0]:.10f}",
                    "normal_y": f"{row['normal'][1]:.10f}",
                    "normal_z": f"{row['normal'][2]:.10f}",
                    "rotation_rad": f"{row['rotation']:.10f}",
                    "high_x": f"{row['high'][0]:.8f}",
                    "high_y": f"{row['high'][1]:.8f}",
                    "high_z": f"{row['high'][2]:.8f}",
                    "low_x": f"{row['low'][0]:.8f}",
                    "low_y": f"{row['low'][1]:.8f}",
                    "low_z": f"{row['low'][2]:.8f}",
                }
            )


def lisp_point(point):
    return "(list " + " ".join(f"{value:.10f}" for value in point) + ")"


def block_definition_lines(unit_count):
    name = f"TALUY-ROW-{unit_count}"
    lines = [
        f'  (if (not (tblsearch "BLOCK" "{name}"))',
        "    (progn",
        "      (entmake (list (cons 0 \"BLOCK\") (cons 100 \"AcDbEntity\") (cons 8 \"0\")",
        f'        (cons 100 "AcDbBlockBegin") (cons 2 "{name}") (cons 70 0) (cons 10 (list 0.0 0.0 0.0))))',
    ]
    for index in range(unit_count):
        y = index * UNIT_Y
        lines.append(
            "      (entmake (list (cons 0 \"INSERT\") (cons 100 \"AcDbEntity\") (cons 8 \"0\") "
            f'(cons 100 "AcDbBlockReference") (cons 2 "LOAI 2") (cons 10 (list 0.0 {y:.10f} 0.0)) '
            "(cons 41 1.0) (cons 42 1.0) (cons 43 1.0) (cons 50 0.0) (cons 210 (list 0.0 0.0 1.0))))"
        )
    lines += [
        "      (entmake (list (cons 0 \"ENDBLK\") (cons 100 \"AcDbEntity\") (cons 8 \"0\") (cons 100 \"AcDbBlockEnd\")))",
        "    )",
        "  )",
    ]
    return lines


def write_script(path, rows, output_dwg, prototype_count):
    selected = rows if prototype_count <= 0 else [rows[i] for i in sorted(set((0, len(rows) // 2, len(rows) - 1)))][:prototype_count]
    counts = sorted({row["count"] for row in rows})
    lines = [
        "FILEDIA 0",
        "CMDECHO 0",
        "OSMODE 0",
        "ATTREQ 0",
        "(progn",
        "  (princ \"\\nTALUY_ROWS_BEGIN\")",
        "  (setq erased 0 i 0)",
        "  (setq ss (ssget \"_X\" '((0 . \"INSERT\"))))",
        "  (if ss",
        "    (progn",
        "      (setq n (sslength ss))",
        "      (while (< i n)",
        "        (setq e (ssname ss i) nm (cdr (assoc 2 (entget e))))",
        "        (if (wcmatch nm \"BLOCK GIA CO,LOAI 1,LOAI 2,LOAI 3,ke,TALUY-ROW-*\")",
        "          (progn (entdel e) (setq erased (1+ erased))))",
        "        (setq i (1+ i))))",
        "  )",
        "  (command \"_.-LAYER\" \"_M\" \"TALUY-GIACO\" \"_C\" \"2\" \"TALUY-GIACO\" \"\")",
    ]
    for count in counts:
        lines.extend(block_definition_lines(count))
    lines += ["  (setq placed 0)"]
    for row in selected:
        lines += [
            "  (setq made (entmake (list (cons 0 \"INSERT\") (cons 100 \"AcDbEntity\")",
            "    (cons 8 \"TALUY-GIACO\") (cons 100 \"AcDbBlockReference\")",
            f'    (cons 2 "TALUY-ROW-{row["count"]}") (cons 10 {lisp_point(row["base_ocs"])})',
            "    (cons 41 1.0) (cons 42 1.0) (cons 43 1.0)",
            f'    (cons 50 {row["rotation"]:.10f}) (cons 210 {lisp_point(row["normal"])}))))',
            "  (if made (setq placed (1+ placed)))",
        ]
    lines += [
        "  (command \"_.REGEN\")",
        "  (command \"_.ZOOM\" \"_E\")",
        "  (princ (strcat \"\\nTALUY_ROWS_ERASED=\" (itoa erased)))",
        "  (princ (strcat \"\\nTALUY_ROWS_PLACED=\" (itoa placed)))",
        f'  (command "_.SAVEAS" "" "{output_dwg.as_posix()}")',
        "  (princ \"\\nTALUY_ROWS_END\")",
        "  (princ)",
        ")",
        "QUIT",
        "Y",
    ]
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return len(selected), counts


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--prototype-count", type=int, default=0)
    parser.add_argument("--output-dwg", type=Path, required=True)
    parser.add_argument("--output-scr", type=Path, required=True)
    parser.add_argument("--output-plan", type=Path, required=True)
    args = parser.parse_args()

    root = Path(__file__).resolve().parent
    surface, unique_edges, rungs, rails = load_support(root / "green_lines_full.csv")
    design = load_design_domain(root / "giaco_inserts.csv")
    rows, first, last, path_length = sample_rows(surface, design)
    write_plan(args.output_plan, rows)
    selected_count, counts = write_script(args.output_scr, rows, args.output_dwg.resolve(), args.prototype_count)

    residuals = [abs(row["edge_residual"]) / 2.0 for row in rows]
    normals = [row["normal"][2] for row in rows]
    print(f"support_edges={len(unique_edges)} rungs={len(rungs)} rails={len(rails)}")
    print(f"domain_indices={first}:{last} path_m={path_length:.6f}")
    print(f"rows={len(rows)} emitted={selected_count} variants={counts}")
    print(f"units_total={sum(row['count'] for row in rows)}")
    print(f"edge_error_max_m={max(residuals):.6f} normal_z={min(normals):.6f}..{max(normals):.6f}")


if __name__ == "__main__":
    main()
