using Forge.Shared;

namespace Forge.Tests;

public sealed class AnnotationClashTests
{
    private static ClashText Text(string handle, double cx, double cy, double w, double h, double angle = 0, string text = "T", bool masked = false, string? owner = null, double ownerSize = 0, double? height = null) => new()
    {
        Handle = handle,
        Type = "TEXT",
        Layer = "A-ANNO",
        Text = text,
        Corners = AnnotationClash.RectCorners(cx, cy, w, h, angle),
        Height = height ?? h,
        Masked = masked,
        Owner = owner,
        OwnerSize = ownerSize
    };

    private static ClashCurve Line(string handle, double x1, double y1, double x2, double y2, ClashCurveKind kind = ClashCurveKind.Generic, string layer = "M-DUCT", string? owner = null, double ownerSize = 0) => new()
    {
        Handle = handle,
        Type = "Line",
        Layer = layer,
        Points = [new Vec2(x1, y1), new Vec2(x2, y2)],
        Kind = kind,
        Owner = owner,
        OwnerSize = ownerSize
    };

    [Fact]
    public void RotatedAabbRecoversOrientedBox()
    {
        var angle = 30 * Math.PI / 180;
        var corners = AnnotationClash.RectCorners(5, 7, 40, 4, angle);
        var minX = corners.Min(c => c.X);
        var minY = corners.Min(c => c.Y);
        var maxX = corners.Max(c => c.X);
        var maxY = corners.Max(c => c.Y);

        var recovered = AnnotationClash.FromRotatedAabb(minX, minY, maxX, maxY, angle, 4);
        Assert.Equal(160.0, Math.Abs(AnnotationClash.SignedArea(recovered)), 6);
        var centroid = AnnotationClash.Centroid(recovered);
        Assert.Equal(5, centroid.X, 6);
        Assert.Equal(7, centroid.Y, 6);
    }

