namespace Forge.Shared;

/// <summary>Pure 2D segment math used by the linework engine (drawing units).</summary>
public static class LineworkGeometry
{
    public static double SegLen(double x1, double y1, double x2, double y2)
        => Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));

    /// <summary>Distance from point to segment; t receives the clamped projection parameter.</summary>
    public static double PtSegDist(double px, double py, double x1, double y1, double x2, double y2, out double t)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        var l2 = dx * dx + dy * dy;
        if (l2 <= 0)
        {
            t = 0;
            return Math.Sqrt((px - x1) * (px - x1) + (py - y1) * (py - y1));
        }

        t = BclCompat.Clamp(((px - x1) * dx + (py - y1) * dy) / l2, 0, 1);
        var cx = x1 + t * dx;
        var cy = y1 + t * dy;
        return Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }

    /// <summary>
    /// Proper segment-segment intersection. Returns the intersection point when the segments
    /// cross (including touches); interior flags report whether the hit is strictly inside
    /// each segment (not at endpoints).
    /// </summary>
    public static bool SegSegIntersect(
        double ax1, double ay1, double ax2, double ay2,
        double bx1, double by1, double bx2, double by2,
        out double ix, out double iy, out double ta, out double tb)
    {
        ix = iy = ta = tb = 0;
        var dax = ax2 - ax1;
        var day = ay2 - ay1;
        var dbx = bx2 - bx1;
        var dby = by2 - by1;
        var denom = dax * dby - day * dbx;
        if (Math.Abs(denom) < 1e-12)
        {
            return false; // parallel / collinear — not treated as a crossing
        }

        var dx = bx1 - ax1;
        var dy = by1 - ay1;
        ta = (dx * dby - dy * dbx) / denom;
        tb = (dx * day - dy * dax) / denom;
        if (ta < -1e-9 || ta > 1 + 1e-9 || tb < -1e-9 || tb > 1 + 1e-9)
        {
            return false;
        }

        ix = ax1 + ta * dax;
        iy = ay1 + ta * day;
        return true;
    }

    /// <summary>
    /// True minimum distance between two segments (0 when they intersect or touch).
    /// </summary>
    public static double SegSegDist(
        double ax1, double ay1, double ax2, double ay2,
        double bx1, double by1, double bx2, double by2)
    {
        if (SegSegIntersect(ax1, ay1, ax2, ay2, bx1, by1, bx2, by2, out _, out _, out _, out _))
        {
            return 0;
        }

        var d = PtSegDist(ax1, ay1, bx1, by1, bx2, by2, out _);
        d = Math.Min(d, PtSegDist(ax2, ay2, bx1, by1, bx2, by2, out _));
        d = Math.Min(d, PtSegDist(bx1, by1, ax1, ay1, ax2, ay2, out _));
        d = Math.Min(d, PtSegDist(bx2, by2, ax1, ay1, ax2, ay2, out _));
        return d;
    }

    /// <summary>Tessellate an arc into chord points. Angles in radians; sweep wraps counter-clockwise.</summary>
    public static Vec2[] TessellateArc(double cx, double cy, double r, double startAngle, double endAngle, double maxStepRad = Math.PI / 12)
    {
        var sweep = endAngle - startAngle;
        while (sweep <= 0) sweep += Math.PI * 2;
        if (sweep > Math.PI * 2) sweep = Math.PI * 2;
        var n = Math.Max(2, (int)Math.Ceiling(sweep / maxStepRad));
        var pts = new Vec2[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var a = startAngle + sweep * i / n;
            pts[i] = new Vec2(cx + r * Math.Cos(a), cy + r * Math.Sin(a));
        }

        return pts;
    }

    /// <summary>Chords approximating a polyline bulge segment (bulge = tan(includedAngle/4)).</summary>
    public static Vec2[] TessellateBulge(Vec2 a, Vec2 b, double bulge)
    {
        if (Math.Abs(bulge) < 1e-9)
        {
            return [a, b];
        }

        var included = 4 * Math.Atan(bulge);
        var chord = a.DistanceTo(b);
        if (chord <= 0)
        {
            return [a, b];
        }

        // sagitta-based arc: radius = chord/2 * (1+b^2)/(2b); center on perpendicular.
        var radius = chord * (1 + bulge * bulge) / (4 * Math.Abs(bulge));
        var mx = (a.X + b.X) / 2;
        var my = (a.Y + b.Y) / 2;
        var d = Math.Sqrt(Math.Max(0, radius * radius - chord * chord / 4));
        var ux = -(b.Y - a.Y) / chord; // unit perpendicular
        var uy = (b.X - a.X) / chord;
        // bulge>0 => counter-clockwise bulge: center sits on the left of a->b? Empirically
        // center offset sign: for CCW arcs the center is on the right side of the chord.
        var sign = bulge > 0 ? -1 : 1;
        var cx = mx + sign * ux * d;
        var cy = my + sign * uy * d;
        var a1 = Math.Atan2(a.Y - cy, a.X - cx);
        var a2 = Math.Atan2(b.Y - cy, b.X - cx);
        var sweep = a2 - a1;
        // normalize sweep to match bulge sign/magnitude
        while (sweep <= 0) sweep += Math.PI * 2;
        if (bulge < 0)
        {
            // clockwise — walk backwards
            var pts = new List<Vec2> { a };
            var cw = Math.PI * 2 - sweep;
            var n = Math.Max(2, (int)Math.Ceiling(cw / (Math.PI / 12)));
            for (var i = 1; i < n; i++)
            {
                var ang = a1 - cw * i / n;
                pts.Add(new Vec2(cx + radius * Math.Cos(ang), cy + radius * Math.Sin(ang)));
            }

            pts.Add(b);
            return pts.ToArray();
        }

        var nn = Math.Max(2, (int)Math.Ceiling(sweep / (Math.PI / 12)));
        var list = new List<Vec2> { a };
        for (var i = 1; i < nn; i++)
        {
            var ang = a1 + sweep * i / nn;
            list.Add(new Vec2(cx + radius * Math.Cos(ang), cy + radius * Math.Sin(ang)));
        }

        list.Add(b);
        return list.ToArray();
    }
}

