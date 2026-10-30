using System.Text;
using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// TOOL-GAP #30: annotation clash sees model content through paper-space viewports (projection
/// and clip maths, pure) and the PDF ink box no longer counts proven-white paints (the full-page
/// white background behind the 2592-pt inkBox x-max vs 35.51" of PNG ink on V-101).
/// </summary>
public sealed class D30ViewportAndInkTests
{
    // ---------- ViewportTransform ----------

    [Fact]
    public void PaperScale_UsesHeightRatio()
    {
        // V-101 ground truth from build-report_R1.json: 15.625 paper height / 750 model = 1/48.
        Assert.Equal(1.0 / 48, ViewportTransform.PaperScale(15.625, 750, 0.020833333333333332), 9);
        Assert.Equal(0.5, ViewportTransform.PaperScale(13.2, 26.4, 0));
    }

    [Fact]
    public void PaperScale_FallsBackToCustomScale()
    {
        Assert.Equal(0.25, ViewportTransform.PaperScale(26.4, 0, 0.25));
        Assert.Equal(0, ViewportTransform.PaperScale(26.4, -1, 0));
    }

    private static AffineXf TopView(
        double cx = 18, double cy = 12, double w = 39.6, double h = 26.4,
        double vcx = 18, double vcy = 12, double vh = 26.4,
        double twist = 0, double tx = 0, double ty = 0, double customScale = 0) =>
        ViewportTransform.ModelToPaper(cx, cy, w, h, vcx, vcy, vh, twist, tx, ty, 0, 0, 0, 1, customScale);

    [Fact]
    public void TopView_ViewCenterMapsToViewportCenter()
    {
        var xf = TopView();
        AssertPoint(18, 12, xf.Apply2D(18, 12));
        AssertPoint(10, 5, xf.Apply2D(10, 5)); // scale 1: identical paper and model coords
    }

    [Fact]
    public void TopView_ScaleApplies()
    {
        var xf = TopView(vh: 52.8); // scale 0.5
        AssertPoint(19, 13, xf.Apply2D(20, 14));
    }

    [Fact]
    public void TopView_TwistRotates()
    {
        // 90° twist: DCS(p) = R(90)·p; paper = c + (dcs − vc): (20,14) → (−14,20).
        AssertPoint(-14, 20, TopView(twist: Math.PI / 2).Apply2D(20, 14));
    }

    [Fact]
    public void TopView_TargetTranslates()
    {
        AssertPoint(20, 14, TopView(tx: 5, ty: 5).Apply2D(25, 19));
    }

    [Fact]
    public void FrontView_UsesArbitraryAxisBasis()
    {
        // Looking along −Y: DCS x = WCS x, DCS y = WCS z. Model (3,0,4) → (18+3, 12+4).
        var xf = ViewportTransform.ModelToPaper(18, 12, 39.6, 26.4, 0, 0, 26.4, 0, 0, 0, 0, 0, -1, 0, 0);
        var (x, y, _) = xf.Apply(3, 0, 4);
        AssertPoint(21, 16, new Vec2(x, y));
    }

    // ---------- ViewportClip ----------

