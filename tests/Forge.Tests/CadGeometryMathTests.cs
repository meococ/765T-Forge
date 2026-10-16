using Forge.Shared;

namespace Forge.Tests;

public sealed class CadGeometryMathTests
{
    [Fact]
    public void BuildRectangle_Plain_ProducesFourCornersAndZeroBulges()
    {
        var spec = CadGeometryMath.BuildRectangle(100, 50, 0, 0);

        Assert.True(spec.Closed);
        Assert.Equal(4, spec.Vertices.Length);
        Assert.Equal([0.0, 0.0], spec.Vertices[0]);
        Assert.Equal([100.0, 0.0], spec.Vertices[1]);
        Assert.Equal([100.0, 50.0], spec.Vertices[2]);
        Assert.Equal([0.0, 50.0], spec.Vertices[3]);
        Assert.All(spec.Bulges, b => Assert.Equal(0, b));
    }

    [Fact]
    public void BuildRectangle_WithFillet_ProducesEightVerticesWithQuarterCircleBulges()
    {
        var spec = CadGeometryMath.BuildRectangle(0, 0, 100, 60, filletRadius: 10);

        Assert.True(spec.Closed);
        Assert.Equal(8, spec.Vertices.Length);
        var quarterBulge = Math.Tan(Math.PI / 8.0);
        // Alternating straight edge (bulge 0) and 90° CCW corner arc (bulge tan(pi/8))
        Assert.Equal(0, spec.Bulges[0], 6);
        Assert.Equal(quarterBulge, spec.Bulges[1], 6);
        Assert.Equal(0, spec.Bulges[2], 6);
        Assert.Equal(quarterBulge, spec.Bulges[3], 6);
    }

    [Fact]
    public void BuildRectangle_WithChamfer_ProducesEightVerticesWithZeroBulges()
    {
        var spec = CadGeometryMath.BuildRectangle(0, 0, 100, 60, chamferDistance1: 5, chamferDistance2: 8);

        Assert.True(spec.Closed);
        Assert.Equal(8, spec.Vertices.Length);
        Assert.All(spec.Bulges, b => Assert.Equal(0, b));
        Assert.Equal([5.0, 0.0], spec.Vertices[0]);
        Assert.Equal([95.0, 0.0], spec.Vertices[1]);
        Assert.Equal([100.0, 8.0], spec.Vertices[2]);
    }

    [Fact]
    public void BuildRectangle_Rotated90Degrees_RotatesAroundCenter()
    {
        var spec = CadGeometryMath.BuildRectangle(-10, -5, 10, 5, rotationDeg: 90);

        Assert.Equal(4, spec.Vertices.Length);
        // (-10, -5) rotated +90 deg around (0, 0) -> (5, -10)
        Assert.Equal(5.0, spec.Vertices[0][0], 6);
        Assert.Equal(-10.0, spec.Vertices[0][1], 6);
    }

    [Fact]
    public void BuildRegularPolygon_InscribedAndCircumscribedHexagon()
    {
        var inscribed = CadGeometryMath.BuildRegularPolygon(0, 0, sides: 6, radius: 10, inscribed: true);
        Assert.Equal(6, inscribed.Vertices.Length);
        Assert.Equal(10.0, inscribed.Vertices[0][0], 6);
        Assert.Equal(0.0, inscribed.Vertices[0][1], 6);

        var circumscribed = CadGeometryMath.BuildRegularPolygon(0, 0, sides: 6, radius: 10, inscribed: false);
        var expectedVertexRadius = 10.0 / Math.Cos(Math.PI / 6.0);
        var actualVertexRadius = Math.Sqrt(
            circumscribed.Vertices[0][0] * circumscribed.Vertices[0][0] +
            circumscribed.Vertices[0][1] * circumscribed.Vertices[0][1]);
        Assert.Equal(expectedVertexRadius, actualVertexRadius, 6);
    }

    [Fact]
    public void BuildRevCloud_CcwPolygon_UsesPositiveBulgesSoLobesPointOutward()
    {
        var rect = CadGeometryMath.BuildRectangle(0, 0, 100, 50);
        var cloud = CadGeometryMath.BuildRevCloud(rect.Vertices, arcLength: 10);

        Assert.True(cloud.Closed);
        Assert.True(cloud.Vertices.Length >= 20);
        // Positive bulge = arc on the RIGHT of travel; a CCW loop has its exterior on the right.
        Assert.All(cloud.Bulges, b => Assert.True(b > 0.4));
        AssertAllLobeMidpointsOutside(cloud, 0, 0, 100, 50);
    }

