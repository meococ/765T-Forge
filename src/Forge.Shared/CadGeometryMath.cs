namespace Forge.Shared;

public sealed record CadPolylineSpec(double[][] Vertices, double[] Bulges, bool Closed);

/// <summary>
/// Pure 2D/3D analytical geometry for CAD authoring and modification tools.
/// Lives in Forge.Shared (no AutoCAD dependency) so all shape generation, fillet,
/// chamfer, polygon, and revcloud math is deterministically unit-testable.
/// </summary>
public static class CadGeometryMath
{
    public sealed record PolylineVertex2D(double X, double Y, double Bulge = 0);

    public sealed record LineFilletResult(
        double[] Line1NewStart,
        double[] Line1NewEnd,
        double[] Line2NewStart,
        double[] Line2NewEnd,
        double[] IntersectionPoint,
        bool HasArc,
        double[]? ArcCenter,
        double ArcRadius,
        double ArcStartAngleDeg,
        double ArcEndAngleDeg,
        double[]? Tangent1,
        double[]? Tangent2)
    {
        public double[] Line1Start => Line1NewStart;
        public double[] Line1End => Line1NewEnd;
        public double[] Line2Start => Line2NewStart;
        public double[] Line2End => Line2NewEnd;
    }

    public sealed record LineChamferResult(
        double[] Line1NewStart,
        double[] Line1NewEnd,
        double[] Line2NewStart,
        double[] Line2NewEnd,
        double[] IntersectionPoint,
        double[] ChamferStart,
        double[] ChamferEnd)
    {
        public double[] Line1Start => Line1NewStart;
        public double[] Line1End => Line1NewEnd;
        public double[] Line2Start => Line2NewStart;
        public double[] Line2End => Line2NewEnd;
        public bool HasChamferSegment => Dist2D(ChamferStart, ChamferEnd) > 1e-9;
    }

    public static CadPolylineSpec ToSpec(IReadOnlyList<PolylineVertex2D> vertices, bool closed = true) =>
        new(
            vertices.Select(v => new[] { v.X, v.Y }).ToArray(),
            vertices.Select(v => v.Bulge).ToArray(),
            closed);

    public static CadPolylineSpec BuildRectangle(
        double x1,
        double y1,
        double x2,
        double y2,
        double filletRadius = 0,
        double chamferDistance1 = 0,
        double chamferDistance2 = 0,
        double rotationDeg = 0) =>
        ToSpec(BuildRectangle([x1, y1], [x2, y2], null, null, null, filletRadius, chamferDistance1, chamferDistance2, rotationDeg), true);

    public static CadPolylineSpec BuildRegularPolygon(
        double cx,
        double cy,
        int sides,
        double radius,
        bool inscribed = true,
        double rotationDeg = 0) =>
        ToSpec(BuildRegularPolygon([cx, cy], sides, radius, inscribed, rotationDeg), true);

    public static CadPolylineSpec BuildRevCloud(
        double[][] vertices,
        double arcLength = 0,
        double bulgeMagnitude = 0.52) =>
        ToSpec(BuildRevCloud(vertices, null, null, arcLength, bulgeMagnitude), true);

