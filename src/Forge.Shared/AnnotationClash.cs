using System.Globalization;

namespace Forge.Shared;

/// <summary>What a linework item represents for the clash checks.</summary>
public enum ClashCurveKind
{
    Generic,
    Wall,
    DoorSwing,
    Leader
}

/// <summary>
/// One annotation box in world XY: TEXT/ATTRIB/MTEXT/MLEADER content/dimension text. Corners is a
/// convex quad (a rectangle, or a parallelogram under a skewing block transform), any winding.
/// </summary>
public sealed record ClashText
{
    public string Handle { get; init; } = "";
    public string Type { get; init; } = "";
    public string Layer { get; init; } = "";
    public string Text { get; init; } = "";
    public Vec2[] Corners { get; init; } = [];

    /// <summary>World text height (drawing units) — the min-height check and crossing tolerance use it.</summary>
    public double Height { get; init; }

    /// <summary>Background mask / dimension text fill: crossings behind it do not show on paper.</summary>
    public bool Masked { get; init; }

    /// <summary>Composite this text belongs to (innermost INSERT, DIMENSION or MLEADER handle).</summary>
    public string? Owner { get; init; }

    /// <summary>Largest world extent of <see cref="Owner"/>; 0 = always a small group (dimension, mleader).</summary>
    public double OwnerSize { get; init; }

    public string? Path { get; init; }
}

/// <summary>One piece of linework as a world-space polyline (arcs pre-tessellated).</summary>
public sealed record ClashCurve
{
    public string Handle { get; init; } = "";
    public string Type { get; init; } = "";
    public string Layer { get; init; } = "";
    public Vec2[] Points { get; init; } = [];
    public bool Closed { get; init; }
    public ClashCurveKind Kind { get; init; }

    /// <summary>Arc centre in world space — with <see cref="Points"/> it spans the swing sector of a door arc.</summary>
    public Vec2? SectorApex { get; init; }

    public string? Owner { get; init; }
    public double OwnerSize { get; init; }
    public string? Path { get; init; }
}

public sealed record ClashOptions
{
    public bool TextOverlap { get; init; } = true;
    public bool TextCrossing { get; init; } = true;
    public bool DoorSwing { get; init; } = true;
    public bool WallStraddle { get; init; } = true;
    public bool MinHeight { get; init; }

    /// <summary>Overlap area / smaller box area at or above which two texts clash.</summary>
    public double MinOverlapRatio { get; init; } = 0.02;

    /// <summary>A curve must run inside a text box for at least this fraction of the text height.</summary>
    public double MinCrossFraction { get; init; } = 0.05;

    /// <summary>Clearance added around every text box (drawing units).</summary>
    public double Padding { get; init; }

    /// <summary>Linework of the text's own composite is ignored when that composite is at most this many text heights big.</summary>
    public double GroupSizeFactor { get; init; } = 20;

    public double? MinPlottedHeightMm { get; init; }

    /// <summary>Drawing units per plotted millimetre (1:100 in a mm drawing → 100; in a metre drawing → 0.1).</summary>
    public double? UnitsPerPaperMm { get; init; }

    /// <summary>Report generic lines crossing masked text too (they are hidden on paper, so off by default).</summary>
    public bool ReportMaskedCrossings { get; init; }

    public int MaxFindings { get; init; } = 200;
}

public sealed record ClashFinding(
    string Check,
    string Severity,
    string Message,
    string[] Handles,
    string[] Layers,
    string?[] Texts,
    double[] At,
    double? Overlap,
    double? Ratio,
    bool? Masked,
    string?[]? Paths);

public sealed record ClashReport(
    int Texts,
    int Curves,
    int Segments,
    IReadOnlyDictionary<string, int> Counts,
    IReadOnlyList<ClashFinding> Findings,
    bool Truncated,
    int MaskedCrossingsSkipped,
    int SameGroupPairsSkipped);

