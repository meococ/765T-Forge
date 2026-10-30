using System.Text;
using Forge.Shared;

namespace Forge.Tests;

/// <summary>forge_plot_to_pdf planning: area/scale parsing, media resolution, orientation, fit and the "tiny in a corner" detector.</summary>
public sealed class PlotLayoutMathTests
{
    private static readonly (string Canonical, string? Locale)[] PdfMedia =
    [
        ("ISO_full_bleed_A1_(841.00_x_594.00_MM)", "ISO full bleed A1 (841.00 x 594.00 MM)"),
        ("ISO_A1_(841.00_x_594.00_MM)", "ISO A1 (841.00 x 594.00 MM)"),
        ("ISO_A1_(594.00_x_841.00_MM)", "ISO A1 (594.00 x 841.00 MM)"),
        ("ISO_A3_(420.00_x_297.00_MM)", "ISO A3 (420.00 x 297.00 MM)"),
        ("ANSI_A_(8.50_x_11.00_Inches)", "ANSI A (8.50 x 11.00 Inches)")
    ];

    [Theory]
    [InlineData("layout", PlotAreaKind.Layout)]
    [InlineData("Extents", PlotAreaKind.Extents)]
    [InlineData(" extent ", PlotAreaKind.Extents)]
    [InlineData("display", PlotAreaKind.Display)]
    [InlineData("VIEW", PlotAreaKind.Display)]
    [InlineData("limits", PlotAreaKind.Limits)]
    [InlineData("window", PlotAreaKind.Window)]
    public void AreaWordsParse(string word, PlotAreaKind expected)
    {
        Assert.True(PlotLayoutMath.TryParseArea(word, out var kind));
        Assert.Equal(expected, kind);
    }

    [Fact]
    public void UnknownAreaIsRefused()
    {
        Assert.False(PlotLayoutMath.TryParseArea("everything", out _));
        Assert.False(PlotLayoutMath.TryParseArea(null, out _));
    }

    [Theory]
    [InlineData(null, true, 1, 1)]
    [InlineData("fit", true, 1, 1)]
    [InlineData("Scaled to Fit", true, 1, 1)]
    [InlineData("1:100", false, 1, 100)]
    [InlineData("1=50", false, 1, 50)]
    [InlineData("1 mm = 100 units", false, 1, 100)]
    [InlineData("2:1", false, 2, 1)]
    [InlineData("0.5:1", false, 0.5, 1)]
    public void ScalesParse(string? text, bool fit, double paper, double drawing)
    {
        Assert.True(PlotLayoutMath.TryParseScale(text, out var scale));
        Assert.Equal(fit, scale.Fit);
        Assert.Equal(paper, scale.PaperUnits);
        Assert.Equal(drawing, scale.DrawingUnits);
    }

    [Theory]
    [InlineData("half")]
    [InlineData("1:0")]
    [InlineData("0:1")]
    [InlineData("1:2:3")]
    public void BadScalesAreRefused(string text)
    {
        Assert.False(PlotLayoutMath.TryParseScale(text, out _));
    }

    [Fact]
    public void ScaleToStringIsStable()
    {
        Assert.Equal("fit", PlotScaleSpec.FitToSheet.ToString());
        Assert.True(PlotLayoutMath.TryParseScale("1:100", out var s));
        Assert.Equal("1:100", s.ToString());
    }

    [Theory]
    [InlineData("ISO A1 (841.00 x 594.00 MM)", "ISO_A1_(841.00_x_594.00_MM)")]
    [InlineData("ISO_A1_(841.00_x_594.00_MM)", "ISO_A1_(841.00_x_594.00_MM)")]
    [InlineData("iso a1 (841.00 x 594.00 mm)", "ISO_A1_(841.00_x_594.00_MM)")]
    [InlineData("ISO A1 (594.00 x 841.00 MM)", "ISO_A1_(594.00_x_841.00_MM)")]
    [InlineData("ANSI A (8.50 x 11.00 Inches)", "ANSI_A_(8.50_x_11.00_Inches)")]
    public void MediaResolvesLocalizedCanonicalAndLooseNames(string requested, string expected)
    {
        Assert.Equal(expected, PlotLayoutMath.ResolveMedia(requested, PdfMedia));
    }

    [Fact]
    public void FullBleedIsNotConfusedWithPlainA1()
    {
        Assert.Equal("ISO_full_bleed_A1_(841.00_x_594.00_MM)", PlotLayoutMath.ResolveMedia("ISO full bleed A1 (841.00 x 594.00 MM)", PdfMedia));
    }

    [Fact]
    public void UnknownMediaResolvesToNull()
    {
        Assert.Null(PlotLayoutMath.ResolveMedia("A1", PdfMedia));
        Assert.Null(PlotLayoutMath.ResolveMedia(null, PdfMedia));
        Assert.Null(PlotLayoutMath.ResolveMedia("ISO A0 (1189.00 x 841.00 MM)", PdfMedia));
    }

    [Theory]
    // landscape-defined paper (841 x 594)
    [InlineData("landscape", 841, 594, 1, 1, 0)]
    [InlineData("portrait", 841, 594, 1, 1, 90)]
    [InlineData("auto", 841, 594, 400, 100, 0)]
    [InlineData("auto", 841, 594, 100, 400, 90)]
    [InlineData(null, 841, 594, 100, 400, 90)]
    // portrait-defined paper (594 x 841): same words, opposite rotation
    [InlineData("landscape", 594, 841, 1, 1, 90)]
    [InlineData("portrait", 594, 841, 1, 1, 0)]
    [InlineData("auto", 594, 841, 400, 100, 90)]
    public void OrientationIsHonouredAgainstTheRealPaper(string? orientation, double pw, double ph, double aw, double ah, int expected)
    {
        Assert.Equal(expected, PlotLayoutMath.ChooseRotation(orientation, pw, ph, aw, ah));
    }

