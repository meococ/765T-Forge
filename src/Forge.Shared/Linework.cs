using System.Globalization;
using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>2D point in drawing units (or meters once normalized by the caller).</summary>
public readonly record struct Vec2(double X, double Y)
{
    public double DistanceTo(Vec2 other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    public override string ToString() => $"({X.ToString("0.###", CultureInfo.InvariantCulture)},{Y.ToString("0.###", CultureInfo.InvariantCulture)})";
}

public enum LineworkKind
{
    Line,
    Polyline,
    Arc,
    Circle
}

/// <summary>
/// One linework entity (a LINE, a LWPOLYLINE/POLYLINE, an ARC, or a tessellated CIRCLE).
/// Points are ordered polyline vertices in drawing units (XY projection; Z is ignored).
/// For arcs, Points is the chord tessellation and Radius carries the true radius.
/// </summary>
public sealed record LineworkEntity
{
    /// <summary>Stable index within the extraction that produced this entity.</summary>
    public int Id { get; init; }

    /// <summary>AutoCAD handle (live drawing source) or a synthetic "row:&lt;n&gt;" id (dump file source).</summary>
    public string? Handle { get; init; }

    public LineworkKind Kind { get; init; }

    public string Layer { get; init; } = "";

    /// <summary>"modelspace", an xref block name, or "dumpfile" — where the entity was found.</summary>
    public string? Container { get; init; }

    /// <summary>Ordered vertices. LINE has 2; polylines have n; arcs/circles are pre-tessellated chords.</summary>
    public Vec2[] Points { get; init; } = [];

    public bool Closed { get; init; }

    /// <summary>Arc/circle radius in drawing units (0 for lines/polylines).</summary>
    public double Radius { get; init; }

    /// <summary>Total entity length in drawing units (perimeter when closed).</summary>
    public double Length { get; init; }

    /// <summary>Number of straight segments this entity contributes (Segments.Count).</summary>
    public int SegCount { get; init; }
}

/// <summary>Common query arguments shared by all forge_linework_* tools.</summary>
public record LineworkQueryArgs
{
    /// <summary>"drawing" (default, live AutoCAD extraction) or "dumpFile" (parse a pl_dump.txt-style file).</summary>
    public string? Source { get; init; }

    /// <summary>Path to a pl_dump.txt-format file. Required (or implied) when source="dumpFile".</summary>
    public string? DumpPath { get; init; }

    /// <summary>
    /// Comma-separated wcmatch-style patterns (* ?). Matches the full layer name AND the tail
    /// after the last '$' or '|' (xref-prefix aware, e.g. "A-Drainage-Pipe" matches
    /// "20260724_BF floor plans$0$A-Drainage-Pipe"). Plain substrings also match.
    /// Default: pipe-like layers (M-P-*,*UPVC*,*P-PIPE*,*DRAIN*,*SANIT*,*WASTE*,*SEWER*,*VENT*).
    /// Plain-pattern semantics are governed by layerMatch; layerSuffix patterns are unioned
    /// with this filter (and suppress the default when layerFilter is unset).
    /// </summary>
    public string? LayerFilter { get; init; }

    /// <summary>
    /// Explicit xref-safe suffix match: comma-separated suffixes tested with EndsWith against the
    /// BARE layer name (tail after the last '$' or '|'), e.g. layerSuffix="A-Drainage-Pipe" keeps
    /// "20260724_BF floor plans$0$A-Drainage-Pipe". Unioned with layerFilter when both are set;
    /// bypasses the default pipe filter when used alone.
    /// </summary>
    public string? LayerSuffix { get; init; }

    /// <summary>
    /// How non-wildcard layerFilter patterns match: "auto" (default: exact/tail-exact/substring),
    /// "exact" (full or tail equality), "suffix" (EndsWith on bare layer name — explicit xref-safe),
    /// "prefix" (StartsWith on the FULL name — legacy; silently drops xref-prefixed content),
    /// "substring" (Contains). Wildcard patterns (* ?) always regex-match full name or tail.
    /// </summary>
    public string? LayerMatch { get; init; }

    /// <summary>Same syntax as layerFilter; matching entities are excluded after inclusion.</summary>
    public string? ExcludeLayerFilter { get; init; }

    /// <summary>Comma-separated subset of line,polyline,arc,circle. Default "line,polyline,arc".</summary>
    public string? EntityTypes { get; init; }

    /// <summary>Optional [x1,y1,x2,y2] region in drawing units; entities whose bbox misses it are skipped.</summary>
    public double[]? Bbox { get; init; }

    /// <summary>Include entities inside xref block records (transformed to host space). Default true.</summary>
    public bool IncludeXrefContents { get; init; } = true;

    /// <summary>Drawing units per meter. 0 = auto (INSUNITS for drawings, 1000 for dump files).</summary>
    public double UnitsPerMeter { get; init; }

    /// <summary>Abort with linework_too_large above this many matched entities. Default 50000.</summary>
    public int MaxEntities { get; init; } = 50000;

    /// <summary>Optional path to write the full JSON report/entity list to disk.</summary>
    public string? OutputPath { get; init; }
}

public sealed record LineworkTraceArgs : LineworkQueryArgs
{
    /// <summary>Query point X. In drawing units, or meters when units="meters".</summary>
    public double X { get; init; }

    /// <summary>Query point Y. In drawing units, or meters when units="meters".</summary>
    public double Y { get; init; }

    /// <summary>"drawing" (default) or "meters" — unit of x/y/tolerance/contextRadius.</summary>
    public string? Units { get; init; }

    /// <summary>Search radius for the nearest segment. Default 500 drawing units.</summary>
    public double Tolerance { get; init; } = 500;

    /// <summary>Also list other linework entities within this radius of the point. Default 0 (off).</summary>
    public double ContextRadius { get; init; }

    /// <summary>Trace a specific entity by handle (or dump-file "row:&lt;n&gt;" id) instead of nearest-to-point.</summary>
    public string? Handle { get; init; }
}

public sealed record LineworkTopologyArgs : LineworkQueryArgs
{
    /// <summary>Endpoints closer than this (drawing units) merge into one graph node. Default 10.</summary>
    public double VertexTolerance { get; init; } = 10;

    /// <summary>A node within this distance of another edge's interior counts as a T-junction. Default 50.</summary>
    public double JunctionTolerance { get; init; } = 50;

    /// <summary>Report proper interior X-crossings between segments. Default true.</summary>
    public bool IncludeCrossings { get; init; } = true;
}

/// <summary>One modeled pipe centerline. Accepts {x1,y1,x2,y2} (all_pipe_segs_live.json) or {s:[x,y],e:[x,y]}.</summary>
public sealed record PipeSegmentDto
{
    /// <summary>Any JSON scalar (number or string) — normalized via ToString().</summary>
    public object? Id { get; init; }
    public double? X1 { get; init; }
    public double? Y1 { get; init; }
    public double? Z1 { get; init; }
    public double? X2 { get; init; }
    public double? Y2 { get; init; }
    public double? Z2 { get; init; }
    public double[]? S { get; init; }
    public double[]? E { get; init; }

    public bool TryGetSegment(out double x1, out double y1, out double x2, out double y2)
    {
        if (X1.HasValue && Y1.HasValue && X2.HasValue && Y2.HasValue)
        {
            x1 = X1.Value; y1 = Y1.Value; x2 = X2.Value; y2 = Y2.Value;
            return true;
        }

        if (S is { Length: >= 2 } && E is { Length: >= 2 })
        {
            x1 = S[0]; y1 = S[1]; x2 = E[0]; y2 = E[1];
            return true;
        }

        x1 = y1 = x2 = y2 = 0;
        return false;
    }

    /// <summary>Elevation endpoints (model units). Null when the source has no Z data.</summary>
    public bool TryGetZ(out double? z1, out double? z2)
    {
        z1 = Z1 ?? (S is { Length: >= 3 } ? S[2] : null);
        z2 = Z2 ?? (E is { Length: >= 3 } ? E[2] : null);
        return z1.HasValue || z2.HasValue;
    }
}

public sealed record LineworkCoverageArgs : LineworkQueryArgs
{
    /// <summary>Inline modeled pipe centerlines (meters). Alternative to modelSegmentsPath.</summary>
    public PipeSegmentDto[]? ModelSegments { get; init; }

    /// <summary>Path to a JSON array of modeled pipe centerlines (e.g. all_pipe_segs_live.json).</summary>
    public string? ModelSegmentsPath { get; init; }

    /// <summary>A CAD/model sample counts as covered when a counterpart lies within this many meters. Default 0.3.</summary>
    public double ToleranceMeters { get; init; } = 0.3;

    /// <summary>Sampling step along each segment in meters. Default 0.5.</summary>
    public double StepMeters { get; init; } = 0.5;

    /// <summary>Covered fraction below this flags a segment as missing/extra. Default 0.5.</summary>
    public double MinCoverageFraction { get; init; } = 0.5;

    /// <summary>Model-side units per meter (1 = already meters). Default 1.</summary>
    public double ModelUnitsPerMeter { get; init; } = 1;
}

/// <summary>
/// Layer matcher that is xref-prefix aware: a pattern is tested against the full layer name
/// and against the tail after the last '$' or '|' character, so callers never have to repeat
/// the "XrefName$0$" prefix. wcmatch-style wildcards (* ?) plus plain-substring matching for
/// patterns without wildcards — filtering "A-Drainage-Pipe" must NOT silently drop
/// "20260724_BF floor plans$0$A-Drainage-Pipe" (regression: a startswith filter did exactly that).
/// </summary>
/// <summary>How non-wildcard layer patterns are applied. See LineworkQueryArgs.LayerMatch.</summary>
public enum LayerMatchMode
{
    /// <summary>Exact OR tail-exact OR substring (default; xref-prefix aware).</summary>
    Auto,
    /// <summary>Full-name or tail equality only.</summary>
    Exact,
    /// <summary>EndsWith on the full name or the bare tail — explicit xref-safe matching.</summary>
    Suffix,
    /// <summary>StartsWith on the FULL name only — legacy; drops xref-prefixed layers.</summary>
    Prefix,
    /// <summary>Substring anywhere in the full name.</summary>
    Substring
}

public sealed class LayerMatcher
{
    /// <summary>Default include set mirroring the plumbing dump filter (dump_pl.lsp pllayer).</summary>
    public const string DefaultPipeLayerFilter = "M-P-*,*UPVC*,*P-PIPE*,*DRAIN*,*SANIT*,*WASTE*,*SEWER*,*VENT*";

    private readonly (string Pattern, Regex? Wildcard)[] _patterns;
    private readonly LayerMatchMode _mode;

    public LayerMatcher(string? filter, string? mode = null)
    {
        var text = string.IsNullOrWhiteSpace(filter) ? DefaultPipeLayerFilter : filter!;
        _patterns = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => (p, p.Contains('*') || p.Contains('?') ? WildcardToRegex(p) : null))
            .ToArray();
        _mode = ParseMode(mode);
    }

    public static LayerMatchMode ParseMode(string? mode)
    {
        return Enum.TryParse<LayerMatchMode>(mode, ignoreCase: true, out var m) ? m : LayerMatchMode.Auto;
    }

    /// <summary>Canonical mode name echoed in reports (defaults to "auto").</summary>
    public static string ModeName(string? mode)
        => ParseMode(mode).ToString().ToLowerInvariant();

    public bool Matches(string? layer)
    {
        if (string.IsNullOrEmpty(layer))
        {
            return false;
        }

        var tail = Tail(layer);
        foreach (var (pattern, wildcard) in _patterns)
        {
            if (wildcard is not null)
            {
                if (wildcard.IsMatch(layer) || wildcard.IsMatch(tail))
                {
                    return true;
                }
            }
            else if (MatchesPlain(layer, tail, pattern))
            {
                return true;
            }
        }

        return false;
    }

    private bool MatchesPlain(string layer, string tail, string pattern)
    {
        return _mode switch
        {
            // Explicit xref-safe: endswith on the bare layer name (or full name).
            LayerMatchMode.Suffix =>
                tail.EndsWith(pattern, StringComparison.OrdinalIgnoreCase) ||
                layer.EndsWith(pattern, StringComparison.OrdinalIgnoreCase),
            // Legacy startswith on the FULL name — xref-prefixed layers silently drop.
            LayerMatchMode.Prefix => layer.StartsWith(pattern, StringComparison.OrdinalIgnoreCase),
            LayerMatchMode.Substring => layer.Contains(pattern, StringComparison.OrdinalIgnoreCase),
            LayerMatchMode.Exact =>
                string.Equals(layer, pattern, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tail, pattern, StringComparison.OrdinalIgnoreCase),
            _ => // Auto: exact, tail-exact, or substring anywhere (prefix-aware).
                string.Equals(layer, pattern, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(tail, pattern, StringComparison.OrdinalIgnoreCase) ||
                layer.Contains(pattern, StringComparison.OrdinalIgnoreCase)
        };
    }

    /// <summary>EndsWith test on the bare layer tail — the explicit xref-safe suffix predicate.</summary>
    public static bool MatchesTailSuffix(string? layer, string suffix)
    {
        if (string.IsNullOrEmpty(layer) || string.IsNullOrEmpty(suffix))
        {
            return false;
        }

        return Tail(layer).EndsWith(suffix, StringComparison.OrdinalIgnoreCase) ||
               layer.EndsWith(suffix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Layer name after the last '$' or '|' xref/dependency delimiter.</summary>
    public static string Tail(string layer)
    {
        var idx = layer.LastIndexOfAny(TailDelimiters);
        return idx >= 0 ? layer[(idx + 1)..] : layer;
    }

    private static readonly char[] TailDelimiters = { '$', '|' };

    private static Regex WildcardToRegex(string pattern)
    {
        var escaped = Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".");
        return new Regex("^" + escaped + "$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    }
}

/// <summary>
/// Parses the flat linework text format produced by dump_pl.lsp / dump_full_linework.lsp:
/// L|layer|x,y,z|x,y,z ; PL|layer|x,y;x,y;… ; A|layer|cx,cy,z|r&lt;r&gt;|a1|a2 ;
/// C|layer|cx,cy,z|r&lt;r&gt; ; plus the dump_full variants LINE|/PLINE|/CIRC|/ARC|.
/// I/T/MT/PT/EL/SP/X/OTHER records are ignored (not linework).
/// </summary>
public static class LineworkDumpParser
{
    public static List<LineworkEntity> ParseFile(string path)
    {
        return Parse(File.ReadLines(path));
    }

    public static List<LineworkEntity> Parse(IEnumerable<string> lines)
    {
        var entities = new List<LineworkEntity>();
        var row = 0;
        foreach (var raw in lines)
        {
            row++;
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var p = raw.TrimEnd('\n', '\r').Split('|');
            var entity = p[0] switch
            {
                "L" or "LINE" => ParseLine(p, row),
                "PL" => ParsePolyline(p, row),
                "PLINE" => ParsePlineFlat(p, row),
                "A" or "ARC" => ParseArc(p, row),
                "C" or "CIRC" => ParseCircle(p, row),
                _ => null
            };

            if (entity is not null)
            {
                entities.Add(entity);
            }
        }

        var id = 0;
        return entities.Select(e => e with { Id = id++ }).ToList();
    }

    private static LineworkEntity? ParseLine(string[] p, int row)
    {
        if (p.Length < 4 || !TryPt(p[2], out var a) || !TryPt(p[3], out var b))
        {
            return null;
        }

        return Make(row, null, LineworkKind.Line, p[1], [a, b], closed: false);
    }

    private static LineworkEntity? ParsePolyline(string[] p, int row)
    {
        // PL|layer|x,y;x,y;…
        if (p.Length < 3)
        {
            return null;
        }

        var nums = Floats(p[2]);
        if (nums.Count < 4 || nums.Count % 2 != 0)
        {
            return null;
        }

        var pts = new Vec2[nums.Count / 2];
        for (var i = 0; i < pts.Length; i++)
        {
            pts[i] = new Vec2(nums[2 * i], nums[2 * i + 1]);
        }

        return Make(row, null, LineworkKind.Polyline, p[1], pts, closed: false);
    }

    private static LineworkEntity? ParsePlineFlat(string[] p, int row)
    {
        // PLINE|layer|x y x y …  (dump_full_linework.lsp: space-separated flat floats)
        if (p.Length < 3)
        {
            return null;
        }

        var nums = Floats(p[2]);
        if (nums.Count < 4 || nums.Count % 2 != 0)
        {
            return null;
        }

        var pts = new Vec2[nums.Count / 2];
        for (var i = 0; i < pts.Length; i++)
        {
            pts[i] = new Vec2(nums[2 * i], nums[2 * i + 1]);
        }

        return Make(row, null, LineworkKind.Polyline, p[1], pts, closed: false);
    }

    private static LineworkEntity? ParseArc(string[] p, int row)
    {
        // A|layer|cx,cy,z|r<radius>|startRad|endRad   (ARC| same, without angles in dump_full)
        if (p.Length < 4 || !TryPt(p[2], out var c))
        {
            return null;
        }

        var r = RadiusField(p[3]);
        if (r <= 0)
        {
            return null;
        }

        var a1 = p.Length > 4 ? TryDouble(p[4]) : 0;
        var a2 = p.Length > 5 ? TryDouble(p[5]) : Math.PI * 2;
        var pts = LineworkGeometry.TessellateArc(c.X, c.Y, r, a1, a2);
        return Make(row, null, LineworkKind.Arc, p[1], pts, closed: false, radius: r);
    }

    private static LineworkEntity? ParseCircle(string[] p, int row)
    {
        if (p.Length < 4 || !TryPt(p[2], out var c))
        {
            return null;
        }

        var r = RadiusField(p[3]);
        if (r <= 0)
        {
            return null;
        }

        var pts = LineworkGeometry.TessellateArc(c.X, c.Y, r, 0, Math.PI * 2);
        return Make(row, null, LineworkKind.Circle, p[1], pts, closed: true, radius: r);
    }

    private static LineworkEntity Make(int row, string? handle, LineworkKind kind, string layer, Vec2[] pts, bool closed, double radius = 0)
    {
        var length = 0.0;
        var n = closed ? pts.Length : pts.Length - 1;
        for (var i = 0; i < n; i++)
        {
            length += pts[i].DistanceTo(pts[(i + 1) % pts.Length]);
        }

        return new LineworkEntity
        {
            Id = row - 1,
            Handle = handle ?? $"row:{row}",
            Kind = kind,
            Layer = layer,
            Container = "dumpfile",
            Points = pts,
            Closed = closed,
            Radius = radius,
            Length = length,
            SegCount = Math.Max(0, closed ? pts.Length : pts.Length - 1)
        };
    }

    private static bool TryPt(string field, out Vec2 pt)
    {
        var nums = Floats(field);
        if (nums.Count >= 2)
        {
            pt = new Vec2(nums[0], nums[1]);
            return true;
        }

        pt = default;
        return false;
    }

    private static double RadiusField(string field)
    {
        // "r436.54" or "436.54"
        var f = field.TrimStart('r', 'R', ':');
        return TryDouble(f);
    }

    private static double TryDouble(string s)
    {
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    private static readonly Regex NumRegex = new(@"-?\d+(?:\.\d+)?(?:[eE][+-]?\d+)?", RegexOptions.Compiled);

    private static List<double> Floats(string field)
    {
        var list = new List<double>();
        foreach (Match m in NumRegex.Matches(field))
        {
            if (double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                list.Add(v);
            }
        }

        return list;
    }
}
