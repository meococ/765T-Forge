using Forge.Shared;

namespace Forge.Tests;

public sealed class SpeedWaveHelpersTests
{
    // ===================== forge_standards_import =====================

    [Fact]
    public void StandardsKindsDefaultToAllInDependencyOrder()
    {
        Assert.True(StandardsImport.TryParseKinds(null, out var all, out _));
        Assert.Equal(StandardsImport.Order, all);
        Assert.Equal(StandardsKind.Linetypes, all[0]);

        Assert.True(StandardsImport.TryParseKinds(["layers", "dim_styles", "LineTypes"], out var picked, out _));
        Assert.Equal(new[] { StandardsKind.Linetypes, StandardsKind.Layers, StandardsKind.DimStyles }, picked);

        Assert.False(StandardsImport.TryParseKinds(["colours"], out _, out var error));
        Assert.Contains("pageSetups", error);
    }

    [Fact]
    public void StandardsPlanSkipsExistingUnlessOverwrite()
    {
        var source = new[] { "0", "A-WALL", "M-DUCT", "plan|X", "*U2", "Defpoints" };
        var dest = new[] { "0", "A-WALL", "Defpoints" };

        var skip = StandardsImport.Plan(StandardsKind.Layers, source, dest, WildcardSet.Empty, overwrite: false);
        Assert.Equal(StandardsAction.SkipProtected, skip.Single(p => p.Name == "0").Action);
        Assert.Equal(StandardsAction.SkipExisting, skip.Single(p => p.Name == "A-WALL").Action);
        Assert.Equal(StandardsAction.Add, skip.Single(p => p.Name == "M-DUCT").Action);
        Assert.DoesNotContain(skip, p => p.Name.Contains('|') || p.Name.StartsWith('*'));

        var overwrite = StandardsImport.Plan(StandardsKind.Layers, source, dest, WildcardSet.Empty, overwrite: true);
        Assert.Equal(StandardsAction.Overwrite, overwrite.Single(p => p.Name == "A-WALL").Action);
        Assert.Equal(StandardsAction.SkipProtected, overwrite.Single(p => p.Name == "Defpoints").Action);
    }

    [Fact]
    public void StandardsFilterLimitsNames()
    {
        var plan = StandardsImport.Plan(StandardsKind.TextStyles, ["Standard", "765T-2.5", "765T-3.5"], ["Standard"], WildcardSet.Parse("765T-*"), overwrite: true);
        Assert.Equal(StandardsAction.SkipFiltered, plan.Single(p => p.Name == "Standard").Action);
        Assert.All(plan.Where(p => p.Name.StartsWith("765T")), p => Assert.Equal(StandardsAction.Add, p.Action));
    }

    [Fact]
    public void StandardsProtectsSystemLinetypes()
    {
        Assert.True(StandardsImport.IsProtected(StandardsKind.Linetypes, "continuous"));
        Assert.False(StandardsImport.IsProtected(StandardsKind.Linetypes, "DASHED"));
        Assert.False(StandardsImport.IsProtected(StandardsKind.TextStyles, "Standard"));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("skip", false)]
    [InlineData("overwrite", true)]
    [InlineData("Replace", true)]
    public void StandardsConflictWords(string? value, bool expected)
    {
        Assert.True(StandardsImport.TryParseConflict(value, out var overwrite, out _));
        Assert.Equal(expected, overwrite);
    }

    // ===================== forge_block_insert_bulk =====================

