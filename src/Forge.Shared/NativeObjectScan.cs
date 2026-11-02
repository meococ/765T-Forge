using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>Flat entity snapshot for native-object QA — extracted plugin-side, analyzed here.</summary>
public sealed record NativeEntitySnapshot(
    string Handle,
    string Type,
    string Layer,
    string Space,
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    string? Content = null,
    double? Height = null,
    double? Length = null,
    double? AngleDeg = null);

public sealed record NativeObjectFinding(
    string Code,
    string Severity,
    string Message,
    string[] Handles,
    double[] At);

/// <summary>
/// Pure detector for forge_qa_native_objects: geometry left behind when real AutoCAD
/// objects were exploded into loose primitives — the "reused public source carried
/// exploded dimensions" incident. Three signatures:
///   exploded_dimension_like — numeric text near parallel linework + arrowhead solids
///                             with NO real DIMENSION nearby;
///   exploded_hatch_suspect  — >=5 parallel lines on one layer at near-constant spacing;
///   exploded_block_suspect  — two spatially separate clusters with an identical
///                             type/length/angle fingerprint (a block exploded then copied).
/// Analysis is pure over snapshots so tests never need AutoCAD.
/// </summary>
public static class NativeObjectScan
{
    public const string CheckDims = "exploded_dimensions";
    public const string CheckHatches = "exploded_hatches";
    public const string CheckBlocks = "exploded_blocks";

    public static readonly string[] AllChecks = { CheckDims, CheckHatches, CheckBlocks };

    /// <summary>Above this pool size the motif-fingerprint scan is skipped (reported as scan_capped).</summary>
    public const int MaxClusterEntities = 10000;

    private static readonly HashSet<string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
        { "DBText", "MText", "AttributeDefinition" };
    private static readonly HashSet<string> LineTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Line", "Polyline", "Polyline2d", "Polyline3d", "Arc" };
    private static readonly HashSet<string> SolidTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Solid", "Trace", "Face", "Solid3d" };
    private static readonly HashSet<string> DimTypes = new(StringComparer.OrdinalIgnoreCase)
        { "Dimension", "RotatedDimension", "AlignedDimension", "RadialDimension",
          "DiametricDimension", "AngularDimension", "OrdinateDimension",
          "ArcDimension", "Leader", "MLeader" };

    private static readonly Regex FormatStrip = new(@"\{[^}]*\}|\\[A-Za-z][^;\\]*;?", RegexOptions.Compiled);
    private static readonly Regex NumericText = new(
        @"^\s*[ØøR]?\s*-?\d[\d,]*(\.\d+)?\s*(mm|cm|m|ft|in|""|')?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public sealed record Result(
        int EntitiesScanned,
        double Window,
        IReadOnlyDictionary<string, int> Summary,
        IReadOnlyList<NativeObjectFinding> Findings,
        bool FindingsTruncated);

