namespace Forge.Shared;

/// <summary>
/// Thrown when a fillet/chamfer cannot decide which side of a line to keep: the corner
/// intersection lies strictly inside the segment and no pick point was supplied.
/// The plugin maps it to the typed error code <c>ambiguous_side</c>.
/// </summary>
public sealed class CadAmbiguousSideException : InvalidOperationException
{
    public CadAmbiguousSideException(int lineNumber, string message)
        : base(message)
    {
        LineNumber = lineNumber;
    }

    /// <summary>1 or 2 — which input line is ambiguous.</summary>
    public int LineNumber { get; }
}

/// <summary>
/// Pure decision math for the precision edit tools (trim / extend / fillet / chamfer side
/// selection). No AutoCAD dependency: the plugin feeds plain numbers (distances along a
/// curve, line endpoints, arc angles in radians) and applies the decisions to the database.
/// </summary>
public static class CadEditMath
{
    // ---------------------------------------------------------------- trim

    /// <summary>Merge tolerance (curve-length units) for split positions along a curve.</summary>
    public static double SplitTolerance(double curveLength) => 1e-9 * Math.Abs(curveLength) + 1e-9;

    /// <summary>
    /// Normalizes raw cut positions (distance along the curve from its start, 0..length) into
    /// the sorted, de-duplicated list of effective cuts.
    /// Open curve: cuts within tolerance of either end are dropped (cutting at an endpoint
    /// removes nothing). Closed curve: a cut at the end is the same point as the start and is
    /// mapped to 0; boundary cuts are kept because they are real cuts on a loop.
    /// </summary>
    public static double[] NormalizeCutDistances(IEnumerable<double> distances, double length, bool closed, double? tolerance = null)
    {
        if (length <= 0 || double.IsNaN(length) || double.IsInfinity(length))
        {
            return [];
        }

        var tol = tolerance ?? SplitTolerance(length);
        var mapped = new List<double>();
        foreach (var raw in distances)
        {
            if (double.IsNaN(raw) || double.IsInfinity(raw))
            {
                continue;
            }

            var d = raw;
            if (closed)
            {
                if (d < -tol || d > length + tol)
                {
                    continue;
                }

                if (d >= length - tol || d <= tol)
                {
                    d = 0;
                }
            }
            else
            {
                if (d <= tol || d >= length - tol)
                {
                    continue;
                }
            }

            mapped.Add(d);
        }

        mapped.Sort();
        var result = new List<double>(mapped.Count);
        foreach (var d in mapped)
        {
            if (result.Count == 0 || d - result[^1] > tol)
            {
                result.Add(d);
            }
        }

        return result.ToArray();
    }

    /// <summary>Minimum number of distinct cuts needed to split the curve into removable pieces.</summary>
    public static int MinimumCuts(bool closed) => closed ? 2 : 1;

    /// <summary>
    /// Index of the logical segment that contains position <paramref name="distance"/>.
    /// Open curve with n cuts: segments 0..n, segment k spans [cut[k-1], cut[k]] (cut[-1]=0,
    /// cut[n]=length). Closed curve with n cuts: segments 0..n-1, segment k spans
    /// [cut[k], cut[k+1]] and segment n-1 wraps from cut[n-1] through the start to cut[0].
    /// </summary>
    public static int FindSegmentIndex(IReadOnlyList<double> sortedCuts, double length, bool closed, double distance)
    {
        var n = sortedCuts.Count;
        if (n == 0)
        {
            return 0;
        }

        if (closed)
        {
            if (distance < sortedCuts[0] || distance >= sortedCuts[n - 1])
            {
                return n - 1;
            }

            for (var k = 0; k < n - 1; k++)
            {
                if (distance >= sortedCuts[k] && distance < sortedCuts[k + 1])
                {
                    return k;
                }
            }

            return n - 1;
        }

        for (var k = 0; k < n; k++)
        {
            if (distance < sortedCuts[k])
            {
                return k;
            }
        }

        return n;
    }

    /// <summary>Number of logical segments produced by <paramref name="cutCount"/> cuts.</summary>
    public static int SegmentCount(int cutCount, bool closed) => closed ? cutCount : cutCount + 1;

