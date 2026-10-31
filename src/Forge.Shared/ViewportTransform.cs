namespace Forge.Shared;

/// <summary>
/// TOOL-GAP #30a: pure model→paper projection of a floating (paper-space) VIEWPORT plus the
/// clipping the annotation-clash walk needs. The plugin composes the identical transform with
/// real Matrix3d calls — the canonical WCS→DCS chain the view code already uses:
///   dcsToWcs = Rotation(−twist, viewDirection, target) · Displacement(target) · PlaneToWorld(dir)
///   DCS(p)   = dcsToWcs⁻¹ · p = Bᵀ·R(+twist)·(p − target)   (B = arbitrary-axis basis)
///   paper    = vpCenter + s·(DCS(p) − viewCenter)
///   s        = Viewport.Height / Viewport.ViewHeight  (≡ CustomScale — paper units per model unit;
///             V-101 1:48 ⇒ 15.625/750 ≈ 0.020833 = CustomScale, checked on sheet-set_R1)
/// viewCenter is a DCS (display) coordinate — it names the model point pinned at the paper centre.
/// </summary>
public static class ViewportTransform
{
    /// <summary>Paper units per model unit; 0 when neither height ratio nor CustomScale is usable.</summary>
    public static double PaperScale(double paperHeight, double viewHeight, double customScale) =>
        paperHeight > 0 && viewHeight > 0 && double.IsFinite(viewHeight)
            ? paperHeight / viewHeight
            : customScale > 0 && double.IsFinite(customScale)
                ? customScale
                : 0;

    /// <summary>
    /// The full model→paper affine (row-major 3x4, column-vector convention like AffineXf),
    /// <paramref name="twist"/> in radians — the managed twin of the plugin's Matrix3d
    /// composition; unit-tested against hand-derived cases so the projection can be trusted.
    /// </summary>
    public static AffineXf ModelToPaper(
        double vpCenterX, double vpCenterY, double vpWidth, double vpHeight,
        double viewCenterX, double viewCenterY, double viewHeight,
        double twist, double targetX, double targetY, double targetZ,
        double dirX, double dirY, double dirZ,
        double customScale)
    {
        var scale = PaperScale(vpHeight, viewHeight, customScale);
        var (dx, dy, dz) = Unit(dirX, dirY, dirZ);

        // AutoCAD arbitrary-axis rule: a nearly-±Z view derives its X axis from +Y.
        double ax = 0, ay = 0, az = 1;
        if (Math.Abs(dx) < 1.0 / 64 && Math.Abs(dy) < 1.0 / 64)
        {
            (ax, ay, az) = (0, 1, 0);
        }

        var (x0, x1, x2) = CrossUnit(ax, ay, az, dx, dy, dz); // DCS X axis in WCS
        var (y0, y1, y2) = Cross(dx, dy, dz, x0, x1, x2);     // DCS Y axis in WCS

        // Rows of Bᵀ·R(twist) are (R(twist)ᵀ·basis)ᵀ; Rodrigues transposed:
        // u = c·v − s·(d̂×v) + (1−c)(d̂·v)d̂.
        var c = Math.Cos(twist);
        var s = Math.Sin(twist);
        var (ux0, ux1, ux2) = RotT(c, s, dx, dy, dz, x0, x1, x2);
        var (uy0, uy1, uy2) = RotT(c, s, dx, dy, dz, y0, y1, y2);

        var l00 = scale * ux0;
        var l01 = scale * ux1;
        var l02 = scale * ux2;
        var l10 = scale * uy0;
        var l11 = scale * uy1;
        var l12 = scale * uy2;

        // paper = vpCenter − s·viewCenter + L·(p − target)
        var tx = vpCenterX - scale * viewCenterX - (l00 * targetX + l01 * targetY + l02 * targetZ);
        var ty = vpCenterY - scale * viewCenterY - (l10 * targetX + l11 * targetY + l12 * targetZ);
        return new AffineXf(l00, l01, l02, tx, l10, l11, l12, ty, 0, 0, 1, 0);
    }

    /// <summary>[minX, minY, maxX, maxY] of the viewport's paper rectangle.</summary>
    public static double[] PaperRect(double centerX, double centerY, double width, double height) =>
        [centerX - width / 2, centerY - height / 2, centerX + width / 2, centerY + height / 2];

    /// <summary>CCW corners of the paper rectangle — the clip operand for AnnotationClash.ClipConvex.</summary>
    public static Vec2[] PaperRectCcw(double centerX, double centerY, double width, double height) =>
    [
        new Vec2(centerX - width / 2, centerY - height / 2),
        new Vec2(centerX + width / 2, centerY - height / 2),
        new Vec2(centerX + width / 2, centerY + height / 2),
        new Vec2(centerX - width / 2, centerY + height / 2),
    ];

