using System.Text;
using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>
/// Pure geometry, OSNAP, MText formatting, and high-level AEC generator math (Waves C29–C30).
/// Lives in Forge.Shared with zero AutoCAD dependency so all spatial search, snap, grid,
/// and wall algorithms are deterministically unit-testable.
/// </summary>
public static class CadSpatialMath
{
    // =========================================================================
    // 1. MText / CAD Text normalization & matching (forge_entity_search_text)
    // =========================================================================

    /// <summary>
    /// Strips AutoCAD MText formatting codes and legacy DText %% control codes into plain
    /// searchable text while preserving Unicode (including Vietnamese diacritics).
    /// </summary>
    public static string StripMTextFormatting(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return string.Empty;
        }

        var s = raw!;
        // Legacy %% codes (case-insensitive)
        s = Regex.Replace(s, @"%%[cC]", "Ø");
        s = Regex.Replace(s, @"%%[dD]", "°");
        s = Regex.Replace(s, @"%%[pP]", "±");
        s = Regex.Replace(s, @"%%[uUoO]", "");

        var sb = new StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (c is '{' or '}')
            {
                continue;
            }

            if (c == '\\' && i + 1 < s.Length)
            {
                var next = s[i + 1];
                switch (next)
                {
                    case '\\':
                    case '{':
                    case '}':
                        sb.Append(next);
                        i++;
                        continue;
                    case 'P':
                    case 'p' when i + 2 >= s.Length || s[i + 2] != 'x':
                        if (next == 'P')
                        {
                            sb.Append('\n');
                            i++;
                            continue;
                        }
                        break;
                    case '~':
                        sb.Append(' ');
                        i++;
                        continue;
                    case 'L':
                    case 'l':
                    case 'O':
                    case 'o':
                    case 'K':
                    case 'k':
                        i++;
                        continue;
                    case 'S':
                    case 's':
                        // Stacked fraction \Sa^b; or \Sa/b; or \Sa#b;
                        var semi = s.IndexOf(';', i + 2);
                        if (semi > i + 1)
                        {
                            var stackContent = s.Substring(i + 2, semi - (i + 2))
                                .Replace('^', '/')
                                .Replace('#', '/');
                            sb.Append(stackContent.Trim());
                            i = semi;
                            continue;
                        }
                        break;
                    case 'f':
                    case 'F':
                    case 'H':
                    case 'h':
                    case 'W':
                    case 'w':
                    case 'C':
                    case 'c':
                    case 'T':
                    case 't':
                    case 'Q':
                    case 'q':
                    case 'A':
                    case 'a':
                    case 'p':
                        // Parameterized code terminated by ';'
                        var endSemi = s.IndexOf(';', i + 2);
                        if (endSemi > i + 1)
                        {
                            i = endSemi;
                            continue;
                        }
                        break;
                }
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    public static bool MatchesTextQuery(
        string plainText,
        string? rawText,
        string query,
        bool caseSensitive = false,
        bool exactMatch = false,
        bool useRegex = false)
    {
        if (string.IsNullOrEmpty(query))
        {
            return false;
        }

        if (useRegex)
        {
            var opts = caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
            return Regex.IsMatch(plainText, query, opts) ||
                   (rawText is not null && Regex.IsMatch(rawText, query, opts));
        }

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (exactMatch)
        {
            return string.Equals(plainText.Trim(), query.Trim(), comparison) ||
                   (rawText is not null && string.Equals(rawText.Trim(), query.Trim(), comparison));
        }

        return plainText.IndexOf(query, comparison) >= 0 ||
               (rawText is not null && rawText.IndexOf(query, comparison) >= 0);
    }

    // =========================================================================
    // 2. AI OSNAP candidate computation & ranking (forge_entity_snap)
    // =========================================================================

    public static readonly HashSet<string> SupportedSnapKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "endpoint",
        "midpoint",
        "center",
        "quadrant",
        "insertion",
        "nearest",
        "perpendicular",
        "intersection"
    };

    public sealed record SnapCandidate(
        string Kind,
        double[] Point,
        double Distance,
        string Handle,
        string EntityType,
        string Layer,
        string? SecondaryHandle = null);

