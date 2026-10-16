using Forge.Shared;

namespace Forge.Tests;

public sealed class LineworkEngineTests
{
    private static string? DumpPath => Environment.GetEnvironmentVariable("FORGE_TEST_LINEWORK_DUMP");
    private static string? ModelPath => Environment.GetEnvironmentVariable("FORGE_TEST_LINEWORK_MODEL");

    private static List<LineworkEntity> Parse(params string[] lines) => LineworkDumpParser.Parse(lines);

    // ---------- parser ----------

    [Fact]
    public void ParserReadsAllRecordKinds()
    {
        var ents = Parse(
            "L|M-P-WD|1000,2000,0|3000,2000,0",
            "PL|M-P-KD|0,0;500,0;500,500;",
            "A|M-P-V|100,100,0|r50|0|1.5708",
            "C|M-P-D|10,10,0|r25",
            "I|M-P-SD|RISER_DOWN|37715.9912,147001.0826,0|rot0|sx10",
            "MT|M-P-WS-Text 2|FROM 1F|55164.9459,145227.9637,0|h1800");

        Assert.Equal(4, ents.Count); // inserts + text are not linework
        Assert.Equal(LineworkKind.Line, ents[0].Kind);
        Assert.Equal(3, ents[1].Points.Length);
        Assert.Equal(LineworkKind.Arc, ents[2].Kind);
        Assert.True(ents[3].Closed);
        Assert.Equal(25, ents[3].Radius);
        Assert.Equal("row:1", ents[0].Handle);
    }

    [Fact]
    public void ParserHandlesPolylineVertexCount()
    {
        var ents = Parse("PL|M-P-KD|137422.6035,-5611.5508;137422.6035,9175.6732;");
        var e = Assert.Single(ents);
        Assert.Equal(2, e.Points.Length);
        Assert.Equal(1, e.SegCount);
        Assert.True(e.Length > 14000); // mm
    }

    // ---------- layer matcher (xref prefix regression) ----------

    [Fact]
    public void LayerMatcherMatchesXrefPrefixedNames()
    {
        var m = new LayerMatcher("A-Drainage-Pipe");
        Assert.True(m.Matches("20260724_BF floor plans$0$A-Drainage-Pipe"));
        Assert.True(m.Matches("A-Drainage-Pipe"));
        Assert.False(m.Matches("M-P-WD"));
    }

    [Fact]
    public void LayerMatcherMatchesBarStyleXrefNames()
    {
        var m = new LayerMatcher("A-Drainage-Pipe");
        Assert.True(m.Matches("20260724_BF floor plans|A-Drainage-Pipe"));
    }

    [Fact]
    public void LayerMatcherWildcardAndDefaults()
    {
        var def = new LayerMatcher(null); // default pipe set
        Assert.True(def.Matches("M-P-WD"));
        Assert.True(def.Matches("20260724_BF floor plans$0$A-Drainage-Pipe")); // *DRAIN*
        Assert.True(def.Matches("20260724_BF floor plans$0$UPVC"));
        Assert.False(def.Matches("A-WALL"));
        Assert.False(def.Matches("M-C-Mention"));

        var wild = new LayerMatcher("M-P-*");
        Assert.True(wild.Matches("M-P-KD"));
        Assert.False(wild.Matches("A-Drainage-Pipe"));
    }

    [Fact]
    public void LayerMatcherSubstringDoesNotRequirePrefix()
    {
        // "A-Drainage-Pipe" as a plain substring must match embedded positions too.
        var m = new LayerMatcher("Drainage");
        Assert.True(m.Matches("20260724_BF floor plans$0$A-Drainage-Pipe"));
    }

    // ---------- query filter ----------

    [Fact]
    public void QueryAppliesIncludeExcludeAndTypes()
    {
        var ents = Parse(
            "L|M-P-WD|0,0,0|100,0,0",
            "L|M-P-D-Text|0,0,0|50,0,0",
            "C|M-P-WD|0,0,0|r10",
            "L|A-WALL|0,0,0|10,0,0");

        var res = LineworkQuery.Apply(ents, new LineworkQueryArgs
        {
            LayerFilter = "M-P-*",
            ExcludeLayerFilter = "*-Text*",
            EntityTypes = "line"
        });

        var e = Assert.Single(res.Entities);
        Assert.Equal("M-P-WD", e.Layer);
        Assert.True(res.SkippedByKind.ContainsKey("layer") || res.SkippedByKind.ContainsKey("layerExcluded"));
    }