    [Fact]
    public void UnknownOrientationIsNull()
    {
        Assert.Null(PlotLayoutMath.ChooseRotation("sideways", 841, 594, 1, 1));
    }

    [Fact]
    public void FitScaleFillsTheTighterAxis()
    {
        var fit = PlotLayoutMath.ComputeFit(42000, 29700, 820, 570, 0, PlotScaleSpec.FitToSheet);
        Assert.Equal(Math.Min(820 / 42000.0, 570 / 29700.0), fit.PaperUnitsPerDrawingUnit, 12);
        Assert.Equal(1.0, fit.FillMax, 9);
        Assert.False(fit.Rotated);
    }

    [Fact]
    public void FixedScaleReportsPartialFill()
    {
        Assert.True(PlotLayoutMath.TryParseScale("1:100", out var scale));
        var fit = PlotLayoutMath.ComputeFit(41000, 28500, 820, 570, 0, scale);
        Assert.Equal(0.01, fit.PaperUnitsPerDrawingUnit, 12);
        Assert.Equal(0.5, fit.FillX, 9);
        Assert.Equal(0.5, fit.FillY, 9);
    }

    [Fact]
    public void RotationSwapsPrintableAxes()
    {
        var fit = PlotLayoutMath.ComputeFit(100, 200, 200, 100, 90, PlotScaleSpec.FitToSheet);
        Assert.True(fit.Rotated);
        Assert.Equal(1.0, fit.PaperUnitsPerDrawingUnit, 12);
        Assert.Equal(1.0, fit.FillX, 9);
        Assert.Equal(1.0, fit.FillY, 9);
    }

    [Fact]
    public void DegenerateFitIsZero()
    {
        var fit = PlotLayoutMath.ComputeFit(0, 10, 100, 100, 0, PlotScaleSpec.FitToSheet);
        Assert.Equal(0, fit.PaperUnitsPerDrawingUnit);
    }

    [Fact]
    public void ContentInOneCornerIsFlaggedTiny()
    {
        // An A3 layout (420 x 297) plotted with plotArea=layout onto an A1 sheet (841 x 594).
        var coverage = PlotLayoutMath.ComputeCoverage([0, 0, 841, 594], [0, 0, 400, 280]);
        Assert.True(coverage.LooksTiny);
        Assert.NotNull(coverage.Warning);
        Assert.Contains("plotArea=extents", coverage.Warning);
        Assert.True(coverage.ContentFillX < 0.5 && coverage.ContentFillY < 0.5);
    }

    [Fact]
    public void ContentFillingTheAreaIsNotFlagged()
    {
        var coverage = PlotLayoutMath.ComputeCoverage([0, 0, 100, 50], [0, 0, 100, 50]);
        Assert.False(coverage.LooksTiny);
        Assert.Null(coverage.Warning);
        Assert.Equal(1.0, coverage.AreaFraction);

        // A long thin plan filling only one axis is fine — the fit is limited by the other axis.
        var thin = PlotLayoutMath.ComputeCoverage([0, 0, 100, 100], [0, 40, 100, 60]);
        Assert.False(thin.LooksTiny);
    }

    [Fact]
    public void ContentOutsideTheAreaCountsAsEmpty()
    {
        var coverage = PlotLayoutMath.ComputeCoverage([0, 0, 10, 10], [20, 20, 30, 30]);
        Assert.Equal(0, coverage.AreaFraction);
        Assert.True(coverage.LooksTiny);
        Assert.False(PlotLayoutMath.ComputeCoverage([0, 0, 10, 10], null).LooksTiny);
    }

    [Fact]
    public void WindowsNormalizeFromNestedOrFlatForms()
    {
        Assert.True(PlotLayoutMath.TryNormalizeWindow([[10, 20], [0, 5]], null, out var box, out _));
        Assert.Equal(new double[] { 0, 5, 10, 20 }, box);

        Assert.True(PlotLayoutMath.TryNormalizeWindow(null, [3, 4, 1, 2], out box, out _));
        Assert.Equal(new double[] { 1, 2, 3, 4 }, box);
    }

    [Fact]
    public void BadWindowsAreRefusedWithReason()
    {
        Assert.False(PlotLayoutMath.TryNormalizeWindow([[1, 1]], null, out _, out var e1));
        Assert.Contains("[[x1,y1],[x2,y2]]", e1);
        Assert.False(PlotLayoutMath.TryNormalizeWindow([[1, 1], [1, 5]], null, out _, out var e2));
        Assert.Contains("zero width", e2);
        Assert.False(PlotLayoutMath.TryNormalizeWindow([[double.NaN, 1], [2, 5]], null, out _, out var e3));
        Assert.Contains("finite", e3);
        Assert.False(PlotLayoutMath.TryNormalizeWindow(null, null, out _, out _));
    }

    [Fact]
    public void PdfMediaBoxIsReadBackInMillimetres()
    {
        var pdf = Encoding.Latin1.GetBytes("%PDF-1.6\n1 0 obj << /Type /Page /MediaBox [0 0 2383.94 1683.78] >> endobj\n%%EOF");
        Assert.True(PlotLayoutMath.TryReadFirstMediaBox(pdf, out var w, out var h));
        Assert.Equal(841.0, w, 0);
        Assert.Equal(594.0, h, 0);

        Assert.False(PlotLayoutMath.TryReadFirstMediaBox(Encoding.Latin1.GetBytes("%PDF-1.6 no media box here"), out _, out _));
    }
}
