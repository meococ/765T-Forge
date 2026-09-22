using System.Globalization;
using System.Text;

namespace Forge.Shared;

/// <summary>Segments query: linework flattened to individual segments.</summary>
public sealed record LineworkSegmentsArgs : LineworkQueryArgs
{
    /// <summary>"drawing" (default) or "meters" — unit of s/e/len in the response.</summary>
    public string? Units { get; init; }

    /// <summary>Optional calibrated transform file; emits transformed sT/eT per segment.</summary>
    public string? TransformPath { get; init; }

    /// <summary>Inline transform spec (alternative to transformPath).</summary>
    public CadTransformSpec? Transform { get; init; }
}

/// <summary>Compare query: mark CAD linework vs modeled pipe segments (missing/extra/matched).</summary>
public sealed record LineworkCompareArgs : LineworkQueryArgs
{
    /// <summary>Inline modeled pipe segments (id + x1,y1[,z1],x2,y2[,z2] or s/e arrays).</summary>
    public PipeSegmentDto[]? ModelSegments { get; init; }

    /// <summary>Path to a JSON array of modeled pipe segments (e.g. all_pipe_segs_live.json).</summary>
    public string? ModelSegmentsPath { get; init; }

    /// <summary>Sample counts as covered when a counterpart lies within this many meters. Default 0.3.</summary>
    public double ToleranceMeters { get; init; } = 0.3;

    /// <summary>Coverage sampling step in meters. Default 0.5.</summary>
    public double StepMeters { get; init; } = 0.5;

    /// <summary>Covered fraction at/above which a segment is "matched" (below = partial/missing). Default 0.5.</summary>
    public double MinCoverageFraction { get; init; } = 0.5;

    /// <summary>Model-side units per meter (1 = already meters). Default 1.</summary>
    public double ModelUnitsPerMeter { get; init; } = 1;

    /// <summary>Keep only model segments whose mid-Z (meters, after scaling) is >= this. Null = off.</summary>
    public double? MinZ { get; init; }

    /// <summary>Keep only model segments whose mid-Z (meters, after scaling) is <= this. Null = off.</summary>
    public double? MaxZ { get; init; }

    /// <summary>Search pad (meters) for nearest-counterpart lookup. Default 50.</summary>
    public double SearchRadiusMeters { get; init; } = 50;

    /// <summary>Calibrated CAD->model transform file applied to CAD segments before comparing.</summary>
    public string? TransformPath { get; init; }

    /// <summary>Inline transform spec (alternative to transformPath).</summary>
    public CadTransformSpec? Transform { get; init; }

    /// <summary>Optional path for an SVG (or .html) overlay color-coded by classification.</summary>
    public string? OverlayPath { get; init; }
}

/// <summary>One modeled pipe segment with optional elevation (model units).</summary>
public readonly record struct ModelSeg(double X1, double Y1, double X2, double Y2, double? Z1, double? Z2, string? Id);

public sealed record CompareCadItem
{
    public int Seg { get; init; }
    public int Entity { get; init; }
    public string? Handle { get; init; }
    public string Layer { get; init; } = "";
    public string? Container { get; init; }
    public int SegIndex { get; init; }
    /// <summary>Endpoints in compare space (meters — model space after optional transform).</summary>
    public double[] S { get; init; } = [];
    public double[] E { get; init; } = [];
    /// <summary>Endpoints in original CAD drawing units.</summary>
    public double[] SCad { get; init; } = [];
    public double[] ECad { get; init; } = [];
    public double LenM { get; init; }
    /// <summary>"matched" | "partial" | "missing_in_revit".</summary>
    public string Classification { get; init; } = "";
    /// <summary>Index into modelItems of the nearest model segment (null when none in range).</summary>
    public int? ModelIndex { get; init; }
    public string? RevitId { get; init; }
    /// <summary>Segment-to-segment closest approach to the nearest model segment, meters.</summary>
    public double? DistanceM { get; init; }
    /// <summary>Fraction of this segment within toleranceMeters of ANY model segment.</summary>
    public double? CoverageFrac { get; init; }
}

