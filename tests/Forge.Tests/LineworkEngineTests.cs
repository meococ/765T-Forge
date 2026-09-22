using Forge.Shared;

namespace Forge.Tests;

public sealed class LineworkEngineTests
{
    private const string DumpPath = @"D:\FreeLancer\AEONMALL BIEN HOA\evidence\forge\pl_dump.txt";
    private const string ModelPath = @"D:\FreeLancer\AEONMALL BIEN HOA\evidence\forge\all_pipe_segs_live.json";

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

    // ---------- real data (gated on evidence files) ----------

    [Fact]
    public void RealDumpParsesExpectedLayers()
    {
        if (!File.Exists(DumpPath))
        {
            return; // evidence file absent on CI
        }

        var ents = LineworkDumpParser.ParseFile(DumpPath);
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
        if (!File.Exists(DumpPath))
        {
            return;
        }

        var ents = LineworkDumpParser.ParseFile(DumpPath);
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
        if (!File.Exists(DumpPath) || !File.Exists(ModelPath))
        {
            return;
        }

        var ents = LineworkDumpParser.ParseFile(DumpPath);
        var filtered = LineworkQuery.Apply(ents, new LineworkQueryArgs { LayerFilter = "M-P-*" });
        var segs = LineworkFlattener.Flatten(filtered.Entities);

        var modelJson = File.ReadAllText(ModelPath);
        var dtos = System.Text.Json.JsonSerializer.Deserialize<PipeSegmentDto[]>(modelJson, ForgeJson.Options)!;
        var model = dtos.Where(d => d.TryGetSegment(out _, out _, out _, out _))
            .Select(d => { d.TryGetSegment(out var x1, out var y1, out var x2, out var y2); return (x1, y1, x2, y2, id: d.Id?.ToString()); })
            .ToList();

        var rep = CoverageEngine.Compare(filtered.Entities, segs, model,
            unitsPerMeter: 1000, modelUnitsPerMeter: 1, toleranceMeters: 0.3, stepMeters: 0.5, minCoverageFraction: 0.5);

        Assert.True(rep.CadTotalM > 100);
        Assert.True(rep.ModelSegCount > 1000);
        Assert.InRange(rep.CadCoveragePct, 0, 100);
        Assert.True(rep.Missing.Length + rep.Partial.Length <= rep.CadSegCount);
        Assert.True(rep.ByLayer.ContainsKey("M-P-WD"));
    }
}