/// <summary>
/// Pure annotation-clash analysis for forge_qa_annotation_clash. The plugin extracts world-space
/// text quads and tessellated linework (block-aware); every decision — overlap, crossing, door
/// swing, wall straddle, plotted height — is made here on plain numbers so it is unit-tested.
/// Convex-polygon maths: SAT for the yes/no test, Sutherland–Hodgman clipping for overlap area,
/// Cyrus–Beck for the length of a segment inside a box. A uniform grid keeps it near-linear.
/// </summary>
public static class AnnotationClash
{
    public const string CheckTextOverlap = "text_overlap";
    public const string CheckTextCrossing = "text_crossing";
    public const string CheckDoorSwing = "door_swing";
    public const string CheckWallStraddle = "wall_straddle";
    public const string CheckMinHeight = "min_height";

    public static readonly string[] AllChecks = [CheckTextOverlap, CheckTextCrossing, CheckDoorSwing, CheckWallStraddle, CheckMinHeight];

    private const double Eps = 1e-12;
    private const int MaxCellsPerItem = 4096;

    // ===================== geometry =====================

    /// <summary>Corners of a w×h rectangle centred on (cx,cy), rotated by angle (radians), CCW.</summary>
    public static Vec2[] RectCorners(double cx, double cy, double width, double height, double angle)
    {
        var c = Math.Cos(angle);
        var s = Math.Sin(angle);
        var hw = width / 2;
        var hh = height / 2;
        return
        [
            new Vec2(cx - hw * c + hh * s, cy - hw * s - hh * c),
            new Vec2(cx + hw * c + hh * s, cy + hw * s - hh * c),
            new Vec2(cx + hw * c - hh * s, cy + hw * s + hh * c),
            new Vec2(cx - hw * c - hh * s, cy - hw * s + hh * c)
        ];
    }

    /// <summary>
    /// Recovers the oriented box of a rotated text from its axis-aligned extents. For a w×h
    /// rectangle at angle θ: W = w|c| + h|s| and H = w|s| + h|c|, solved for w and h. Near 45°
    /// the system is singular, so the known text height fixes h and w follows from W.
    /// </summary>
    public static Vec2[] FromRotatedAabb(double minX, double minY, double maxX, double maxY, double angle, double fallbackHeight)
    {
        var bigW = Math.Max(0, maxX - minX);
        var bigH = Math.Max(0, maxY - minY);
        var cx = (minX + maxX) / 2;
        var cy = (minY + maxY) / 2;
        var c = Math.Abs(Math.Cos(angle));
        var s = Math.Abs(Math.Sin(angle));
        var det = c * c - s * s;
        double w, h;
        if (Math.Abs(det) > 0.2)
        {
            w = (bigW * c - bigH * s) / det;
            h = (bigH * c - bigW * s) / det;
        }
        else
        {
            h = fallbackHeight > 0 ? fallbackHeight : Math.Min(bigW, bigH);
            w = c > Eps ? (bigW - h * s) / c : bigH;
        }

        if (!(w > 0) || !(h > 0) || !double.IsFinite(w) || !double.IsFinite(h))
        {
            // Degenerate solve (empty text, rounding): fall back to the axis-aligned box.
            return RectCorners(cx, cy, bigW, bigH, 0);
        }

        return RectCorners(cx, cy, w, h, angle);
    }

    /// <summary>Signed area (positive when counter-clockwise).</summary>
    public static double SignedArea(IReadOnlyList<Vec2> poly)
    {
        double sum = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            sum += a.X * b.Y - b.X * a.Y;
        }