public sealed record CompareModelItem
{
    public int Index { get; init; }
    public string? Id { get; init; }
    public double[] S { get; init; } = [];
    public double[] E { get; init; } = [];
    public double? Z1 { get; init; }
    public double? Z2 { get; init; }
    public double LenM { get; init; }
    /// <summary>"matched" | "partial" | "extra_off_cad".</summary>
    public string Classification { get; init; } = "";
    /// <summary>Index into cadItems of the nearest CAD segment (null when none in range).</summary>
    public int? CadSeg { get; init; }
    public double? DistanceM { get; init; }
    public double? CoverageFrac { get; init; }
}

public sealed record ComparePair
{
    public int CadSeg { get; init; }
    public int ModelIndex { get; init; }
    public string? RevitId { get; init; }
    public string Layer { get; init; } = "";
    public double DistanceM { get; init; }
    public double CoverageFracM { get; init; }
}

public sealed record CompareLayerRollup
{
    public double TotalM { get; set; }
    public int SegCount { get; set; }
    public int Matched { get; set; }
    public int Partial { get; set; }
    public int Missing { get; set; }
}

public sealed record CompareReport
{
    public int CadSegCount { get; init; }
    public int ModelSegCount { get; init; }
    public double CadTotalM { get; init; }
    public double ModelTotalM { get; init; }
    public int MatchedCad { get; init; }
    public int PartialCad { get; init; }
    public int MissingInRevit { get; init; }
    public int MatchedModel { get; init; }
    public int PartialModel { get; init; }
    public int ExtraOffCad { get; init; }
    public int PairCount { get; init; }
    /// <summary>Model segments dropped by minZ/maxZ or zero plan length.</summary>
    public int SkippedModelSegs { get; init; }
    public CompareCadItem[] CadItems { get; init; } = [];
    public CompareModelItem[] ModelItems { get; init; } = [];
    /// <summary>Matched pairs (CAD seg -> nearest model seg), sorted by cadSeg.</summary>
    public ComparePair[] Pairs { get; init; } = [];
    public Dictionary<string, CompareLayerRollup> ByLayer { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public required object Params { get; init; }
}

/// <summary>
/// Per-item CAD-vs-model marking: each CAD segment and each model segment is classified
/// matched / partial / missing_in_revit / extra_off_cad, with its nearest counterpart and
/// distance in meters. CAD side is compared in model space: either an optional calibrated
/// transform maps drawing units -> model meters, or a plain 1/unitsPerMeter scale is used.
/// </summary>
public static class CompareEngine
{
    public static CompareReport Compare(
        IReadOnlyList<LineworkEntity> entities,
        IReadOnlyList<WorkSeg> cadSegs,
        IReadOnlyList<ModelSeg> modelSegsRaw,
        CadTransform? transform,
        double unitsPerMeter,
        double modelUnitsPerMeter,
        double toleranceMeters,
        double stepMeters,
        double minCoverageFraction,
        double? minZ,
        double? maxZ,
        double searchRadiusMeters)
    {
        var toM = 1.0 / Math.Max(unitsPerMeter, 1e-9);
        var mToM = 1.0 / Math.Max(modelUnitsPerMeter, 1e-9);

        // CAD -> compare space (meters): calibrated transform or plain unit scale.
        var cad = cadSegs.Select(s =>
        {
            double x1, y1, x2, y2;
            if (transform is not null)
            {
                var a = transform.Apply(s.X1, s.Y1);
                var b = transform.Apply(s.X2, s.Y2);
                (x1, y1, x2, y2) = (a[0], a[1], b[0], b[1]);
            }
            else
            {
                (x1, y1, x2, y2) = (s.X1 * toM, s.Y1 * toM, s.X2 * toM, s.Y2 * toM);
            }

            return (x1, y1, x2, y2, seg: s);
        }).ToArray();

        // Model -> compare space (meters); drop degenerate/z-filtered segments.
        var model = new List<(double x1, double y1, double x2, double y2, double? z1, double? z2, string? id)>();
        var skipped = 0;
        for (var i = 0; i < modelSegsRaw.Count; i++)
        {
            var m = modelSegsRaw[i];
            var x1 = m.X1 * mToM;
            var y1 = m.Y1 * mToM;
            var x2 = m.X2 * mToM;
            var y2 = m.Y2 * mToM;
            if (LineworkGeometry.SegLen(x1, y1, x2, y2) <= 1e-9)
            {
                skipped++; // zero plan length (e.g. vertical riser) — nothing to compare in plan
                continue;
            }

            var z1 = m.Z1 * mToM;
            var z2 = m.Z2 * mToM;
            if ((minZ.HasValue || maxZ.HasValue) && (z1.HasValue || z2.HasValue))
            {
                var mid = ((z1 ?? z2) + (z2 ?? z1))!.Value / 2;
                if ((minZ.HasValue && mid < minZ.Value) || (maxZ.HasValue && mid > maxZ.Value))
                {
                    skipped++;
                    continue;
                }
            }

            model.Add((x1, y1, x2, y2, z1, z2, m.Id));
        }

        var modelPlain = model.Select(m => (m.x1, m.y1, m.x2, m.y2)).ToArray();
        var modelRef = model.Select(m => (m.x1, m.y1, m.x2, m.y2, m.id)).ToArray();
        var cadPlain = cad.Select(c => (c.x1, c.y1, c.x2, c.y2)).ToArray();
        var cadRef = cad.Select(c => (c.x1, c.y1, c.x2, c.y2, (string?)null)).ToArray();
        var modelGrid = CoverageEngine.BuildGrid(modelPlain, toleranceMeters);
        var cadGrid = CoverageEngine.BuildGrid(cadPlain, toleranceMeters);
        var pad = Math.Max(searchRadiusMeters, toleranceMeters);

        var cadItems = new CompareCadItem[cad.Length];
        var pairs = new List<ComparePair>();
        var byLayer = new Dictionary<string, CompareLayerRollup>(StringComparer.OrdinalIgnoreCase);
        double cadTot = 0;
        int matchedCad = 0, partialCad = 0, missingCad = 0;

        for (var i = 0; i < cad.Length; i++)
        {
            var c = cad[i];
            var len = LineworkGeometry.SegLen(c.x1, c.y1, c.x2, c.y2);
            cadTot += len;
            var (frac, _) = CoverageEngine.SampleCoverage(c.x1, c.y1, c.x2, c.y2, modelRef, modelGrid, toleranceMeters, stepMeters);
            var (nearest, dist) = NearestSeg((c.x1, c.y1, c.x2, c.y2), modelPlain, modelGrid, pad);

            var classification = frac >= minCoverageFraction ? "matched" : frac > 0 ? "partial" : "missing_in_revit";
            if (classification == "matched") matchedCad++;
            else if (classification == "partial") partialCad++;
            else missingCad++;

            var e = entities[c.seg.Entity];
            cadItems[i] = new CompareCadItem
            {
                Seg = i,
                Entity = e.Id,
                Handle = e.Handle,
                Layer = e.Layer,
                Container = e.Container,
                SegIndex = c.seg.SegIndex,
                S = [c.x1, c.y1],
                E = [c.x2, c.y2],
                SCad = [c.seg.X1, c.seg.Y1],
                ECad = [c.seg.X2, c.seg.Y2],
                LenM = Math.Round(len, 4),
                Classification = classification,
                ModelIndex = nearest >= 0 ? nearest : null,
                RevitId = nearest >= 0 ? model[nearest].id : null,
                DistanceM = dist,
                CoverageFrac = Math.Round(frac, 4)
            };

            if (!byLayer.TryGetValue(e.Layer, out var roll))
            {
                roll = new CompareLayerRollup();
                byLayer[e.Layer] = roll;
            }

            roll.TotalM += len;
            roll.SegCount++;
            if (classification == "matched") roll.Matched++;
            else if (classification == "partial") roll.Partial++;
            else roll.Missing++;
        }

        var modelItems = new CompareModelItem[model.Count];
        double modTot = 0;
        int matchedModel = 0, partialModel = 0, extraModel = 0;
        for (var j = 0; j < model.Count; j++)
        {
            var m = model[j];
            var len = LineworkGeometry.SegLen(m.x1, m.y1, m.x2, m.y2);
            modTot += len;
            var (frac, _) = CoverageEngine.SampleCoverage(m.x1, m.y1, m.x2, m.y2, cadRef, cadGrid, toleranceMeters, stepMeters);
            var (nearest, dist) = NearestSeg((m.x1, m.y1, m.x2, m.y2), cadPlain, cadGrid, pad);

            var classification = frac >= minCoverageFraction ? "matched" : frac > 0 ? "partial" : "extra_off_cad";
            if (classification == "matched") matchedModel++;
            else if (classification == "partial") partialModel++;
            else extraModel++;

            modelItems[j] = new CompareModelItem
            {
                Index = j,
                Id = m.id,
                S = [m.x1, m.y1],
                E = [m.x2, m.y2],
                Z1 = m.z1,
                Z2 = m.z2,
                LenM = Math.Round(len, 4),
                Classification = classification,
                CadSeg = nearest >= 0 ? nearest : null,
                DistanceM = dist,
                CoverageFrac = Math.Round(frac, 4)
            };
        }

        foreach (var item in cadItems)
        {
            if (item.Classification == "matched" && item.ModelIndex is { } mi)
            {
                pairs.Add(new ComparePair
                {
                    CadSeg = item.Seg,
                    ModelIndex = mi,
                    RevitId = item.RevitId,
                    Layer = item.Layer,
                    DistanceM = item.DistanceM ?? 0,
                    CoverageFracM = item.CoverageFrac ?? 0
                });
            }
        }

        return new CompareReport
        {
            CadSegCount = cad.Length,
            ModelSegCount = model.Count,
            CadTotalM = Math.Round(cadTot, 3),
            ModelTotalM = Math.Round(modTot, 3),
            MatchedCad = matchedCad,
            PartialCad = partialCad,
            MissingInRevit = missingCad,
            MatchedModel = matchedModel,
            PartialModel = partialModel,
            ExtraOffCad = extraModel,
            PairCount = pairs.Count,
            SkippedModelSegs = skipped,
            CadItems = cadItems,
            ModelItems = modelItems,
            Pairs = pairs.OrderBy(p => p.CadSeg).ToArray(),
            ByLayer = byLayer,
            Params = new
            {
                toleranceMeters,
                stepMeters,
                minCoverageFraction,
                unitsPerMeter,
                modelUnitsPerMeter,
                transformApplied = transform is not null,
                minZ,
                maxZ,
                searchRadiusMeters,
                units = "meters"
            }
        };
    }