    /// <summary>
    /// Decides, for each split piece (identified by the position of its midpoint along the
    /// original curve), whether it is removed: exactly the pieces lying in the segment that
    /// contains the pick position. Returns per-piece removal flags.
    /// </summary>
    public static bool[] SelectPiecesToRemove(
        IReadOnlyList<double> sortedCuts,
        double length,
        bool closed,
        double pickDistance,
        IReadOnlyList<double> pieceMidDistances)
    {
        var removeSegment = FindSegmentIndex(sortedCuts, length, closed, pickDistance);
        var flags = new bool[pieceMidDistances.Count];
        for (var i = 0; i < flags.Length; i++)
        {
            flags[i] = FindSegmentIndex(sortedCuts, length, closed, pieceMidDistances[i]) == removeSegment;
        }

        return flags;
    }

    /// <summary>Index of the smallest value (closest piece), or -1 for an empty list.</summary>
    public static int IndexOfClosest(IReadOnlyList<double> distances)
    {
        var best = -1;
        var bestValue = double.PositiveInfinity;
        for (var i = 0; i < distances.Count; i++)
        {
            if (distances[i] < bestValue)
            {
                bestValue = distances[i];
                best = i;
            }
        }

        return best;
    }

    // ---------------------------------------------------------------- extend

    /// <summary>
    /// Line parameter t of point p on the line s→e (t=0 at s, t=1 at e), or null when the
    /// line is degenerate or p is not on the infinite line (perpendicular offset beyond
    /// tolerance). Points are [x,y] or [x,y,z].
    /// </summary>
    public static double? LineParameter(double[] s, double[] e, double[] p)
    {
        var dx = e[0] - s[0];
        var dy = e[1] - s[1];
        var dz = Z(e) - Z(s);
        var len2 = dx * dx + dy * dy + dz * dz;
        if (len2 <= 1e-24)
        {
            return null;
        }

        var px = p[0] - s[0];
        var py = p[1] - s[1];
        var pz = Z(p) - Z(s);
        var t = (px * dx + py * dy + pz * dz) / len2;

        // Perpendicular offset check: candidates come from IntersectWith(ExtendThis) so they
        // should lie on the infinite line; reject anything that clearly does not.
        var ox = px - t * dx;
        var oy = py - t * dy;
        var oz = pz - t * dz;
        var len = Math.Sqrt(len2);
        var offset = Math.Sqrt(ox * ox + oy * oy + oz * oz);
        return offset > 1e-6 * Math.Max(1.0, len) ? null : t;
    }

    /// <summary>
    /// True when parameter t lies strictly beyond the extended end: t &gt; 1+eps for the end,
    /// t &lt; -eps for the start. Interior points (which would shorten the line) are rejected.
    /// </summary>
    public static bool IsBeyondLineEnd(double t, bool atEnd, double eps = 1e-9) =>
        atEnd ? t > 1.0 + eps : t < -eps;

    /// <summary>
    /// Picks the candidate nearest to the extended end among those strictly beyond it.
    /// Returns the candidate index (or -1) and the extension length in curve units.
    /// </summary>
    public static int SelectLineExtension(
        double[] s,
        double[] e,
        IReadOnlyList<double[]> candidates,
        bool atEnd,
        out double extensionLength)
    {
        extensionLength = double.PositiveInfinity;
        var len = Math.Sqrt(Sq(e[0] - s[0]) + Sq(e[1] - s[1]) + Sq(Z(e) - Z(s)));
        var best = -1;
        for (var i = 0; i < candidates.Count; i++)
        {
            if (LineParameter(s, e, candidates[i]) is not { } t || !IsBeyondLineEnd(t, atEnd))
            {
                continue;
            }

            var extra = (atEnd ? t - 1.0 : -t) * len;
            if (extra < extensionLength)
            {
                extensionLength = extra;
                best = i;
            }
        }

        if (best < 0)
        {
            extensionLength = 0;
        }

        return best;
    }

    /// <summary>Normalizes an angle (radians) into [0, 2π).</summary>
    public static double NormalizeRadians(double angle)
    {
        var twoPi = 2.0 * Math.PI;
        var m = angle % twoPi;
        if (m < 0)
        {
            m += twoPi;
        }

        return m >= twoPi ? 0 : m;
    }