/// <summary>Uniform grid index over 2D segments for fast proximity queries.</summary>
public sealed class SegmentGrid
{
    private readonly Dictionary<(int, int), List<int>> _cells = new();
    private readonly double _cellSize;

    public SegmentGrid(double cellSize)
    {
        _cellSize = Math.Max(cellSize, 1e-6);
    }

    public void Insert(int index, double x1, double y1, double x2, double y2)
    {
        var (cx0, cy0) = Cell(Math.Min(x1, x2), Math.Min(y1, y2));
        var (cx1, cy1) = Cell(Math.Max(x1, x2), Math.Max(y1, y2));
        for (var cx = cx0; cx <= cx1; cx++)
        {
            for (var cy = cy0; cy <= cy1; cy++)
            {
                if (!_cells.TryGetValue((cx, cy), out var list))
                {
                    list = new List<int>();
                    _cells[(cx, cy)] = list;
                }

                list.Add(index);
            }
        }
    }

    public IEnumerable<int> Query(double x1, double y1, double x2, double y2, double pad)
    {
        var (cx0, cy0) = Cell(Math.Min(x1, x2) - pad, Math.Min(y1, y2) - pad);
        var (cx1, cy1) = Cell(Math.Max(x1, x2) + pad, Math.Max(y1, y2) + pad);
        var seen = new HashSet<int>();
        for (var cx = cx0; cx <= cx1; cx++)
        {
            for (var cy = cy0; cy <= cy1; cy++)
            {
                if (!_cells.TryGetValue((cx, cy), out var list))
                {
                    continue;
                }

                foreach (var i in list)
                {
                    if (seen.Add(i))
                    {
                        yield return i;
                    }
                }
            }
        }
    }

    private (int, int) Cell(double x, double y)
        => ((int)Math.Floor(x / _cellSize), (int)Math.Floor(y / _cellSize));
}

/// <summary>
/// A linework segment flattened from an entity (drawing units).
/// <paramref name="Entity"/> is the INDEX into the entity list passed to Flatten —
/// use entities[seg.Entity].Id/Handle for the entity's logical identity.
/// </summary>
public readonly record struct WorkSeg(int Entity, int SegIndex, double X1, double Y1, double X2, double Y2)
{
    public double Length => LineworkGeometry.SegLen(X1, Y1, X2, Y2);
}

/// <summary>Flatten entities to segments, honoring entity types + closed polylines.</summary>
public static class LineworkFlattener
{
    public static List<WorkSeg> Flatten(IReadOnlyList<LineworkEntity> entities)
    {
        var segs = new List<WorkSeg>();
        for (var ei = 0; ei < entities.Count; ei++)
        {
            var e = entities[ei];
            var pts = e.Points;
            var n = e.Closed ? pts.Length : pts.Length - 1;
            for (var i = 0; i < n; i++)
            {
                var a = pts[i];
                var b = pts[(i + 1) % pts.Length];
                if (a.DistanceTo(b) < 1e-9)
                {
                    continue;
                }

                segs.Add(new WorkSeg(ei, i, a.X, a.Y, b.X, b.Y));
            }
        }

        return segs;
    }
}

/// <summary>Shared query pipeline: layer include/exclude + entity-type + bbox + cap.</summary>
public static class LineworkQuery
{
    public sealed record FilterResult
    {
        public required List<LineworkEntity> Entities { get; init; }
        public required Dictionary<string, int> SkippedByKind { get; init; }
        public required bool Truncated { get; init; }
        public required string[] IncludePatterns { get; init; }
        public required string[] ExcludePatterns { get; init; }
        /// <summary>Layer-match mode actually applied ("auto" when unset).</summary>
        public required string LayerMatch { get; init; }
        /// <summary>Explicit xref-safe suffix patterns applied to the bare layer tail.</summary>
        public required string[] SuffixPatterns { get; init; }
        public string? TooLargeMessage { get; init; }
    }

    public static HashSet<LineworkKind> ParseEntityTypes(string? entityTypes)
    {
        var set = new HashSet<LineworkKind>();
        var text = string.IsNullOrWhiteSpace(entityTypes) ? "line,polyline,arc" : entityTypes!;
        foreach (var token in BclCompat.SplitTrimmed(text, ','))
        {
            if (Enum.TryParse<LineworkKind>(token, ignoreCase: true, out var kind))
            {
                set.Add(kind);
            }
        }

        return set;
    }