    [Fact]
    public void RotatedAabbNear45UsesKnownHeight()
    {
        var angle = Math.PI / 4;
        var corners = AnnotationClash.RectCorners(0, 0, 30, 3, angle);
        var recovered = AnnotationClash.FromRotatedAabb(
            corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y), angle, 3);
        Assert.Equal(90.0, Math.Abs(AnnotationClash.SignedArea(recovered)), 6);
    }

    [Fact]
    public void OverlapAreaOfOffsetSquares()
    {
        var a = AnnotationClash.RectCorners(0, 0, 2, 2, 0);
        var b = AnnotationClash.RectCorners(1, 1, 2, 2, 0);
        var (area, at) = AnnotationClash.OverlapArea(a, b);
        Assert.Equal(1.0, area, 9);
        Assert.Equal(0.5, at.X, 9);
        Assert.Equal(0.5, at.Y, 9);
        Assert.True(AnnotationClash.ConvexOverlap(a, b));
        Assert.False(AnnotationClash.ConvexOverlap(a, AnnotationClash.RectCorners(10, 0, 2, 2, 0)));
    }

    [Fact]
    public void SatSeparatesRotatedBoxesWhoseAabbsOverlap()
    {
        // Two thin diagonal boxes side by side: axis-aligned boxes overlap, oriented boxes do not.
        var angle = Math.PI / 4;
        var a = AnnotationClash.RectCorners(0, 0, 10, 1, angle);
        var b = AnnotationClash.RectCorners(1.5, -1.5, 10, 1, angle);
        Assert.False(AnnotationClash.ConvexOverlap(a, b));
    }

    [Fact]
    public void SegmentInsideMeasuresClippedLength()
    {
        var box = AnnotationClash.EnsureCcw(AnnotationClash.RectCorners(0, 0, 4, 2, 0));
        var (length, mid) = AnnotationClash.SegmentInside(new Vec2(-10, 0), new Vec2(10, 0), box);
        Assert.Equal(4.0, length, 9);
        Assert.Equal(0.0, mid.X, 9);

        var (miss, _) = AnnotationClash.SegmentInside(new Vec2(-10, 5), new Vec2(10, 5), box);
        Assert.Equal(0.0, miss);

        var (partial, _) = AnnotationClash.SegmentInside(new Vec2(1, 0), new Vec2(10, 0), box);
        Assert.Equal(1.0, partial, 9);
    }

    [Fact]
    public void InflateGrowsEveryEdge()
    {
        var box = AnnotationClash.EnsureCcw(AnnotationClash.RectCorners(0, 0, 4, 2, 0));
        var grown = AnnotationClash.Inflate(box, 0.5);
        Assert.Equal(15.0, Math.Abs(AnnotationClash.SignedArea(grown)), 9);
    }

    [Fact]
    public void ReportsOverlappingTextsOnce()
    {
        var texts = new[]
        {
            Text("A1", 0, 0, 10, 2, text: "ROOM 101"),
            Text("B2", 4, 0.5, 10, 2, text: "AHU-1"),
            Text("C3", 50, 50, 10, 2)
        };

        var report = AnnotationClash.Analyze(texts, [], new ClashOptions());
        var finding = Assert.Single(report.Findings);
        Assert.Equal(AnnotationClash.CheckTextOverlap, finding.Check);
        Assert.Equal("error", finding.Severity);
        Assert.Equal(new[] { "A1", "B2" }, finding.Handles);
        Assert.Equal(1, report.Counts[AnnotationClash.CheckTextOverlap]);
    }

    [Fact]
    public void TouchingTextsBelowRatioAreIgnored()
    {
        var texts = new[] { Text("A", 0, 0, 10, 2), Text("B", 9.95, 0, 10, 2) };
        var report = AnnotationClash.Analyze(texts, [], new ClashOptions { MinOverlapRatio = 0.02 });
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void FlagsDuplicateText()
    {
        var texts = new[] { Text("A", 0, 0, 10, 2, text: "SD-1"), Text("B", 0, 0, 10, 2, text: "SD-1") };
        var finding = Assert.Single(AnnotationClash.Analyze(texts, [], new ClashOptions()).Findings);
        Assert.Contains("duplicated", finding.Message);
    }

    [Fact]
    public void LineThroughTextIsACrossingUnlessMasked()
    {
        var curves = new[] { Line("L1", -20, 0, 20, 0) };
        var plain = AnnotationClash.Analyze([Text("T1", 0, 0, 10, 2)], curves, new ClashOptions());
        var crossing = Assert.Single(plain.Findings);
        Assert.Equal(AnnotationClash.CheckTextCrossing, crossing.Check);
        Assert.Equal(10.0, crossing.Overlap!.Value, 6);

        var masked = AnnotationClash.Analyze([Text("T1", 0, 0, 10, 2, masked: true)], curves, new ClashOptions());
        Assert.Empty(masked.Findings);
        Assert.Equal(1, masked.MaskedCrossingsSkipped);

        var forced = AnnotationClash.Analyze([Text("T1", 0, 0, 10, 2, masked: true)], curves, new ClashOptions { ReportMaskedCrossings = true });
        Assert.True(Assert.Single(forced.Findings).Masked);
    }

    [Fact]
    public void GrazingLineBelowToleranceIsIgnored()
    {
        // Clips the top-right corner for ~0.07 units; tolerance is 5% of a 2-unit text height (0.1).
        var curves = new[] { Line("L1", 4.9, 1.05, 5.05, 0.9) };
        var report = AnnotationClash.Analyze([Text("T1", 0, 0, 10, 2)], curves, new ClashOptions());
        Assert.Empty(report.Findings);
    }

    [Fact]
    public void WallThroughTagIsAStraddleEvenWhenMasked()
    {
        var curves = new[] { Line("W1", 0, -20, 0, 20, ClashCurveKind.Wall, "A-WALL") };
        var report = AnnotationClash.Analyze([Text("T1", 0, 0, 10, 2, masked: true)], curves, new ClashOptions());
        var finding = Assert.Single(report.Findings);
        Assert.Equal(AnnotationClash.CheckWallStraddle, finding.Check);
        Assert.Equal("error", finding.Severity);
        Assert.True(finding.Masked);
    }

    [Fact]
    public void WallStraddleOffFallsBackToCrossing()
    {
        var curves = new[] { Line("W1", 0, -20, 0, 20, ClashCurveKind.Wall, "A-WALL") };
        var report = AnnotationClash.Analyze([Text("T1", 0, 0, 10, 2)], curves, new ClashOptions { WallStraddle = false });
        Assert.Equal(AnnotationClash.CheckTextCrossing, Assert.Single(report.Findings).Check);
    }

    [Fact]
    public void TagInsideDoorSwingSectorIsFlaggedWithoutTouchingTheArc()
    {
        // Quarter swing of radius 900 centred at the hinge (0,0), from 0° to 90°.
        var pts = Enumerable.Range(0, 7)
            .Select(i => i * (Math.PI / 2) / 6)
            .Select(a => new Vec2(900 * Math.Cos(a), 900 * Math.Sin(a)))
            .ToArray();
        var swing = new ClashCurve
        {
            Handle = "ARC1",
            Type = "Arc",
            Layer = "A-DOOR",
            Points = pts,
            Kind = ClashCurveKind.DoorSwing,
            SectorApex = new Vec2(0, 0)
        };

        var inside = AnnotationClash.Analyze([Text("TAG", 300, 300, 200, 100)], [swing], new ClashOptions());
        var finding = Assert.Single(inside.Findings);
        Assert.Equal(AnnotationClash.CheckDoorSwing, finding.Check);
        Assert.Contains("inside the door swing", finding.Message);

        var outside = AnnotationClash.Analyze([Text("TAG", 2000, 300, 200, 100)], [swing], new ClashOptions());
        Assert.Empty(outside.Findings);
    }

    [Fact]
    public void OwnLinesOfASmallTagBlockAreIgnoredButAPlanBlockIsNot()
    {
        var circle = Line("C1", -3, 0, 3, 0, owner: "TAGBLOCK", ownerSize: 6);
        var tagText = Text("ATT1", 0, 0, 4, 1, owner: "TAGBLOCK", ownerSize: 6);
        var small = AnnotationClash.Analyze([tagText], [circle], new ClashOptions());
        Assert.Empty(small.Findings);
        Assert.Equal(1, small.SameGroupPairsSkipped);

        var wall = Line("W1", -3, 0, 3, 0, owner: "PLAN", ownerSize: 30000);
        var planText = Text("T1", 0, 0, 4, 1, owner: "PLAN", ownerSize: 30000);
        Assert.Single(AnnotationClash.Analyze([planText], [wall], new ClashOptions()).Findings);
    }

    [Fact]
    public void MinHeightUsesPlotScale()
    {
        // mm drawing at 1:100 → 100 units per paper mm; 150 tall plots 1.5 mm.
        var upm = AnnotationClash.UnitsPerPaperMm(100, 0.001);
        Assert.Equal(100.0, upm, 9);
        Assert.Equal(0.1, AnnotationClash.UnitsPerPaperMm(100, 1.0), 12);

        var options = new ClashOptions { TextOverlap = false, MinHeight = true, MinPlottedHeightMm = 2.0, UnitsPerPaperMm = upm };
        var report = AnnotationClash.Analyze(
            [Text("SMALL", 0, 0, 800, 150, height: 150), Text("OK", 5000, 0, 1200, 250, height: 250)],
            [],
            options);
        var finding = Assert.Single(report.Findings);
        Assert.Equal(AnnotationClash.CheckMinHeight, finding.Check);
        Assert.Equal("SMALL", finding.Handles[0]);
        Assert.Equal(1.5, finding.Overlap!.Value, 6);
    }

    [Fact]
    public void FindingsAreCappedButCountsStayTotal()
    {
        var texts = Enumerable.Range(0, 30).Select(i => Text("T" + i, i * 100, 0, 10, 2)).ToList();
        var curves = new[] { Line("L", -50, 0, 5000, 0) };
        var report = AnnotationClash.Analyze(texts, curves, new ClashOptions { MaxFindings = 5 });
        Assert.Equal(5, report.Findings.Count);
        Assert.True(report.Truncated);
        Assert.Equal(30, report.Counts[AnnotationClash.CheckTextCrossing]);
    }

    [Fact]
    public void ClassifiesDoorSwingsWallsAndGenericLines()
    {
        var doors = WildcardSet.Parse("*DOOR*");
        var doorBlocks = WildcardSet.Parse("DR-*");
        var walls = WildcardSet.Parse("*WALL*");
        Assert.Equal(ClashCurveKind.DoorSwing, AnnotationClash.Classify("A-DOOR", null, isArc: true, doors, doorBlocks, walls));
        Assert.Equal(ClashCurveKind.Generic, AnnotationClash.Classify("A-DOOR", null, isArc: false, doors, doorBlocks, walls));
        Assert.Equal(ClashCurveKind.DoorSwing, AnnotationClash.Classify("0", ["DR-SINGLE"], isArc: true, doors, doorBlocks, walls));
        Assert.Equal(ClashCurveKind.Wall, AnnotationClash.Classify("A-WALL", null, isArc: false, doors, doorBlocks, walls));
        Assert.Equal(ClashCurveKind.Generic, AnnotationClash.Classify("M-DUCT", null, isArc: true, doors, doorBlocks, walls));
    }

    [Fact]
    public void ManyTextsStayFast()
    {
        var texts = new List<ClashText>();
        for (var i = 0; i < 5000; i++)
        {
            texts.Add(Text("T" + i, (i % 100) * 30, (i / 100) * 10, 20, 3));
        }

        var curves = Enumerable.Range(0, 2000).Select(i => Line("L" + i, -10, i * 0.25 + 1.5, 3010, i * 0.25 + 1.5)).ToList();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var report = AnnotationClash.Analyze(texts, curves, new ClashOptions { MaxFindings = 10 });
        watch.Stop();
        Assert.Equal(5000, report.Texts);
        Assert.True(watch.ElapsedMilliseconds < 20000, $"took {watch.ElapsedMilliseconds} ms");
    }
}