    // ---------- topology ----------

    private static LineworkTopologyReport Topo(IEnumerable<string> lines, double vt = 10, double jt = 50)
    {
        var ents = Parse(lines.ToArray());
        var segs = LineworkFlattener.Flatten(ents);
        return TopologyBuilder.Build(ents, segs, vt, jt, includeCrossings: true);
    }

    [Fact]
    public void TopologyChainsSharedVertices()
    {
        // two polylines sharing an endpoint -> one pass-through node, single component
        var r = Topo([
            "PL|L|0,0;100,0;100,100;",
            "PL|L|100,100;200,100;200,0;"
        ]);
        var joint = r.Nodes.Single(n => Math.Abs(n.X - 100) < 1e-6 && Math.Abs(n.Y - 100) < 1e-6);
        Assert.Equal(2, joint.Degree);
        Assert.Equal(TopoNodeKind.PassThrough, joint.Kind);
        Assert.Single(r.Components);
        Assert.Equal(4, r.EdgeCount);
        Assert.Equal(2, r.DeadEnds.Length); // only the two free ends
        Assert.Equal(5, r.NodeCount);
    }

    [Fact]
    public void TopologyDetectsDeadEnds()
    {
        var r = Topo(["L|L|0,0|100,0"]);
        Assert.Equal(2, r.DeadEnds.Length);
        Assert.All(r.Nodes, n => Assert.Equal(TopoNodeKind.DeadEnd, n.Kind));
    }

    [Fact]
    public void TopologyDetectsTJunction()
    {
        // main run 0->200, branch ending at (100,0)->(100,50): its endpoint touches main interior
        // jt must stay well below the 50-unit branch length so the free end isn't a junction.
        var r = Topo([
            "L|L|0,0|200,0",
            "L|L|100,0|100,50"
        ], jt: 5);
        Assert.Single(r.TJunctions);
        var tj = r.TJunctions[0];
        var node = r.Nodes[tj.Node];
        Assert.InRange(node.X, 99.9, 100.1);
        Assert.Equal(TopoNodeKind.Junction, node.Kind);
        Assert.Equal(3, r.DeadEnds.Length); // main's two ends + branch's free end
    }

    [Fact]
    public void TopologyDetectsXCrossing()
    {
        var r = Topo([
            "L|L|0,0|100,100",
            "L|L|0,100|100,0"
        ]);
        Assert.Single(r.Crossings);
        Assert.InRange(r.Crossings[0].X, 49, 51);
        Assert.InRange(r.Crossings[0].Y, 49, 51);
        // no shared nodes -> every node is a dead end
        Assert.Equal(4, r.DeadEnds.Length);
    }

    [Fact]
    public void TopologyMergesWithinVertexTolerance()
    {
        var r = Topo([
            "L|L|0,0|100,0",
            "L|L|105,0|200,0"
        ], vt: 10);
        Assert.Single(r.Components);
        Assert.Equal(2, r.Nodes.Count(n => n.Kind == TopoNodeKind.DeadEnd)); // gap merged -> 2 free ends
    }

    [Fact]
    public void TopologyRunsMergeThroughPassThrough()
    {
        // U-shaped run of 3 segments + a spur at middle vertex (junction via shared node)
        var r = Topo([
            "PL|L|0,0;100,0;100,100;200,100;",
            "L|L|100,0|100,-50"
        ]);
        var junction = r.Nodes.Single(n => n.Degree >= 3);
        Assert.Equal(3, junction.Degree);
        Assert.True(r.Runs.Length >= 2);
    }

    // ---------- coverage ----------

    [Fact]
    public void CoverageReportsMissingExtraAndPartial()
    {
        var ents = Parse(
            "L|M-P-WD|0,0,0|10000,0,0",      // 10 m along x — model covers first 6 m
            "L|M-P-WD|0,5000,0|10000,5000,0"); // 10 m parallel — no model at all
        var segs = LineworkFlattener.Flatten(ents);
        var model = new List<(double x1, double y1, double x2, double y2, string? id)>
        {
            (0, 0, 6, 0, "p1"),
            (50, 50, 60, 50, "p-extra") // far away -> extra
        };

        var rep = CoverageEngine.Compare(ents, segs, model, unitsPerMeter: 1000, modelUnitsPerMeter: 1,
            toleranceMeters: 0.3, stepMeters: 0.5, minCoverageFraction: 0.5);

        Assert.Equal(20, rep.CadTotalM, 1);
        Assert.True(rep.CadCoveragePct is > 20 and < 40);
        Assert.Single(rep.Missing);                 // second CAD seg has no model
        Assert.Single(rep.Partial);                 // first CAD seg partially covered
        Assert.Single(rep.Extra);                   // p-extra has no CAD
        var partial = rep.Partial[0];
        Assert.True(partial.UncoveredRanges.Length >= 1);
        Assert.True(partial.UncoveredRanges[0][0] > 0.5); // uncovered tail starts after ~60%
        Assert.Equal("p-extra", rep.Extra[0].Id);
    }