    public static FilterResult Apply(IEnumerable<LineworkEntity> source, LineworkQueryArgs args)
    {
        var suffixes = SplitPatterns(args.LayerSuffix);
        // Explicit include (layerFilter and/or layerSuffix) bypasses the default pipe filter;
        // layerSuffix alone still restricts to just those suffixes.
        var include = !string.IsNullOrWhiteSpace(args.LayerFilter)
            ? new LayerMatcher(args.LayerFilter, args.LayerMatch)
            : suffixes.Length == 0
                ? new LayerMatcher(null, args.LayerMatch)
                : null;
        var exclude = string.IsNullOrWhiteSpace(args.ExcludeLayerFilter) ? null : new LayerMatcher(args.ExcludeLayerFilter, args.LayerMatch);
        var kinds = ParseEntityTypes(args.EntityTypes);
        var skipped = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var entities = new List<LineworkEntity>();
        var truncated = false;

        foreach (var e in source)
        {
            if (!kinds.Contains(e.Kind))
            {
                Bump(skipped, e.Kind.ToString());
                continue;
            }

            var included = include?.Matches(e.Layer) ?? false;
            if (!included && suffixes.Length > 0)
            {
                included = suffixes.Any(s => LayerMatcher.MatchesTailSuffix(e.Layer, s));
            }

            if (!included)
            {
                Bump(skipped, "layer");
                continue;
            }

            if (exclude is not null && exclude.Matches(e.Layer))
            {
                Bump(skipped, "layerExcluded");
                continue;
            }

            if (args.Bbox is { Length: >= 4 } bb && !BBoxIntersects(e, bb))
            {
                Bump(skipped, "bbox");
                continue;
            }

            if (entities.Count >= args.MaxEntities)
            {
                truncated = true;
                break;
            }

            entities.Add(e);
        }

        return new FilterResult
        {
            Entities = entities,
            SkippedByKind = skipped,
            Truncated = truncated,
            IncludePatterns = SplitPatterns(
                !string.IsNullOrWhiteSpace(args.LayerFilter)
                    ? args.LayerFilter!
                    : suffixes.Length == 0
                        ? LayerMatcher.DefaultPipeLayerFilter
                        : ""),
            ExcludePatterns = SplitPatterns(args.ExcludeLayerFilter),
            LayerMatch = LayerMatcher.ModeName(args.LayerMatch),
            SuffixPatterns = suffixes,
            TooLargeMessage = truncated
                ? $"Matched more than {args.MaxEntities} entities; refine layerFilter/bbox or raise maxEntities."
                : null
        };
    }

    public static string[] SplitPatterns(string? filter)
        => string.IsNullOrWhiteSpace(filter)
            ? []
            : BclCompat.SplitTrimmed(filter!, ',');

    private static void Bump(Dictionary<string, int> map, string key)
    {
        map[key] = map.TryGetValue(key, out var v) ? v + 1 : 1;
    }

    private static bool BBoxIntersects(LineworkEntity e, double[] bb)
    {
        var (x1, y1, x2, y2) = (Math.Min(bb[0], bb[2]), Math.Min(bb[1], bb[3]), Math.Max(bb[0], bb[2]), Math.Max(bb[1], bb[3]));
        var ex1 = double.MaxValue; var ey1 = double.MaxValue; var ex2 = double.MinValue; var ey2 = double.MinValue;
        foreach (var p in e.Points)
        {
            ex1 = Math.Min(ex1, p.X); ey1 = Math.Min(ey1, p.Y);
            ex2 = Math.Max(ex2, p.X); ey2 = Math.Max(ey2, p.Y);
        }

        return ex2 >= x1 && ex1 <= x2 && ey2 >= y1 && ey1 <= y2;
    }
}

// ===================== Topology =====================

public enum TopoNodeKind { DeadEnd, PassThrough, Junction }

public sealed record TopoNode
{
    public int Id { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
    public int Degree { get; init; }
    public TopoNodeKind Kind { get; init; }
    public int[] Edges { get; init; } = [];
}

public sealed record TopoEdge
{
    public int Id { get; init; }
    public int Entity { get; init; }
    public string? Handle { get; init; }
    public int SegIndex { get; init; }
    public string Layer { get; init; } = "";
    public int N0 { get; init; }
    public int N1 { get; init; }
    public double X1 { get; init; }
    public double Y1 { get; init; }
    public double X2 { get; init; }
    public double Y2 { get; init; }
    public double Len { get; init; }
}

public sealed record TopoTJunction
{
    public int Node { get; init; }
    public int Edge { get; init; }
    public double T { get; init; }
    public double X { get; init; }
    public double Y { get; init; }
}

public sealed record TopoCrossing
{
    public double X { get; init; }
    public double Y { get; init; }
    public int[] Edges { get; init; } = [];
}

public sealed record TopoRun
{
    public int[] Edges { get; init; } = [];
    public int[] Nodes { get; init; } = [];
    public double Len { get; init; }
    public string[] EndKinds { get; init; } = [];
}

public sealed record TopoComponent
{
    public int Id { get; init; }
    public int[] Edges { get; init; } = [];
    public double Len { get; init; }
}

public sealed record LineworkTopologyReport
{
    public int NodeCount { get; init; }
    public int EdgeCount { get; init; }
    public double VertexTolerance { get; init; }
    public double JunctionTolerance { get; init; }
    public TopoNode[] Nodes { get; init; } = [];
    public TopoEdge[] Edges { get; init; } = [];
    public TopoTJunction[] TJunctions { get; init; } = [];
    public TopoCrossing[] Crossings { get; init; } = [];
    public int[] DeadEnds { get; init; } = [];
    public TopoRun[] Runs { get; init; } = [];
    public TopoComponent[] Components { get; init; } = [];
    public bool Truncated { get; init; }
}

public static class TopologyBuilder
{
    private sealed class DSU
    {
        private readonly int[] _p;
        public DSU(int n) { _p = Enumerable.Range(0, n).ToArray(); }
        public int Find(int x) => _p[x] == x ? x : _p[x] = Find(_p[x]);
        public void Union(int a, int b) { a = Find(a); b = Find(b); if (a != b) _p[b] = a; }
    }