    /// <summary>
    /// Builds a closed rectangle as ordered 2D polyline vertices (with optional corner fillet
    /// radius or chamfer distances and optional rotation in degrees CCW).
    /// Provide either corners (p1, p2) OR (center, width, height).
    /// </summary>
    public static PolylineVertex2D[] BuildRectangle(
        double[]? p1,
        double[]? p2,
        double[]? center,
        double? width,
        double? height,
        double filletRadius = 0,
        double chamferDistance1 = 0,
        double chamferDistance2 = 0,
        double rotationDeg = 0)
    {
        double cx, cy, w, h;
        double rotPivotX, rotPivotY;

        if (center is { Length: >= 2 } && width is > 0 && height is > 0)
        {
            if (center.Any(double.IsNaN) || center.Any(double.IsInfinity) ||
                double.IsNaN(width.Value) || double.IsInfinity(width.Value) ||
                double.IsNaN(height.Value) || double.IsInfinity(height.Value))
            {
                throw new ArgumentException("Rectangle center, width, and height must be finite.");
            }

            cx = center[0];
            cy = center[1];
            w = width.Value;
            h = height.Value;
            rotPivotX = cx;
            rotPivotY = cy;
        }
        else if (p1 is { Length: >= 2 } && p2 is { Length: >= 2 })
        {
            if (p1.Any(double.IsNaN) || p1.Any(double.IsInfinity) ||
                p2.Any(double.IsNaN) || p2.Any(double.IsInfinity))
            {
                throw new ArgumentException("Rectangle corner points p1 and p2 must be finite.");
            }

            w = Math.Abs(p2[0] - p1[0]);
            h = Math.Abs(p2[1] - p1[1]);
            if (w <= 1e-9 || h <= 1e-9)
            {
                throw new ArgumentException("Rectangle width and height must both be greater than 0.");
            }

            cx = (p1[0] + p2[0]) / 2.0;
            cy = (p1[1] + p2[1]) / 2.0;
            rotPivotX = cx;
            rotPivotY = cy;
        }
        else
        {
            throw new ArgumentException("Provide either (p1 and p2) or (center, width > 0, height > 0).");
        }

        if (filletRadius < 0 || chamferDistance1 < 0 || chamferDistance2 < 0)
        {
            throw new ArgumentException("filletRadius and chamfer distances must be >= 0.");
        }

        if (filletRadius > 0 && (chamferDistance1 > 0 || chamferDistance2 > 0))
        {
            throw new ArgumentException("Specify either filletRadius or chamferDistance, not both.");
        }

        var halfW = w / 2.0;
        var halfH = h / 2.0;
        var minX = cx - halfW;
        var maxX = cx + halfW;
        var minY = cy - halfH;
        var maxY = cy + halfH;

        var raw = new List<PolylineVertex2D>(8);
        if (filletRadius > 0)
        {
            var r = filletRadius;
            if (r * 2.0 > w + 1e-9 || r * 2.0 > h + 1e-9)
            {
                throw new ArgumentException($"filletRadius ({r}) exceeds half of the rectangle's smaller side ({Math.Min(halfW, halfH)}).");
            }

            // Quarter-circle CCW bulge = tan((pi/2)/4) = tan(pi/8) = sqrt(2) - 1
            var quarterBulge = Math.Sqrt(2.0) - 1.0;
            // Bottom edge (left to right), then bottom-right arc, right edge, top-right arc, top edge, top-left arc, left edge, bottom-left arc
            raw.Add(new PolylineVertex2D(minX + r, minY, 0));
            raw.Add(new PolylineVertex2D(maxX - r, minY, quarterBulge));
            raw.Add(new PolylineVertex2D(maxX, minY + r, 0));
            raw.Add(new PolylineVertex2D(maxX, maxY - r, quarterBulge));
            raw.Add(new PolylineVertex2D(maxX - r, maxY, 0));
            raw.Add(new PolylineVertex2D(minX + r, maxY, quarterBulge));
            raw.Add(new PolylineVertex2D(minX, maxY - r, 0));
            raw.Add(new PolylineVertex2D(minX, minY + r, quarterBulge));
        }
        else if (chamferDistance1 > 0 || chamferDistance2 > 0)
        {
            var d1 = chamferDistance1 > 0 ? chamferDistance1 : chamferDistance2;
            var d2 = chamferDistance2 > 0 ? chamferDistance2 : d1;
            if (d1 * 2.0 > w + 1e-9 || d2 * 2.0 > h + 1e-9)
            {
                throw new ArgumentException("Chamfer distances exceed half of the rectangle dimensions.");
            }

            raw.Add(new PolylineVertex2D(minX + d1, minY, 0));
            raw.Add(new PolylineVertex2D(maxX - d1, minY, 0));
            raw.Add(new PolylineVertex2D(maxX, minY + d2, 0));
            raw.Add(new PolylineVertex2D(maxX, maxY - d2, 0));
            raw.Add(new PolylineVertex2D(maxX - d1, maxY, 0));
            raw.Add(new PolylineVertex2D(minX + d1, maxY, 0));
            raw.Add(new PolylineVertex2D(minX, maxY - d2, 0));
            raw.Add(new PolylineVertex2D(minX, minY + d2, 0));
        }
        else
        {
            raw.Add(new PolylineVertex2D(minX, minY, 0));
            raw.Add(new PolylineVertex2D(maxX, minY, 0));
            raw.Add(new PolylineVertex2D(maxX, maxY, 0));
            raw.Add(new PolylineVertex2D(minX, maxY, 0));
        }

        // A fillet radius / chamfer equal to half a side makes two consecutive vertices coincide
        // (zero-length straight segment). Drop the straight-segment vertex and keep the arc one.
        for (var i = raw.Count - 1; i >= 0 && raw.Count > 3; i--)
        {
            var next = raw[(i + 1) % raw.Count];
            if (Math.Abs(raw[i].X - next.X) <= 1e-9 && Math.Abs(raw[i].Y - next.Y) <= 1e-9 && Math.Abs(raw[i].Bulge) <= 1e-12)
            {
                raw.RemoveAt(i);
            }
        }

        if (Math.Abs(rotationDeg) > 1e-12)
        {
            var rad = rotationDeg * Math.PI / 180.0;
            var cos = Math.Cos(rad);
            var sin = Math.Sin(rad);
            for (var i = 0; i < raw.Count; i++)
            {
                var dx = raw[i].X - rotPivotX;
                var dy = raw[i].Y - rotPivotY;
                raw[i] = new PolylineVertex2D(
                    rotPivotX + dx * cos - dy * sin,
                    rotPivotY + dx * sin + dy * cos,
                    raw[i].Bulge);
            }
        }

        return raw.ToArray();
    }