    [Fact]
    public void CoverageRespectsLayerFilterIntegration()
    {
        var ents = Parse(
            "L|M-P-WD|0,0,0|10000,0,0",
            "L|A-WALL|0,0,0|10000,0,0");
        var filtered = LineworkQuery.Apply(ents, new LineworkQueryArgs { LayerFilter = "M-P-*" });
        Assert.Single(filtered.Entities); // wall dropped
    }

    // ---------- trace ----------

    [Fact]
    public void TraceFindsNearestEntityAndSegment()
    {
        var ents = Parse(
            "PL|M-P-WD|0,0;100,0;100,100;",
            "L|M-P-KD|500,500,0|600,500,0");
        var segs = LineworkFlattener.Flatten(ents);
        var r = TraceEngine.Trace(ents, segs, 99, 30, tolerance: 100, contextRadius: 0, handle: null);

        Assert.True(r.Found);
        Assert.Equal("M-P-WD", r.Entity!.Layer);
        Assert.Equal(1, r.Hit!.SegIndex);      // second segment (vertical leg)
        Assert.Equal(1, r.Hit.Distance, 3);    // point is 1 unit left of the leg
        Assert.Equal(100, r.Hit.Point[0], 3);
        Assert.Equal(30, r.Hit.Point[1], 3);
        Assert.Equal(3, r.Entity.Points.Length);
    }

    [Fact]
    public void TraceHonorsToleranceAndHandle()
    {
        var ents = Parse("L|M-P-WD|0,0,0|100,0,0");
        var segs = LineworkFlattener.Flatten(ents);

        var miss = TraceEngine.Trace(ents, segs, 0, 5000, tolerance: 100, contextRadius: 0, handle: null);
        Assert.False(miss.Found);

        var byHandle = TraceEngine.Trace(ents, segs, 0, 0, tolerance: 0, contextRadius: 0, handle: ents[0].Handle);
        Assert.True(byHandle.Found);
        Assert.Equal(2, byHandle.Entity!.Points.Length);
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(-0.25)]
    [InlineData(0.41421356237309503)] // tan(pi/8) = 90 deg arc
    [InlineData(-0.41421356237309503)]
    [InlineData(1.0)]                 // semicircle
    [InlineData(-1.0)]
    [InlineData(2.0)]                 // major arc (> 180 deg)
    [InlineData(-2.0)]
    public void TessellateBulgePlacesArcAndCenterOnCorrectSide(double bulge)
    {
        // Chord from (0,0) to (100,0) along +X.
        // In AutoCAD LWPolyline, bulge = tan(theta/4), positive = CCW from A to B.
        // Therefore, for bulge > 0, the arc bulges to the RIGHT of A->B (negative Y),
        // with midpoint at (50, -50 * bulge).
        var pts = LineworkGeometry.TessellateBulge(new Vec2(0, 0), new Vec2(100, 0), bulge);
        Assert.True(pts.Length >= 3);
        var mid = pts[pts.Length / 2];
        var expectedMidY = -50.0 * bulge;
        Assert.Equal(50.0, mid.X, 4);
        Assert.Equal(expectedMidY, mid.Y, 4);

        // All tessellated vertices must lie on the circle of radius R = (chord/4)*(b + 1/b).
        var centerY = 25.0 * (1.0 - bulge * bulge) / bulge;
        var radius = 25.0 * (Math.Abs(bulge) + 1.0 / Math.Abs(bulge));
        foreach (var p in pts)
        {
            var d = Math.Sqrt((p.X - 50.0) * (p.X - 50.0) + (p.Y - centerY) * (p.Y - centerY));
            Assert.Equal(radius, d, 5);
        }
    }