    public static Result Scan(
        IReadOnlyList<NativeEntitySnapshot> entities,
        double window = 1500.0,
        IEnumerable<string>? checks = null,
        int maxFindings = 200)
    {
        var enabled = new HashSet<string>(checks ?? AllChecks, StringComparer.OrdinalIgnoreCase);
        var findings = new List<NativeObjectFinding>();
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (enabled.Contains(CheckDims))
        {
            DetectExplodedDimensions(entities, window, findings, claimed);
        }
        if (enabled.Contains(CheckHatches))
        {
            DetectExplodedHatches(entities, claimed, findings);
        }
        if (enabled.Contains(CheckBlocks))
        {
            DetectDuplicateClusters(entities, window, claimed, findings);
        }

        var summary = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [CheckDims] = findings.Count(f => f.Code == "exploded_dimension_like"),
            [CheckHatches] = findings.Count(f => f.Code == "exploded_hatch_suspect"),
            [CheckBlocks] = findings.Count(f => f.Code == "exploded_block_suspect")
        };
        var truncated = findings.Count > maxFindings;
        return new Result(
            entities.Count,
            window,
            summary,
            truncated ? findings.Take(maxFindings).ToArray() : findings,
            truncated);
    }

    private static bool BoxNear(NativeEntitySnapshot a, NativeEntitySnapshot b, double pad)
        => a.MinX <= b.MaxX + pad && a.MaxX >= b.MinX - pad
           && a.MinY <= b.MaxY + pad && a.MaxY >= b.MinY - pad;

    private static double ParseNumber(NativeEntitySnapshot text)
    {
        var digits = Regex.Replace(FormatStrip.Replace(text.Content ?? "", ""), "[^0-9.,]", "").Replace(",", "");
        return double.TryParse(digits, out var v) ? v : 0.0;
    }

    private static double WindowFor(NativeEntitySnapshot text, double fallback)
    {
        var v = ParseNumber(text);
        return v > 0 ? Math.Max(fallback, v * 1.5) : fallback;
    }

    private static bool IsNumericText(NativeEntitySnapshot e)
    {
        if (!TextTypes.Contains(e.Type) || e.Content is null)
        {
            return false;
        }
        var stripped = FormatStrip.Replace(e.Content, "").Trim();
        return stripped.Length > 0 && NumericText.IsMatch(stripped);
    }

    /// <summary>
    /// A dimension explosion leaves numeric annotation text beside short linework and
    /// arrowhead solids — but never a live DIMENSION/leader entity inside the window.
    /// </summary>
    private static void DetectExplodedDimensions(
        IReadOnlyList<NativeEntitySnapshot> entities,
        double window,
        List<NativeObjectFinding> findings,
        HashSet<string> claimed)
    {
        var lines = entities.Where(e => LineTypes.Contains(e.Type)).ToArray();
        var solids = entities.Where(e => SolidTypes.Contains(e.Type)).ToArray();
        var dims = entities.Where(e => DimTypes.Contains(e.Type)).ToArray();

        foreach (var text in entities.Where(IsNumericText))
        {
            // Extension lines sit at the dim's ends — half the measured value from centered
            // text — so scale the window to the text's own number, not just the caller's radius.
            var w = WindowFor(text, window);
            var nearLines = lines.Where(l => BoxNear(text, l, w)).ToArray();
            var nearSolids = solids.Where(s => BoxNear(text, s, w)).ToArray();
            var nearDims = dims.Where(d => BoxNear(text, d, w)).ToArray();
            if (nearDims.Length > 0)
            {
                continue;
            }

            // Path A: classic explosion — linework + arrowhead SOLIDs beside the text.
            // Path B: lines-only explosion — a line whose length equals the text's own
            // value, the text sitting inside that line's padded bbox, with extension/tick
            // linework nearby (no SOLIDs at all in this style).
            var value = ParseNumber(text);
            var solidPath = nearLines.Length >= 2 && nearSolids.Length >= 1;
            var measuredPath = value > 0
                && nearLines.Any(l => l.Length is { } len
                                      && Math.Abs(len - value) <= value * 0.12
                                      && BoxNear(text, l, value * 0.15))
                && nearLines.Length >= 4;
            if (!solidPath && !measuredPath)
            {
                continue;
            }

            var handles = new[] { text.Handle }
                .Concat(nearLines.Select(l => l.Handle))
                .Concat(nearSolids.Select(s => s.Handle))
                .Distinct()
                .Take(15)
                .ToArray();
            foreach (var h in handles)
            {
                claimed.Add(h);
            }
            findings.Add(new NativeObjectFinding(
                "exploded_dimension_like",
                "warning",
                $"Numeric text '{text.Content?.Trim()}' at {text.Space} sits beside {nearLines.Length} linework + {nearSolids.Length} arrowhead solids with no live DIMENSION — exploded dimension geometry.",
                handles,
                new[] { (text.MinX + text.MaxX) / 2.0, (text.MinY + text.MaxY) / 2.0 }));
        }
    }

    /// <summary>
    /// An exploded hatch is an array of parallel lines at constant spacing on one layer —
    /// linework could not produce that spacing regularity by accident often enough to matter.
    /// </summary>
    private static void DetectExplodedHatches(
        IReadOnlyList<NativeEntitySnapshot> entities,
        HashSet<string> claimed,
        List<NativeObjectFinding> findings)
    {
        var lines = entities
            .Where(e => e.Type.Equals("Line", StringComparison.OrdinalIgnoreCase)
                        && e.AngleDeg is not null
                        && !claimed.Contains(e.Handle))
            .GroupBy(e => (e.Layer, Bucket(e.AngleDeg!.Value, 0.5)));

        foreach (var group in lines)
        {
            var members = group.ToArray();
            if (members.Length < 5)
            {
                continue;
            }

            var angRad = group.Key.Item2 * Math.PI / 180.0;
            var nx = -Math.Sin(angRad);
            var ny = Math.Cos(angRad);
            var ordered = members
                .Select(e => (e, off: ((e.MinX + e.MaxX) / 2.0) * nx + ((e.MinY + e.MaxY) / 2.0) * ny))
                .OrderBy(x => x.off)
                .ToArray();

            var spacings = ordered.Skip(1).Select((x, i) => x.off - ordered[i].off).Where(s => s > 0.001).ToArray();
            if (spacings.Length < 3)
            {
                continue;
            }
            var median = spacings.OrderBy(s => s).ElementAt(spacings.Length / 2);
            if (median <= 0.001)
            {
                continue;
            }
            var regular = spacings.Count(s => Math.Abs(s - median) <= median * 0.2);
            if (regular < 3)
            {
                continue;
            }

            var handles = ordered.Select(x => x.e.Handle).Take(15).ToArray();
            foreach (var h in handles)
            {
                claimed.Add(h);
            }
            findings.Add(new NativeObjectFinding(
                "exploded_hatch_suspect",
                "warning",
                $"{members.Length} parallel lines on layer '{group.Key.Layer}' at near-constant {median:0.#} spacing — exploded hatch pattern.",
                handles,
                new[] { (members[0].MinX + members[0].MaxX) / 2.0, (members[0].MinY + members[0].MaxY) / 2.0 }));
        }
    }

    /// <summary>
    /// Two spatially separate clusters with an identical type/length/angle fingerprint are
    /// the signature of a block that was exploded then copied (or loose geometry pasted
    /// twice). Organic linework does not reproduce an exact fingerprint by chance.
    /// </summary>
    private static void DetectDuplicateClusters(
        IReadOnlyList<NativeEntitySnapshot> entities,
        double window,
        HashSet<string> claimed,
        List<NativeObjectFinding> findings)
    {
        var pool = entities.Where(e => !claimed.Contains(e.Handle)).ToArray();
        // Cap: union-find clustering is quadratic-ish on dense sheets; a huge drawing gets
        // dims+hatch detection but skips the motif fingerprint rather than stalling preflight.
        if (pool.Length > MaxClusterEntities)
        {
            findings.Add(new NativeObjectFinding(
                "scan_capped",
                "info",
                $"exploded_blocks skipped: {pool.Length} entities exceeds the {MaxClusterEntities} cap.",
                Array.Empty<string>(),
                Array.Empty<double>()));
            return;
        }

        var parent = new int[pool.Length];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }
        int Root(int i) => parent[i] == i ? i : parent[i] = Root(parent[i]);

        // Spatial grid: entities can only merge when their window-padded boxes overlap, so
        // register each entity in every cell its padded box covers; any mergeable pair
        // shares at least one cell. Turns the pairwise loop into ~O(n·k) on real sheets.
        var cell = Math.Max(window, 1.0);
        var grid = new Dictionary<(int cx, int cy), List<int>>();
        for (var i = 0; i < pool.Length; i++)
        {
            var e = pool[i];
            var x0 = (int)Math.Floor((e.MinX - window) / cell);
            var x1 = (int)Math.Floor((e.MaxX + window) / cell);
            var y0 = (int)Math.Floor((e.MinY - window) / cell);
            var y1 = (int)Math.Floor((e.MaxY + window) / cell);
            for (var cx = x0; cx <= x1; cx++)
            {
                for (var cy = y0; cy <= y1; cy++)
                {
                    var key = (cx, cy);
                    if (grid.TryGetValue(key, out var bucket))
                    {
                        foreach (var j in bucket)
                        {
                            if (BoxNear(e, pool[j], window))
                            {
                                var ri = Root(i);
                                var rj = Root(j);
                                if (ri != rj)
                                {
                                    parent[rj] = ri;
                                }
                            }
                        }
                    }
                    else
                    {
                        grid[key] = bucket = new List<int>();
                    }
                    bucket.Add(i);
                }
            }
        }

        var clusters = pool
            .Select((e, i) => (e, root: Root(i)))
            .GroupBy(x => x.root)
            .Select(g => g.Select(x => x.e).ToArray())
            .Where(c => c.Length >= 4 && c.Count(e => TextTypes.Contains(e.Type)) < c.Length)
            .ToArray();

        var byFingerprint = clusters
            .Select(c => (cluster: c, fp: Fingerprint(c)))
            .Where(x => x.fp.Length > 0)
            .GroupBy(x => x.fp)
            .Where(g => g.Count() >= 2);

        foreach (var dup in byFingerprint)
        {
            var handles = dup.SelectMany(x => x.cluster.Select(e => e.Handle)).Distinct().Take(15).ToArray();
            var first = dup.First().cluster[0];
            findings.Add(new NativeObjectFinding(
                "exploded_block_suspect",
                "warning",
                $"{dup.Count()} separate clusters share an identical {dup.First().cluster.Length}-entity fingerprint on layer '{first.Layer}' — repeated exploded-block motif.",
                handles,
                new[] { (first.MinX + first.MaxX) / 2.0, (first.MinY + first.MaxY) / 2.0 }));
        }
    }

    private static string Fingerprint(NativeEntitySnapshot[] cluster)
        => string.Join("|", cluster
            .Select(e => $"{e.Type}:{Quant(e.Length)}:{Quant(e.AngleDeg)}:{e.Content?.Trim()}")
            .OrderBy(t => t, StringComparer.Ordinal));

    private static string Quant(double? v) => v is null ? "-" : Math.Round(Math.Log10(Math.Abs(v.Value) + 1e-9), 1).ToString("0.0");

    private static double Bucket(double angleDeg, double step)
    {
        var a = angleDeg % 180.0;
        if (a < 0)
        {
            a += 180.0;
        }
        return Math.Round(a / step) * step;
    }
}
