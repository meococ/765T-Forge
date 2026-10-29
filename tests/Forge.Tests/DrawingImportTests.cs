using Forge.Shared;

namespace Forge.Tests;

public sealed class DrawingImportTests
{
    [Theory]
    [InlineData(null, DrawingImportMode.Block)]
    [InlineData("block", DrawingImportMode.Block)]
    [InlineData("insert_as_block", DrawingImportMode.Block)]
    [InlineData("MERGE", DrawingImportMode.Merge)]
    [InlineData("explode", DrawingImportMode.Merge)]
    [InlineData("xref", DrawingImportMode.Xref)]
    [InlineData("attach", DrawingImportMode.Xref)]
    public void ParsesModes(string? value, DrawingImportMode expected)
    {
        Assert.True(DrawingImport.TryParseMode(value, out var mode, out var error));
        Assert.Null(error);
        Assert.Equal(expected, mode);
    }

    [Fact]
    public void RejectsUnknownMode()
    {
        Assert.False(DrawingImport.TryParseMode("paste", out _, out var error));
        Assert.Contains("block, merge or xref", error);
    }

    [Theory]
    [InlineData(@"C:\a\plan.dxf", "dxf")]
    [InlineData(@"C:\a\plan.DWG", "dwg")]
    [InlineData(@"C:\a\std.dwt", "dwg")]
    [InlineData(@"C:\a\plan.pdf", null)]
    [InlineData(null, null)]
    public void ClassifiesSourceKinds(string? path, string? expected)
    {
        Assert.Equal(expected, DrawingImport.SourceKind(path));
    }

    [Fact]
    public void UnitScaleConvertsMetresToMillimetres()
    {
        Assert.Equal(1000.0, DrawingImport.UnitScale(6, 4, out var warning), 9);
        Assert.Null(warning);
        Assert.Equal(25.4, DrawingImport.UnitScale(1, 4, out _), 9);
        Assert.Equal(0.001, DrawingImport.UnitScale(4, 6, out _), 12);
        Assert.Equal(12.0, DrawingImport.UnitScale(2, 1, out _), 9);
    }

    [Fact]
    public void UnitlessSourceImportsOneToOneWithWarning()
    {
        Assert.Equal(1.0, DrawingImport.UnitScale(0, 4, out var warning));
        Assert.NotNull(warning);
        Assert.Contains("unitless", warning);
        Assert.Equal(1.0, DrawingImport.UnitScale(4, 4, out var none));
        Assert.Null(none);
    }

    [Theory]
    [InlineData("M-HVAC", true)]
    [InlineData("0", true)]
    [InlineData("", false)]
    [InlineData(" lead", false)]
    [InlineData("a|b", false)]
    [InlineData("a*b", false)]
    [InlineData("A<B", false)]
    public void ValidatesSymbolNames(string name, bool expected)
    {
        Assert.Equal(expected, DrawingImport.IsValidSymbolName(name));
    }

    [Theory]
    [InlineData("GF HVAC plan", "GF HVAC plan")]
    [InlineData("a:b/c", "a_b_c")]
    [InlineData("  ", "IMPORT")]
    [InlineData(null, "IMPORT")]
    public void SanitizesBlockNames(string? raw, string expected)
    {
        Assert.Equal(expected, DrawingImport.SanitizeBlockName(raw));
    }

    [Fact]
    public void MapLayerPrefersExactThenMostSpecificWildcard()
    {
        var map = new Dictionary<string, string>
        {
            ["*"] = "X-*",
            ["WALL*"] = "A-WALL",
            ["WALL-EXT*"] = "A-WALL-EXT",
            ["DOOR"] = "A-DOOR"
        };

        Assert.Equal("A-DOOR", DrawingImport.MapLayer("door", map, null));
        Assert.Equal("A-WALL-EXT", DrawingImport.MapLayer("WALL-EXT-1", map, null));
        Assert.Equal("A-WALL", DrawingImport.MapLayer("WALL-INT", map, null));
        Assert.Equal("X-DUCT", DrawingImport.MapLayer("DUCT", map, null));
    }

    [Fact]
    public void WildcardTargetStarTakesTheCapturedPart()
    {
        var map = new Dictionary<string, string>
        {
            ["A-*"] = "M-*",
            ["*-OLD"] = "*",
            ["T?XT*"] = "ANNO-*",
            ["DUCT"] = "HVAC-*"
        };

        Assert.Equal("M-WALL", DrawingImport.MapLayer("A-WALL", map, null));
        Assert.Equal("M-wall", DrawingImport.MapLayer("a-wall", map, null));
        Assert.Equal("PIPE", DrawingImport.MapLayer("PIPE-OLD", map, null));
        Assert.Equal("ANNO-TEXT1", DrawingImport.MapLayer("TEXT1", map, null));
        Assert.Equal("HVAC-DUCT", DrawingImport.MapLayer("DUCT", map, null));

        Assert.Equal("WALL", DrawingImport.WildcardCapture("A-*", "A-WALL"));
        Assert.Equal("", DrawingImport.WildcardCapture("A-*", "A-"));
        Assert.Equal("MID", DrawingImport.WildcardCapture("X*Y", "XMIDY"));
        Assert.Equal("A-B-C", DrawingImport.WildcardCapture("*-*", "A-B-C"));
    }

