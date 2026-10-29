using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// Block-aware traversal (forge_linework_dump / forge_entity_list includeBlocks, forge_text_list):
/// the pure transform maths and the descend policy. The AutoCAD walk itself feeds these.
/// </summary>
public sealed class BlockWalkTests
{
    private const double Tol = 1e-9;

    private static void Near(double expected, double actual) => Assert.InRange(actual, expected - Tol, expected + Tol);

    [Fact]
    public void InsertAppliesScaleThenRotationThenTranslation()
    {
        var xf = AffineXf.Insert(100, 50, Math.PI / 2, 2, 3);
        var (x, y, _) = xf.Apply(1, 0);
        Near(100, x);
        Near(52, y); // (1,0) scaled to (2,0), rotated 90° to (0,2), moved by (100,50)

        var (x2, y2, _) = xf.Apply(0, 1);
        Near(97, x2); // (0,1) scaled to (0,3), rotated to (-3,0)
        Near(50, y2);
    }

    [Fact]
    public void NestedInsertsComposeParentAfterChild()
    {
        // A door block (child) inserted at (10,0) inside a unit block placed at (1000,2000) rotated 90°.
        var parent = AffineXf.Insert(1000, 2000, Math.PI / 2);
        var child = AffineXf.Insert(10, 0, 0);
        var world = parent.Then(child);

        var (x, y, _) = world.Apply(1, 0);
        // child: (11,0); parent rotates to (0,11) and moves to (1000,2011)
        Near(1000, x);
        Near(2011, y);

        // Same as applying child then parent by hand.
        var (cx, cy, cz) = child.Apply(1, 0);
        var (px, py, _) = parent.Apply(cx, cy, cz);
        Near(px, x);
        Near(py, y);
    }

    [Fact]
    public void IdentityIsDetectedAndNeutral()
    {
        Assert.True(AffineXf.Identity.IsIdentity);
        Assert.False(AffineXf.Insert(1, 0, 0).IsIdentity);
        var xf = AffineXf.Insert(5, 6, 0.3, 2, 2);
        Assert.Equal(xf, AffineXf.Identity.Then(xf));
        Assert.Equal(xf, xf.Then(AffineXf.Identity));
    }

    [Fact]
    public void RowMajorMatrixRoundTrips()
    {
        var m = new double[] { 1, 0, 0, 7, 0, 1, 0, 8, 0, 0, 1, 9, 0, 0, 0, 1 };
        var xf = AffineXf.FromRowMajor16(m);
        var (x, y, z) = xf.Apply(1, 2, 3);
        Assert.Equal((8.0, 10.0, 12.0), (x, y, z));
        Assert.Throws<ArgumentException>(() => AffineXf.FromRowMajor16([1, 2, 3]));
    }

    [Fact]
    public void MirroredInsertIsReported()
    {
        Assert.True(AffineXf.Insert(0, 0, 0, -1, 1).IsMirrored2D);
        Assert.False(AffineXf.Insert(0, 0, 0, -1, -1).IsMirrored2D); // -1,-1 is a 180° rotation, not a mirror
        Assert.False(AffineXf.Insert(0, 0, 1.2, 2, 2).IsMirrored2D);
    }

    [Fact]
    public void AnglesAndLengthsTransform()
    {
        var xf = AffineXf.Insert(0, 0, Math.PI / 6, 2, 2);
        Near(Math.PI / 6, xf.TransformAngle(0));
        Near(2, xf.ScaleAlong(0));
        Near(2, xf.ScaleAlong(Math.PI / 2));

        var stretched = AffineXf.Insert(0, 0, 0, 4, 0.5);
        Near(4, stretched.ScaleAlong(0));
        Near(0.5, stretched.ScaleAlong(Math.PI / 2));
    }

    [Fact]
    public void LocalBoxBecomesConservativeWorldBox()
    {
        var xf = AffineXf.Insert(10, 10, Math.PI / 4);
        var box = xf.TransformBox(0, 0, 0, 2, 2, 0);
        var h = Math.Sqrt(2);
        Near(10 - h, box[0]);
        Near(10, box[1]);
        Near(10 + h, box[2]);
        Near(10 + 2 * h, box[3]);
    }

    [Fact]
    public void DepthIsClampedToSaneBounds()
    {
        Assert.Equal(BlockWalk.DefaultMaxDepth, BlockWalk.ClampDepth(null));
        Assert.Equal(1, BlockWalk.ClampDepth(0));
        Assert.Equal(1, BlockWalk.ClampDepth(-4));
        Assert.Equal(3, BlockWalk.ClampDepth(3));
        Assert.Equal(BlockWalk.HardMaxDepth, BlockWalk.ClampDepth(1000));
    }