    public static IEnumerable<SnapCandidate> ComputeLineSegmentSnaps(
        double[] s,
        double[] e,
        double[] queryPoint,
        double[]? fromPoint,
        string handle,
        string entityType,
        string layer,
        ISet<string> enabledKinds,
        bool includeStartEndpoint = true,
        bool includeEndEndpoint = true)
    {
        var z = s.Length >= 3 ? s[2] : 0.0;
        var ez = e.Length >= 3 ? e[2] : z;

        if (enabledKinds.Contains("endpoint"))
        {
            if (includeStartEndpoint)
            {
                double[] p = [s[0], s[1], z];
                yield return new SnapCandidate("endpoint", p, Dist2D(p, queryPoint), handle, entityType, layer);
            }

            if (includeEndEndpoint)
            {
                double[] p = [e[0], e[1], ez];
                yield return new SnapCandidate("endpoint", p, Dist2D(p, queryPoint), handle, entityType, layer);
            }
        }

        if (enabledKinds.Contains("midpoint"))
        {
            double[] mid = [(s[0] + e[0]) * 0.5, (s[1] + e[1]) * 0.5, (z + ez) * 0.5];
            yield return new SnapCandidate("midpoint", mid, Dist2D(mid, queryPoint), handle, entityType, layer);
        }

        var dx = e[0] - s[0];
        var dy = e[1] - s[1];
        var len2 = dx * dx + dy * dy;
        if (len2 > 1e-18)
        {
            if (enabledKinds.Contains("nearest"))
            {
                var t = Math.Clamp(((queryPoint[0] - s[0]) * dx + (queryPoint[1] - s[1]) * dy) / len2, 0.0, 1.0);
                double[] near = [s[0] + t * dx, s[1] + t * dy, z + t * (ez - z)];
                yield return new SnapCandidate("nearest", near, Dist2D(near, queryPoint), handle, entityType, layer);
            }

            if (enabledKinds.Contains("perpendicular"))
            {
                var refPt = fromPoint ?? queryPoint;
                var t = ((refPt[0] - s[0]) * dx + (refPt[1] - s[1]) * dy) / len2;
                if (t >= -1e-6 && t <= 1.0 + 1e-6)
                {
                    var tc = Math.Clamp(t, 0.0, 1.0);
                    double[] perp = [s[0] + tc * dx, s[1] + tc * dy, z + tc * (ez - z)];
                    yield return new SnapCandidate("perpendicular", perp, Dist2D(perp, queryPoint), handle, entityType, layer);
                }
            }
        }
    }