    [Fact]
    public void PrefixAppliesOnlyToUnmappedLayersAndNeverToProtected()
    {
        var map = new Dictionary<string, string> { ["DIM*"] = "A-ANNO-DIMS" };
        Assert.Equal("NEW-DUCT", DrawingImport.MapLayer("DUCT", map, "NEW-"));
        Assert.Equal("A-ANNO-DIMS", DrawingImport.MapLayer("DIMS", map, "NEW-"));
        Assert.Equal("0", DrawingImport.MapLayer("0", map, "NEW-"));
        Assert.Equal("Defpoints", DrawingImport.MapLayer("Defpoints", map, "NEW-"));
    }

    [Fact]
    public void PlanSplitsRenamesFromMerges()
    {
        var sources = new[] { "0", "WALL1", "WALL2", "DUCT", "A-DUCT", "TEXT" };
        var map = new Dictionary<string, string>
        {
            ["WALL*"] = "A-WALL",
            ["DUCT"] = "A-DUCT",
            ["TEXT"] = "bad|name",
            ["0"] = "ZERO"
        };

        var plan = DrawingImport.PlanLayerMap(sources, map, null);

        // WALL1 + WALL2 share a free target: the first is renamed, the second merged into it.
        Assert.Contains(plan.Renames, r => r.Source == "WALL1" && r.Target == "A-WALL");
        Assert.Contains(plan.Merges, r => r.Source == "WALL2" && r.Target == "A-WALL");

        // DUCT -> A-DUCT where A-DUCT already exists in the source: entities move.
        Assert.Contains(plan.Merges, r => r.Source == "DUCT" && r.Target == "A-DUCT");
        Assert.DoesNotContain(plan.Renames, r => r.Source == "DUCT");

        Assert.Contains("TEXT -> bad|name", plan.Invalid);
        Assert.Contains("0", plan.Protected);
        Assert.Equal(3, plan.Changed);
        Assert.Equal("A-WALL", plan.AsDictionary()["wall2"]);
    }

    [Fact]
    public void CaseOnlyRenameIsARename()
    {
        var plan = DrawingImport.PlanLayerMap(["wall"], new Dictionary<string, string> { ["wall"] = "WALL" }, null);
        Assert.Single(plan.Renames);
        Assert.Empty(plan.Merges);
    }

    [Fact]
    public void SwappedLayersBothBecomeMerges()
    {
        var plan = DrawingImport.PlanLayerMap(["A", "B"], new Dictionary<string, string> { ["A"] = "B", ["B"] = "A" }, null);
        Assert.Empty(plan.Renames);
        Assert.Equal(2, plan.Merges.Count);
    }

    [Fact]
    public void MergeTransformPutsBasePointOnPosition()
    {
        var m = DrawingImport.MergeTransform([100, 200], 2.0, Math.PI / 2, [10, 0]);
        var xf = AffineXf.FromRowMajor16(m);

        var basePoint = xf.Apply2D(10, 0);
        Assert.Equal(100, basePoint.X, 9);
        Assert.Equal(200, basePoint.Y, 9);

        // One unit along +X from the base point: scaled x2 and rotated 90° CCW → +Y.
        var next = xf.Apply2D(11, 0);
        Assert.Equal(100, next.X, 9);
        Assert.Equal(202, next.Y, 9);
        Assert.Equal(16, m.Length);
        Assert.Equal(1.0, m[15]);
    }

    [Fact]
    public void UnionExtentsSkipsMissingBoxes()
    {
        double[]? acc = null;
        acc = DrawingImport.UnionExtents(acc, null);
        Assert.Null(acc);
        acc = DrawingImport.UnionExtents(acc, [0, 0, 0, 1, 1, 0]);
        acc = DrawingImport.UnionExtents(acc, [-5, 2, 0, 0, 9, 3]);
        acc = DrawingImport.UnionExtents(acc, [double.NaN, 0, 0, 1, 1, 1]);
        Assert.Equal(new double[] { -5, 0, 0, 1, 9, 3 }, acc);
    }

    [Fact]
    public void TryPointAcceptsNullAsOrigin()
    {
        Assert.True(DrawingImport.TryPoint(null, "position", out var origin, out _));
        Assert.Equal(new double[] { 0, 0, 0 }, origin);
        Assert.True(DrawingImport.TryPoint([1, 2], "position", out var p, out _));
        Assert.Equal(new double[] { 1, 2, 0 }, p);
        Assert.False(DrawingImport.TryPoint([1], "position", out _, out var error));
        Assert.Contains("position", error);
    }

    [Fact]
    public void WildcardSetMatchesFullNameAndXrefTail()
    {
        var set = WildcardSet.Parse("*DOOR*, A-WALL");
        Assert.True(set.Matches("A-DOOR"));
        Assert.True(set.Matches("plan|A-WALL"));
        Assert.False(set.Matches("A-WALL-EXT"));
        Assert.False(WildcardSet.Empty.Matches("x"));
        Assert.True(WildcardSet.Empty.Matches("x", emptyMatchesAll: true));
        Assert.Equal(2, set.Patterns.Count);
    }
}