    /// <summary>
    /// Builds a regular N-gon (3..1024 sides). When inscribed=true, vertices lie on the circle of
    /// given radius. When inscribed=false (circumscribed), the circle of given radius is tangent
    /// to the polygon's edges (vertex radius = radius / cos(pi/sides)).
    /// </summary>
    public static PolylineVertex2D[] BuildRegularPolygon(
        double[]? center,
        int sides,
        double radius,
        bool inscribed = true,
        double rotationDeg = 0)
    {
        if (center is null || center.Length < 2 || center.Any(double.IsNaN) || center.Any(double.IsInfinity))
        {
            throw new ArgumentException("center [x, y] is required and must be finite.");
        }

        if (sides is < 3 or > 1024)
        {
            throw new ArgumentException("sides must be between 3 and 1024.");
        }

        if (radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius))
        {
            throw new ArgumentException("radius must be greater than 0.");
        }

        var vertexRadius = inscribed ? radius : radius / Math.Cos(Math.PI / sides);
        var startRad = rotationDeg * Math.PI / 180.0;
        var stepRad = 2.0 * Math.PI / sides;
        var vertices = new PolylineVertex2D[sides];
        for (var i = 0; i < sides; i++)
        {
            var angle = startRad + i * stepRad;
            vertices[i] = new PolylineVertex2D(
                center[0] + vertexRadius * Math.Cos(angle),
                center[1] + vertexRadius * Math.Sin(angle),
                0);
        }

