using Forge.Shared;

namespace Forge.Tests;

public sealed class CadSpatialMathTests
{
    [Fact]
    public void StripMTextFormatting_RemovesControlCodesAndDecodesSpecialSymbols()
    {
        var raw = @"{\fArial|b1|i0;\H2.5x;\C1;TRỤC \LA-1\l\P%%c20 @ 150\S+5^-2; %%d %%p}";
        var plain = CadSpatialMath.StripMTextFormatting(raw);

        Assert.Equal("TRỤC A-1\nØ20 @ 150+5/-2 ° ±", plain);
    }

    [Fact]
    public void MatchesTextQuery_SupportsSubstringExactAndRegex()
    {
        Assert.True(CadSpatialMath.MatchesTextQuery("PHÒNG KHÁCH 25m2", null, "phòng khách", caseSensitive: false, exactMatch: false, useRegex: false));
        Assert.False(CadSpatialMath.MatchesTextQuery("PHÒNG KHÁCH 25m2", null, "phòng khách", caseSensitive: true, exactMatch: false, useRegex: false));
        Assert.True(CadSpatialMath.MatchesTextQuery("D1", null, "d1", caseSensitive: false, exactMatch: true, useRegex: false));
        Assert.False(CadSpatialMath.MatchesTextQuery("D10", null, "d1", caseSensitive: false, exactMatch: true, useRegex: false));
        Assert.True(CadSpatialMath.MatchesTextQuery("CỘT C12 - 300x400", null, @"C\d+\s*-\s*\d+x\d+", caseSensitive: false, exactMatch: false, useRegex: true));

        Assert.ThrowsAny<ArgumentException>(() =>
            CadSpatialMath.MatchesTextQuery("abc", null, "[unclosed", caseSensitive: false, exactMatch: false, useRegex: true));
    }

    [Fact]
    public void ComputeLineSegmentSnaps_FindsEndpointMidpointPerpendicularAndNearest()
    {
        var snaps = CadSpatialMath.ComputeLineSegmentSnaps(
            [0.0, 0.0, 0.0],
            [10.0, 0.0, 0.0],
            [4.8, 0.5],
            fromPoint: [3.0, 5.0],
            handle: "10A",
            entityType: "Line",
            layer: "A-WALL",
            enabledKinds: CadSpatialMath.SupportedSnapKinds).ToArray();

        Assert.Contains(snaps, s => s.Kind == "endpoint" && Math.Abs(s.Point[0] - 0.0) < 1e-9);
        Assert.Contains(snaps, s => s.Kind == "endpoint" && Math.Abs(s.Point[0] - 10.0) < 1e-9);
        Assert.Contains(snaps, s => s.Kind == "midpoint" && Math.Abs(s.Point[0] - 5.0) < 1e-9 && Math.Abs(s.Point[1] - 0.0) < 1e-9);
        Assert.Contains(snaps, s => s.Kind == "perpendicular" && Math.Abs(s.Point[0] - 3.0) < 1e-9 && Math.Abs(s.Point[1] - 0.0) < 1e-9);
        Assert.Contains(snaps, s => s.Kind == "nearest" && Math.Abs(s.Point[0] - 4.8) < 1e-9 && Math.Abs(s.Point[1] - 0.0) < 1e-9);
    }

    [Fact]
    public void RankSnapCandidates_PrioritizesExactSnapsBeforeNearestWithinRadius()
    {
        var raw = new[]
        {
            new CadSpatialMath.SnapCandidate("nearest", [4.9, 0.0, 0.0], 0.1, "10A", "Line", "0"),
            new CadSpatialMath.SnapCandidate("midpoint", [5.0, 0.0, 0.0], 0.14, "10A", "Line", "0"),
            new CadSpatialMath.SnapCandidate("endpoint", [0.0, 0.0, 0.0], 4.9, "10A", "Line", "0")
        };

        var ranked = CadSpatialMath.RankSnapCandidates(raw, apertureRadius: 1.0, maxResults: 5);

        Assert.Equal(2, ranked.Length);
        Assert.Equal("midpoint", ranked[0].Kind);
        Assert.Equal("nearest", ranked[1].Kind);
    }