    [Fact]
    public void BulkInsertMergesDefaults()
    {
        var items = new BulkInsertItem?[]
        {
            new() { Position = [1, 2] },
            new() { Name = "SD-300", Position = [3, 4, 5], Scale = 2, RotationDeg = 90, Layer = "M-DIFF", Attributes = new() { ["TAG"] = "SD-2" } }
        };

        var resolved = BlockBulkInsert.Resolve(items, "SD-200", 1.5, 45, "M-EQPM", null, null, null, out var errors);
        Assert.Empty(errors);
        Assert.Equal(2, resolved.Count);
        Assert.Equal("SD-200", resolved[0].Name);
        Assert.Equal(1.5, resolved[0].Scale);
        Assert.Equal(Math.PI / 4, resolved[0].RotationRad, 12);
        Assert.Equal("M-EQPM", resolved[0].Layer);
        Assert.Equal(new double[] { 1, 2, 0 }, resolved[0].Position);
        Assert.Equal("SD-300", resolved[1].Name);
        Assert.Equal("SD-2", resolved[1].Attributes["tag"]);
        Assert.Equal(new[] { "SD-200", "SD-300" }, BlockBulkInsert.DistinctNames(resolved));
    }

    [Fact]
    public void BulkInsertReportsEveryBadItem()
    {
        var items = new BulkInsertItem?[]
        {
            new() { Position = [0, 0] },
            new() { Name = "B", Position = [0] },
            new() { Name = "B", Position = [0, 0], Scale = 0 },
            new() { Name = "B", Position = [0, 0], ColorIndex = 300 },
            new() { Name = "B", Position = [0, 0], Leader = new BulkLeaderSpec() },
            null
        };

        BlockBulkInsert.Resolve(items, null, null, null, null, null, null, null, out var errors);
        Assert.Equal(
            new[] { "missing_block_name", "invalid_position", "invalid_scale", "invalid_args", "invalid_leader", "missing_block_name" },
            errors.Select(e => e.Code));
        Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, errors.Select(e => e.Index));
    }

    [Fact]
    public void BulkLeaderWithOneLandingPointStartsAtTheInsert()
    {
        var items = new BulkInsertItem?[]
        {
            new() { Name = "VAV", Position = [10, 20], Leader = new BulkLeaderSpec { Points = [[30, 40]], Text = "VAV-1" } }
        };

        var resolved = BlockBulkInsert.Resolve(items, null, null, null, "M-EQPM", "M-ANNO", "765T-Tag", 2.5, out var errors);
        Assert.Empty(errors);
        var leader = resolved[0].Leader!;
        Assert.Equal(new double[] { 10, 20, 0 }, leader.Points[0]);
        Assert.Equal(new double[] { 30, 40, 0 }, leader.Points[1]);
        Assert.Equal("M-ANNO", leader.Layer);
        Assert.Equal("765T-Tag", leader.Style);
        Assert.Equal(2.5, leader.TextHeight);
    }

    [Fact]
    public void BulkInsertRefusesOversizedSets()
    {
        var items = Enumerable.Range(0, BlockBulkInsert.MaxItems + 1).Select(_ => (BulkInsertItem?)new BulkInsertItem { Name = "A", Position = [0, 0] }).ToList();
        BlockBulkInsert.Resolve(items, null, null, null, null, null, null, null, out var errors);
        Assert.Equal("batch_too_large", Assert.Single(errors).Code);
    }

    // ===================== forge_plot_preset_apply =====================

    [Fact]
    public void PresetLayoutsDefaultToCurrent()
    {
        var layouts = PlotPreset.ResolveLayouts(null, ["Model", "A1-01", "A1-02"], "A1-02", out var unknown);
        Assert.Equal(new[] { "A1-02" }, layouts);
        Assert.Empty(unknown);
    }

    [Fact]
    public void PresetStarMeansEveryPaperLayout()
    {
        var layouts = PlotPreset.ResolveLayouts(["*"], ["Model", "A1-01", "A1-02"], "Model", out _);
        Assert.Equal(new[] { "A1-01", "A1-02" }, layouts);
    }

    [Fact]
    public void PresetMatchesNamesAndWildcardsAndReportsUnknown()
    {
        var layouts = PlotPreset.ResolveLayouts(["model", "M-*", "a1-01", "Nope"], ["Model", "A1-01", "M-101", "M-102"], "Model", out var unknown);
        Assert.Equal(new[] { "Model", "M-101", "M-102", "A1-01" }, layouts);
        Assert.Equal(new[] { "Nope" }, unknown);
    }
}