    public static LineworkTopologyReport Build(
        IReadOnlyList<LineworkEntity> entities,
        IReadOnlyList<WorkSeg> segs,
        double vertexTolerance,
        double junctionTolerance,
        bool includeCrossings)
    {
        // 1) Merge endpoints within vertexTolerance via endpoint spatial grid + union-find.
        var epCount = segs.Count * 2;
        var dsu = new DSU(Math.Max(1, epCount));
        var ptGrid = new SegmentGrid(Math.Max(vertexTolerance, 1e-3));
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            ptGrid.Insert(2 * i, s.X1, s.Y1, s.X1, s.Y1);
            ptGrid.Insert(2 * i + 1, s.X2, s.Y2, s.X2, s.Y2);
        }

        var tol2 = vertexTolerance * vertexTolerance;
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            for (var e = 0; e < 2; e++)
            {
                var (px, py) = e == 0 ? (s.X1, s.Y1) : (s.X2, s.Y2);
                var k = 2 * i + e;
                foreach (var j in ptGrid.Query(px, py, px, py, vertexTolerance))
                {
                    if (j >= k)
                    {
                        continue;
                    }

                    var o = segs[j / 2];
                    var (ox, oy) = j % 2 == 0 ? (o.X1, o.Y1) : (o.X2, o.Y2);
                    var dx = px - ox;
                    var dy = py - oy;
                    if (dx * dx + dy * dy <= tol2)
                    {
                        dsu.Union(k, j);
                    }
                }
            }
        }

        // node id per merged cluster
        var nodeOf = new Dictionary<int, int>();
        var nodePts = new Dictionary<int, (double sx, double sy, int n)>();
        var edgeNode = new (int n0, int n1)[segs.Count];
        for (var i = 0; i < segs.Count; i++)
        {
            var r0 = dsu.Find(2 * i);
            var r1 = dsu.Find(2 * i + 1);
            var n0 = NodeId(nodeOf, r0);
            var n1 = NodeId(nodeOf, r1);
            edgeNode[i] = (n0, n1);
            Acc(nodePts, n0, segs[i].X1, segs[i].Y1);
            Acc(nodePts, n1, segs[i].X2, segs[i].Y2);
        }

        var nodeXy = nodePts.ToDictionary(kv => kv.Key, kv => new Vec2(kv.Value.sx / kv.Value.n, kv.Value.sy / kv.Value.n));

        // adjacency
        var adj = Enumerable.Range(0, nodeOf.Count).Select(_ => new List<int>()).ToArray();
        for (var i = 0; i < segs.Count; i++)
        {
            var (n0, n1) = edgeNode[i];
            if (n0 == n1)
            {
                // Self-loop: both ends merged (closed ring or sub-tolerance stub).
                // A loop contributes degree 2 to its node — a pure ring is a run, not a dead end.
                adj[n0].Add(i);
                adj[n0].Add(i);
                continue;
            }

            adj[n0].Add(i);
            adj[n1].Add(i);
        }