    [Fact]
    public void DescendPolicyStopsAtDepthCycleBboxAndXref()
    {
        var none = Array.Empty<string>();
        var filter = new double[] { 0, 0, 100, 100 };

        Assert.Equal(BlockDescendDecision.Descend, BlockWalk.ShouldDescend(0, 1, null, null, none, "A", false, true));
        // A model-space INSERT (depth 0) with maxDepth 1 is opened; an INSERT inside it (depth 1) is not.
        Assert.Equal(BlockDescendDecision.DepthLimit, BlockWalk.ShouldDescend(1, 1, null, null, none, "B", false, true));

        Assert.Equal(BlockDescendDecision.Cycle, BlockWalk.ShouldDescend(1, 8, null, null, ["A", "B"], "A", false, true));

        Assert.Equal(BlockDescendDecision.OutsideBbox, BlockWalk.ShouldDescend(0, 8, [200, 200, 300, 300], filter, none, "A", false, true));
        Assert.Equal(BlockDescendDecision.Descend, BlockWalk.ShouldDescend(0, 8, [90, 90, 300, 300], filter, none, "A", false, true));
        // Unknown INSERT extents never prune.
        Assert.Equal(BlockDescendDecision.Descend, BlockWalk.ShouldDescend(0, 8, null, filter, none, "A", false, true));

        Assert.Equal(BlockDescendDecision.Xref, BlockWalk.ShouldDescend(0, 8, null, null, none, "X", true, false));
        Assert.Equal(BlockDescendDecision.Descend, BlockWalk.ShouldDescend(0, 8, null, null, none, "X", true, true));
    }

    [Fact]
    public void StatsCountSkipReasons()
    {
        var stats = new BlockWalkStats();
        stats.Count(BlockDescendDecision.DepthLimit);
        stats.Count(BlockDescendDecision.DepthLimit);
        stats.Count(BlockDescendDecision.OutsideBbox);
        stats.Count(BlockDescendDecision.Cycle);
        stats.Count(BlockDescendDecision.Xref);
        stats.Count(BlockDescendDecision.Descend);
        Assert.Equal(2, stats.SkippedDepthLimit);
        Assert.Equal(1, stats.SkippedOutsideBbox);
        Assert.Equal(1, stats.SkippedCycle);
        Assert.Equal(1, stats.SkippedXref);
    }

    [Fact]
    public void BoxAndPointTestsAreInclusive()
    {
        Assert.True(BlockWalk.BoxesIntersect([0, 0, 10, 10], [10, 10, 20, 20]));
        Assert.False(BlockWalk.BoxesIntersect([0, 0, 10, 10], [10.01, 0, 20, 10]));
        Assert.True(BlockWalk.PointInBox(5, 5, [0, 0, 10, 10]));
        Assert.False(BlockWalk.PointInBox(11, 5, [0, 0, 10, 10]));
        Assert.True(BlockWalk.PointInBox(11, 5, null));
    }

    [Fact]
    public void LayerZeroInheritsTheInsertLayer()
    {
        Assert.Equal("A-DOOR", BlockWalk.EffectiveLayer("0", "A-DOOR"));
        Assert.Equal("A-GLAZ", BlockWalk.EffectiveLayer("A-GLAZ", "A-DOOR"));
        Assert.Equal("0", BlockWalk.EffectiveLayer("0", null));
        // Chained: layer 0 inside layer 0 inside an INSERT on A-WALL shows on A-WALL.
        Assert.Equal("A-WALL", BlockWalk.EffectiveLayer("0", BlockWalk.EffectiveLayer("0", "A-WALL")));
    }

    [Fact]
    public void PathLabelsAreStable()
    {
        Assert.Equal("modelspace", BlockWalk.ContainerLabel([]));
        var path = new List<BlockPathStep> { new("1F2A", "UNIT-A"), new("2B07", "DOOR-900") };
        Assert.Equal("block:UNIT-A>DOOR-900", BlockWalk.ContainerLabel(path));
        Assert.Equal("1F2A>2B07", BlockWalk.InsertPathLabel(path));
        Assert.Equal("2B07", BlockWalk.FormatHandle(0x2B07));
    }

    [Theory]
    [InlineData(0.0, 2)]
    [InlineData(Math.PI / 12, 2)]
    [InlineData(Math.PI / 2, 6)]
    [InlineData(Math.PI * 2, 24)]
    [InlineData(Math.PI * 5, 24)]
    [InlineData(-Math.PI, 12)]
    public void ArcSamplingFollowsSweep(double sweep, int expected)
    {
        Assert.Equal(expected, BlockWalk.ArcSampleCount(sweep));
    }
}