    /// <summary>
    /// Extra sweep (radians) needed to extend an arc (CCW from startAngle to endAngle) to a
    /// candidate angle on its circle, or null when the candidate is not beyond the extended
    /// end. Extending the end means going CCW from endAngle; extending the start means going
    /// CW from startAngle; in both cases the candidate must lie in the open gap that the arc
    /// does not cover (so the arc never shrinks and never closes into a full circle).
    /// </summary>
    public static double? ArcExtensionSweep(double startAngle, double endAngle, double candidateAngle, bool atEnd, double eps = 1e-9)
    {
        var sweep = NormalizeRadians(endAngle - startAngle);
        if (sweep <= eps)
        {
            sweep = 2.0 * Math.PI; // full circle: no gap to extend into
        }

        var gap = 2.0 * Math.PI - sweep;
        if (gap <= 2 * eps)
        {
            return null;
        }

        var extra = atEnd
            ? NormalizeRadians(candidateAngle - endAngle)
            : NormalizeRadians(startAngle - candidateAngle);
        return extra > eps && extra < gap - eps ? extra : null;
    }

    /// <summary>
    /// Picks the candidate angle needing the smallest extra sweep beyond the extended end.
    /// Returns the candidate index (or -1) and the extra sweep in radians.
    /// </summary>
    public static int SelectArcExtension(
        double startAngle,
        double endAngle,
        IReadOnlyList<double> candidateAngles,
        bool atEnd,
        out double extraSweep)
    {
        extraSweep = double.PositiveInfinity;
        var best = -1;
        for (var i = 0; i < candidateAngles.Count; i++)
        {
            if (ArcExtensionSweep(startAngle, endAngle, candidateAngles[i], atEnd) is { } extra && extra < extraSweep)
            {
                extraSweep = extra;
                best = i;
            }
        }

        if (best < 0)
        {
            extraSweep = 0;
        }

        return best;
    }

    /// <summary>Supported extend modes (lower-case); null for an unknown mode.</summary>
    public static string? NormalizeExtendMode(string? mode)
    {
        var m = string.IsNullOrWhiteSpace(mode) ? "end" : mode.Trim().ToLowerInvariant();
        return m is "start" or "end" or "both" or "auto" or "near" ? m : null;
    }

    // ---------------------------------------------------------------- fillet / chamfer side

    /// <summary>
    /// Decides which endpoint of line start→end survives a fillet/chamfer at the corner
    /// intersection pInt. Returns true when the START survives (the end is moved to the
    /// tangent/corner point), false when the END survives.
    /// <list type="bullet">
    /// <item>Intersection outside the segment (or at an endpoint): the far endpoint survives.</item>
    /// <item>Intersection strictly inside the segment (the line straddles the corner): the
    /// side containing the projection of <paramref name="pick"/> survives; without a pick a
    /// <see cref="CadAmbiguousSideException"/> is thrown.</item>
    /// </list>
    /// </summary>
    public static bool ChooseKeepStart(double[] start, double[] end, double[] pInt, double[]? pick, int lineNumber = 1)
    {
        var dx = end[0] - start[0];
        var dy = end[1] - start[1];
        var len2 = dx * dx + dy * dy;
        if (len2 <= 1e-24)
        {
            throw new InvalidOperationException($"Line {lineNumber} has zero length.");
        }

        var len = Math.Sqrt(len2);
        var tInt = ((pInt[0] - start[0]) * dx + (pInt[1] - start[1]) * dy) / len2;
        var tol = SplitTolerance(len) / len; // parameter-space tolerance
        var straddles = tInt > tol && tInt < 1.0 - tol;
        if (!straddles)
        {
            // Whole segment lies on one side: keep the far end (start if the corner is at/after the end).
            return tInt >= 0.5;
        }

        if (pick is null || pick.Length < 2)
        {
            throw new CadAmbiguousSideException(
                lineNumber,
                $"Line {lineNumber} crosses the corner point (intersection is inside the segment), so the side to keep is ambiguous.");
        }

        var tPick = ((pick[0] - start[0]) * dx + (pick[1] - start[1]) * dy) / len2;
        if (Math.Abs(tPick - tInt) <= tol)
        {
            throw new CadAmbiguousSideException(
                lineNumber,
                $"pickPoint{lineNumber} projects onto the corner point itself; pick a point clearly on the side to keep.");
        }

        return tPick < tInt;
    }

    private static double Z(double[] p) => p.Length >= 3 ? p[2] : 0.0;

    private static double Sq(double v) => v * v;
}