        // 2) T-junctions: node lies on interior of an edge it is not an endpoint of.
        var tJunctions = new List<TopoTJunction>();
        var tFlaggedNodes = new HashSet<int>();
        var edgeGrid = new SegmentGrid(Math.Max(junctionTolerance, 1e-3) * 4);
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            edgeGrid.Insert(i, s.X1, s.Y1, s.X2, s.Y2);
        }

        foreach (var (nid, xy) in nodeXy)
        {
            foreach (var ei in edgeGrid.Query(xy.X, xy.Y, xy.X, xy.Y, junctionTolerance))
            {
                var (a, b) = edgeNode[ei];
                if (ei < 0 || a == nid || b == nid)
                {
                    continue;
                }

                var s = segs[ei];
                var d = LineworkGeometry.PtSegDist(xy.X, xy.Y, s.X1, s.Y1, s.X2, s.Y2, out var t);
                if (d <= junctionTolerance && t > 1e-6 && t < 1 - 1e-6)
                {
                    tJunctions.Add(new TopoTJunction { Node = nid, Edge = ei, T = Math.Round(t, 6), X = xy.X, Y = xy.Y });
                    tFlaggedNodes.Add(nid);
                }
            }
        }

        // 3) X-crossings: proper interior intersections between edges sharing no node.
        var crossings = new List<TopoCrossing>();
        if (includeCrossings)
        {
            var seenPairs = new HashSet<long>();
            for (var i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                var pad = Math.Max(vertexTolerance, 1e-3);
                foreach (var j in edgeGrid.Query(s.X1, s.Y1, s.X2, s.Y2, pad))
                {
                    if (j <= i)
                    {
                        continue;
                    }

                    var (a0, a1) = edgeNode[i];
                    var (b0, b1) = edgeNode[j];
                    if (a0 == b0 || a0 == b1 || a1 == b0 || a1 == b1)
                    {
                        continue; // share a node — a junction, not a crossing
                    }

                    var o = segs[j];
                    if (LineworkGeometry.SegSegIntersect(s.X1, s.Y1, s.X2, s.Y2, o.X1, o.Y1, o.X2, o.Y2, out var ix, out var iy, out var ta, out var tb)
                        && ta > 1e-6 && ta < 1 - 1e-6 && tb > 1e-6 && tb < 1 - 1e-6)
                    {
                        if (seenPairs.Add(((long)i << 32) | (uint)j))
                        {
                            crossings.Add(new TopoCrossing { X = ix, Y = iy, Edges = [i, j] });
                        }
                    }
                }
            }
        }

        // 4) Nodes, kinds, dead ends.
        var nodes = new TopoNode[nodeOf.Count];
        var deadEnds = new List<int>();
        foreach (var (nid, xy) in nodeXy)
        {
            var degree = adj[nid].Count;
            var kind = degree <= 1 ? TopoNodeKind.DeadEnd : degree == 2 ? TopoNodeKind.PassThrough : TopoNodeKind.Junction;
            if (tFlaggedNodes.Contains(nid))
            {
                // Endpoint/cluster touching another edge's interior: a junction (tee), not a dead end.
                kind = TopoNodeKind.Junction;
            }

            nodes[nid] = new TopoNode { Id = nid, X = xy.X, Y = xy.Y, Degree = degree, Kind = kind, Edges = adj[nid].ToArray() };
            if (kind == TopoNodeKind.DeadEnd)
            {
                deadEnds.Add(nid);
            }
        }

        // 5) Edges. Entity carries the entity's logical Id (not its list index).
        var edges = segs.Select((s, i) =>
        {
            var (n0, n1) = edgeNode[i];
            var e = entities[s.Entity];
            return new TopoEdge
            {
                Id = i,
                Entity = e.Id,
                Handle = e.Handle,
                SegIndex = s.SegIndex,
                Layer = e.Layer,
                N0 = n0,
                N1 = n1,
                X1 = s.X1,
                Y1 = s.Y1,
                X2 = s.X2,
                Y2 = s.Y2,
                Len = s.Length
            };
        }).ToArray();

        // 6) Connected components over edges.
        var edgeDsu = new DSU(Math.Max(1, segs.Count));
        for (var i = 0; i < segs.Count; i++)
        {
            var (n0, n1) = edgeNode[i];
            if (n0 == n1)
            {
                continue;
            }

            foreach (var other in adj[n0])
            {
                edgeDsu.Union(i, other);
            }

            foreach (var other in adj[n1])
            {
                edgeDsu.Union(i, other);
            }
        }

        var compMap = new Dictionary<int, List<int>>();
        for (var i = 0; i < segs.Count; i++)
        {
            var root = edgeDsu.Find(i);
            if (!compMap.TryGetValue(root, out var list))
            {
                list = new List<int>();
                compMap[root] = list;
            }

            list.Add(i);
        }

        var components = compMap.Values
            .Select((l, idx) => new TopoComponent { Id = idx, Edges = l.ToArray(), Len = l.Sum(e => edges[e].Len) })
            .OrderByDescending(c => c.Len)
            .ToArray();

        // 7) Runs: maximal chains between non-pass-through nodes.
        var runs = BuildRuns(segs, edges, nodes, tFlaggedNodes);

        return new LineworkTopologyReport
        {
            NodeCount = nodes.Length,
            EdgeCount = edges.Length,
            VertexTolerance = vertexTolerance,
            JunctionTolerance = junctionTolerance,
            Nodes = nodes,
            Edges = edges,
            TJunctions = tJunctions.OrderBy(t => t.Edge).ToArray(),
            Crossings = crossings.OrderBy(c => c.X).ToArray(),
            DeadEnds = deadEnds.OrderBy(x => x).ToArray(),
            Runs = runs,
            Components = components,
            Truncated = false
        };
    }

    private static int NodeId(Dictionary<int, int> map, int root)
    {
        if (!map.TryGetValue(root, out var id))
        {
            id = map.Count;
            map[root] = id;
        }

        return id;
    }

    private static void Acc(Dictionary<int, (double sx, double sy, int n)> map, int node, double x, double y)
    {
        if (map.TryGetValue(node, out var v))
        {
            map[node] = (v.sx + x, v.sy + y, v.n + 1);
        }
        else
        {
            map[node] = (x, y, 1);
        }
    }

    private static TopoRun[] BuildRuns(
        IReadOnlyList<WorkSeg> segs,
        TopoEdge[] edges,
        TopoNode[] nodes,
        HashSet<int> tFlaggedNodes)
    {
        var boundary = new HashSet<int>(tFlaggedNodes);
        foreach (var n in nodes)
        {
            if (n.Degree != 2)
            {
                boundary.Add(n.Id);
            }
        }

        var runs = new List<TopoRun>();
        var visited = new HashSet<int>();
        // adjacency edgeId -> (n0,n1) already in edges
        for (var ei = 0; ei < edges.Length; ei++)
        {
            if (visited.Contains(ei))
            {
                continue;
            }

            var runEdges = new List<int>();
            var runNodes = new List<int>();
            var endKinds = new List<string>();
            // start a run: walk from a boundary node if possible, else arbitrary (loop component)
            var e = edges[ei];
            int startNode;
            int curNode;
            if (boundary.Contains(e.N0)) { startNode = e.N0; curNode = e.N1; }
            else if (boundary.Contains(e.N1)) { startNode = e.N1; curNode = e.N0; }
            else { startNode = e.N0; curNode = e.N1; }

            runNodes.Add(startNode);
            var curEdge = ei;
            var fromNode = startNode;
            while (true)
            {
                visited.Add(curEdge);
                runEdges.Add(curEdge);
                var ce = edges[curEdge];
                var next = ce.N0 == fromNode ? ce.N1 : ce.N0;
                runNodes.Add(next);
                if (boundary.Contains(next))
                {
                    endKinds.Add(KindName(nodes[next]));
                    break;
                }

                // continue through a pass-through node: pick the other unvisited edge
                var nextEdge = -1;
                foreach (var candidate in nodes[next].Edges)
                {
                    if (candidate != curEdge && !visited.Contains(candidate))
                    {
                        nextEdge = candidate;
                        break;
                    }
                }

                if (nextEdge < 0)
                {
                    // looped back into visited edges — close the run
                    endKinds.Add("loop");
                    break;
                }

                fromNode = next;
                curEdge = nextEdge;
            }

            var startKind = KindName(nodes[startNode]);
            endKinds.Insert(0, startKind);
            runs.Add(new TopoRun
            {
                Edges = runEdges.ToArray(),
                Nodes = runNodes.Distinct().ToArray(),
                Len = runEdges.Sum(x => edges[x].Len),
                EndKinds = endKinds.ToArray()
            });
        }

        return runs.OrderByDescending(r => r.Len).ToArray();

        static string KindName(TopoNode n) => n.Kind switch
        {
            TopoNodeKind.DeadEnd => "dead_end",
            TopoNodeKind.Junction => "junction",
            _ => "pass_through"
        };
    }
}