    /// <summary>Nearest ref segment by true seg-seg distance; (-1, null) when none inside pad.</summary>
    private static (int index, double? dist) NearestSeg(
        (double x1, double y1, double x2, double y2) q,
        (double x1, double y1, double x2, double y2)[] refSegs,
        SegmentGrid grid,
        double pad)
    {
        var best = double.MaxValue;
        var bestIdx = -1;
        foreach (var ri in grid.Query(Math.Min(q.x1, q.x2), Math.Min(q.y1, q.y2), Math.Max(q.x1, q.x2), Math.Max(q.y1, q.y2), pad))
        {
            var r = refSegs[ri];
            var d = LineworkGeometry.SegSegDist(q.x1, q.y1, q.x2, q.y2, r.x1, r.y1, r.x2, r.y2);
            if (d < best)
            {
                best = d;
                bestIdx = ri;
            }
        }

        return bestIdx < 0 ? (-1, null) : (bestIdx, Math.Round(best, 4));
    }
}

/// <summary>Minimal SVG overlay writer: color-coded segments + legend, browser-openable.</summary>
public static class LineworkSvg
{
    public const string ColorMatched = "#1a9850";   // green
    public const string ColorMissing = "#d73027";   // red — CAD run with no Revit pipe
    public const string ColorExtra = "#f46d43";     // orange — Revit pipe off CAD
    public const string ColorPartial = "#762a83";   // purple — partially covered

