using Forge.Shared;
using Xunit;

namespace Forge.Tests;

public class CadEditMathTests
{
    [Fact]
    public void NormalizeCutDistances_OpenCurve_DropsEndpointsAndMergesNearDuplicates()
    {
        var cuts = CadEditMath.NormalizeCutDistances([0.0, 1e-12, 25.0, 25.0 + 1e-12, 75.0, 100.0], length: 100.0, closed: false);
        Assert.Equal([25.0, 75.0], cuts);
        Assert.Equal(1, CadEditMath.MinimumCuts(closed: false));
    }

    [Fact]
    public void NormalizeCutDistances_ClosedCurve_KeepsBoundaryAndRequiresTwoCuts()
    {
        var cuts = CadEditMath.NormalizeCutDistances([0.0, 100.0, 50.0], length: 100.0, closed: true);
        Assert.Equal([0.0, 50.0], cuts);
        Assert.Equal(2, CadEditMath.MinimumCuts(closed: true));
    }

    [Fact]
    public void SelectPiecesToRemove_OpenAndClosedCurves_RemovesOnlyPickedSegment()
    {
        // Open curve of length 100 cut at 30 and 70 -> 3 segments: [0,30], [30,70], [70,100]
        var openFlags = CadEditMath.SelectPiecesToRemove(
            sortedCuts: [30.0, 70.0],
            length: 100.0,
            closed: false,
            pickDistance: 50.0,
            pieceMidDistances: [15.0, 50.0, 85.0]);
        Assert.Equal([false, true, false], openFlags);

        // Closed curve of length 100 cut at 20 and 60 -> segment 0: [20,60], segment 1 wraps [60..100..20]
        var closedFlags = CadEditMath.SelectPiecesToRemove(
            sortedCuts: [20.0, 60.0],
            length: 100.0,
            closed: true,
            pickDistance: 10.0,
            pieceMidDistances: [40.0, 90.0]);
        Assert.Equal([false, true], closedFlags);
    }

    [Fact]
    public void SelectLineExtension_RejectsBehindAndInterior_SelectsNearestBeyondEnd()
    {
        double[] s = [0, 0, 0];
        double[] e = [10, 0, 0];
        double[][] candidates =
        [
            [-5, 0, 0],  // behind start
            [5, 0, 0],   // interior (would shorten!)
            [10, 0, 0],  // on endpoint
            [20, 0, 0],  // beyond end (extra = 10)
            [15, 0, 0],  // beyond end (extra = 5, nearest)
            [15, 2, 0]   // off-line
        ];

        var endIdx = CadEditMath.SelectLineExtension(s, e, candidates, atEnd: true, out var endExtra);
        Assert.Equal(4, endIdx);
        Assert.Equal(5.0, endExtra, 6);

        var startIdx = CadEditMath.SelectLineExtension(s, e, candidates, atEnd: false, out var startExtra);
        Assert.Equal(0, startIdx);
        Assert.Equal(5.0, startExtra, 6);
    }

    [Fact]
    public void SelectArcExtension_RespectsCcwEndAndCwStartInOpenGap()
    {
        // Quarter-circle arc from 0 to pi/2 (CCW). Gap is (pi/2, 2*pi).
        var startAngle = 0.0;
        var endAngle = Math.PI / 2.0;
        double[] candidates =
        [
            Math.PI / 4.0,       // interior of arc -> rejected
            Math.PI,             // CCW beyond end by pi/2
            3.0 * Math.PI / 4.0, // CCW beyond end by pi/4 (nearest for end)
            7.0 * Math.PI / 4.0  // CW beyond start by pi/4 (nearest for start)
        ];

        var endIdx = CadEditMath.SelectArcExtension(startAngle, endAngle, candidates, atEnd: true, out var endSweep);
        Assert.Equal(2, endIdx);
        Assert.Equal(Math.PI / 4.0, endSweep, 6);

        var startIdx = CadEditMath.SelectArcExtension(startAngle, endAngle, candidates, atEnd: false, out var startSweep);
        Assert.Equal(3, startIdx);
        Assert.Equal(Math.PI / 4.0, startSweep, 6);
    }