    private static (double X, double Y, double Z) Unit(double x, double y, double z)
    {
        var len = Math.Sqrt(x * x + y * y + z * z);
        return len > 1e-12 ? (x / len, y / len, z / len) : (0, 0, 1);
    }

    private static (double X, double Y, double Z) Cross(double ax, double ay, double az, double bx, double by, double bz) =>
        (ay * bz - az * by, az * bx - ax * bz, ax * by - ay * bx);

    private static (double X, double Y, double Z) CrossUnit(double ax, double ay, double az, double bx, double by, double bz) =>
        Unit(Cross(ax, ay, az, bx, by, bz).X, Cross(ax, ay, az, bx, by, bz).Y, Cross(ax, ay, az, bx, by, bz).Z);

    private static (double X, double Y, double Z) RotT(double c, double s, double dx, double dy, double dz, double vx, double vy, double vz)
    {
        var (cx, cy, cz) = Cross(dx, dy, dz, vx, vy, vz);
        var dot = dx * vx + dy * vy + dz * vz;
        return (c * vx - s * cx + (1 - c) * dot * dx,
                c * vy - s * cy + (1 - c) * dot * dy,
                c * vz - s * cz + (1 - c) * dot * dz);
    }
}

/// <summary>Clipping of projected model linework to a viewport's axis-aligned paper rectangle.</summary>
public static class ViewportClip
{
    /// <summary>
    /// Liang–Barsky, segment by segment; returns the inside runs of a (possibly clipped) polyline.
    /// A fully-inside closed ring arrives as one run ending at its first point, so callers should
    /// emit the runs as open polylines — every painted edge is already materialised.
    /// </summary>
    public static List<Vec2[]> Polyline(IReadOnlyList<Vec2> points, bool closed, double minX, double minY, double maxX, double maxY)
    {
        var runs = new List<Vec2[]>();
        if (points.Count < 2)
        {
            return runs;
        }

        var edges = closed ? points.Count : points.Count - 1;
        List<Vec2>? run = null;
        void Flush()
        {
            if (run is { Count: >= 2 })
            {
                runs.Add(run.ToArray());
            }

            run = null;
        }

        for (var k = 0; k < edges; k++)
        {
            var p = points[k];
            var q = points[(k + 1) % points.Count];
            if (!ClipSegment(p.X, p.Y, q.X, q.Y, minX, minY, maxX, maxY, out var ax, out var ay, out var bx, out var by))
            {
                Flush();
                continue;
            }

            var a = new Vec2(ax, ay);
            var b = new Vec2(bx, by);
            if (run is null)
            {
                run = [a];
            }
            else if (run[^1] != a)
            {
                Flush();
                run = [a];
            }

            if (run![^1] != b)
            {
                run.Add(b);
            }
        }

        Flush();
        return runs;
    }

    /// <summary>Convex subject polygon clipped to the rectangle (Sutherland–Hodgman via ClipConvex).</summary>
    public static Vec2[] Polygon(IReadOnlyList<Vec2> corners, double minX, double minY, double maxX, double maxY) =>
        AnnotationClash.ClipConvex(
            corners,
            [new Vec2(minX, minY), new Vec2(maxX, minY), new Vec2(maxX, maxY), new Vec2(minX, maxY)]);

    public static bool Inside(Vec2 p, double minX, double minY, double maxX, double maxY) =>
        p.X >= minX && p.X <= maxX && p.Y >= minY && p.Y <= maxY;

    private static bool ClipSegment(double x0, double y0, double x1, double y1, double minX, double minY, double maxX, double maxY, out double ax, out double ay, out double bx, out double by)
    {
        var dx = x1 - x0;
        var dy = y1 - y0;
        double t0 = 0, t1 = 1;
        Span<double> ps = stackalloc double[] { -dx, dx, -dy, dy };
        Span<double> qs = stackalloc double[] { x0 - minX, maxX - x0, y0 - minY, maxY - y0 };
        for (var i = 0; i < 4; i++)
        {
            var pi = ps[i];
            var qi = qs[i];
            if (pi == 0)
            {
                if (qi < 0)
                {
                    ax = ay = bx = by = 0;
                    return false;
                }

                continue;
            }

            var r = qi / pi;
            if (pi < 0)
            {
                if (r > t1)
                {
                    ax = ay = bx = by = 0;
                    return false;
                }

                if (r > t0)
                {
                    t0 = r;
                }
            }
            else
            {
                if (r < t0)
                {
                    ax = ay = bx = by = 0;
                    return false;
                }

                if (r < t1)
                {
                    t1 = r;
                }
            }
        }

        ax = x0 + t0 * dx;
        ay = y0 + t0 * dy;
        bx = x0 + t1 * dx;
        by = y0 + t1 * dy;
        return true;
    }
}
