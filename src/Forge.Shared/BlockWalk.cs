using System.Globalization;

namespace Forge.Shared;

/// <summary>
/// 3D affine transform (row-major 3x4: the top three rows of an AutoCAD Matrix3d). The plugin feeds
/// <c>Matrix3d.ToArray()</c> (16 doubles, row-major) through <see cref="FromRowMajor16"/>; everything
/// else here is pure so the block-traversal maths is unit-tested without AutoCAD.
/// </summary>
public readonly record struct AffineXf(
    double M00, double M01, double M02, double M03,
    double M10, double M11, double M12, double M13,
    double M20, double M21, double M22, double M23)
{
    public static AffineXf Identity => new(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0);

    public static AffineXf FromRowMajor16(double[] m)
    {
        if (m is not { Length: >= 12 })
        {
            throw new ArgumentException("Expected at least 12 row-major matrix values.", nameof(m));
        }

        return new AffineXf(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11]);
    }

    /// <summary>2D convenience: translation, CCW rotation (radians), then non-uniform scale (applied first).</summary>
    public static AffineXf Insert(double x, double y, double rotation, double scaleX = 1, double scaleY = 1, double scaleZ = 1)
    {
        var c = Math.Cos(rotation);
        var s = Math.Sin(rotation);
        return new AffineXf(c * scaleX, -s * scaleY, 0, x, s * scaleX, c * scaleY, 0, y, 0, 0, scaleZ, 0);
    }

    /// <summary>this ∘ inner: apply <paramref name="inner"/> first, then this (same order as Matrix3d parent * child).</summary>
    public AffineXf Then(AffineXf inner) => new(
        M00 * inner.M00 + M01 * inner.M10 + M02 * inner.M20,
        M00 * inner.M01 + M01 * inner.M11 + M02 * inner.M21,
        M00 * inner.M02 + M01 * inner.M12 + M02 * inner.M22,
        M00 * inner.M03 + M01 * inner.M13 + M02 * inner.M23 + M03,
        M10 * inner.M00 + M11 * inner.M10 + M12 * inner.M20,
        M10 * inner.M01 + M11 * inner.M11 + M12 * inner.M21,
        M10 * inner.M02 + M11 * inner.M12 + M12 * inner.M22,
        M10 * inner.M03 + M11 * inner.M13 + M12 * inner.M23 + M13,
        M20 * inner.M00 + M21 * inner.M10 + M22 * inner.M20,
        M20 * inner.M01 + M21 * inner.M11 + M22 * inner.M21,
        M20 * inner.M02 + M21 * inner.M12 + M22 * inner.M22,
        M20 * inner.M03 + M21 * inner.M13 + M22 * inner.M23 + M23);

    public (double X, double Y, double Z) Apply(double x, double y, double z = 0) => (
        M00 * x + M01 * y + M02 * z + M03,
        M10 * x + M11 * y + M12 * z + M13,
        M20 * x + M21 * y + M22 * z + M23);

    public Vec2 Apply2D(double x, double y, double z = 0)
    {
        var (wx, wy, _) = Apply(x, y, z);
        return new Vec2(wx, wy);
    }

    /// <summary>Determinant of the XY linear part; negative when the transform mirrors (e.g. a block inserted with X scale -1).</summary>
    public double Det2D => M00 * M11 - M01 * M10;

    public bool IsMirrored2D => Det2D < 0;

    public bool IsIdentity =>
        M00 == 1 && M01 == 0 && M02 == 0 && M03 == 0 &&
        M10 == 0 && M11 == 1 && M12 == 0 && M13 == 0 &&
        M20 == 0 && M21 == 0 && M22 == 1 && M23 == 0;

    /// <summary>World angle (radians) of a local XY direction given by its angle.</summary>
    public double TransformAngle(double localAngle)
    {
        var dx = Math.Cos(localAngle);
        var dy = Math.Sin(localAngle);
        return Math.Atan2(M10 * dx + M11 * dy, M00 * dx + M01 * dy);
    }

    /// <summary>Length factor of a local XY direction (e.g. text height along the text's up vector).</summary>
    public double ScaleAlong(double localAngle)
    {
        var dx = Math.Cos(localAngle);
        var dy = Math.Sin(localAngle);
        var wx = M00 * dx + M01 * dy;
        var wy = M10 * dx + M11 * dy;
        return Math.Sqrt(wx * wx + wy * wy);
    }

    /// <summary>Axis-aligned world box [minX,minY,maxX,maxY] of a local 3D box (all 8 corners transformed — conservative).</summary>
    public double[] TransformBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        double bx0 = double.PositiveInfinity, by0 = double.PositiveInfinity, bx1 = double.NegativeInfinity, by1 = double.NegativeInfinity;
        foreach (var x in new[] { minX, maxX })
        {
            foreach (var y in new[] { minY, maxY })
            {
                foreach (var z in new[] { minZ, maxZ })
                {
                    var (wx, wy, _) = Apply(x, y, z);
                    bx0 = Math.Min(bx0, wx);
                    by0 = Math.Min(by0, wy);
                    bx1 = Math.Max(bx1, wx);
                    by1 = Math.Max(by1, wy);
                }
            }
        }

        return [bx0, by0, bx1, by1];
    }
}