    [Fact]
    public void Polyline_CrossingKeepsInsideRun()
    {
        var runs = ViewportClip.Polyline(
            [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10)],
            closed: false, 4, -1, 8, 11);
        var run = Assert.Single(runs);
        Assert.Equal([new Vec2(4, 0), new Vec2(8, 0)], run);
    }

    [Fact]
    public void Polyline_FullyInsideClosedRing_StaysOneRun()
    {
        var runs = ViewportClip.Polyline(
            [new Vec2(1, 1), new Vec2(3, 1), new Vec2(3, 3), new Vec2(1, 3)],
            closed: true, 0, 0, 5, 5);
        var run = Assert.Single(runs);
        Assert.Equal(5, run.Length);
        Assert.Equal(run[0], run[^1]);
    }

    [Fact]
    public void Polyline_ClosedRingClippedIntoRuns()
    {
        var runs = ViewportClip.Polyline(
            [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 10), new Vec2(0, 10)],
            closed: true, 0, 0, 5, 5);
        Assert.Equal(2, runs.Count);
        Assert.All(runs, r => Assert.Equal(2, r.Length));
    }

    // ---------- proven-white paints are not ink ----------

    private static PdfInkResult Content(string stream) =>
        PdfContentBox.ReadContent(Encoding.Latin1.GetBytes(stream), [0, 0, 1000, 1000]);

    [Fact]
    public void WhiteFullPageFill_IsEmpty()
    {
        // The DWG-to-PDF driver paints the sheet white first (1 1 1 rg … re f) — the operator
        // pair behind the 2592-pt inkBox x-max. Proven white is not ink.
        Assert.Equal(PdfInkStatus.Empty, Content("1 1 1 rg 0 0 1000 1000 re f").Status);
        Assert.Equal(PdfInkStatus.Empty, Content("1 1 1 rg 0 0 1000 1000 re f*").Status);
    }

    [Fact]
    public void WhiteGrayAndCmykFills_AreEmpty()
    {
        Assert.Equal(PdfInkStatus.Empty, Content("1 g 0 0 1000 1000 re f").Status);
        Assert.Equal(PdfInkStatus.Empty, Content("0 0 0 0 k 0 0 1000 1000 re f").Status);
    }

    [Fact]
    public void WhiteBackground_BlackLineWins()
    {
        var r = Content("1 1 1 rg 0 0 1000 1000 re f 0 g 100 100 m 200 200 l S");
        Assert.Equal(PdfInkStatus.Ok, r.Status);
        Assert.Equal(99.5, r.InkBox![0], 9);
        Assert.Equal(200.5, r.InkBox[2], 9);
        Assert.True(r.LooksTiny); // 101-pt line on a 1000-pt page is <50% fill both axes
    }

    [Fact]
    public void WhiteStroke_PaintsNothing_WhiteFillBlackStroke_Paints()
    {
        Assert.Equal(PdfInkStatus.Empty, Content("1 G 100 100 m 200 200 l S").Status);
        Assert.Equal(PdfInkStatus.Ok, Content("1 1 1 rg 0 G 1 w 100 100 m 200 200 l B").Status);
    }

    [Fact]
    public void WhiteText_IsEmpty()
    {
        Assert.Equal(PdfInkStatus.Empty, Content("1 1 1 rg BT /F1 10 Tf 100 100 Td (ABC) Tj ET").Status);
    }

    [Fact]
    public void UnsetOrPatternColors_StillCountAsInk()
    {
        var restored = Content("q 1 1 1 rg Q 0 0 1000 1000 re f"); // back to default black → ink
        Assert.Equal(PdfInkStatus.Ok, restored.Status);
        Assert.Equal(1000, restored.InkBox![2], 6);
        Assert.Equal(PdfInkStatus.Ok, Content("/P1 scn 100 100 m 200 200 l B").Status); // pattern → unproven
    }

    [Fact]
    public void ClipAlone_NeverExpandsBounds()
    {
        Assert.Equal(PdfInkStatus.Empty, Content("0 0 1000 1000 re W n").Status);
        Assert.Equal(PdfInkStatus.Empty, Content("0 0 500 500 re W 1 1 1 rg 0 0 1000 1000 re f").Status);
    }

    [Fact]
    public void ColorSpaceSelection_ResetsToUnproven()
    {
        // cs/CS select a colour SPACE, not a colour: the paint stays ink until proven white.
        Assert.Equal(PdfInkStatus.Ok, Content("1 1 1 rg /DeviceRGB cs 0 0 1000 1000 re f").Status);
    }

    private static void AssertPoint(double x, double y, Vec2 p)
    {
        Assert.Equal(x, p.X, 9);
        Assert.Equal(y, p.Y, 9);
    }
}