    [Fact]
    public void SegmentGridHandlesLongDiagonalWithTinyToleranceWithoutHanging()
    {
        // A 10,000-unit diagonal segment with cellSize = 0.1 has a 100,000 x 100,000 bounding box
        // (10^10 cells). Exact slice traversal touches only the cells along the segment tube.
        var grid = new SegmentGrid(0.1);
        grid.Insert(0, 0, 0, 10000, 10000);

        var hits = grid.Query(5000, 5000, 5000, 5000, 0.2);
        Assert.Contains(0, hits);

        var miss = grid.Query(5000, 5010, 5000, 5010, 0.2);
        Assert.DoesNotContain(0, miss);
    }

    [Fact]
    public void SegmentGridHandlesNegativeSlopeLongDiagonalLargePadAndVn2000Coordinates()
    {
        // 1. Negative-slope long diagonal: (0, 100) -> (100, 0) on a 0.5m grid (200x200 bbox = 40,000 cells > 256).
        // Querying with a negative-slope segment (20, 80) -> (80, 20) or interior point (27.3, 72.7) must hit!
        var grid = new SegmentGrid(0.5);
        grid.Insert(0, 0, 100, 100, 0);
        grid.Insert(1, 30, 70, 31, 69); // short segment along the negative diagonal

        Assert.Contains(0, grid.Query(27.3, 72.7, 27.3, 72.7, 0.2));
        // Querying with a long negative-slope segment (0, 100) -> (100, 0) must find the short segment 1 at (30, 70)
        // (previously failed when NearestDist/NearestSeg passed Min/Max bbox corners instead of true endpoints!).
        Assert.Contains(1, grid.Query(0, 100, 100, 0, 0.3));

        // 2. Large pad (> 8 cells): segment at y = 25m on a 0.5m grid is 50 cells away from y = 0.
        // Querying at (50, 0) with pad = 30m (60 cells) must find it (previously failed when padCells was clamped to 8).
        var padGrid = new SegmentGrid(0.5);
        padGrid.Insert(7, 45, 25, 55, 25);
        Assert.Contains(7, padGrid.Query(50, 0, 50, 0, pad: 30.0));

        // 3. VN2000 millimeter coordinates (~600,000,000 mm, 1,200,000,000 mm) with tiny cellSize = 0.1 mm
        // exceed int.MaxValue cell index (1.2e10 > 2.14e9); (long, long) cell keys prevent overflow.
        var vnGrid = new SegmentGrid(0.1);
        const double vx = 600_000_000.0;
        const double vy = 1_200_000_000.0;
        vnGrid.Insert(42, vx, vy, vx + 10.0, vy - 10.0);
        Assert.Contains(42, vnGrid.Query(vx + 5.0, vy - 5.0, vx + 5.0, vy - 5.0, 0.2));
    }

    [Fact]
    public void CoverageMatchesNegativeSlopeLongDiagonalPipeRuns()
    {
        // 50 m negative-slope diagonal pipe from (0, 50000) to (50000, 0) in mm (bbox = 100x100m = 40,000 cells on 0.5m grid).
        var ents = Parse("L|M-P-WD|0,50000,0|50000,0,0");
        var segs = LineworkFlattener.Flatten(ents);
        var model = new List<(double x1, double y1, double x2, double y2, string? id)>
        {
            (0, 50, 50, 0, "diag-neg-1")
        };

        var rep = CoverageEngine.Compare(
            ents, segs, model,
            unitsPerMeter: 1000, modelUnitsPerMeter: 1,
            toleranceMeters: 0.3, stepMeters: 0.5, minCoverageFraction: 0.5);

        Assert.Empty(rep.Missing);
        Assert.Empty(rep.Partial);
        Assert.Empty(rep.Extra);
        Assert.InRange(rep.CadCoveragePct, 99.0, 100.0);
    }