    public static IEnumerable<SnapCandidate> ComputeCircleOrArcSnaps(
        double[] center,
        double radius,
        double? startAngleRad,
        double? endAngleRad,
        double[] queryPoint,
        string handle,
        string entityType,
        string layer,
        ISet<string> enabledKinds)
    {
        var z = center.Length >= 3 ? center[2] : 0.0;
        var isArc = startAngleRad.HasValue && endAngleRad.HasValue;

        if (enabledKinds.Contains("center"))
        {
            double[] c = [center[0], center[1], z];
            yield return new SnapCandidate("center", c, Dist2D(c, queryPoint), handle, entityType, layer);
        }

        if (isArc)
        {
            var sa = CadEditMath.NormalizeRadians(startAngleRad!.Value);
            var ea = CadEditMath.NormalizeRadians(endAngleRad!.Value);
            var sweep = CadEditMath.NormalizeRadians(ea - sa);
            if (sweep <= 1e-9)
            {
                sweep = 2.0 * Math.PI;
            }

            if (enabledKinds.Contains("endpoint"))
            {
                double[] pStart = [center[0] + radius * Math.Cos(sa), center[1] + radius * Math.Sin(sa), z];
                double[] pEnd = [center[0] + radius * Math.Cos(ea), center[1] + radius * Math.Sin(ea), z];
                yield return new SnapCandidate("endpoint", pStart, Dist2D(pStart, queryPoint), handle, entityType, layer);
                yield return new SnapCandidate("endpoint", pEnd, Dist2D(pEnd, queryPoint), handle, entityType, layer);
            }

            if (enabledKinds.Contains("midpoint"))
            {
                var midAng = sa + sweep * 0.5;
                double[] pMid = [center[0] + radius * Math.Cos(midAng), center[1] + radius * Math.Sin(midAng), z];
                yield return new SnapCandidate("midpoint", pMid, Dist2D(pMid, queryPoint), handle, entityType, layer);
            }

            if (enabledKinds.Contains("quadrant"))
            {
                for (var q = 0; q < 4; q++)
                {
                    var qAng = q * (Math.PI * 0.5);
                    if (IsAngleOnArc(qAng, sa, sweep))
                    {
                        double[] pq = [center[0] + radius * Math.Cos(qAng), center[1] + radius * Math.Sin(qAng), z];
                        yield return new SnapCandidate("quadrant", pq, Dist2D(pq, queryPoint), handle, entityType, layer);
                    }
                }
            }

            if (enabledKinds.Contains("nearest"))
            {
                var ang = CadEditMath.NormalizeRadians(Math.Atan2(queryPoint[1] - center[1], queryPoint[0] - center[0]));
                if (IsAngleOnArc(ang, sa, sweep))
                {
                    double[] pn = [center[0] + radius * Math.Cos(ang), center[1] + radius * Math.Sin(ang), z];
                    yield return new SnapCandidate("nearest", pn, Dist2D(pn, queryPoint), handle, entityType, layer);
                }
            }
        }
        else
        {
            if (enabledKinds.Contains("quadrant"))
            {
                for (var q = 0; q < 4; q++)
                {
                    var qAng = q * (Math.PI * 0.5);
                    double[] pq = [center[0] + radius * Math.Cos(qAng), center[1] + radius * Math.Sin(qAng), z];
                    yield return new SnapCandidate("quadrant", pq, Dist2D(pq, queryPoint), handle, entityType, layer);
                }
            }

            if (enabledKinds.Contains("nearest"))
            {
                var dx = queryPoint[0] - center[0];
                var dy = queryPoint[1] - center[1];
                var d = Math.Sqrt(dx * dx + dy * dy);
                var ang = d > 1e-12 ? Math.Atan2(dy, dx) : 0.0;
                double[] pn = [center[0] + radius * Math.Cos(ang), center[1] + radius * Math.Sin(ang), z];
                yield return new SnapCandidate("nearest", pn, Dist2D(pn, queryPoint), handle, entityType, layer);
            }
        }
    }

