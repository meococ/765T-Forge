using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// B5: forge_qa_native_objects detectors — exploded dimensions (numeric text + linework +
/// arrowhead solids, no live DIMENSION), exploded hatches (regular parallel line arrays),
/// exploded blocks (identical repeated clusters).
/// </summary>
public sealed class NativeObjectScanTests
{
    private static NativeEntitySnapshot Ent(
        string handle, string type, double x1, double y1, double x2, double y2,
        string layer = "0", string? content = null, double? length = null, double? angle = null)
        => new(handle, type, layer, "*Model_Space",
               Math.Min(x1, x2), Math.Min(y1, y2), Math.Max(x1, x2), Math.Max(y1, y2),
               content, null, length, angle);

    private static NativeEntitySnapshot Line(string h, double x1, double y1, double x2, double y2, string layer = "0")
    {
        var len = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
        var ang = Math.Atan2(y2 - y1, x2 - x1) * 180.0 / Math.PI;
        return Ent(h, "Line", x1, y1, x2, y2, layer, null, len, ang);
    }

    private static List<NativeEntitySnapshot> ExplodedDim(double ox, double oy, string tag)
        =>
        [
            Line($"{tag}L1", ox, oy, ox + 3600, oy),
            Line($"{tag}L2", ox, oy - 50, ox, oy + 50),
            Line($"{tag}L3", ox + 3600, oy - 50, ox + 3600, oy + 50),
            Ent($"{tag}S", "Solid", ox + 20, oy - 15, ox + 60, oy + 15, "0", null, 40),
            Ent($"{tag}T", "MText", ox + 1700, oy + 30, ox + 1900, oy + 250, "0", "3600")
        ];

    [Fact]
    public void ExplodedDimensionClusterIsFlagged()
    {
        var result = NativeObjectScan.Scan(ExplodedDim(0, 0, "a"));
        var finding = Assert.Single(result.Findings);
        Assert.Equal("exploded_dimension_like", finding.Code);
        Assert.Contains("aT", finding.Handles);
        Assert.Equal(1, result.Summary[NativeObjectScan.CheckDims]);
        Assert.False(result.FindingsTruncated);
    }

    [Fact]
    public void LinesOnlyExplodedDimensionIsFlagged()
    {
        // t6 seed shape: dimension LINEs whose length equals the text value, plus tick
        // diagonals and extension LINEs — no SOLIDs at all.
        var ents = new List<NativeEntitySnapshot>
        {
            Line("dl", 0, -600, 3600, -600),
            Line("t1", -60, -660, 60, -540),
            Line("t2", 3540, -660, 3660, -540),
            Line("e1", 0, -100, 0, -800),
            Line("e2", 3600, -100, 3600, -800),
            Ent("txt", "MText", 1600, -520, 1900, -480, "0", "3600")
        };
        var result = NativeObjectScan.Scan(ents);
        var finding = Assert.Single(result.Findings, f => f.Code == "exploded_dimension_like");
        Assert.Contains("txt", finding.Handles);
    }

    [Fact]
    public void RealDimensionNearbySuppressesTheFlag()
    {
        var ents = ExplodedDim(0, 0, "a");
        ents.Add(Ent("d1", "RotatedDimension", -100, -800, 3700, -100));
        var result = NativeObjectScan.Scan(ents);
        Assert.DoesNotContain(result.Findings, f => f.Code == "exploded_dimension_like");
    }

    [Fact]
    public void RegularParallelLineArrayIsAnExplodedHatch()
    {
        var ents = Enumerable.Range(0, 6)
            .Select(i => Line($"h{i}", 0, i * 100, 900, i * 100 + 900, "A-HATCH"))
            .ToList();
        var result = NativeObjectScan.Scan(ents);
        var finding = Assert.Single(result.Findings, f => f.Code == "exploded_hatch_suspect");
        Assert.Contains("A-HATCH", finding.Message);
    }