    public static string ClassificationColor(string classification) => classification switch
    {
        "matched" => ColorMatched,
        "partial" => ColorPartial,
        "missing_in_revit" => ColorMissing,
        "extra_off_cad" => ColorExtra,
        _ => "#666666"
    };

    public static void WriteCompareOverlay(
        string path,
        IReadOnlyList<CompareCadItem> cadItems,
        IReadOnlyList<CompareModelItem> modelItems,
        string title)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        void Acc(double x, double y)
        {
            minX = Math.Min(minX, x); minY = Math.Min(minY, y);
            maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }

        foreach (var c in cadItems) { Acc(c.S[0], c.S[1]); Acc(c.E[0], c.E[1]); }
        foreach (var m in modelItems) { Acc(m.S[0], m.S[1]); Acc(m.E[0], m.E[1]); }
        if (minX > maxX) { minX = 0; maxX = 1; minY = 0; maxY = 1; }

        var padX = Math.Max((maxX - minX) * 0.03, 1);
        var padY = Math.Max((maxY - minY) * 0.03, 1);
        minX -= padX; maxX += padX; minY -= padY; maxY += padY;
        var w = maxX - minX;
        var h = maxY - minY;

        var sb = new StringBuilder(cadItems.Count * 120 + 4096);
        sb.Append(CultureInfo.InvariantCulture,
            $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="{minX:0.###} {minY:0.###} {w:0.###} {h:0.###}" width="1400">""");
        sb.Append('\n');
        sb.Append(CultureInfo.InvariantCulture, $"<title>{Esc(title)}</title>\n");
        // CAD y-up -> SVG y-down: flip inside a group so coordinates stay in model units.
        sb.Append(CultureInfo.InvariantCulture,
            $"""<g transform="translate(0,{minY + maxY:0.###}) scale(1,-1)" fill="none" stroke-linecap="round">""");
        sb.Append('\n');

        foreach (var m in modelItems)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"""<path d="M{m.S[0]:0.###},{m.S[1]:0.###} L{m.E[0]:0.###},{m.E[1]:0.###}" stroke="{ClassificationColor(m.Classification)}" stroke-width="1.6" vector-effect="non-scaling-stroke"><title>revit {Esc(m.Id ?? "?")} [{m.Classification}] d={Fmt(m.DistanceM)}m</title></path>""");
            sb.Append('\n');
        }

        foreach (var c in cadItems)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"""<path d="M{c.S[0]:0.###},{c.S[1]:0.###} L{c.E[0]:0.###},{c.E[1]:0.###}" stroke="{ClassificationColor(c.Classification)}" stroke-width="2.2" vector-effect="non-scaling-stroke"><title>cad seg {c.Seg} {Esc(c.Layer)} [{c.Classification}] d={Fmt(c.DistanceM)}m revit={Esc(c.RevitId ?? "-")}</title></path>""");
            sb.Append('\n');
        }

        sb.Append("</g>\n");
        // Legend in unflipped SVG space.
        var lx = minX + w * 0.01;
        var ly = minY + h * 0.02;
        var fs = h * 0.016;
        sb.Append(CultureInfo.InvariantCulture, $"""<g font-family="monospace" font-size="{fs:0.###}">""");
        sb.Append('\n');
        sb.Append(CultureInfo.InvariantCulture,
            $"""<text x="{lx:0.###}" y="{ly - fs * 0.6:0.###}" fill="#222" font-weight="bold">{Esc(title)}</text>""");
        sb.Append('\n');
        var entries = new[]
        {
            (ColorMatched, "matched (cad+revit)"),
            (ColorMissing, "missing_in_revit (cad only)"),
            (ColorExtra, "extra_off_cad (revit only)"),
            (ColorPartial, "partial coverage")
        };
        for (var i = 0; i < entries.Length; i++)
        {
            sb.Append(CultureInfo.InvariantCulture,
                $"""<rect x="{lx:0.###}" y="{ly + i * fs * 1.5 - fs * 0.8:0.###}" width="{fs:0.###}" height="{fs * 0.8:0.###}" fill="{entries[i].Item1}"/>""");
            sb.Append(CultureInfo.InvariantCulture,
                $"""<text x="{lx + fs * 1.3:0.###}" y="{ly + i * fs * 1.5:0.###}" fill="#222">{entries[i].Item2}</text>""");
            sb.Append('\n');
        }

        sb.Append("</g>\n</svg>\n");

        var svg = sb.ToString();
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        if (full.EndsWith(".html", StringComparison.OrdinalIgnoreCase) || full.EndsWith(".htm", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(full, "<!doctype html><meta charset=\"utf-8\"><title>" + Esc(title) + "</title>\n" + svg);
        }
        else
        {
            File.WriteAllText(full, svg);
        }
    }

    private static string Fmt(double? v) => v.HasValue ? v.Value.ToString("0.###", CultureInfo.InvariantCulture) : "-";

    private static string Esc(string s)
        => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