    /// <summary>
    /// Filters snap candidates to those within <paramref name="apertureRadius"/>, de-duplicates
    /// coincident points of the same kind, and orders exact geometric snaps (intersection,
    /// endpoint, midpoint, center, insertion, quadrant, perpendicular) ahead of fallback
    /// 'nearest', then by distance and snap priority.
    /// </summary>
    public static SnapCandidate[] RankSnapCandidates(
        IEnumerable<SnapCandidate> rawCandidates,
        double apertureRadius,
        int maxResults = 20)
    {
        var filtered = rawCandidates
            .Where(c => c.Distance <= apertureRadius + 1e-9)
            .OrderBy(c => string.Equals(c.Kind, "nearest", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(c => Math.Round(c.Distance, 6))
            .ThenBy(c => SnapPriority(c.Kind))
            .ToList();

        var result = new List<SnapCandidate>(Math.Min(filtered.Count, maxResults));
        foreach (var cand in filtered)
        {
            var isDuplicate = result.Any(existing =>
                string.Equals(existing.Kind, cand.Kind, StringComparison.OrdinalIgnoreCase) &&
                Dist2D(existing.Point, cand.Point) <= 1e-6);
            if (!isDuplicate)
            {
                result.Add(cand);
                if (result.Count >= maxResults)
                {
                    break;
                }
            }
        }

        return result.ToArray();
    }

    private static int SnapPriority(string kind) => kind.ToLowerInvariant() switch
    {
        "intersection" => 0,
        "endpoint" => 1,
        "midpoint" => 2,
        "center" => 3,
        "insertion" => 4,
        "quadrant" => 5,
        "perpendicular" => 6,
        _ => 7
    };

    private static bool IsAngleOnArc(double angleRad, double startAngleRad, double sweepRad)
    {
        var rel = CadEditMath.NormalizeRadians(angleRad - startAngleRad);
        return rel <= sweepRad + 1e-7;
    }

    // =========================================================================
    // 3. Structural Grid Generator (forge_draw_grid)
    // =========================================================================

    public sealed record GridLineSpec(
        string AxisLabel,
        string Orientation, // "vertical" (constant X) | "horizontal" (constant Y)
        double Coordinate,
        double[] Start,
        double[] End);

    public sealed record GridBubbleSpec(
        string AxisLabel,
        string Side,
        double[] Center,
        double Radius,
        double TextHeight);

    public sealed record GridDimensionSpec(
        string Kind, // "bay" | "overall"
        string Orientation, // "horizontal" | "vertical"
        double[] P1,
        double[] P2,
        double[] DimLinePoint);

    public sealed record StructuralGridPlan(
        GridLineSpec[] GridLines,
        GridBubbleSpec[] Bubbles,
        GridDimensionSpec[] Dimensions,
        Dictionary<string, double[]> Intersections,
        double[] MinExtents,
        double[] MaxExtents);

    /// <summary>
    /// Computes the complete geometric layout for an architectural/structural axis grid:
    /// vertical axes (X coordinates, typically labeled "1", "2", "3"...) and horizontal axes
    /// (Y coordinates, typically labeled "A", "B", "C"...), extension lines, bubble circles/labels,
    /// named grid intersections ("A-1" -> [x,y]), and optional bay/overall dimension specs.
    /// </summary>
    public static StructuralGridPlan BuildStructuralGrid(
        double[]? origin,
        double[]? xPositions,
        double[]? xSpacings,
        string[]? xLabels,
        double[]? yPositions,
        double[]? ySpacings,
        string[]? yLabels,
        double? extensionLength = null,
        double? bubbleRadius = null,
        string bubbleSides = "start",
        bool includeDimensions = true)
    {
        var ox = origin is { Length: >= 2 } ? origin[0] : 0.0;
        var oy = origin is { Length: >= 2 } ? origin[1] : 0.0;

        var xs = ResolveAxisCoordinates(ox, xPositions, xSpacings, "X");
        var ys = ResolveAxisCoordinates(oy, yPositions, ySpacings, "Y");

        if (xs.Length < 1 || ys.Length < 1)
        {
            throw new ArgumentException("Both X and Y axes require at least 1 grid line.");
        }

        var vLabels = ResolveLabels(xLabels, xs.Length, numeric: true);
        var hLabels = ResolveLabels(yLabels, ys.Length, numeric: false);

        var minX = xs.Min();
        var maxX = xs.Max();
        var minY = ys.Min();
        var maxY = ys.Max();
        var span = Math.Max(maxX - minX, maxY - minY);
        if (span <= 0)
        {
            span = 1000.0;
        }

        var ext = extensionLength is > 0 ? extensionLength.Value : Math.Max(span * 0.08, 100.0);
        var rBubble = bubbleRadius is > 0 ? bubbleRadius.Value : Math.Max(ext * 0.25, 25.0);
        var textH = rBubble * 0.9;

        var sidesMode = (bubbleSides ?? "start").Trim().ToLowerInvariant();
        if (sidesMode is not ("start" or "end" or "both" or "none"))
        {
            throw new ArgumentException("bubbleSides must be 'start', 'end', 'both', or 'none'.");
        }

        var lines = new List<GridLineSpec>(xs.Length + ys.Length);
        var bubbles = new List<GridBubbleSpec>((xs.Length + ys.Length) * 2);

        // Vertical grid lines (constant X, spanning Y from minY - ext to maxY + ext)
        var yLineStart = minY - ext;
        var yLineEnd = maxY + ext;
        for (var i = 0; i < xs.Length; i++)
        {
            var x = xs[i];
            var lbl = vLabels[i];
            lines.Add(new GridLineSpec(lbl, "vertical", x, [x, yLineStart], [x, yLineEnd]));

            if (sidesMode is "start" or "both")
            {
                bubbles.Add(new GridBubbleSpec(lbl, "bottom", [x, yLineStart - rBubble], rBubble, textH));
            }

            if (sidesMode is "end" or "both")
            {
                bubbles.Add(new GridBubbleSpec(lbl, "top", [x, yLineEnd + rBubble], rBubble, textH));
            }
        }

        // Horizontal grid lines (constant Y, spanning X from minX - ext to maxX + ext)
        var xLineStart = minX - ext;
        var xLineEnd = maxX + ext;
        for (var j = 0; j < ys.Length; j++)
        {
            var y = ys[j];
            var lbl = hLabels[j];
            lines.Add(new GridLineSpec(lbl, "horizontal", y, [xLineStart, y], [xLineEnd, y]));

            if (sidesMode is "start" or "both")
            {
                bubbles.Add(new GridBubbleSpec(lbl, "left", [xLineStart - rBubble, y], rBubble, textH));
            }

            if (sidesMode is "end" or "both")
            {
                bubbles.Add(new GridBubbleSpec(lbl, "right", [xLineEnd + rBubble, y], rBubble, textH));
            }
        }

        // Named intersections ("A-1" and "1-A" canonicalized as "{hLabel}-{vLabel}")
        var intersections = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
        for (var j = 0; j < ys.Length; j++)
        {
            for (var i = 0; i < xs.Length; i++)
            {
                var key = $"{hLabels[j]}-{vLabels[i]}";
                intersections[key] = [xs[i], ys[j]];
            }
        }

        var dims = new List<GridDimensionSpec>();
        if (includeDimensions)
        {
            var bayOffset = ext * 0.45;
            var overallOffset = ext * 0.75;

            if (xs.Length >= 2)
            {
                var yBay = minY - bayOffset;
                for (var i = 0; i < xs.Length - 1; i++)
                {
                    dims.Add(new GridDimensionSpec(
                        "bay",
                        "horizontal",
                        [xs[i], minY],
                        [xs[i + 1], minY],
                        [(xs[i] + xs[i + 1]) * 0.5, yBay]));
                }

                if (xs.Length >= 3)
                {
                    dims.Add(new GridDimensionSpec(
                        "overall",
                        "horizontal",
                        [xs[0], minY],
                        [xs[^1], minY],
                        [(xs[0] + xs[^1]) * 0.5, minY - overallOffset]));
                }
            }

            if (ys.Length >= 2)
            {
                var xBay = minX - bayOffset;
                for (var j = 0; j < ys.Length - 1; j++)
                {
                    dims.Add(new GridDimensionSpec(
                        "bay",
                        "vertical",
                        [minX, ys[j]],
                        [minX, ys[j + 1]],
                        [xBay, (ys[j] + ys[j + 1]) * 0.5]));
                }

                if (ys.Length >= 3)
                {
                    dims.Add(new GridDimensionSpec(
                        "overall",
                        "vertical",
                        [minX, ys[0]],
                        [minX, ys[^1]],
                        [minX - overallOffset, (ys[0] + ys[^1]) * 0.5]));
                }
            }
        }

        return new StructuralGridPlan(
            lines.ToArray(),
            bubbles.ToArray(),
            dims.ToArray(),
            intersections,
            [xLineStart - 2 * rBubble, yLineStart - 2 * rBubble],
            [xLineEnd + 2 * rBubble, yLineEnd + 2 * rBubble]);
    }

    private static double[] ResolveAxisCoordinates(double originCoord, double[]? positions, double[]? spacings, string axisName)
    {
        if (positions is { Length: > 0 } && spacings is { Length: > 0 })
        {
            throw new ArgumentException($"Specify either {axisName.ToLowerInvariant()}Positions or {axisName.ToLowerInvariant()}Spacings, not both.");
        }

        if (positions is { Length: > 0 })
        {
            if (positions.Any(v => double.IsNaN(v) || double.IsInfinity(v)))
            {
                throw new ArgumentException($"{axisName} positions must be finite numbers.");
            }

            return positions.ToArray();
        }

        if (spacings is { Length: > 0 })
        {
            var coords = new double[spacings.Length + 1];
            coords[0] = originCoord;
            var acc = originCoord;
            for (var i = 0; i < spacings.Length; i++)
            {
                var s = spacings[i];
                if (s <= 0 || double.IsNaN(s) || double.IsInfinity(s))
                {
                    throw new ArgumentException($"{axisName} spacings must be > 0.");
                }

                acc += s;
                coords[i + 1] = acc;
            }

            return coords;
        }

        throw new ArgumentException($"Provide either {axisName.ToLowerInvariant()}Positions[] or {axisName.ToLowerInvariant()}Spacings[].");
    }

    private static string[] ResolveLabels(string[]? customLabels, int count, bool numeric)
    {
        if (customLabels is { Length: > 0 })
        {
            if (customLabels.Length != count)
            {
                throw new ArgumentException($"Label count ({customLabels.Length}) does not match axis count ({count}).");
            }

            return customLabels;
        }

        var labels = new string[count];
        for (var i = 0; i < count; i++)
        {
            if (numeric)
            {
                labels[i] = (i + 1).ToString();
            }
            else
            {
                // A..Z, then AA, AB...
                labels[i] = IndexToAlphaLabel(i);
            }
        }

        return labels;
    }

    private static string IndexToAlphaLabel(int index)
    {
        var sb = new StringBuilder();
        var n = index;
        while (n >= 0)
        {
            sb.Insert(0, (char)('A' + (n % 26)));
            n = (n / 26) - 1;
        }

        return sb.ToString();
    }

    // =========================================================================
    // 4. Double-Line Wall with Openings Generator (forge_draw_wall)
    // =========================================================================

    public sealed record WallOpeningInput(
        int SegmentIndex,
        double OffsetFromStart,
        double Width);

    public sealed record WallOpeningGeometry(
        int SegmentIndex,
        double[] Center,
        double Width,
        double AngleDeg,
        double[][] JambLeft,
        double[][] JambRight);

    public sealed record DoubleLineWallPlan(
        double[][] LeftRail,
        double[][] RightRail,
        CadPolylineSpec[] ClosedWallLoops,
        WallOpeningGeometry[] Openings);

    /// <summary>
    /// Computes a mitered double-line wall along a polyline path (vertices >= 2) with
    /// given thickness (> 0), justification ('center' | 'left' | 'right'), closed flag,
    /// and optional door/window openings along segments. Produces closed wall boundary loops
    /// (ready for Polyline + Hatch) and exact opening centers/angles.
    /// </summary>
    public static DoubleLineWallPlan BuildDoubleLineWall(
        double[][] vertices,
        double thickness,
        string justification = "center",
        bool closed = false,
        IReadOnlyList<WallOpeningInput>? openings = null)
    {
        if (vertices is null || vertices.Length < (closed ? 3 : 2))
        {
            throw new ArgumentException(closed
                ? "Closed wall path requires at least 3 vertices."
                : "Open wall path requires at least 2 vertices.");
        }

        if (thickness <= 0 || double.IsNaN(thickness) || double.IsInfinity(thickness))
        {
            throw new ArgumentException("Wall thickness must be finite and > 0.");
        }

        foreach (var v in vertices)
        {
            if (v is null || v.Length < 2 || double.IsNaN(v[0]) || double.IsNaN(v[1]))
            {
                throw new ArgumentException("Every wall vertex requires finite [x, y] coordinates.");
            }
        }

        var just = (justification ?? "center").Trim().ToLowerInvariant();
        var (leftOffset, rightOffset) = just switch
        {
            "center" or "middle" => (thickness * 0.5, -thickness * 0.5),
            "left" => (0.0, -thickness),
            "right" => (thickness, 0.0),
            _ => throw new ArgumentException("justification must be 'center', 'left', or 'right'.")
        };

        var n = vertices.Length;
        var segCount = closed ? n : n - 1;
        var segDirs = new double[segCount][];
        var segNormals = new double[segCount][];
        var segLengths = new double[segCount];

        for (var i = 0; i < segCount; i++)
        {
            var a = vertices[i];
            var b = vertices[(i + 1) % n];
            var dx = b[0] - a[0];
            var dy = b[1] - a[1];
            var len = Math.Sqrt(dx * dx + dy * dy);
            if (len <= 1e-9)
            {
                throw new ArgumentException($"Wall segment {i} has zero length.");
            }

            segLengths[i] = len;
            var ux = dx / len;
            var uy = dy / len;
            segDirs[i] = [ux, uy];
            // Left normal (90 deg CCW)
            segNormals[i] = [-uy, ux];
        }

        var leftRail = ComputeOffsetRail(vertices, segDirs, segNormals, leftOffset, closed);
        var rightRail = ComputeOffsetRail(vertices, segDirs, segNormals, rightOffset, closed);

        var validOpenings = openings?.ToList() ?? [];
        foreach (var op in validOpenings)
        {
            if (op.SegmentIndex < 0 || op.SegmentIndex >= segCount)
            {
                throw new ArgumentException($"Opening segmentIndex {op.SegmentIndex} is out of range (0..{segCount - 1}).");
            }

            if (op.Width <= 0 || op.OffsetFromStart < 0 || op.OffsetFromStart + op.Width > segLengths[op.SegmentIndex] + 1e-6)
            {
                throw new ArgumentException($"Opening on segment {op.SegmentIndex} (offset={op.OffsetFromStart}, width={op.Width}) exceeds segment length ({segLengths[op.SegmentIndex]:F3}).");
            }
        }

        var loops = new List<CadPolylineSpec>();
        var openingGeoms = new List<WallOpeningGeometry>(validOpenings.Count);

        if (validOpenings.Count == 0)
        {
            if (closed)
            {
                // Closed wall without openings: outer loop and inner loop as two closed polylines
                loops.Add(new CadPolylineSpec(leftRail, new double[leftRail.Length], true));
                loops.Add(new CadPolylineSpec(rightRail, new double[rightRail.Length], true));
            }
            else
            {
                // Single closed polygon going forward along leftRail and backward along rightRail
                var pts = new List<double[]>(leftRail.Length + rightRail.Length);
                pts.AddRange(leftRail);
                for (var i = rightRail.Length - 1; i >= 0; i--)
                {
                    pts.Add(rightRail[i]);
                }

                loops.Add(new CadPolylineSpec(pts.ToArray(), new double[pts.Count], true));
            }
        }
        else
        {
            // Build continuous wall runs across mitered corners, slicing only at door/window openings
            var currentLeft = new List<double[]>();
            var currentRight = new List<double[]>();

            void FlushCurrentRun()
            {
                if (currentLeft.Count < 2)
                {
                    currentLeft.Clear();
                    currentRight.Clear();
                    return;
                }

                var pts = new List<double[]>(currentLeft.Count + currentRight.Count);
                pts.AddRange(currentLeft);
                for (var i = currentRight.Count - 1; i >= 0; i--)
                {
                    pts.Add(currentRight[i]);
                }

                loops.Add(new CadPolylineSpec(pts.ToArray(), new double[pts.Count], true));
                currentLeft.Clear();
                currentRight.Clear();
            }

            for (var s = 0; s < segCount; s++)
            {
                var nextIdx = (s + 1) % n;
                var a = vertices[s];
                var ux = segDirs[s][0];
                var uy = segDirs[s][1];
                var nx = segNormals[s][0];
                var ny = segNormals[s][1];
                var segLen = segLengths[s];

                var segOps = validOpenings
                    .Where(o => o.SegmentIndex == s)
                    .OrderBy(o => o.OffsetFromStart)
                    .ToList();

                // Check overlapping openings on same segment
                for (var k = 0; k < segOps.Count - 1; k++)
                {
                    if (segOps[k].OffsetFromStart + segOps[k].Width > segOps[k + 1].OffsetFromStart + 1e-6)
                    {
                        throw new ArgumentException($"Overlapping openings on wall segment {s}.");
                    }
                }

                double[] LeftAt(double dist) =>
                    dist <= 1e-9 ? leftRail[s] :
                    dist >= segLen - 1e-9 ? leftRail[nextIdx] :
                    [a[0] + ux * dist + nx * leftOffset, a[1] + uy * dist + ny * leftOffset];

                double[] RightAt(double dist) =>
                    dist <= 1e-9 ? rightRail[s] :
                    dist >= segLen - 1e-9 ? rightRail[nextIdx] :
                    [a[0] + ux * dist + nx * rightOffset, a[1] + uy * dist + ny * rightOffset];

                if (currentLeft.Count == 0 && (segOps.Count == 0 || segOps[0].OffsetFromStart > 1e-6))
                {
                    currentLeft.Add(leftRail[s]);
                    currentRight.Add(rightRail[s]);
                }

                var cursor = 0.0;
                foreach (var op in segOps)
                {
                    if (op.OffsetFromStart - cursor > 1e-6)
                    {
                        if (currentLeft.Count == 0)
                        {
                            currentLeft.Add(LeftAt(cursor));
                            currentRight.Add(RightAt(cursor));
                        }

                        currentLeft.Add(LeftAt(op.OffsetFromStart));
                        currentRight.Add(RightAt(op.OffsetFromStart));
                        FlushCurrentRun();
                    }
                    else
                    {
                        FlushCurrentRun();
                    }

                    var opEnd = op.OffsetFromStart + op.Width;
                    var midDist = op.OffsetFromStart + op.Width * 0.5;
                    var midOffset = (leftOffset + rightOffset) * 0.5;
                    double[] opCenter = [a[0] + ux * midDist + nx * midOffset, a[1] + uy * midDist + ny * midOffset];
                    var angleDeg = Math.Atan2(uy, ux) * 180.0 / Math.PI;

                    openingGeoms.Add(new WallOpeningGeometry(
                        s,
                        opCenter,
                        op.Width,
                        angleDeg,
                        [LeftAt(op.OffsetFromStart), RightAt(op.OffsetFromStart)],
                        [LeftAt(opEnd), RightAt(opEnd)]));

                    cursor = opEnd;
                    if (segLen - cursor > 1e-6)
                    {
                        currentLeft.Add(LeftAt(cursor));
                        currentRight.Add(RightAt(cursor));
                    }
                }

                if (segLen - cursor > 1e-6)
                {
                    currentLeft.Add(leftRail[nextIdx]);
                    currentRight.Add(rightRail[nextIdx]);
                }
            }

            FlushCurrentRun();
        }

        return new DoubleLineWallPlan(leftRail, rightRail, loops.ToArray(), openingGeoms.ToArray());
    }

    private static double[][] ComputeOffsetRail(
        double[][] vertices,
        double[][] segDirs,
        double[][] segNormals,
        double signedOffset,
        bool closed)
    {
        var n = vertices.Length;
        var rail = new double[n][];

        for (var i = 0; i < n; i++)
        {
            if (!closed && i == 0)
            {
                rail[i] =
                [
                    vertices[0][0] + segNormals[0][0] * signedOffset,
                    vertices[0][1] + segNormals[0][1] * signedOffset
                ];
                continue;
            }

            if (!closed && i == n - 1)
            {
                var lastSeg = n - 2;
                rail[i] =
                [
                    vertices[i][0] + segNormals[lastSeg][0] * signedOffset,
                    vertices[i][1] + segNormals[lastSeg][1] * signedOffset
                ];
                continue;
            }

            var prevSeg = (i - 1 + (closed ? n : n - 1)) % (closed ? n : n - 1);
            var nextSeg = i;
            var d1 = segDirs[prevSeg];
            var d2 = segDirs[nextSeg];
            var n1 = segNormals[prevSeg];
            var n2 = segNormals[nextSeg];

            var p1 = new[] { vertices[i][0] + n1[0] * signedOffset, vertices[i][1] + n1[1] * signedOffset };
            var p2 = new[] { vertices[i][0] + n2[0] * signedOffset, vertices[i][1] + n2[1] * signedOffset };

            // Intersect offset line 1 (through p1 with direction d1) and offset line 2 (through p2 with direction d2)
            var cross = d1[0] * d2[1] - d1[1] * d2[0];
            if (Math.Abs(cross) <= 1e-6)
            {
                // Collinear or near-180 turn
                rail[i] = [(p1[0] + p2[0]) * 0.5, (p1[1] + p2[1]) * 0.5];
            }
            else
            {
                var dx = p2[0] - p1[0];
                var dy = p2[1] - p1[1];
                var t = (dx * d2[1] - dy * d2[0]) / cross;
                // Clamp extreme miter spikes (> 4x offset)
                var maxMiter = Math.Max(Math.Abs(signedOffset) * 4.0, 1.0);
                if (Math.Abs(t) > maxMiter)
                {
                    t = Math.Sign(t) * maxMiter;
                }

                rail[i] = [p1[0] + d1[0] * t, p1[1] + d1[1] * t];
            }
        }

        return rail;
    }

    private static double Dist2D(double[] a, double[] b)
    {
        var dx = a[0] - b[0];
        var dy = a[1] - b[1];
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

public sealed record WallOpeningDto
{
    public int SegmentIndex { get; init; }
    public double OffsetFromStart { get; init; }
    public double Width { get; init; }
}