        return sum / 2;
    }

    public static Vec2[] EnsureCcw(Vec2[] poly)
    {
        if (SignedArea(poly) >= 0)
        {
            return poly;
        }

        var copy = (Vec2[])poly.Clone();
        Array.Reverse(copy);
        return copy;
    }

    public static Vec2 Centroid(IReadOnlyList<Vec2> poly)
    {
        if (poly.Count == 0)
        {
            return new Vec2(0, 0);
        }

        var area = SignedArea(poly);
        if (Math.Abs(area) < Eps)
        {
            return new Vec2(poly.Average(p => p.X), poly.Average(p => p.Y));
        }

        double cx = 0, cy = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var cross = a.X * b.Y - b.X * a.Y;
            cx += (a.X + b.X) * cross;
            cy += (a.Y + b.Y) * cross;
        }

        return new Vec2(cx / (6 * area), cy / (6 * area));
    }

    public static bool IsConvex(IReadOnlyList<Vec2> poly)
    {
        if (poly.Count < 3)
        {
            return false;
        }

        var sign = 0;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            var c = poly[(i + 2) % poly.Count];
            var cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (Math.Abs(cross) < 1e-12)
            {
                continue;
            }

            var s = cross > 0 ? 1 : -1;
            if (sign == 0)
            {
                sign = s;
            }
            else if (s != sign)
            {
                return false;
            }
        }

        return sign != 0;
    }

    /// <summary>Separating-axis test for two convex polygons (touching counts as overlap).</summary>
    public static bool ConvexOverlap(IReadOnlyList<Vec2> a, IReadOnlyList<Vec2> b)
    {
        return !HasSeparatingAxis(a, b) && !HasSeparatingAxis(b, a);
    }

    private static bool HasSeparatingAxis(IReadOnlyList<Vec2> a, IReadOnlyList<Vec2> b)
    {
        for (var i = 0; i < a.Count; i++)
        {
            var p = a[i];
            var q = a[(i + 1) % a.Count];
            var nx = -(q.Y - p.Y);
            var ny = q.X - p.X;
            if (Math.Abs(nx) < Eps && Math.Abs(ny) < Eps)
            {
                continue;
            }

            Project(a, nx, ny, out var minA, out var maxA);
            Project(b, nx, ny, out var minB, out var maxB);
            if (maxA < minB || maxB < minA)
            {
                return true;
            }
        }

        return false;
    }

    private static void Project(IReadOnlyList<Vec2> poly, double nx, double ny, out double min, out double max)
    {
        min = double.MaxValue;
        max = double.MinValue;
        foreach (var p in poly)
        {
            var d = p.X * nx + p.Y * ny;
            min = Math.Min(min, d);
            max = Math.Max(max, d);
        }
    }

    /// <summary>Sutherland–Hodgman: <paramref name="subject"/> clipped by a convex CCW <paramref name="clip"/>.</summary>
    public static Vec2[] ClipConvex(IReadOnlyList<Vec2> subject, IReadOnlyList<Vec2> clip)
    {
        var output = new List<Vec2>(subject);
        for (var i = 0; i < clip.Count && output.Count > 0; i++)
        {
            var a = clip[i];
            var b = clip[(i + 1) % clip.Count];
            var input = output;
            output = new List<Vec2>(input.Count + 2);
            for (var j = 0; j < input.Count; j++)
            {
                var current = input[j];
                var previous = input[(j + input.Count - 1) % input.Count];
                var currentIn = Side(a, b, current) >= -Eps;
                var previousIn = Side(a, b, previous) >= -Eps;
                if (currentIn)
                {
                    if (!previousIn)
                    {
                        output.Add(Intersect(previous, current, a, b));
                    }

                    output.Add(current);
                }
                else if (previousIn)
                {
                    output.Add(Intersect(previous, current, a, b));
                }
            }
        }

        return output.ToArray();
    }

    private static double Side(Vec2 a, Vec2 b, Vec2 p) => (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);

    private static Vec2 Intersect(Vec2 p, Vec2 q, Vec2 a, Vec2 b)
    {
        var sp = Side(a, b, p);
        var sq = Side(a, b, q);
        var denom = sp - sq;
        var t = Math.Abs(denom) < Eps ? 0 : sp / denom;
        return new Vec2(p.X + (q.X - p.X) * t, p.Y + (q.Y - p.Y) * t);
    }

    /// <summary>Area of the intersection of two convex polygons, with the intersection's centroid.</summary>
    public static (double Area, Vec2 Centroid) OverlapArea(Vec2[] a, Vec2[] b)
    {
        var clipped = ClipConvex(EnsureCcw(a), EnsureCcw(b));
        if (clipped.Length < 3)
        {
            return (0, new Vec2(0, 0));
        }

        return (Math.Abs(SignedArea(clipped)), Centroid(clipped));
    }

    /// <summary>
    /// Cyrus–Beck: length of segment p→q that lies inside a convex CCW polygon, and the midpoint
    /// of that inside part. Zero when the segment misses (or only touches) the polygon.
    /// </summary>
    public static (double Length, Vec2 Mid) SegmentInside(Vec2 p, Vec2 q, IReadOnlyList<Vec2> convexCcw)
    {
        double t0 = 0, t1 = 1;
        var dx = q.X - p.X;
        var dy = q.Y - p.Y;
        for (var i = 0; i < convexCcw.Count; i++)
        {
            var a = convexCcw[i];
            var b = convexCcw[(i + 1) % convexCcw.Count];

            // Inside is to the left of a→b for a CCW polygon: Side(a,b,x) >= 0.
            var num = Side(a, b, p);
            var den = (b.X - a.X) * dy - (b.Y - a.Y) * dx;
            if (Math.Abs(den) < Eps)
            {
                if (num < 0)
                {
                    return (0, p);
                }

                continue;
            }

            var t = -num / den;
            if (den > 0)
            {
                t0 = Math.Max(t0, t);
            }
            else
            {
                t1 = Math.Min(t1, t);
            }

            if (t0 > t1)
            {
                return (0, p);
            }
        }

        var length = (t1 - t0) * Math.Sqrt(dx * dx + dy * dy);
        var tm = (t0 + t1) / 2;
        return (length, new Vec2(p.X + dx * tm, p.Y + dy * tm));
    }

    /// <summary>Moves every edge of a convex CCW polygon outward by <paramref name="distance"/>.</summary>
    public static Vec2[] Inflate(Vec2[] convexCcw, double distance)
    {
        if (Math.Abs(distance) < Eps || convexCcw.Length < 3)
        {
            return convexCcw;
        }

        var n = convexCcw.Length;
        var lines = new (Vec2 P, Vec2 D)[n];
        for (var i = 0; i < n; i++)
        {
            var a = convexCcw[i];
            var b = convexCcw[(i + 1) % n];
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len < Eps)
            {
                return convexCcw;
            }

            // Outward normal of a CCW edge is (dy, -dx).
            var ox = dy / len * distance;
            var oy = -dx / len * distance;
            lines[i] = (new Vec2(a.X + ox, a.Y + oy), new Vec2(dx, dy));
        }

        var result = new Vec2[n];
        for (var i = 0; i < n; i++)
        {
            var (p1, d1) = lines[(i + n - 1) % n];
            var (p2, d2) = lines[i];
            var cross = d1.X * d2.Y - d1.Y * d2.X;
            if (Math.Abs(cross) < Eps)
            {
                result[i] = p2;
                continue;
            }

            var t = ((p2.X - p1.X) * d2.Y - (p2.Y - p1.Y) * d2.X) / cross;
            result[i] = new Vec2(p1.X + d1.X * t, p1.Y + d1.Y * t);
        }

        return result;
    }

    /// <summary>Plotted height in millimetres of a text <paramref name="heightUnits"/> drawing units tall.</summary>
    public static double PlottedHeightMm(double heightUnits, double unitsPerPaperMm) =>
        unitsPerPaperMm > 0 ? heightUnits / unitsPerPaperMm : double.NaN;

    /// <summary>
    /// Drawing units per plotted mm for a 1:<paramref name="scaleDenominator"/> plot of a drawing whose
    /// unit is <paramref name="metersPerUnit"/> metres (mm drawing at 1:100 → 100; metre drawing → 0.1).
    /// </summary>
    public static double UnitsPerPaperMm(double scaleDenominator, double metersPerUnit) =>
        scaleDenominator * 0.001 / metersPerUnit;

    // ===================== analysis =====================

    public static ClashReport Analyze(IReadOnlyList<ClashText> texts, IReadOnlyList<ClashCurve> curves, ClashOptions options)
    {
        var findings = new List<ClashFinding>();
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var maskedSkipped = 0;
        var groupSkipped = 0;

        var boxes = new List<PreparedText>(texts.Count);
        foreach (var text in texts)
        {
            if (text.Corners is not { Length: >= 3 } || text.Corners.Any(c => !double.IsFinite(c.X) || !double.IsFinite(c.Y)))
            {
                continue;
            }

            var quad = EnsureCcw(text.Corners);
            if (options.Padding != 0)
            {
                quad = Inflate(quad, options.Padding);
            }

            var area = Math.Abs(SignedArea(quad));
            if (area < Eps)
            {
                continue;
            }

            boxes.Add(new PreparedText(text, quad, Aabb(quad), area));
        }

        var cell = ChooseCellSize(boxes);

        if (options.TextOverlap && boxes.Count > 1)
        {
            var grid = new Grid(cell);
            for (var i = 0; i < boxes.Count; i++)
            {
                grid.Add(i, boxes[i].Box);
            }

            for (var i = 0; i < boxes.Count; i++)
            {
                var a = boxes[i];
                foreach (var j in grid.Query(a.Box))
                {
                    if (j <= i)
                    {
                        continue;
                    }

                    var b = boxes[j];
                    if (!BoxesOverlap(a.Box, b.Box) || !ConvexOverlap(a.Quad, b.Quad))
                    {
                        continue;
                    }

                    var (area, at) = OverlapArea(a.Quad, b.Quad);
                    var ratio = area / Math.Min(a.Area, b.Area);
                    if (ratio < options.MinOverlapRatio)
                    {
                        continue;
                    }

                    var duplicate = ratio > 0.98 && string.Equals(a.Source.Text, b.Source.Text, StringComparison.Ordinal);
                    Add(findings, counts, new ClashFinding(
                        CheckTextOverlap,
                        ratio >= 0.25 ? "error" : "warning",
                        duplicate
                            ? $"{a.Source.Type} '{Excerpt(a.Source.Text)}' is duplicated on top of {b.Source.Type} {b.Source.Handle}."
                            : $"{a.Source.Type} '{Excerpt(a.Source.Text)}' overlaps {b.Source.Type} '{Excerpt(b.Source.Text)}' ({Pct(ratio)} of the smaller box).",
                        [a.Source.Handle, b.Source.Handle],
                        [a.Source.Layer, b.Source.Layer],
                        [Excerpt(a.Source.Text), Excerpt(b.Source.Text)],
                        [Round(at.X), Round(at.Y)],
                        Round(area),
                        Math.Round(ratio, 4),
                        null,
                        Paths(a.Source.Path, b.Source.Path)));
                }
            }
        }

        var segmentCount = 0;
        var wantCurves = options.TextCrossing || options.DoorSwing || options.WallStraddle;
        if (wantCurves && boxes.Count > 0 && curves.Count > 0)
        {
            var segGrid = new Grid(cell);
            var segments = new List<(int Curve, Vec2 P, Vec2 Q)>();
            for (var c = 0; c < curves.Count; c++)
            {
                var curve = curves[c];
                if (!Wanted(curve.Kind, options))
                {
                    continue;
                }

                var pts = curve.Points;
                if (pts is not { Length: >= 2 })
                {
                    continue;
                }

                var last = curve.Closed ? pts.Length : pts.Length - 1;
                for (var k = 0; k < last; k++)
                {
                    var p = pts[k];
                    var q = pts[(k + 1) % pts.Length];
                    if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || !double.IsFinite(q.X) || !double.IsFinite(q.Y))
                    {
                        continue;
                    }

                    var index = segments.Count;
                    segments.Add((c, p, q));
                    segGrid.Add(index, [Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y)]);
                }
            }

            segmentCount = segments.Count;

            // Door sectors are areas, not lines: index the curves that carry an apex separately.
            var sectors = new List<(int Curve, Vec2[] Poly, double[] Box)>();
            if (options.DoorSwing)
            {
                for (var c = 0; c < curves.Count; c++)
                {
                    var curve = curves[c];
                    if (curve.Kind != ClashCurveKind.DoorSwing || curve.SectorApex is not { } apex || curve.Points.Length < 2)
                    {
                        continue;
                    }

                    var poly = new Vec2[curve.Points.Length + 1];
                    poly[0] = apex;
                    Array.Copy(curve.Points, 0, poly, 1, curve.Points.Length);
                    poly = EnsureCcw(poly);
                    if (IsConvex(poly))
                    {
                        sectors.Add((c, poly, Aabb(poly)));
                    }
                }
            }

            var sectorGrid = new Grid(cell);
            for (var s = 0; s < sectors.Count; s++)
            {
                sectorGrid.Add(s, sectors[s].Box);
            }

            var inside = new Dictionary<int, (double Length, Vec2 Mid)>();
            for (var t = 0; t < boxes.Count; t++)
            {
                var text = boxes[t];
                inside.Clear();
                foreach (var s in segGrid.Query(text.Box))
                {
                    var (curveIndex, p, q) = segments[s];
                    if (!BoxesOverlap(text.Box, [Math.Min(p.X, q.X), Math.Min(p.Y, q.Y), Math.Max(p.X, q.X), Math.Max(p.Y, q.Y)]))
                    {
                        continue;
                    }

                    var (length, mid) = SegmentInside(p, q, text.Quad);
                    if (length <= 0)
                    {
                        continue;
                    }

                    inside[curveIndex] = inside.TryGetValue(curveIndex, out var acc)
                        ? (acc.Length + length, acc.Mid)
                        : (length, mid);
                }

                var sectorHits = new Dictionary<int, (double Area, Vec2 At)>();
                foreach (var s in sectorGrid.Query(text.Box))
                {
                    var (curveIndex, poly, box) = sectors[s];
                    if (!BoxesOverlap(text.Box, box) || !ConvexOverlap(text.Quad, poly))
                    {
                        continue;
                    }

                    var (area, at) = OverlapArea(text.Quad, poly);
                    if (area / text.Area >= options.MinOverlapRatio)
                    {
                        sectorHits[curveIndex] = (area, at);
                    }
                }

                var minCross = Math.Max(options.MinCrossFraction * Math.Max(text.Source.Height, 0), 1e-9);
                foreach (var curveIndex in inside.Keys.Union(sectorHits.Keys).OrderBy(i => i))
                {
                    var curve = curves[curveIndex];
                    if (SameSmallGroup(text.Source, curve, options))
                    {
                        groupSkipped++;
                        continue;
                    }

                    inside.TryGetValue(curveIndex, out var cross);
                    var crosses = cross.Length >= minCross;
                    var inSector = sectorHits.TryGetValue(curveIndex, out var sector);
                    var kind = curve.Kind == ClashCurveKind.Wall && !options.WallStraddle ? ClashCurveKind.Generic : curve.Kind;
                    switch (kind)
                    {
                        case ClashCurveKind.Wall when crosses:
                            Add(findings, counts, CurveFinding(CheckWallStraddle, "error", text.Source, curve, cross.Length, cross.Mid,
                                $"{text.Source.Type} '{Excerpt(text.Source.Text)}' straddles wall {curve.Type} on {curve.Layer}"));
                            break;
                        case ClashCurveKind.DoorSwing when crosses || inSector:
                        {
                            var at = crosses ? cross.Mid : sector.At;
                            var what = crosses ? "crosses the door swing" : "sits inside the door swing";
                            Add(findings, counts, CurveFinding(CheckDoorSwing, "warning", text.Source, curve, crosses ? cross.Length : sector.Area, at,
                                $"{text.Source.Type} '{Excerpt(text.Source.Text)}' {what} {curve.Type} on {curve.Layer}"));
                            break;
                        }
                        case ClashCurveKind.Generic or ClashCurveKind.Leader when crosses && options.TextCrossing:
                            if (text.Source.Masked && !options.ReportMaskedCrossings)
                            {
                                maskedSkipped++;
                                break;
                            }

                            Add(findings, counts, CurveFinding(CheckTextCrossing, "warning", text.Source, curve, cross.Length, cross.Mid,
                                curve.Kind == ClashCurveKind.Leader
                                    ? $"Leader {curve.Handle} runs through {text.Source.Type} '{Excerpt(text.Source.Text)}'"
                                    : $"{curve.Type} on {curve.Layer} runs through {text.Source.Type} '{Excerpt(text.Source.Text)}'"));
                            break;
                    }
                }
            }
        }

        if (options.MinHeight && options.MinPlottedHeightMm is > 0 && options.UnitsPerPaperMm is > 0)
        {
            foreach (var text in boxes)
            {
                var plotted = PlottedHeightMm(text.Source.Height, options.UnitsPerPaperMm.Value);
                if (plotted + 1e-9 < options.MinPlottedHeightMm.Value)
                {
                    var at = Centroid(text.Quad);
                    Add(findings, counts, new ClashFinding(
                        CheckMinHeight,
                        "warning",
                        $"{text.Source.Type} '{Excerpt(text.Source.Text)}' plots {plotted.ToString("0.##", CultureInfo.InvariantCulture)} mm high (minimum {options.MinPlottedHeightMm.Value.ToString("0.##", CultureInfo.InvariantCulture)} mm).",
                        [text.Source.Handle],
                        [text.Source.Layer],
                        [Excerpt(text.Source.Text)],
                        [Round(at.X), Round(at.Y)],
                        Math.Round(plotted, 3),
                        null,
                        null,
                        Paths(text.Source.Path)));
                }
            }
        }

        var ordered = findings
            .OrderBy(f => SeverityRank(f.Severity))
            .ThenBy(f => Array.IndexOf(AllChecks, f.Check))
            .ThenBy(f => f.At[0])
            .ThenBy(f => f.At[1])
            .ToList();
        var max = Math.Clamp(options.MaxFindings, 1, 5000);
        var truncated = ordered.Count > max;
        return new ClashReport(
            boxes.Count,
            curves.Count,
            segmentCount,
            counts,
            truncated ? ordered.Take(max).ToList() : ordered,
            truncated,
            maskedSkipped,
            groupSkipped);
    }

    /// <summary>Kind of a curve from its effective layer, owning block names and whether it is an arc.</summary>
    public static ClashCurveKind Classify(string effectiveLayer, IEnumerable<string>? blockNames, bool isArc, WildcardSet doorLayers, WildcardSet doorBlocks, WildcardSet wallLayers)
    {
        var inDoorBlock = blockNames is not null && doorBlocks.MatchesAny(blockNames);
        if (isArc && (doorLayers.Matches(effectiveLayer) || inDoorBlock))
        {
            return ClashCurveKind.DoorSwing;
        }

        return wallLayers.Matches(effectiveLayer) && !inDoorBlock ? ClashCurveKind.Wall : ClashCurveKind.Generic;
    }

    public static string Excerpt(string? text)
    {
        var flat = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 40 ? flat : flat[..39] + "…";
    }

    private static bool Wanted(ClashCurveKind kind, ClashOptions options) => kind switch
    {
        ClashCurveKind.Wall => options.WallStraddle || options.TextCrossing,
        ClashCurveKind.DoorSwing => options.DoorSwing,
        _ => options.TextCrossing
    };

    private static bool SameSmallGroup(ClashText text, ClashCurve curve, ClashOptions options)
    {
        if (string.IsNullOrEmpty(text.Owner) || !string.Equals(text.Owner, curve.Owner, StringComparison.Ordinal))
        {
            return false;
        }

        var size = Math.Max(text.OwnerSize, curve.OwnerSize);
        return size <= 0 || size <= options.GroupSizeFactor * Math.Max(text.Height, Eps);
    }

    private static ClashFinding CurveFinding(string check, string severity, ClashText text, ClashCurve curve, double amount, Vec2 at, string message) => new(
        check,
        severity,
        message + ".",
        [text.Handle, curve.Handle],
        [text.Layer, curve.Layer],
        [Excerpt(text.Text), null],
        [Round(at.X), Round(at.Y)],
        Round(amount),
        null,
        text.Masked ? true : null,
        Paths(text.Path, curve.Path));

    private static void Add(List<ClashFinding> findings, Dictionary<string, int> counts, ClashFinding finding)
    {
        findings.Add(finding);
        counts[finding.Check] = counts.TryGetValue(finding.Check, out var n) ? n + 1 : 1;
    }

    private static string?[]? Paths(params string?[] paths) => paths.All(string.IsNullOrEmpty) ? null : paths;

    private static int SeverityRank(string severity) => severity switch
    {
        "error" => 0,
        "warning" => 1,
        _ => 2
    };

    private static string Pct(double ratio) => (ratio * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    private static double Round(double value) => Math.Round(value, 4);

    private static double[] Aabb(IReadOnlyList<Vec2> poly)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var p in poly)
        {
            minX = Math.Min(minX, p.X);
            minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X);
            maxY = Math.Max(maxY, p.Y);
        }

        return [minX, minY, maxX, maxY];
    }

    private static bool BoxesOverlap(double[] a, double[] b) =>
        a[0] <= b[2] && b[0] <= a[2] && a[1] <= b[3] && b[1] <= a[3];

    private static double ChooseCellSize(List<PreparedText> boxes)
    {
        if (boxes.Count == 0)
        {
            return 1;
        }

        var sizes = boxes
            .Select(b => Math.Max(b.Box[2] - b.Box[0], b.Box[3] - b.Box[1]))
            .Where(s => s > 0 && double.IsFinite(s))
            .OrderBy(s => s)
            .ToList();
        var median = sizes.Count > 0 ? sizes[sizes.Count / 2] : 1;
        return Math.Max(median * 2, 1e-6);
    }

    private sealed record PreparedText(ClashText Source, Vec2[] Quad, double[] Box, double Area);

    /// <summary>Uniform hash grid; items spanning too many cells go to an overflow list checked always.</summary>
    private sealed class Grid
    {
        private readonly double _cell;
        private readonly Dictionary<(long, long), List<int>> _cells = new();
        private readonly List<int> _overflow = [];

        public Grid(double cell) => _cell = cell;

        public void Add(int item, double[] box)
        {
            var (x0, y0, x1, y1) = Range(box);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) > MaxCellsPerItem)
            {
                _overflow.Add(item);
                return;
            }

            for (var x = x0; x <= x1; x++)
            {
                for (var y = y0; y <= y1; y++)
                {
                    if (!_cells.TryGetValue((x, y), out var list))
                    {
                        list = [];
                        _cells[(x, y)] = list;
                    }

                    list.Add(item);
                }
            }
        }

        public IEnumerable<int> Query(double[] box)
        {
            var seen = new HashSet<int>();
            var (x0, y0, x1, y1) = Range(box);
            if ((x1 - x0 + 1) * (y1 - y0 + 1) > MaxCellsPerItem)
            {
                foreach (var list in _cells.Values)
                {
                    foreach (var item in list)
                    {
                        if (seen.Add(item))
                        {
                            yield return item;
                        }
                    }
                }
            }
            else
            {
                for (var x = x0; x <= x1; x++)
                {
                    for (var y = y0; y <= y1; y++)
                    {
                        if (_cells.TryGetValue((x, y), out var list))
                        {
                            foreach (var item in list)
                            {
                                if (seen.Add(item))
                                {
                                    yield return item;
                                }
                            }
                        }
                    }
                }
            }

            foreach (var item in _overflow)
            {
                if (seen.Add(item))
                {
                    yield return item;
                }
            }
        }

        private (long X0, long Y0, long X1, long Y1) Range(double[] box) => (
            (long)Math.Floor(box[0] / _cell),
            (long)Math.Floor(box[1] / _cell),
            (long)Math.Floor(box[2] / _cell),
            (long)Math.Floor(box[3] / _cell));
    }
}