// ===================== Coverage =====================

public sealed record CoverageSeg
{
    public int Entity { get; init; }
    public string? Handle { get; init; }
    public string? Id { get; init; }
    public string Layer { get; init; } = "";
    public int SegIndex { get; init; }
    public double[] S { get; init; } = [];
    public double[] E { get; init; } = [];
    public double LenM { get; init; }
    public double CoveredFrac { get; init; }
    public double[][] UncoveredRanges { get; init; } = [];   // [t0,t1] params along the segment
    public double[][] UncoveredPoints { get; init; } = [];   // [x,y] endpoints of uncovered ranges
    public double? NearestOtherM { get; init; }
}

public sealed record LayerRollup
{
    public double TotalM { get; set; }
    public double CoveredM { get; set; }
    public int SegCount { get; set; }
    public int MissingSegs { get; set; }
    public int PartialSegs { get; set; }
    public double Pct => TotalM <= 0 ? 0 : Math.Round(100 * CoveredM / TotalM, 1);
}

public sealed record CoverageReport
{
    public double CadTotalM { get; init; }
    public double CadCoveredM { get; init; }
    public double CadCoveragePct { get; init; }
    public int CadSegCount { get; init; }
    public double ModelTotalM { get; init; }
    public double ModelCoveredM { get; init; }
    public double ModelCoveragePct { get; init; }
    public int ModelSegCount { get; init; }
    public CoverageSeg[] Missing { get; init; } = [];
    public CoverageSeg[] Partial { get; init; } = [];
    public CoverageSeg[] Extra { get; init; } = [];
    public Dictionary<string, LayerRollup> ByLayer { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public required object Params { get; init; }
}

public static class CoverageEngine
{
    /// <summary>
    /// Compare CAD linework segments (drawing units) against modeled pipe centerlines (model units).
    /// Both sides normalized to meters. Returns missing (cad w/o model), extra (model w/o cad),
    /// and partial coverage with uncovered sub-ranges.
    /// </summary>
    public static CoverageReport Compare(
        IReadOnlyList<LineworkEntity> entities,
        IReadOnlyList<WorkSeg> cadSegs,
        IReadOnlyList<(double x1, double y1, double x2, double y2, string? id)> modelSegsRaw,
        double unitsPerMeter,
        double modelUnitsPerMeter,
        double toleranceMeters,
        double stepMeters,
        double minCoverageFraction)
    {
        var toM = 1.0 / Math.Max(unitsPerMeter, 1e-9);
        var mToM = 1.0 / Math.Max(modelUnitsPerMeter, 1e-9);

        var cad = cadSegs.Select(s => (x1: s.X1 * toM, y1: s.Y1 * toM, x2: s.X2 * toM, y2: s.Y2 * toM, seg: s)).ToArray();
        var model = modelSegsRaw
            .Select(m => (x1: m.x1 * mToM, y1: m.y1 * mToM, x2: m.x2 * mToM, y2: m.y2 * mToM, m.id))
            .Where(m => LineworkGeometry.SegLen(m.x1, m.y1, m.x2, m.y2) > 1e-9)
            .ToArray();

        var modelPlain = model.Select(m => (m.x1, m.y1, m.x2, m.y2)).ToArray();
        var cadPlain = cad.Select(c => (c.x1, c.y1, c.x2, c.y2)).ToArray();
        var cadRef = cad.Select(c => (c.x1, c.y1, c.x2, c.y2, (string?)null)).ToArray();
        var modelGrid = BuildGrid(modelPlain, toleranceMeters);
        var cadGrid = BuildGrid(cadPlain, toleranceMeters);

        // CAD side coverage
        var missing = new List<CoverageSeg>();
        var partial = new List<CoverageSeg>();
        double cadTot = 0, cadHit = 0;
        var byLayer = new Dictionary<string, LayerRollup>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < cad.Length; i++)
        {
            var c = cad[i];
            var len = c.seg.Length * toM;
            cadTot += len;
            var (frac, ranges) = SampleCoverage(c.x1, c.y1, c.x2, c.y2, model, modelGrid, toleranceMeters, stepMeters);
            var covered = len * frac;
            cadHit += covered;

            var layer = entities[c.seg.Entity].Layer;
            if (!byLayer.TryGetValue(layer, out var roll))
            {
                roll = new LayerRollup();
                byLayer[layer] = roll;
            }

            roll.TotalM += len;
            roll.CoveredM += covered;
            roll.SegCount++;

            if (frac < minCoverageFraction)
            {
                roll.MissingSegs++;
                missing.Add(MakeCoverageSeg(c.seg, entities, c, len, frac, ranges, nearest: NearestDist((c.x1, c.y1, c.x2, c.y2), modelPlain, modelGrid)));
            }
            else if (frac < 0.999 && ranges.Length > 0)
            {
                roll.PartialSegs++;
                partial.Add(MakeCoverageSeg(c.seg, entities, c, len, frac, ranges, nearest: null));
            }
        }