    [Fact]
    public void ParserIsCultureInvariantUnderViVn()
    {
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("vi-VN");
            var ents = Parse("L|M-P-WD|1234.56,7890.12,0|2234.56,7890.12,0");
            var e = Assert.Single(ents);
            Assert.Equal(1000.0, e.Length, 3);
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = prev;
        }
    }

    // ---------- real data (uses env fixture or deterministic synthetic metro dataset) ----------

    internal static List<LineworkEntity> LoadRealOrSyntheticDump()
    {
        if (!string.IsNullOrWhiteSpace(DumpPath) && File.Exists(DumpPath))
        {
            return LineworkDumpParser.ParseFile(DumpPath);
        }

        var lines = new List<string>(2300);
        for (var i = 0; i < 1100; i++)
        {
            var x0 = (i % 50) * 2000;
            var y0 = (i / 50) * 2000;
            lines.Add($"L|20260724_BF floor plans$0$A-Drainage-Pipe|{x0},{y0},0|{x0 + 1800},{y0},0");
        }

        for (var i = 0; i < 120; i++)
        {
            var x0 = i * 1000;
            lines.Add($"L|M-P-WD|{x0},0,0|{x0 + 1000},0,0");
            if (i % 3 == 0)
            {
                lines.Add($"L|M-P-WD|{x0 + 1000},0,0|{x0 + 1000},1500,0");
            }
        }

        for (var i = 0; i < 800; i++)
        {
            var x0 = i * 500;
            lines.Add($"L|A-WALL|{x0},10000,0|{x0 + 400},10000,0");
        }

        return LineworkDumpParser.Parse(lines);
    }

    internal static List<(double x1, double y1, double x2, double y2, string? id)> LoadRealOrSyntheticModel()
    {
        if (!string.IsNullOrWhiteSpace(ModelPath) && File.Exists(ModelPath))
        {
            var modelJson = File.ReadAllText(ModelPath);
            var dtos = System.Text.Json.JsonSerializer.Deserialize<PipeSegmentDto[]>(modelJson, ForgeJson.Options)!;
            return dtos.Where(d => d.TryGetSegment(out _, out _, out _, out _))
                .Select(d =>
                {
                    d.TryGetSegment(out var x1, out var y1, out var x2, out var y2);
                    return (x1, y1, x2, y2, id: d.Id?.ToString());
                })
                .ToList();
        }

        var model = new List<(double x1, double y1, double x2, double y2, string? id)>(1100);
        for (var i = 0; i < 100; i++)
        {
            model.Add((i * 1.0, 0, (i + 1) * 1.0, 0, $"wd-{i}"));
        }

        for (var i = 0; i < 950; i++)
        {
            var x0 = (i % 50) * 2.0;
            var y0 = (i / 50) * 2.0;
            model.Add((x0, y0, x0 + 1.8, y0, $"dr-{i}"));
        }

        return model;
    }

    [Fact]
    public void RealDumpParsesExpectedLayers()
    {
        var ents = LoadRealOrSyntheticDump();
        Assert.True(ents.Count > 2000);
        var layers = ents.GroupBy(e => e.Layer).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(layers["20260724_BF floor plans$0$A-Drainage-Pipe"] > 1000);
        Assert.True(layers["M-P-WD"] > 100);

        // default pipe filter must keep the xref layer (the startswith regression)
        var filtered = LineworkQuery.Apply(ents, new LineworkQueryArgs());
        Assert.Contains(filtered.Entities, e => e.Layer.Contains("A-Drainage-Pipe"));
    }

    [Fact]
    public void RealDumpTopologyOnMpWd()
    {
        var ents = LoadRealOrSyntheticDump();
        var filtered = LineworkQuery.Apply(ents, new LineworkQueryArgs { LayerFilter = "M-P-WD" });
        var segs = LineworkFlattener.Flatten(filtered.Entities);
        var rep = TopologyBuilder.Build(filtered.Entities, segs, vertexTolerance: 10, junctionTolerance: 50, includeCrossings: true);

        Assert.True(rep.EdgeCount > 100);
        Assert.True(rep.NodeCount > 100);
        Assert.True(rep.Components.Length >= 1);
        Assert.True(rep.DeadEnds.Length > 0);
        // every node in deadEnds has degree 1
        Assert.All(rep.DeadEnds, n => Assert.Equal(1, rep.Nodes[n].Degree));
    }

    [Fact]
    public void RealCoverageProducesReport()
    {
        var ents = LoadRealOrSyntheticDump();
        var filtered = LineworkQuery.Apply(ents, new LineworkQueryArgs { LayerFilter = "M-P-*" });
        var segs = LineworkFlattener.Flatten(filtered.Entities);
        var model = LoadRealOrSyntheticModel();

        var rep = CoverageEngine.Compare(filtered.Entities, segs, model,
            unitsPerMeter: 1000, modelUnitsPerMeter: 1, toleranceMeters: 0.3, stepMeters: 0.5, minCoverageFraction: 0.5);

        Assert.True(rep.CadTotalM > 100);
        Assert.True(rep.ModelSegCount > 1000);
        Assert.InRange(rep.CadCoveragePct, 0, 100);
        Assert.True(rep.Missing.Length + rep.Partial.Length <= rep.CadSegCount);
        Assert.True(rep.ByLayer.ContainsKey("M-P-WD"));
    }
}
