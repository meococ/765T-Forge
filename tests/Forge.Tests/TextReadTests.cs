using Forge.Shared;

namespace Forge.Tests;

/// <summary>forge_text_list (block-aware text read): type filter, layer wildcards and world text metrics.</summary>
public sealed class TextReadTests
{
    [Fact]
    public void KindsDefaultToEverything()
    {
        Assert.True(TextRead.TryParseKinds(null, out var kinds, out _));
        Assert.Equal(TextKinds.Default, kinds);
        Assert.True(TextRead.TryParseKinds(" ", out kinds, out _));
        Assert.Equal(TextKinds.Default, kinds);
    }

    [Fact]
    public void KindsAcceptAliasesAndSeparators()
    {
        Assert.True(TextRead.TryParseKinds("DBText, MTEXT;attribute multileader", out var kinds, out var error));
        Assert.Null(error);
        Assert.Equal(TextKinds.Text | TextKinds.MText | TextKinds.Attrib | TextKinds.MLeader, kinds);
        Assert.True(TextRead.TryParseKinds("attdef", out kinds, out _));
        Assert.Equal(TextKinds.AttDef, kinds);
    }

    [Fact]
    public void UnknownKindIsRefusedByName()
    {
        Assert.False(TextRead.TryParseKinds("text,dimension", out _, out var error));
        Assert.Contains("dimension", error);
    }

    [Fact]
    public void LayerPatternsUseWildcardsAndXrefTails()
    {
        Assert.True(TextRead.LayerMatches(null, "anything"));
        Assert.True(TextRead.LayerMatches("A-ANNO-*", "a-anno-text"));
        Assert.True(TextRead.LayerMatches("A-WALL,A-DOOR", "A-DOOR"));
        Assert.True(TextRead.LayerMatches("A-ROOM-?", "A-ROOM-1"));
        Assert.False(TextRead.LayerMatches("A-ROOM-?", "A-ROOM-12"));

        // Xref-dependent names match on the tail after '|' (or the bound '$0$' form).
        Assert.True(TextRead.LayerMatches("A-ANNO", "ARCH-BASE|A-ANNO"));
        Assert.True(TextRead.LayerMatches("A-ANNO", "ARCH-BASE$0$A-ANNO"));
        Assert.True(TextRead.LayerMatches("ARCH-BASE|*", "ARCH-BASE|A-ANNO"));

        // Any of the given layers (own layer or effective layer) may match.
        Assert.True(TextRead.LayerMatches("A-DOOR", "0", "A-DOOR"));
        Assert.False(TextRead.LayerMatches("A-DOOR", "0", null));
    }

    [Fact]
    public void ClampsResultCount()
    {
        Assert.Equal(TextRead.DefaultMaxResults, TextRead.ClampMaxResults(null));
        Assert.Equal(1, TextRead.ClampMaxResults(0));
        Assert.Equal(TextRead.HardMaxResults, TextRead.ClampMaxResults(1_000_000));
    }

    [Fact]
    public void WorldMetricsFollowTheInsert()
    {
        // Text rotated 10° inside a block inserted at 30° with scale 50 → reads at 40°, 50x taller.
        var xf = AffineXf.Insert(1000, 0, 30 * Math.PI / 180, 50, 50);
        var (rot, height, mirrored) = TextRead.WorldMetrics(xf, 10 * Math.PI / 180, 2.5);
        Assert.Equal(40.0, rot, 6);
        Assert.Equal(125.0, height, 9);
        Assert.False(mirrored);
    }

    [Fact]
    public void WorldMetricsReportMirrorAndNormalizeAngle()
    {
        var mirror = AffineXf.Insert(0, 0, 0, -1, 1);
        var (rot, height, mirrored) = TextRead.WorldMetrics(mirror, 0, 3);
        Assert.Equal(180.0, rot, 6);
        Assert.Equal(3.0, height, 9);
        Assert.True(mirrored);

        var (negative, _, _) = TextRead.WorldMetrics(AffineXf.Identity, -Math.PI / 2, 1);
        Assert.Equal(270.0, negative, 6);
    }

    [Fact]
    public void NonUniformScaleUsesTheUpAxisForHeight()
    {
        // X scale 4, Y scale 0.5: a horizontal text's height follows Y.
        var xf = AffineXf.Insert(0, 0, 0, 4, 0.5);
        var (_, height, _) = TextRead.WorldMetrics(xf, 0, 2);
        Assert.Equal(1.0, height, 9);
    }
}