        return vertices;
    }

    /// <summary>
    /// Builds a revision cloud (closed polyline with outward arc bulges) from either a bounding
    /// rectangle (p1, p2) or an explicit polygon vertex list.
    /// Ensures outward lobes regardless of whether input vertices are CW or CCW.
    /// </summary>
    public static PolylineVertex2D[] BuildRevCloud(
        double[][]? vertices,
        double[]? p1,
        double[]? p2,
        double arcLength = 0,
        double bulgeMagnitude = 0.52)
    {
        if (double.IsNaN(bulgeMagnitude) || Math.Abs(bulgeMagnitude) <= 1e-6 || Math.Abs(bulgeMagnitude) > 1.0)
        {
            throw new ArgumentException("bulgeMagnitude must be in (0, 1] (1 = semicircular lobes; default 0.52 ≈ 110° arcs).");
        }

        if (double.IsNaN(arcLength) || arcLength < 0)
        {
            throw new ArgumentException("arcLength must be >= 0 (0 = perimeter / 24).");
        }

        double[][] loop;
        if (vertices is { Length: >= 3 })
        {
            loop = vertices;
        }
        else if (p1 is { Length: >= 2 } && p2 is { Length: >= 2 })
        {
            var rect = BuildRectangle(p1, p2, null, null, null);
            loop = rect.Select(v => new[] { v.X, v.Y }).ToArray();
        }
        else
        {
            throw new ArgumentException("Provide either vertices (at least 3 points) or rectangular corners p1 and p2.");
        }

        // Compute signed area to orient vertices so outward arc lobes always bulge outside the polygon.
        double signedArea2 = 0;
        var n = loop.Length;
        double perimeter = 0;
        for (var i = 0; i < n; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % n];
            if (a is null || a.Length < 2 || b is null || b.Length < 2)
            {
                throw new ArgumentException("Each revcloud vertex must be [x, y].");
            }

            signedArea2 += a[0] * b[1] - b[0] * a[1];
            var dx = b[0] - a[0];
            var dy = b[1] - a[1];
            perimeter += Math.Sqrt(dx * dx + dy * dy);
        }

        if (perimeter <= 1e-6)
        {
            throw new ArgumentException("Revision cloud boundary has zero perimeter.");
        }

        var effectiveArcLen = arcLength > 0 ? arcLength : perimeter / 24.0;
        // AutoCAD LWPolyline bulge: positive = the arc runs CCW from vertex i to i+1, which places the
        // arc on the RIGHT of the direction of travel (e.g. (0,0)->(2,0) with bulge 1 passes (1,-1)).
        // For a CCW loop the exterior is on the right, so outward lobes need a POSITIVE bulge;
        // for a CW loop they need a negative one.
        var isCcw = signedArea2 >= 0;
        var bulge = isCcw ? Math.Abs(bulgeMagnitude) : -Math.Abs(bulgeMagnitude);

        var result = new List<PolylineVertex2D>();
        for (var i = 0; i < n; i++)
        {
            var a = loop[i];
            var b = loop[(i + 1) % n];
            var dx = b[0] - a[0];
            var dy = b[1] - a[1];
            var segLen = Math.Sqrt(dx * dx + dy * dy);
            if (segLen <= 1e-9)
            {
                continue;
            }

            var steps = Math.Clamp((int)Math.Round(segLen / effectiveArcLen), 1, 200);
            for (var s = 0; s < steps; s++)
            {
                var t = (double)s / steps;
                result.Add(new PolylineVertex2D(a[0] + dx * t, a[1] + dy * t, bulge));
            }
        }

        return result.ToArray();
    }

    /// <summary>
    /// Computes the 2D fillet between two lines (start1->end1 and start2->end2) for radius >= 0.
    /// For each line one endpoint survives and the other is trimmed/extended to the tangent point
    /// (or to the intersection itself when radius == 0). Side selection
    /// (<see cref="CadEditMath.ChooseKeepStart"/>): when the corner intersection lies outside the
    /// segment the far endpoint survives; when the line straddles the intersection the side
    /// containing the projection of pick1/pick2 survives, and without a pick a
    /// <see cref="CadAmbiguousSideException"/> is thrown.
    /// </summary>
    public static LineFilletResult ComputeLineFillet(
        double[] start1,
        double[] end1,
        double[] start2,
        double[] end2,
        double radius,
        double[]? pick1 = null,
        double[]? pick2 = null)
    {
        if (radius < 0 || double.IsNaN(radius) || double.IsInfinity(radius))
        {
            throw new ArgumentException("Fillet radius must be >= 0.");
        }

        var pInt = IntersectInfiniteLines2D(start1, end1, start2, end2)
                   ?? throw new InvalidOperationException("Cannot fillet parallel or collinear lines.");

        // Determine which endpoint of Line 1 and Line 2 is the "surviving" end vs the corner end.
        var keepStart1 = CadEditMath.ChooseKeepStart(start1, end1, pInt, pick1, 1);
        var far1 = keepStart1 ? start1 : end1;

        var keepStart2 = CadEditMath.ChooseKeepStart(start2, end2, pInt, pick2, 2);
        var far2 = keepStart2 ? start2 : end2;

        // Unit vectors pointing from intersection toward the surviving far endpoints
        var u1x = far1[0] - pInt[0];
        var u1y = far1[1] - pInt[1];
        var len1 = Math.Sqrt(u1x * u1x + u1y * u1y);
        var u2x = far2[0] - pInt[0];
        var u2y = far2[1] - pInt[1];
        var len2 = Math.Sqrt(u2x * u2x + u2y * u2y);

        if (len1 <= 1e-9 || len2 <= 1e-9)
        {
            throw new InvalidOperationException("Degenerate line length relative to intersection.");
        }

        u1x /= len1;
        u1y /= len1;
        u2x /= len2;
        u2y /= len2;

        var z = start1.Length >= 3 ? start1[2] : 0.0;
        var intersection3D = new[] { pInt[0], pInt[1], z };

        if (radius <= 1e-12)
        {
            var newS1 = keepStart1 ? start1 : intersection3D;
            var newE1 = keepStart1 ? intersection3D : end1;
            var newS2 = keepStart2 ? start2 : intersection3D;
            var newE2 = keepStart2 ? intersection3D : end2;
            return new LineFilletResult(newS1, newE1, newS2, newE2, intersection3D, false, null, 0, 0, 0, null, null);
        }

        var dot = Math.Clamp(u1x * u2x + u1y * u2y, -1.0, 1.0);
        var angle = Math.Acos(dot);
        if (angle <= 1e-6 || Math.Abs(Math.PI - angle) <= 1e-6)
        {
            throw new InvalidOperationException("Cannot fillet collinear lines.");
        }

        var halfAngle = angle / 2.0;
        var tangentDist = radius / Math.Tan(halfAngle);
        if (tangentDist > len1 + 1e-6 || tangentDist > len2 + 1e-6)
        {
            throw new InvalidOperationException($"Fillet radius {radius} is too large for the available line lengths ({Math.Min(len1, len2):F3}).");
        }

        var t1 = new[] { pInt[0] + u1x * tangentDist, pInt[1] + u1y * tangentDist, z };
        var t2 = new[] { pInt[0] + u2x * tangentDist, pInt[1] + u2y * tangentDist, z };

        var bx = u1x + u2x;
        var by = u1y + u2y;
        var bLen = Math.Sqrt(bx * bx + by * by);
        bx /= bLen;
        by /= bLen;

        var centerDist = radius / Math.Sin(halfAngle);
        var center = new[] { pInt[0] + bx * centerDist, pInt[1] + by * centerDist, z };

        var ang1 = NormalizeDegrees(Math.Atan2(t1[1] - center[1], t1[0] - center[0]) * 180.0 / Math.PI);
        var ang2 = NormalizeDegrees(Math.Atan2(t2[1] - center[1], t2[0] - center[0]) * 180.0 / Math.PI);

        // AutoCAD Arc always sweeps CCW from startAngle to endAngle.
        // Pick start/end so the CCW sweep is the shorter interior arc (< 180 deg).
        var ccwSweep1To2 = NormalizeDegrees(ang2 - ang1);
        var startDeg = ccwSweep1To2 <= 180.0 ? ang1 : ang2;
        var endDeg = ccwSweep1To2 <= 180.0 ? ang2 : ang1;

        var l1Start = keepStart1 ? start1 : t1;
        var l1End = keepStart1 ? t1 : end1;
        var l2Start = keepStart2 ? start2 : t2;
        var l2End = keepStart2 ? t2 : end2;

        return new LineFilletResult(
            l1Start,
            l1End,
            l2Start,
            l2End,
            intersection3D,
            true,
            center,
            radius,
            startDeg,
            endDeg,
            t1,
            t2);
    }

    /// <summary>
    /// Computes the 2D chamfer between two lines (start1->end1 and start2->end2) with distances
    /// d1, d2 > 0 measured from the corner intersection along the surviving side of each line.
    /// Side selection is identical to <see cref="ComputeLineFillet"/> (optional pick1/pick2;
    /// <see cref="CadAmbiguousSideException"/> when a line straddles the corner without a pick).
    /// </summary>
    public static LineChamferResult ComputeLineChamfer(
        double[] start1,
        double[] end1,
        double[] start2,
        double[] end2,
        double distance1,
        double distance2,
        double[]? pick1 = null,
        double[]? pick2 = null)
    {
        if (distance1 <= 0 || distance2 <= 0 || double.IsNaN(distance1) || double.IsNaN(distance2))
        {
            throw new ArgumentException("Chamfer distances must be > 0.");
        }

        var pInt = IntersectInfiniteLines2D(start1, end1, start2, end2)
                   ?? throw new InvalidOperationException("Cannot chamfer parallel or collinear lines.");

        var keepStart1 = CadEditMath.ChooseKeepStart(start1, end1, pInt, pick1, 1);
        var far1 = keepStart1 ? start1 : end1;

        var keepStart2 = CadEditMath.ChooseKeepStart(start2, end2, pInt, pick2, 2);
        var far2 = keepStart2 ? start2 : end2;

        var u1x = far1[0] - pInt[0];
        var u1y = far1[1] - pInt[1];
        var len1 = Math.Sqrt(u1x * u1x + u1y * u1y);
        var u2x = far2[0] - pInt[0];
        var u2y = far2[1] - pInt[1];
        var len2 = Math.Sqrt(u2x * u2x + u2y * u2y);

        if (len1 <= 1e-9 || len2 <= 1e-9)
        {
            throw new InvalidOperationException("Degenerate line length relative to intersection.");
        }

        if (distance1 > len1 + 1e-6 || distance2 > len2 + 1e-6)
        {
            throw new InvalidOperationException("Chamfer distance exceeds available line length.");
        }

        u1x /= len1;
        u1y /= len1;
        u2x /= len2;
        u2y /= len2;

        var z = start1.Length >= 3 ? start1[2] : 0.0;
        var intersection3D = new[] { pInt[0], pInt[1], z };
        var c1 = new[] { pInt[0] + u1x * distance1, pInt[1] + u1y * distance1, z };
        var c2 = new[] { pInt[0] + u2x * distance2, pInt[1] + u2y * distance2, z };

        var l1Start = keepStart1 ? start1 : c1;
        var l1End = keepStart1 ? c1 : end1;
        var l2Start = keepStart2 ? start2 : c2;
        var l2End = keepStart2 ? c2 : end2;

        return new LineChamferResult(l1Start, l1End, l2Start, l2End, intersection3D, c1, c2);
    }

    public static CadPolylineSpec FilletPolylineCorners(
        double[][] vertices,
        double[]? bulges,
        bool closed,
        double radius) =>
        FilletPolylineCorners(vertices, bulges, closed, radius, out _);

    /// <summary>
    /// Same as <see cref="FilletPolylineCorners(double[][], double[], bool, double)"/> and also
    /// reports the indices of interior corners that could not be filleted.
    /// </summary>
    public static CadPolylineSpec FilletPolylineCorners(
        double[][] vertices,
        double[]? bulges,
        bool closed,
        double radius,
        out int[] skippedCorners)
    {
        var detailed = FilletPolylineCornersDetailed(ToVertexList(vertices, bulges), closed, radius);
        skippedCorners = detailed.SkippedCorners.Select(s => s.Index).ToArray();
        return ToSpec(detailed.Vertices, closed);
    }

    private static PolylineVertex2D[] ToVertexList(double[][] vertices, double[]? bulges)
    {
        var input = new PolylineVertex2D[vertices.Length];
        for (var i = 0; i < vertices.Length; i++)
        {
            var b = bulges is not null && i < bulges.Length ? bulges[i] : 0.0;
            input[i] = new PolylineVertex2D(vertices[i][0], vertices[i][1], b);
        }

        return input;
    }

    /// <summary>Provenance of an output vertex of <see cref="FilletPolylineCornersDetailed"/>.</summary>
    public enum PolylineFilletVertexRole
    {
        /// <summary>Unchanged input vertex.</summary>
        Original,

        /// <summary>Tangent point on the incoming segment; carries the fillet arc bulge.</summary>
        TangentIn,

        /// <summary>Tangent point on the outgoing segment; starts the (straight) outgoing segment.</summary>
        TangentOut
    }

    /// <summary>An interior corner that was not filleted and why (arc_segment, degenerate_segment, collinear, degenerate_angle, segment_too_short).</summary>
    public sealed record PolylineSkippedCorner(int Index, string Reason);

    /// <summary>
    /// Detailed polyline fillet result. <c>SourceIndex[k]</c> is the input vertex that output
    /// vertex k came from (the filleted corner for tangent vertices) and <c>Roles[k]</c> tells
    /// whether it is the original vertex or one of the corner's tangent points — the plugin uses
    /// both to carry per-vertex widths over.
    /// </summary>
    public sealed record PolylineFilletResult(
        PolylineVertex2D[] Vertices,
        int[] SourceIndex,
        PolylineFilletVertexRole[] Roles,
        int[] FilletedCorners,
        PolylineSkippedCorner[] SkippedCorners);

    /// <summary>
    /// Fillets all straight-segment corners of a 2D polyline with the given radius.
    /// Corners whose adjacent segments are too short for the radius are left untouched.
    /// </summary>
    public static PolylineVertex2D[] FilletPolylineCorners(
        IReadOnlyList<PolylineVertex2D> vertices,
        bool closed,
        double radius) =>
        FilletPolylineCornersDetailed(vertices, closed, radius).Vertices;

    /// <summary>
    /// Fillets every interior corner whose two adjacent segments are straight. Tangent length
    /// budget per segment: a segment shared by two filleted corners must hold both tangent
    /// lengths (td_i + td_j &lt;= len); a segment whose other end is not filleted (including the
    /// first/last segment of an open polyline) may use its full length. A conflict on a shared
    /// segment skips both corners (segment_too_short) — the result never depends on scan order.
    /// Bulge sign follows the turn: left turn (CCW) → positive, right turn (CW) → negative.
    /// </summary>
    public static PolylineFilletResult FilletPolylineCornersDetailed(
        IReadOnlyList<PolylineVertex2D> vertices,
        bool closed,
        double radius)
    {
        var n = vertices.Count;
        if (radius <= 0 || double.IsNaN(radius) || double.IsInfinity(radius) || n < 3)
        {
            return new PolylineFilletResult(
                vertices.ToArray(),
                Enumerable.Range(0, n).ToArray(),
                Enumerable.Repeat(PolylineFilletVertexRole.Original, n).ToArray(),
                [],
                []);
        }

        var tangentDist = new double[n];
        var candidate = new bool[n];
        var bulgeAt = new double[n];
        var uIn = new double[n][];
        var uOut = new double[n][];
        var skipped = new List<PolylineSkippedCorner>();

        double SegLen(int a, int b)
        {
            var dx = vertices[b].X - vertices[a].X;
            var dy = vertices[b].Y - vertices[a].Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        // Pass 1: geometric candidates.
        for (var i = 0; i < n; i++)
        {
            if (!closed && (i == 0 || i == n - 1))
            {
                continue; // open ends are not corners
            }

            var prevIdx = (i - 1 + n) % n;
            var nextIdx = (i + 1) % n;
            if (Math.Abs(vertices[prevIdx].Bulge) > 1e-9 || Math.Abs(vertices[i].Bulge) > 1e-9)
            {
                skipped.Add(new PolylineSkippedCorner(i, "arc_segment"));
                continue;
            }

            var curr = vertices[i];
            var vInX = vertices[prevIdx].X - curr.X;
            var vInY = vertices[prevIdx].Y - curr.Y;
            var lenIn = Math.Sqrt(vInX * vInX + vInY * vInY);
            var vOutX = vertices[nextIdx].X - curr.X;
            var vOutY = vertices[nextIdx].Y - curr.Y;
            var lenOut = Math.Sqrt(vOutX * vOutX + vOutY * vOutY);
            if (lenIn <= 1e-9 || lenOut <= 1e-9)
            {
                skipped.Add(new PolylineSkippedCorner(i, "degenerate_segment"));
                continue;
            }

            var uInX = vInX / lenIn;
            var uInY = vInY / lenIn;
            var uOutX = vOutX / lenOut;
            var uOutY = vOutY / lenOut;
            var dot = Math.Clamp(uInX * uOutX + uInY * uOutY, -1.0, 1.0);
            var cornerAngle = Math.Acos(dot);
            if (Math.Abs(Math.PI - cornerAngle) <= 1e-5)
            {
                skipped.Add(new PolylineSkippedCorner(i, "collinear"));
                continue;
            }

            if (cornerAngle <= 1e-5)
            {
                skipped.Add(new PolylineSkippedCorner(i, "degenerate_angle"));
                continue;
            }

            // Sweep of the arc is (pi - cornerAngle); sign from the turn direction of
            // forward-in (-uIn) to forward-out (uOut).
            var cross = -uInX * uOutY - -uInY * uOutX;
            tangentDist[i] = radius / Math.Tan(cornerAngle / 2.0);
            bulgeAt[i] = Math.Tan((Math.PI - cornerAngle) / 4.0) * (cross >= 0 ? 1.0 : -1.0);
            uIn[i] = [uInX, uInY];
            uOut[i] = [uOutX, uOutY];
            candidate[i] = true;
        }

        // Pass 2: tangent-length budget per adjacent segment (symmetric, order independent).
        var filleted = new bool[n];
        for (var i = 0; i < n; i++)
        {
            if (!candidate[i])
            {
                continue;
            }

            var prevIdx = (i - 1 + n) % n;
            var nextIdx = (i + 1) % n;
            var needIn = tangentDist[i] + (candidate[prevIdx] ? tangentDist[prevIdx] : 0);
            var needOut = tangentDist[i] + (candidate[nextIdx] ? tangentDist[nextIdx] : 0);
            var lenIn = SegLen(prevIdx, i);
            var lenOut = SegLen(i, nextIdx);
            if (needIn > lenIn * (1 + 1e-9) + 1e-12 || needOut > lenOut * (1 + 1e-9) + 1e-12)
            {
                skipped.Add(new PolylineSkippedCorner(i, "segment_too_short"));
                continue;
            }

            filleted[i] = true;
        }

        var outVerts = new List<PolylineVertex2D>(n * 2);
        var outSource = new List<int>(n * 2);
        var outRoles = new List<PolylineFilletVertexRole>(n * 2);
        for (var i = 0; i < n; i++)
        {
            var curr = vertices[i];
            if (!filleted[i])
            {
                outVerts.Add(curr);
                outSource.Add(i);
                outRoles.Add(PolylineFilletVertexRole.Original);
                continue;
            }

            var td = tangentDist[i];
            outVerts.Add(new PolylineVertex2D(curr.X + uIn[i][0] * td, curr.Y + uIn[i][1] * td, bulgeAt[i]));
            outSource.Add(i);
            outRoles.Add(PolylineFilletVertexRole.TangentIn);
            outVerts.Add(new PolylineVertex2D(curr.X + uOut[i][0] * td, curr.Y + uOut[i][1] * td, curr.Bulge));
            outSource.Add(i);
            outRoles.Add(PolylineFilletVertexRole.TangentOut);
        }

        // A tangent point that lands exactly on its neighbour (full-length budget used) would leave
        // a zero-length segment: drop the earlier vertex of the pair (its outgoing segment is the
        // empty one) and keep the later one, which carries the next segment's bulge.
        static bool Same(PolylineVertex2D a, PolylineVertex2D b) =>
            Math.Abs(a.X - b.X) <= 1e-9 * (1 + Math.Abs(a.X)) && Math.Abs(a.Y - b.Y) <= 1e-9 * (1 + Math.Abs(a.Y));

        for (var k = 0; k < outVerts.Count - 1;)
        {
            var involvesTangent = outRoles[k] != PolylineFilletVertexRole.Original || outRoles[k + 1] != PolylineFilletVertexRole.Original;
            if (involvesTangent && Same(outVerts[k], outVerts[k + 1]))
            {
                outVerts.RemoveAt(k);
                outSource.RemoveAt(k);
                outRoles.RemoveAt(k);
                continue;
            }

            k++;
        }

        if (closed && outVerts.Count > 2)
        {
            var last = outVerts.Count - 1;
            var involvesTangent = outRoles[last] != PolylineFilletVertexRole.Original || outRoles[0] != PolylineFilletVertexRole.Original;
            if (involvesTangent && Same(outVerts[last], outVerts[0]))
            {
                outVerts.RemoveAt(last);
                outSource.RemoveAt(last);
                outRoles.RemoveAt(last);
            }
        }

        return new PolylineFilletResult(
            outVerts.ToArray(),
            outSource.ToArray(),
            outRoles.ToArray(),
            Enumerable.Range(0, n).Where(i => filleted[i]).ToArray(),
            skipped.OrderBy(s => s.Index).ToArray());
    }

    private static double[]? IntersectInfiniteLines2D(double[] a1, double[] a2, double[] b1, double[] b2)
    {
        var x1 = a1[0];
        var y1 = a1[1];
        // Shift into local coordinates centered at a1 so large survey coordinates (e.g. VN2000
        // X ~ 6e8 mm, Y ~ 1.2e9 mm) never suffer catastrophic cancellation in x1*y2 - y1*x2.
        var dx1 = a2[0] - x1;
        var dy1 = a2[1] - y1;
        var dx2 = b2[0] - b1[0];
        var dy2 = b2[1] - b1[1];
        var rx = b1[0] - x1;
        var ry = b1[1] - y1;

        var denom = dx1 * dy2 - dy1 * dx2;
        if (Math.Abs(denom) <= 1e-12)
        {
            return null;
        }

        var t = (rx * dy2 - ry * dx2) / denom;
        return [x1 + t * dx1, y1 + t * dy1];
    }

    private static double Dist2D(double[] a, double[] b)
    {
        var dx = a[0] - b[0];
        var dy = a[1] - b[1];
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double NormalizeDegrees(double deg)
    {
        var m = deg % 360.0;
        return m < 0 ? m + 360.0 : m;
    }
}
