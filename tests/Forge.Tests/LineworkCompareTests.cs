using System.Text.Json;
using Forge.Shared;

namespace Forge.Tests;

public sealed class LineworkCompareTests
{
    private static readonly double[][] ApplyPoints = [[5000.0, 2000.0]];
    private static readonly double[][] RejectPoints = [[1.0, 2.0]];
    private static readonly double[] Origin2 = [0.0, 0.0];
    private static readonly double[] XUnit = [1.0, 0.0];
    private static readonly string[] DrainageSuffixPattern = ["A-Drainage-Pipe"];
    private static readonly string[] CadClassifications = ["matched", "partial", "missing_in_revit"];
    private static readonly string[] ModelClassifications = ["matched", "partial", "extra_off_cad"];

    private static List<LineworkEntity> Parse(params string[] lines) => LineworkDumpParser.Parse(lines);

    private static ForgeResult InvokeLocal(string tool, object args)
        => LineworkLocal.Execute(new ForgeCommand { Tool = tool, Args = ForgeJson.ToElement(args) });

    private static string WriteTempDump(params string[] lines)
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge_test_{Guid.NewGuid():N}.txt");
        File.WriteAllLines(path, lines);
        return path;
    }

    // ---------- transform fitting ----------

    [Fact]
    public void SimilarityFitTwoPairsIsExact()
    {
        // CAD mm -> Revit m, scale 0.001, rotation 90 deg, translation (10, 20)
        var xf0 = CadTransform.FromSimilarity(0.001, 90, 10, 20);
        var pairs = new[] { (1000.0, 2000.0), (5000.0, -3000.0) }
            .Select(c => (cx: c.Item1, cy: c.Item2, rx: xf0.Apply(c.Item1, c.Item2)[0], ry: xf0.Apply(c.Item1, c.Item2)[1]))
            .ToArray();

        var fit = TransformFitter.FitSimilarity(pairs);
        Assert.True(fit.RmsM < 1e-9);
        Assert.Equal(0.001, fit.Transform.Scale, 9);
        Assert.Equal(90, fit.Transform.RotationDeg, 6);
        Assert.Equal(10, fit.Transform.Tx, 6);
        Assert.Equal(20, fit.Transform.Ty, 6);
    }

    [Fact]
    public void SimilarityFitLeastSquaresReportsResiduals()
    {
        var xf0 = CadTransform.FromSimilarity(0.001, 0, 5, -3);
        var cadPts = new[] { (0.0, 0.0), (10000.0, 0.0), (10000.0, 8000.0), (0.0, 8000.0) };
        var pairs = cadPts.Select((c, i) =>
        {
            var r = xf0.Apply(c.Item1, c.Item2);
            // non-uniform noise the fit cannot fully absorb (one corner +20 mm on x)
            return (cx: c.Item1, cy: c.Item2, rx: r[0] + (i == 2 ? 0.02 : 0.0), ry: r[1]);
        }).ToArray();

        var fit = TransformFitter.FitSimilarity(pairs);
        Assert.True(fit.RmsM > 0.001 && fit.RmsM < 0.02);
        Assert.Equal(4, fit.Residuals.Length);
        Assert.Equal(fit.Residuals.Max(), fit.MaxResidualM, 9);
    }

    [Fact]
    public void AffineFitRecoversShearFreeMatrix()
    {
        var xf0 = new CadTransform { Kind = "affine", M11 = 0.001, M12 = 0.0002, M21 = -0.0001, M22 = 0.001, Tx = 3, Ty = -7 };
        var cadPts = new[] { (0.0, 0.0), (1000.0, 0.0), (0.0, 1000.0), (1000.0, 1000.0), (500.0, 250.0) };
        var pairs = cadPts.Select(c => (cx: c.Item1, cy: c.Item2, rx: xf0.Apply(c.Item1, c.Item2)[0], ry: xf0.Apply(c.Item1, c.Item2)[1])).ToArray();

        var fit = TransformFitter.FitAffine(pairs);
        Assert.True(fit.RmsM < 1e-9);
        Assert.Equal(0.001, fit.Transform.M11, 9);
        Assert.Equal(0.0002, fit.Transform.M12, 9);
        Assert.Equal(3, fit.Transform.Tx, 6);
    }

    [Fact]
    public void TransformInverseRoundTrips()
    {
        var xf = CadTransform.FromSimilarity(0.001, 30, 12, -4);
        var q = xf.Apply(137422.6, -5611.5);
        var back = xf.ApplyInverse(q[0], q[1]);
        Assert.NotNull(back);
        Assert.Equal(137422.6, back![0], 6);
        Assert.Equal(-5611.5, back[1], 6);
    }

    [Fact]
    public void TransformToolCalibratesPersistsAndApplies()
    {
        var calibPath = Path.Combine(Path.GetTempPath(), $"forge_xf_{Guid.NewGuid():N}.json");
        try
        {
            // Calibrate + persist (scale 0.001 + translation).
            var xf0 = CadTransform.FromSimilarity(0.001, 0, 100, 50);
            var pairs = new[]
            {
                new TransformPairDto { Cad = [1000, 2000], Revit = xf0.Apply(1000, 2000), Label = "grid A/1" },
                new TransformPairDto { Cad = [9000, 2000], Revit = xf0.Apply(9000, 2000), Label = "grid A/9" }
            };
            var cal = CoordTransformTool.Execute(new ForgeCommand
            {
                Tool = "forge_linework_transform",
                Args = ForgeJson.ToElement(new { pairs, transformPath = calibPath, name = "test-calib" })
            });
            Assert.True(cal.Ok, JsonSerializer.Serialize(cal.Error));
            Assert.True(File.Exists(calibPath));

            // Apply using the persisted file.
            var app = CoordTransformTool.Execute(new ForgeCommand
            {
                Tool = "forge_linework_transform",
                Args = ForgeJson.ToElement(new { transformPath = calibPath, points = ApplyPoints })
            });
            Assert.True(app.Ok, JsonSerializer.Serialize(app.Error));
            var json = JsonSerializer.Serialize(app.Data, ForgeJson.Options);
            using var doc = JsonDocument.Parse(json);
            var pt = doc.RootElement.GetProperty("transformedPoints")[0];
            var expected = xf0.Apply(5000, 2000);
            Assert.Equal(expected[0], pt[0].GetDouble(), 6);
            Assert.Equal(expected[1], pt[1].GetDouble(), 6);
        }
        finally
        {
            File.Delete(calibPath);
        }
    }

    [Fact]
    public void TransformToolRejectsBadArgs()
    {
        var missing = CoordTransformTool.Execute(new ForgeCommand
        {
            Tool = "forge_linework_transform",
            Args = ForgeJson.ToElement(new { points = RejectPoints })
        });
        Assert.False(missing.Ok);
        Assert.Equal("missing_transform", missing.Error!.Code);

        var affineShort = CoordTransformTool.Execute(new ForgeCommand
        {
            Tool = "forge_linework_transform",
            Args = ForgeJson.ToElement(new
            {
                affine = true,
                pairs = new[]
                {
                    new { cad = Origin2, revit = Origin2 },
                    new { cad = XUnit, revit = XUnit }
                }
            })
        });
        Assert.False(affineShort.Ok);
        Assert.Equal("insufficient_pairs", affineShort.Error!.Code);
    }

    // ---------- layer suffix / match modes ----------

    [Fact]
    public void LayerSuffixMatchesBareXrefLayerName()
    {
        var ents = Parse(
            "L|20260724_BF floor plans$0$A-Drainage-Pipe|0,0,0|100,0,0",
            "L|M-P-WD|0,0,0|100,0,0");
        var res = LineworkQuery.Apply(ents, new LineworkQueryArgs { LayerSuffix = "A-Drainage-Pipe" });
        var e = Assert.Single(res.Entities);
        Assert.EndsWith("A-Drainage-Pipe", e.Layer);
        Assert.Equal(DrainageSuffixPattern, res.SuffixPatterns);
        Assert.Empty(res.IncludePatterns);
    }

    [Fact]
    public void LayerMatchModesAreExplicit()
    {
        var xrefLayer = "20260724_BF floor plans$0$A-Drainage-Pipe";
        // suffix: endswith on bare name — xref-safe
        Assert.True(new LayerMatcher("A-Drainage-Pipe", "suffix").Matches(xrefLayer));
        // prefix: startswith on FULL name — the legacy trap, must NOT match
        Assert.False(new LayerMatcher("A-Drainage-Pipe", "prefix").Matches(xrefLayer));
        Assert.True(new LayerMatcher("20260724_BF floor plans", "prefix").Matches(xrefLayer));
        // exact: full or tail equality
        Assert.True(new LayerMatcher("A-Drainage-Pipe", "exact").Matches(xrefLayer));
        Assert.False(new LayerMatcher("Drainage-Pipe", "exact").Matches(xrefLayer));
        // substring: contains anywhere
        Assert.True(new LayerMatcher("Drainage", "substring").Matches(xrefLayer));
        // unknown mode strings fall back to auto
        Assert.Equal("auto", LayerMatcher.ModeName("bogus"));
    }

    // ---------- segments tool ----------

    [Fact]
    public void SegmentsFlattensToStableRows()
    {
        var dump = WriteTempDump(
            "PL|M-P-WD|0,0;1000,0;1000,500;",
            "L|M-P-KD|2000,0,0|3000,0,0");
        try
        {
            var res = InvokeLocal("forge_linework_segments", new { source = "dumpFile", dumpPath = dump, units = "meters", layerFilter = "M-P-*" });
            Assert.True(res.Ok, JsonSerializer.Serialize(res.Error));
            var json = JsonSerializer.Serialize(res.Data, ForgeJson.Options);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            Assert.Equal("meters", root.GetProperty("coordinateUnits").GetString());
            var segs = root.GetProperty("segments");
            Assert.Equal(3, segs.GetArrayLength());
            var s0 = segs[0];
            Assert.Equal(0, s0.GetProperty("seg").GetInt32());
            Assert.Equal("M-P-WD", s0.GetProperty("layer").GetString());
            Assert.Equal(1, s0.GetProperty("len").GetDouble(), 6);      // 1000 mm -> 1 m
            Assert.Equal(1, s0.GetProperty("lenM").GetDouble(), 6);
            Assert.Equal(1.0, s0.GetProperty("e")[0].GetDouble(), 6); // x2 = 1000 mm -> 1 m
            Assert.Equal("dumpfile", s0.GetProperty("container").GetString());
        }
        finally
        {
            File.Delete(dump);
        }
    }

    [Fact]
    public void SegmentsEmitsTransformedEndpoints()
    {
        var dump = WriteTempDump("L|M-P-WD|0,0,0|1000,0,0");
        try
        {
            var res = InvokeLocal("forge_linework_segments", new
            {
                source = "dumpFile",
                dumpPath = dump,
                layerFilter = "M-P-*",
                transform = new { scale = 0.001, tx = 100.0 }
            });
            Assert.True(res.Ok, JsonSerializer.Serialize(res.Error));
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(res.Data, ForgeJson.Options));
            var s0 = doc.RootElement.GetProperty("segments")[0];
            Assert.True(doc.RootElement.GetProperty("transformApplied").GetBoolean());
            Assert.Equal(100.0, s0.GetProperty("sT")[0].GetDouble(), 6);   // 0*0.001 + 100
            Assert.Equal(101.0, s0.GetProperty("eT")[0].GetDouble(), 6);  // 1000*0.001 + 100
        }
        finally
        {
            File.Delete(dump);
        }
    }

    // ---------- compare engine ----------

    private static CompareReport Compare(
        IReadOnlyList<LineworkEntity> ents,
        IReadOnlyList<ModelSeg> model,
        CadTransform? xf = null,
        double tol = 0.3,
        double? minZ = null,
        double? maxZ = null)
    {
        var segs = LineworkFlattener.Flatten(ents);
        return CompareEngine.Compare(ents, segs, model, xf, unitsPerMeter: 1000, modelUnitsPerMeter: 1,
            toleranceMeters: tol, stepMeters: 0.5, minCoverageFraction: 0.5, minZ, maxZ, searchRadiusMeters: 50);
    }

    [Fact]
    public void CompareClassifiesMatchedMissingAndExtra()
    {
        var ents = Parse(
            "L|M-P-WD|0,0,0|10000,0,0",        // 10 m — modeled
            "L|M-P-WD|0,5000,0|10000,5000,0"); // 10 m parallel — no model -> missing_in_revit
        var model = new List<ModelSeg>
        {
            new(0, 0, 10, 0, null, null, "p1"),
            new(50, 50, 60, 50, null, null, "p-extra")
        };

        var rep = Compare(ents, model);
        Assert.Equal(2, rep.CadSegCount);
        Assert.Equal(1, rep.MatchedCad);
        Assert.Equal(1, rep.MissingInRevit);
        Assert.Equal(1, rep.MatchedModel);
        Assert.Equal(1, rep.ExtraOffCad);
        Assert.Single(rep.Pairs);
        Assert.Equal("p1", rep.Pairs[0].RevitId);
        Assert.True(rep.Pairs[0].DistanceM < 0.01);

        var missing = rep.CadItems.Single(i => i.Classification == "missing_in_revit");
        Assert.Equal(5.0, missing.S[1], 6);                       // y = 5000 mm -> 5 m
        Assert.True(missing.DistanceM > 1);                        // nearest model is far
        var extra = rep.ModelItems.Single(i => i.Classification == "extra_off_cad");
        Assert.Equal("p-extra", extra.Id);
    }

    [Fact]
    public void CompareFlagsPartialCoverage()
    {
        var ents = Parse("L|M-P-WD|0,0,0|10000,0,0"); // 10 m
        var model = new List<ModelSeg> { new(0, 0, 4, 0, null, null, "p-half") }; // only first 4 m

        var rep = Compare(ents, model);
        var item = Assert.Single(rep.CadItems);
        Assert.Equal("partial", item.Classification);
        Assert.InRange(item.CoverageFrac ?? 0, 0.3, 0.6);
        Assert.Equal(0, rep.PairCount); // partial is not a matched pair
    }

    [Fact]
    public void CompareHonorsZFilter()
    {
        var ents = Parse("L|M-P-WD|0,0,0|10000,0,0");
        var model = new List<ModelSeg>
        {
            new(0, 0, 10, 0, -4.6, -4.6, "bf-pipe"),   // at BF level
            new(0, 0, 10, 0, 3.2, 3.2, "l1-pipe")     // at L1 level
        };

        var all = Compare(ents, model);
        Assert.Equal(2, all.ModelSegCount);

        var bfOnly = Compare(ents, model, minZ: -5.0, maxZ: -4.0);
        Assert.Equal(1, bfOnly.ModelSegCount);
        Assert.Equal(1, bfOnly.SkippedModelSegs);
        Assert.Equal("bf-pipe", bfOnly.ModelItems[0].Id);
    }

    [Fact]
    public void CompareAppliesCalibratedTransform()
    {
        var ents = Parse("L|M-P-WD|0,0,0|10000,0,0"); // 10 m along +x in mm
        // Transform: mm -> m + offset (100, 50)
        var xf = CadTransform.FromSimilarity(0.001, 0, 100, 50);
        var model = new List<ModelSeg> { new(100, 50, 110, 50, null, null, "p1") };

        var rep = Compare(ents, model, xf);
        var item = Assert.Single(rep.CadItems);
        Assert.Equal("matched", item.Classification);
        Assert.Equal(100.0, item.S[0], 6);   // compare-space endpoints are transformed
        Assert.Equal(50.0, item.S[1], 6);
        Assert.Equal(0.0, item.SCad[0], 6);  // original drawing units kept
        Assert.Equal(10000.0, item.ECad[0], 6);
    }

    // ---------- compare end-to-end (dumpFile) + overlay ----------

    [Fact]
    public void CompareEndToEndWritesJsonAndSvg()
    {
        var dump = WriteTempDump(
            "L|M-P-WD|0,0,0|10000,0,0",
            "L|M-P-WD|0,5000,0|10000,5000,0");
        var outJson = Path.Combine(Path.GetTempPath(), $"forge_cmp_{Guid.NewGuid():N}.json");
        var outSvg = Path.Combine(Path.GetTempPath(), $"forge_cmp_{Guid.NewGuid():N}.svg");
        try
        {
            var res = InvokeLocal("forge_linework_compare", new
            {
                source = "dumpFile",
                dumpPath = dump,
                layerFilter = "M-P-*",
                modelSegments = new[]
                {
                    new { id = 42, x1 = 0.0, y1 = 0.0, x2 = 10.0, y2 = 0.0 }
                },
                outputPath = outJson,
                overlayPath = outSvg
            });
            Assert.True(res.Ok, JsonSerializer.Serialize(res.Error));
            Assert.True(File.Exists(outJson));
            Assert.True(File.Exists(outSvg));

            using var doc = JsonDocument.Parse(File.ReadAllText(outJson));
            var report = doc.RootElement.GetProperty("report");
            Assert.Equal(2, report.GetProperty("cadSegCount").GetInt32());
            Assert.Equal(1, report.GetProperty("missingInRevit").GetInt32());
            var items = report.GetProperty("cadItems");
            Assert.Equal("matched", items[0].GetProperty("classification").GetString());
            Assert.Equal("missing_in_revit", items[1].GetProperty("classification").GetString());
            Assert.Equal("42", items[0].GetProperty("revitId").GetString());

            var svg = File.ReadAllText(outSvg);
            Assert.Contains("<svg", svg);
            Assert.Contains(LineworkSvg.ColorMissing, svg);
            Assert.Contains(LineworkSvg.ColorMatched, svg);
            Assert.Contains("missing_in_revit", svg);
        }
        finally
        {
            File.Delete(dump);
            File.Delete(outJson);
            File.Delete(outSvg);
        }
    }

    [Fact]
    public void CompareRejectsMissingModelSegments()
    {
        var dump = WriteTempDump("L|M-P-WD|0,0,0|1000,0,0");
        try
        {
            var res = InvokeLocal("forge_linework_compare", new { source = "dumpFile", dumpPath = dump, layerFilter = "M-P-*" });
            Assert.False(res.Ok);
            Assert.Equal("missing_model_segments", res.Error!.Code);
        }
        finally
        {
            File.Delete(dump);
        }
    }

    // ---------- real data (gated on evidence files) ----------

    [LineworkEvidenceModelFact]
    public void RealCompareProducesStableReport()
    {
        var res = InvokeLocal("forge_linework_compare", new
        {
            source = "dumpFile",
            dumpPath = LineworkEvidence.DumpPath,
            layerFilter = "M-P-*",
            modelSegmentsPath = LineworkEvidence.ModelPath,
            toleranceMeters = 0.3
        });
        Assert.True(res.Ok, JsonSerializer.Serialize(res.Error));
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(res.Data, ForgeJson.Options));
        var report = doc.RootElement.GetProperty("report");
        Assert.True(report.GetProperty("cadSegCount").GetInt32() > 100);
        Assert.True(report.GetProperty("modelSegCount").GetInt32() > 1000);
        var items = report.GetProperty("cadItems");
        Assert.True(items.GetArrayLength() > 100);
        // classifications are one of the stable enum values
        foreach (var item in items.EnumerateArray())
        {
            Assert.Contains(item.GetProperty("classification").GetString(),
                CadClassifications);
        }

        foreach (var item in report.GetProperty("modelItems").EnumerateArray())
        {
            Assert.Contains(item.GetProperty("classification").GetString(),
                ModelClassifications);
        }
    }
}