    [Fact]
    public void ChooseKeepStart_XCrossingWithoutPick_ThrowsCadAmbiguousSideException()
    {
        // Two crossing lines: L1 (-10,0)->(10,0), L2 (0,-10)->(0,10), intersection at (0,0)
        var ex = Assert.Throws<CadAmbiguousSideException>(() =>
            CadGeometryMath.ComputeLineFillet([-10, 0], [10, 0], [0, -10], [0, 10], radius: 2));
        Assert.Equal(1, ex.LineNumber);

        // Supplying pickPoint1 and pickPoint2 resolves the ambiguity and fillets the positive quadrant
        var res = CadGeometryMath.ComputeLineFillet(
            [-10, 0],
            [10, 0],
            [0, -10],
            [0, 10],
            radius: 2,
            pick1: [8, 0],
            pick2: [0, 8]);
        Assert.True(res.HasArc);
        Assert.Equal(2.0, res.ArcCenter![0], 6);
        Assert.Equal(2.0, res.ArcCenter![1], 6);
        // Positive X side kept on L1 -> tangent at (2,0), far end at (10,0)
        Assert.Equal(2.0, res.Line1Start[0], 6);
        Assert.Equal(10.0, res.Line1End[0], 6);
        // Positive Y side kept on L2 -> tangent at (0,2), far end at (0,10)
        Assert.Equal(2.0, res.Line2Start[1], 6);
        Assert.Equal(10.0, res.Line2End[1], 6);
    }

    [Fact]
    public void FilletPolylineCornersDetailed_ReportsSkippedAndPreservesFullOpenEndSegments()
    {
        // Open L-shape: (0,0) -> (5,0) -> (5,5) with radius 4.
        // Tangent distance for 90 deg is 4. Since segments are open end segments of length 5,
        // full segment length (5 >= 4) is available.
        CadGeometryMath.PolylineVertex2D[] openL =
        [
            new(0, 0, 0),
            new(5, 0, 0),
            new(5, 5, 0)
        ];
        var res = CadGeometryMath.FilletPolylineCornersDetailed(openL, closed: false, radius: 4.0);
        Assert.Equal([1], res.FilletedCorners);
        Assert.Empty(res.SkippedCorners);
        Assert.Equal(4, res.Vertices.Length);

        // Collinear middle vertex + arc segment should be reported in SkippedCorners
        CadGeometryMath.PolylineVertex2D[] withSkips =
        [
            new(0, 0, 0),
            new(5, 0, 0),   // collinear corner (index 1)
            new(10, 0, 0.5),// arc segment starting at index 2 -> corners 2 and 3 skipped
            new(10, 10, 0),
            new(0, 10, 0)
        ];
        var res2 = CadGeometryMath.FilletPolylineCornersDetailed(withSkips, closed: true, radius: 1.0);
        Assert.Contains(res2.SkippedCorners, s => s.Index == 1 && s.Reason == "collinear");
        Assert.Contains(res2.SkippedCorners, s => s.Index == 2 && s.Reason == "arc_segment");
        Assert.Contains(res2.SkippedCorners, s => s.Index == 3 && s.Reason == "arc_segment");
    }

    [Fact]
    public void ComputeLineFillet_AtVn2000Coordinates_WithoutPickPoints_SucceedsWithoutFalseAmbiguousSide()
    {
        // VN2000 coordinates in mm: X ~ 600,000,000, Y ~ 1,200,000,000.
        // Two non-orthogonal lines meeting at corner (cx, cy) without straddling:
        double cx = 600_000_000.0;
        double cy = 1_200_000_000.0;
        double[] l1Start = [cx, cy];
        double[] l1End = [cx + 5000.0, cy + 1234.5];
        double[] l2Start = [cx, cy];
        double[] l2End = [cx - 1500.25, cy + 4800.75];

        var res = CadGeometryMath.ComputeLineFillet(l1Start, l1End, l2Start, l2End, radius: 200.0);
        Assert.True(res.HasArc);
        Assert.NotNull(res.ArcCenter);
        Assert.InRange(res.ArcCenter![0], cx - 1000.0, cx + 1000.0);
        Assert.InRange(res.ArcCenter[1], cy - 1000.0, cy + 1000.0);
    }
}