    [Fact]
    public void IrregularSpacingIsNotAnExplodedHatch()
    {
        var ys = new[] { 0, 100, 260, 900, 1500 };
        var ents = ys.Select((y, i) => Line($"h{i}", 0, y, 800, y + 800, "A-HATCH")).ToList();
        var result = NativeObjectScan.Scan(ents);
        Assert.DoesNotContain(result.Findings, f => f.Code == "exploded_hatch_suspect");
    }

    [Fact]
    public void IdenticalCopiedClustersAreAnExplodedBlock()
    {
        var ents = new List<NativeEntitySnapshot>();
        foreach (var (ox, tag) in new[] { (0.0, "a"), (20000.0, "b") })
        {
            ents.Add(Line($"{tag}1", ox, 0, ox + 100, 0));
            ents.Add(Line($"{tag}2", ox + 100, 0, ox + 100, 100));
            ents.Add(Line($"{tag}3", ox + 100, 100, ox, 100));
            ents.Add(Line($"{tag}4", ox, 100, ox, 0));
        }
        var result = NativeObjectScan.Scan(ents, window: 1500);
        var finding = Assert.Single(result.Findings, f => f.Code == "exploded_block_suspect");
        Assert.Contains("a1", finding.Handles);
        Assert.Contains("b1", finding.Handles);
    }

    [Fact]
    public void CleanGeometryPasses()
    {
        var ents = new List<NativeEntitySnapshot>
        {
            Line("w1", 0, 0, 12000, 0, "A-WALL"),
            Line("w2", 0, 5400, 12000, 5400, "A-WALL"),
            Ent("d1", "RotatedDimension", 0, -800, 12000, -100, "A-DIMS"),
            Ent("t1", "MText", 100, 200, 2000, 500, "A-ANNO", "LIVING ROOM"),
            Ent("h1", "Hatch", 0, 0, 500, 500, "A-HATCH"),
            Ent("b1", "BlockReference", 100, 100, 400, 400, "0")
        };
        var result = NativeObjectScan.Scan(ents);
        Assert.Empty(result.Findings);
        Assert.Equal(ents.Count, result.EntitiesScanned);
    }

    [Fact]
    public void LongEntitySpanningManyCellsStillMerges()
    {
        // A wall-long line (bbox covers >10 cells at window 1500) must still union with the
        // cluster it touches — the center-cell shortcut would have missed this.
        var ents = new List<NativeEntitySnapshot>();
        foreach (var (ox, tag) in new[] { (0.0, "a"), (30000.0, "b") })
        {
            ents.Add(Line($"{tag}w", ox - 5000, -500, ox + 100, -500)); // long member
            ents.Add(Line($"{tag}1", ox, 0, ox + 100, 0));
            ents.Add(Line($"{tag}2", ox + 100, 0, ox + 100, 100));
            ents.Add(Line($"{tag}3", ox + 100, 100, ox, 100));
            ents.Add(Line($"{tag}4", ox, 100, ox, 0));
        }
        var result = NativeObjectScan.Scan(ents, window: 1500);
        var finding = Assert.Single(result.Findings, f => f.Code == "exploded_block_suspect");
        Assert.Contains("aw", finding.Handles);
        Assert.Contains("bw", finding.Handles);
    }

    [Fact]
    public void OversizedPoolIsCappedNotScanned()
    {
        var ents = Enumerable.Range(0, NativeObjectScan.MaxClusterEntities + 1)
            .Select(i => Line($"e{i}", i * 3000, 0, i * 3000 + 50, 0))
            .ToList();
        var result = NativeObjectScan.Scan(ents, checks: new[] { NativeObjectScan.CheckBlocks });
        var finding = Assert.Single(result.Findings);
        Assert.Equal("scan_capped", finding.Code);
    }

    [Fact]
    public void CheckFilterNarrowsDetection()
    {
        var ents = ExplodedDim(0, 0, "a");
        var result = NativeObjectScan.Scan(ents, checks: new[] { NativeObjectScan.CheckHatches });
        Assert.Empty(result.Findings);
    }
}