    [Fact]
    public void ComputeCircleOrArcSnaps_RespectsArcSweepForQuadrantsAndMidpoint()
    {
        // Quarter arc in Q1: 0 to 90 degrees, radius 10 at origin
        var snaps = CadSpatialMath.ComputeCircleOrArcSnaps(
            [0.0, 0.0, 0.0],
            radius: 10.0,
            startAngleRad: 0.0,
            endAngleRad: Math.PI * 0.5,
            queryPoint: [7.0, 7.0],
            handle: "20B",
            entityType: "Arc",
            layer: "0",
            enabledKinds: CadSpatialMath.SupportedSnapKinds).ToArray();

        // Only 0 deg and 90 deg quadrants lie on the arc; 180 and 270 do not
        var quadrants = snaps.Where(s => s.Kind == "quadrant").ToArray();
        Assert.Equal(2, quadrants.Length);

        var mid = Assert.Single(snaps.Where(s => s.Kind == "midpoint"));
        Assert.Equal(10.0 * Math.Cos(Math.PI * 0.25), mid.Point[0], 6);
        Assert.Equal(10.0 * Math.Sin(Math.PI * 0.25), mid.Point[1], 6);
    }

    [Fact]
    public void BuildStructuralGrid_GeneratesAxesBubblesDimensionsAndNamedIntersections()
    {
        var plan = CadSpatialMath.BuildStructuralGrid(
            origin: [100.0, 200.0],
            xPositions: null,
            xSpacings: [6000.0, 6000.0],
            xLabels: null,
            yPositions: null,
            ySpacings: [4000.0, 5000.0],
            yLabels: null,
            extensionLength: 1500.0,
            bubbleRadius: 400.0,
            bubbleSides: "both",
            includeDimensions: true);

        // 3 X axes (A, B, C) + 3 Y axes (1, 2, 3) = 6 grid lines
        Assert.Equal(6, plan.GridLines.Length);
        // "both" sides => 2 bubbles per grid line = 12 bubbles
        Assert.Equal(12, plan.Bubbles.Length);
        // 2 X bays + 1 X overall + 2 Y bays + 1 Y overall = 6 dimensions
        Assert.Equal(6, plan.Dimensions.Length);
        // 3 x 3 = 9 named intersections
        Assert.Equal(9, plan.Intersections.Count);
        Assert.Equal([100.0, 200.0], plan.Intersections["A-1"]);
        Assert.Equal([6100.0, 4200.0], plan.Intersections["B-2"]);
        Assert.Equal([12100.0, 9200.0], plan.Intersections["C-3"]);
    }

    [Fact]
    public void BuildDoubleLineWall_MiteredLWallAndDoorOpenings()
    {
        // L-shaped wall: (0,0) -> (5000,0) -> (5000,4000), thickness 200, center justification,
        // with a 900mm door opening on segment 0 starting at 1000mm
        var plan = CadSpatialMath.BuildDoubleLineWall(
            vertices: [[0.0, 0.0], [5000.0, 0.0], [5000.0, 4000.0]],
            thickness: 200.0,
            justification: "center",
            closed: false,
            openings: [new CadSpatialMath.WallOpeningInput(0, 1000.0, 900.0)]);

        // 1 opening splits segment 0 into 2 pieces; the second piece connects continuously around the corner into segment 1
        Assert.Equal(2, plan.ClosedWallLoops.Length);
        var opening = Assert.Single(plan.Openings);
        Assert.Equal(0, opening.SegmentIndex);
        Assert.Equal(900.0, opening.Width, 6);
        Assert.Equal(1450.0, opening.Center[0], 6);
        Assert.Equal(0.0, opening.Center[1], 6);
        Assert.Equal(0.0, opening.AngleDeg, 6);

        // Mitered corner on left rail at vertex 1 should be at (4900, 100) and right rail at (5100, -100)
        Assert.Equal(4900.0, plan.LeftRail[1][0], 6);
        Assert.Equal(100.0, plan.LeftRail[1][1], 6);
        Assert.Equal(5100.0, plan.RightRail[1][0], 6);
        Assert.Equal(-100.0, plan.RightRail[1][1], 6);
    }

    [Fact]
    public void BuildDoubleLineWall_ClosedRoomWithoutOpeningsProducesTwoPerimeterLoops()
    {
        var plan = CadSpatialMath.BuildDoubleLineWall(
            vertices: [[0.0, 0.0], [4000.0, 0.0], [4000.0, 3000.0], [0.0, 3000.0]],
            thickness: 200.0,
            justification: "center",
            closed: true,
            openings: null);

        Assert.Equal(2, plan.ClosedWallLoops.Length);
        Assert.All(plan.ClosedWallLoops, loop => Assert.True(loop.Closed));
    }
}