        // Model side coverage (extra = model pipes with no CAD linework)
        var extra = new List<CoverageSeg>();
        double modTot = 0, modHit = 0;
        for (var i = 0; i < model.Length; i++)
        {
            var m = model[i];
            var len = LineworkGeometry.SegLen(m.x1, m.y1, m.x2, m.y2);
            modTot += len;
            var (frac, ranges) = SampleCoverage(m.x1, m.y1, m.x2, m.y2, cadRef, cadGrid, toleranceMeters, stepMeters);
            modHit += len * frac;
            if (frac < minCoverageFraction)
            {
                extra.Add(new CoverageSeg
                {
                    Id = m.id,
                    Entity = -1,
                    Layer = "(model)",
                    S = [m.x1, m.y1],
                    E = [m.x2, m.y2],
                    LenM = Math.Round(len, 4),
                    CoveredFrac = Math.Round(frac, 4),
                    UncoveredRanges = ranges.Select(r => new[] { r.t0, r.t1 }).ToArray(),
                    UncoveredPoints = ranges.SelectMany(r => new[] { new[] { m.x1 + (m.x2 - m.x1) * r.t0, m.y1 + (m.y2 - m.y1) * r.t0 }, new[] { m.x1 + (m.x2 - m.x1) * r.t1, m.y1 + (m.y2 - m.y1) * r.t1 } }).ToArray(),
                    NearestOtherM = NearestDist((m.x1, m.y1, m.x2, m.y2), cadPlain, cadGrid)
                });
            }
        }

        return new CoverageReport
        {
            CadTotalM = Math.Round(cadTot, 3),
            CadCoveredM = Math.Round(cadHit, 3),
            CadCoveragePct = cadTot > 0 ? Math.Round(100 * cadHit / cadTot, 1) : 0,
            CadSegCount = cad.Length,
            ModelTotalM = Math.Round(modTot, 3),
            ModelCoveredM = Math.Round(modHit, 3),
            ModelCoveragePct = modTot > 0 ? Math.Round(100 * modHit / modTot, 1) : 0,
            ModelSegCount = model.Length,
            Missing = missing.OrderByDescending(x => x.LenM).ToArray(),
            Partial = partial.OrderByDescending(x => x.LenM).ToArray(),
            Extra = extra.OrderByDescending(x => x.LenM).ToArray(),
            ByLayer = byLayer,
            Params = new
            {
                toleranceMeters,
                stepMeters,
                minCoverageFraction,
                unitsPerMeter,
                modelUnitsPerMeter,
                units = "meters"
            }
        };
    }

    private static CoverageSeg MakeCoverageSeg(
        WorkSeg s,
        IReadOnlyList<LineworkEntity> entities,
        (double x1, double y1, double x2, double y2, WorkSeg seg) c,
        double len,
        double frac,
        (double t0, double t1)[] ranges,
        double? nearest)
    {
        var e = entities[s.Entity];
        return new CoverageSeg
        {
            Entity = e.Id,
            Handle = e.Handle,
            Layer = e.Layer,
            SegIndex = s.SegIndex,
            S = [c.x1, c.y1],
            E = [c.x2, c.y2],
            LenM = Math.Round(len, 4),
            CoveredFrac = Math.Round(frac, 4),
            UncoveredRanges = ranges.Select(r => new[] { r.t0, r.t1 }).ToArray(),
            UncoveredPoints = ranges.SelectMany(r => new[] { new[] { c.x1 + (c.x2 - c.x1) * r.t0, c.y1 + (c.y2 - c.y1) * r.t0 }, new[] { c.x1 + (c.x2 - c.x1) * r.t1, c.y1 + (c.y2 - c.y1) * r.t1 } }).ToArray(),
            NearestOtherM = nearest
        };
    }

    /// <summary>Sample a segment every stepMeters; return covered fraction + uncovered t-ranges.</summary>
    internal static (double frac, (double t0, double t1)[] ranges) SampleCoverage(
        double x1, double y1, double x2, double y2,
        (double x1, double y1, double x2, double y2, string? id)[] refSegs,
        SegmentGrid grid,
        double tol,
        double step)
    {
        var len = LineworkGeometry.SegLen(x1, y1, x2, y2);
        if (len <= 0)
        {
            return (0, []);
        }

        var n = Math.Max(1, (int)Math.Ceiling(len / step));
        var covered = new bool[n + 1];
        for (var i = 0; i <= n; i++)
        {
            var t = (double)i / n;
            var px = x1 + (x2 - x1) * t;
            var py = y1 + (y2 - y1) * t;
            foreach (var ri in grid.Query(px, py, px, py, tol))
            {
                var r = refSegs[ri];
                if (LineworkGeometry.PtSegDist(px, py, r.x1, r.y1, r.x2, r.y2, out _) <= tol)
                {
                    covered[i] = true;
                    break;
                }
            }
        }

        var hits = covered.Count(c => c);
        var frac = (double)hits / (n + 1);

        var ranges = new List<(double, double)>();
        var runStart = -1;
        for (var i = 0; i <= n; i++)
        {
            if (!covered[i] && runStart < 0)
            {
                runStart = i;
            }
            else if (covered[i] && runStart >= 0)
            {
                ranges.Add(((double)runStart / n, (double)i / n));
                runStart = -1;
            }
        }

        if (runStart >= 0)
        {
            ranges.Add(((double)runStart / n, 1.0));
        }

        return (frac, ranges.ToArray());
    }

    private static double? NearestDist(
        (double x1, double y1, double x2, double y2) q,
        (double x1, double y1, double x2, double y2)[] refSegs,
        SegmentGrid grid)
    {
        // min distance from q's endpoints+midpoint to any ref seg within a search pad
        var mx = (q.x1 + q.x2) / 2;
        var my = (q.y1 + q.y2) / 2;
        var best = double.MaxValue;
        foreach (var ri in grid.Query(Math.Min(q.x1, q.x2), Math.Min(q.y1, q.y2), Math.Max(q.x1, q.x2), Math.Max(q.y1, q.y2), 50))
        {
            var r = refSegs[ri];
            var d = Math.Min(
                LineworkGeometry.PtSegDist(q.x1, q.y1, r.x1, r.y1, r.x2, r.y2, out _),
                Math.Min(
                    LineworkGeometry.PtSegDist(q.x2, q.y2, r.x1, r.y1, r.x2, r.y2, out _),
                    LineworkGeometry.PtSegDist(mx, my, r.x1, r.y1, r.x2, r.y2, out _)));
            if (d < best)
            {
                best = d;
            }
        }

        return best == double.MaxValue ? null : Math.Round(best, 4);
    }