/// <summary>One INSERT on the path from the space being walked down to a nested entity.</summary>
public readonly record struct BlockPathStep(string InsertHandle, string BlockName);

/// <summary>Why a nested INSERT was not descended into (counted in the traversal stats).</summary>
public enum BlockDescendDecision
{
    Descend,
    DepthLimit,
    OutsideBbox,
    Cycle,
    Xref
}

/// <summary>
/// Policy + bookkeeping for block-aware traversal (forge_linework_dump includeBlocks, forge_entity_list
/// includeBlocks, forge_text_list). The plugin owns the AutoCAD walk; the decisions live here.
/// </summary>
public static class BlockWalk
{
    public const int DefaultMaxDepth = 8;
    public const int HardMaxDepth = 32;

    public static int ClampDepth(int? requested) => Math.Clamp(requested ?? DefaultMaxDepth, 1, HardMaxDepth);

    /// <summary>
    /// Decide whether to walk into a nested INSERT. <paramref name="depth"/> is the depth of the
    /// INSERT itself (a model-space INSERT is depth 0; its contents are depth 1).
    /// <paramref name="worldBox"/> is the INSERT's world-space extents, if known.
    /// </summary>
    public static BlockDescendDecision ShouldDescend(
        int depth,
        int maxDepth,
        double[]? worldBox,
        double[]? filterBox,
        IReadOnlyCollection<string> ancestorBlockIds,
        string childBlockId,
        bool isXref,
        bool includeXrefs)
    {
        if (isXref && !includeXrefs)
        {
            return BlockDescendDecision.Xref;
        }

        if (depth + 1 > maxDepth)
        {
            return BlockDescendDecision.DepthLimit;
        }

        if (ancestorBlockIds.Contains(childBlockId))
        {
            return BlockDescendDecision.Cycle;
        }

        if (filterBox is { Length: 4 } && worldBox is { Length: 4 } && !BoxesIntersect(worldBox, filterBox))
        {
            return BlockDescendDecision.OutsideBbox;
        }

        return BlockDescendDecision.Descend;
    }

    public static bool BoxesIntersect(double[] a, double[] b)
        => a[0] <= b[2] && a[2] >= b[0] && a[1] <= b[3] && a[3] >= b[1];

    public static bool PointInBox(double x, double y, double[]? box)
        => box is not { Length: 4 } || (x >= box[0] && x <= box[2] && y >= box[1] && y <= box[3]);

    /// <summary>"blockspace:A>B" style container label used by linework (stable, readable).</summary>
    public static string ContainerLabel(IReadOnlyList<BlockPathStep> path)
        => path.Count == 0 ? "modelspace" : "block:" + string.Join(">", path.Select(p => p.BlockName));

    /// <summary>Insert handles from the outermost INSERT inwards, e.g. "1F2A>2B07".</summary>
    public static string InsertPathLabel(IReadOnlyList<BlockPathStep> path)
        => string.Join(">", path.Select(p => p.InsertHandle));

    /// <summary>
    /// Layer-0 entities inside a block display on the INSERT's layer. Returns the layer an entity
    /// effectively shows on, given its own layer and the effective layer of the INSERT that holds it.
    /// </summary>
    public static string EffectiveLayer(string ownLayer, string? parentEffectiveLayer)
        => parentEffectiveLayer is not null && string.Equals(ownLayer, "0", StringComparison.Ordinal)
            ? parentEffectiveLayer
            : ownLayer;

    /// <summary>Sweep-correct arc chord sampling count used for transformed arcs (matches LineworkGeometry.TessellateArc density).</summary>
    public static int ArcSampleCount(double sweepRadians, double maxStepRad = Math.PI / 12)
    {
        var sweep = Math.Abs(sweepRadians);
        if (sweep > Math.PI * 2)
        {
            sweep = Math.PI * 2;
        }

        return Math.Max(2, (int)Math.Ceiling(sweep / maxStepRad));
    }

    public static string FormatHandle(long value) => value.ToString("X", CultureInfo.InvariantCulture);
}

/// <summary>Counters reported back with every block-aware read so agents can see what the walk skipped.</summary>
public sealed class BlockWalkStats
{
    public int InsertsVisited { get; set; }
    public int MaxDepthReached { get; set; }
    public int SkippedDepthLimit { get; set; }
    public int SkippedOutsideBbox { get; set; }
    public int SkippedCycle { get; set; }
    public int SkippedXref { get; set; }
    public int SkippedInvisible { get; set; }
    public bool Truncated { get; set; }

    public void Count(BlockDescendDecision decision)
    {
        switch (decision)
        {
            case BlockDescendDecision.DepthLimit:
                SkippedDepthLimit++;
                break;
            case BlockDescendDecision.OutsideBbox:
                SkippedOutsideBbox++;
                break;
            case BlockDescendDecision.Cycle:
                SkippedCycle++;
                break;
            case BlockDescendDecision.Xref:
                SkippedXref++;
                break;
        }
    }
}