    [Fact]
    public void BuildRevCloud_CwPolygon_UsesNegativeBulgesSoLobesStillPointOutward()
    {
        double[][] cw = [[0, 0], [0, 50], [100, 50], [100, 0]];
        var cloud = CadGeometryMath.BuildRevCloud(cw, arcLength: 10);

        Assert.All(cloud.Bulges, b => Assert.True(b < -0.4));
        AssertAllLobeMidpointsOutside(cloud, 0, 0, 100, 50);
    }

    [Fact]
    public void BuildRevCloud_FromCorners_DefaultArcLengthIsPerimeterOver24()
    {
        var cloud = CadGeometryMath.BuildRevCloud(null, [0, 0], [2, 1], 0, 0.52);

        // Perimeter 6 -> default arc length 0.25 (no unit-dependent minimum) -> ~24 lobes.
        Assert.InRange(cloud.Length, 20, 28);
        AssertAllLobeMidpointsOutside(CadGeometryMath.ToSpec(cloud), 0, 0, 2, 1);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.5)]
    public void BuildRevCloud_RejectsOutOfRangeBulge(double bulge)
    {
        Assert.Throws<ArgumentException>(() => CadGeometryMath.BuildRevCloud(null, [0, 0], [10, 10], 0, bulge));
    }

    [Fact]
    public void BuildRectangle_ChamferDistance2Only_ChamfersAllCorners()
    {
        var raw = CadGeometryMath.BuildRectangle([0, 0], [100, 60], null, null, null, chamferDistance2: 5);

        Assert.Equal(8, raw.Length);
        Assert.Equal(5.0, raw[0].X, 6);
        Assert.Equal(0.0, raw[0].Y, 6);
    }

    [Fact]
    public void BuildRectangle_FilletEqualToHalfShortSide_HasNoZeroLengthSegments()
    {
        var raw = CadGeometryMath.BuildRectangle([0, 0], [100, 20], null, null, null, filletRadius: 10);

        // Left/right straight edges vanish: 8 -> 6 vertices, none coincident with its successor.
        Assert.Equal(6, raw.Length);
        for (var i = 0; i < raw.Length; i++)
        {
            var next = raw[(i + 1) % raw.Length];
            Assert.True(Math.Abs(raw[i].X - next.X) + Math.Abs(raw[i].Y - next.Y) > 1e-6);
        }
    }

    [Fact]
    public void BuildRectangle_RejectsFilletWithChamferAndOversizeRadius()
    {
        Assert.Throws<ArgumentException>(() => CadGeometryMath.BuildRectangle([0, 0], [10, 10], null, null, null, filletRadius: 1, chamferDistance1: 1));
        Assert.Throws<ArgumentException>(() => CadGeometryMath.BuildRectangle([0, 0], [10, 10], null, null, null, filletRadius: 6));
        Assert.Throws<ArgumentException>(() => CadGeometryMath.BuildRectangle([0, 0], [0, 10], null, null, null));
    }

    [Fact]
    public void BuildRectangle_CenterWidthHeight_MatchesCornerForm()
    {
        var a = CadGeometryMath.BuildRectangle(null, null, [50, 30], 100, 60);
        var b = CadGeometryMath.BuildRectangle([0, 0], [100, 60], null, null, null);

        Assert.Equal(b.Length, a.Length);
        for (var i = 0; i < a.Length; i++)
        {
            Assert.Equal(b[i].X, a[i].X, 9);
            Assert.Equal(b[i].Y, a[i].Y, 9);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(1025)]
    public void BuildRegularPolygon_RejectsSideCountOutOfRange(int sides)
    {
        Assert.Throws<ArgumentException>(() => CadGeometryMath.BuildRegularPolygon([0, 0], sides, 10));
    }

    [Fact]
    public void BuildRegularPolygon_IsCcwAndRotationMovesFirstVertex()
    {
        var square = CadGeometryMath.BuildRegularPolygon([0, 0], 4, 10, true, 45);

        Assert.Equal(10 * Math.Cos(Math.PI / 4), square[0].X, 9);
        Assert.Equal(10 * Math.Sin(Math.PI / 4), square[0].Y, 9);
        double area2 = 0;
        for (var i = 0; i < square.Length; i++)
        {
            var p = square[i];
            var q = square[(i + 1) % square.Length];
            area2 += p.X * q.Y - q.X * p.Y;
        }

        Assert.True(area2 > 0);
        Assert.Equal(200.0, area2 / 2, 6); // square inscribed in r=10 has side 10*sqrt(2) -> area 200
    }

    /// <summary>
    /// Evaluates each bulged segment's arc midpoint and asserts it lies outside the base box —
    /// the geometric definition of an outward revision-cloud lobe (independent of sign conventions).
    /// </summary>
    private static void AssertAllLobeMidpointsOutside(CadPolylineSpec spec, double minX, double minY, double maxX, double maxY)
    {
        var n = spec.Vertices.Length;
        for (var i = 0; i < n; i++)
        {
            var a = spec.Vertices[i];
            var b = spec.Vertices[(i + 1) % n];
            var bulge = spec.Bulges[i];
            var mx = (a[0] + b[0]) / 2;
            var my = (a[1] + b[1]) / 2;
            var dx = b[0] - a[0];
            var dy = b[1] - a[1];
            // Arc midpoint = chord midpoint offset by sagitta (bulge * chord/2) toward the RIGHT normal (dy, -dx).
            var px = mx + bulge * dy / 2;
            var py = my - bulge * dx / 2;
            var inside = px > minX + 1e-9 && px < maxX - 1e-9 && py > minY + 1e-9 && py < maxY - 1e-9;
            Assert.False(inside, $"Lobe {i} midpoint ({px:F3}, {py:F3}) points inward.");
        }
    }

    [Fact]
    public void ComputeLineFillet_ZeroRadius_TrimsOrExtendsToSharpCorner()
    {
        var res = CadGeometryMath.ComputeLineFillet([0, 0], [8, 0], [10, 2], [10, 12], radius: 0);

        Assert.NotNull(res);
        Assert.False(res!.HasArc);
        Assert.Equal(10.0, res.Line1End[0], 6);
        Assert.Equal(0.0, res.Line1End[1], 6);
        Assert.Equal(10.0, res.Line2Start[0], 6);
        Assert.Equal(0.0, res.Line2Start[1], 6);
    }

    [Fact]
    public void ComputeLineFillet_PositiveRadius_ComputesTangentPointsAndArc()
    {
        // Two orthogonal lines meeting at (0, 0): L1 from (0, 0) to (100, 0), L2 from (0, 0) to (0, 100)
        var res = CadGeometryMath.ComputeLineFillet([0, 0], [100, 0], [0, 0], [0, 100], radius: 15);

        Assert.NotNull(res);
        Assert.True(res.HasArc);
        Assert.NotNull(res.ArcCenter);
        Assert.Equal(15.0, res.ArcRadius, 6);
        Assert.Equal(15.0, res.ArcCenter![0], 6);
        Assert.Equal(15.0, res.ArcCenter[1], 6);
        Assert.Equal(15.0, res.Line1Start[0], 6);
        Assert.Equal(0.0, res.Line1Start[1], 6);
        Assert.Equal(0.0, res.Line2Start[0], 6);
        Assert.Equal(15.0, res.Line2Start[1], 6);
    }

    [Fact]
    public void ComputeLineChamfer_OrthogonalLines_TrimsAndCreatesBevelSegment()
    {
        var res = CadGeometryMath.ComputeLineChamfer([0, 0], [100, 0], [0, 0], [0, 100], distance1: 10, distance2: 20);

        Assert.NotNull(res);
        Assert.True(res.HasChamferSegment);
        Assert.Equal(10.0, res.ChamferStart[0], 6);
        Assert.Equal(0.0, res.ChamferStart[1], 6);
        Assert.Equal(0.0, res.ChamferEnd[0], 6);
        Assert.Equal(20.0, res.ChamferEnd[1], 6);
    }

    [Fact]
    public void FilletPolylineCorners_OpenLShape_ReplacesCornerWithTangentArc()
    {
        double[][] verts = [[0, 0], [50, 0], [50, 50]];
        double[] bulges = [0, 0, 0];

        var filleted = CadGeometryMath.FilletPolylineCorners(verts, bulges, closed: false, radius: 10);

        Assert.False(filleted.Closed);
        Assert.Equal(4, filleted.Vertices.Length);
        Assert.Equal([0.0, 0.0], filleted.Vertices[0]);
        Assert.Equal(40.0, filleted.Vertices[1][0], 6);
        Assert.Equal(0.0, filleted.Vertices[1][1], 6);
        Assert.Equal(Math.Tan(Math.PI / 8.0), filleted.Bulges[1], 6);
        Assert.Equal(50.0, filleted.Vertices[2][0], 6);
        Assert.Equal(10.0, filleted.Vertices[2][1], 6);
        Assert.Equal([50.0, 50.0], filleted.Vertices[3]);
    }
}