    internal static SegmentGrid BuildGrid((double x1, double y1, double x2, double y2)[] segs, double tol)
    {
        var grid = new SegmentGrid(Math.Max(tol * 4, 0.5));
        for (var i = 0; i < segs.Length; i++)
        {
            grid.Insert(i, segs[i].x1, segs[i].y1, segs[i].x2, segs[i].y2);
        }

        return grid;
    }
}

// ===================== Trace =====================

public sealed record TraceHit
{
    public int SegIndex { get; init; }
    public double T { get; init; }
    public double[] Point { get; init; } = [];
    public double Distance { get; init; }
}

public sealed record TraceNeighbor
{
    public int Entity { get; init; }
    public string? Handle { get; init; }
    public string Kind { get; init; } = "";
    public string Layer { get; init; } = "";
    public double Distance { get; init; }
}

public sealed record LineworkTraceResult
{
    public bool Found { get; init; }
    public double[] Query { get; init; } = [];
    public LineworkEntity? Entity { get; init; }
    public TraceHit? Hit { get; init; }
    public TraceNeighbor[]? Context { get; init; }
    public string? Message { get; init; }
}

public static class TraceEngine
{
    /// <summary>Find the linework entity nearest to (x,y) within tolerance, or look one up by handle.</summary>
    public static LineworkTraceResult Trace(
        IReadOnlyList<LineworkEntity> entities,
        IReadOnlyList<WorkSeg> segs,
        double x,
        double y,
        double tolerance,
        double contextRadius,
        string? handle)
    {
        if (!string.IsNullOrWhiteSpace(handle))
        {
            var ent = entities.FirstOrDefault(e =>
                string.Equals(e.Handle, handle, StringComparison.OrdinalIgnoreCase) ||
                string.Equals($"row:{e.Id + 1}", handle, StringComparison.OrdinalIgnoreCase) ||
                (int.TryParse(handle, out var hid) && e.Id == hid));
            if (ent is null)
            {
                return new LineworkTraceResult { Found = false, Message = $"No linework entity with handle/id '{handle}'." };
            }

            return new LineworkTraceResult
            {
                Found = true,
                Entity = ent,
                Hit = null,
                Context = contextRadius > 0 ? Neighbors(entities, segs, x, y, contextRadius, ent.Id) : null
            };
        }

        var grid = new SegmentGrid(Math.Max(tolerance, 1e-3));
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            grid.Insert(i, s.X1, s.Y1, s.X2, s.Y2);
        }

        var best = -1;
        var bestDist = double.MaxValue;
        var bestT = 0.0;
        foreach (var i in grid.Query(x, y, x, y, tolerance))
        {
            var s = segs[i];
            var d = LineworkGeometry.PtSegDist(x, y, s.X1, s.Y1, s.X2, s.Y2, out var t);
            if (d < bestDist)
            {
                bestDist = d;
                best = i;
                bestT = t;
            }
        }

        if (best < 0 || bestDist > tolerance)
        {
            return new LineworkTraceResult
            {
                Found = false,
                Query = [x, y],
                Message = $"No linework within {tolerance} units of ({x},{y})."
            };
        }

        var bs = segs[best];
        var entity = entities[bs.Entity];
        return new LineworkTraceResult
        {
            Found = true,
            Query = [x, y],
            Entity = entity,
            Hit = new TraceHit
            {
                SegIndex = bs.SegIndex,
                T = Math.Round(bestT, 6),
                Point = [bs.X1 + (bs.X2 - bs.X1) * bestT, bs.Y1 + (bs.Y2 - bs.Y1) * bestT],
                Distance = Math.Round(bestDist, 6)
            },
            Context = contextRadius > 0 ? Neighbors(entities, segs, x, y, contextRadius, entity.Id) : null
        };
    }

    private static TraceNeighbor[] Neighbors(
        IReadOnlyList<LineworkEntity> entities,
        IReadOnlyList<WorkSeg> segs,
        double x,
        double y,
        double radius,
        int excludeEntity)
    {
        var grid = new SegmentGrid(Math.Max(radius, 1e-3));
        for (var i = 0; i < segs.Count; i++)
        {
            var s = segs[i];
            grid.Insert(i, s.X1, s.Y1, s.X2, s.Y2);
        }

        var bestByEntity = new Dictionary<int, double>();
        foreach (var i in grid.Query(x, y, x, y, radius))
        {
            var s = segs[i];
            if (s.Entity == excludeEntity)
            {
                continue;
            }

            var d = LineworkGeometry.PtSegDist(x, y, s.X1, s.Y1, s.X2, s.Y2, out _);
            if (d <= radius && (!bestByEntity.TryGetValue(s.Entity, out var cur) || d < cur))
            {
                bestByEntity[s.Entity] = d;
            }
        }

        return bestByEntity
            .OrderBy(kv => kv.Value)
            .Take(20)
            .Select(kv => new TraceNeighbor
            {
                Entity = kv.Key,
                Handle = entities[kv.Key].Handle,
                Kind = entities[kv.Key].Kind.ToString(),
                Layer = entities[kv.Key].Layer,
                Distance = Math.Round(kv.Value, 6)
            })
            .ToArray();
    }
}
