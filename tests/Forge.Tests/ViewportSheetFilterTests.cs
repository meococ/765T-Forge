using Forge.Shared;

namespace Forge.Tests;

public sealed class ViewportSheetFilterTests
{
    // Measured live values from sheet-set_R1.dxf loaded into AutoCAD (G29 repro):
    // DXF group 69 survives as 1 for sheet viewports but is zeroed for model viewports.

    [Fact]
    public void SheetViewport_Number1_IsExcluded()
    {
        // V-101 sheet viewport, handle 917: center=(18,12), viewCenter=(18,12), height=26.4, viewHeight=26.4
        Assert.True(ViewportSheetFilter.IsSheetViewport(1, 18.0, 12.0, 18.0, 12.0, 26.4, 26.4));
    }

    [Fact]
    public void ModelViewport_Number0_IsIncluded()
    {
        // V-101 model viewport, handle A0D: center=(9.9375,12.675), viewCenter=(1933,516.84),
        // height=15.625, viewHeight=750.0 — DXF import zeroed Number.
        Assert.False(ViewportSheetFilter.IsSheetViewport(0, 9.9375, 12.675, 1933.0, 516.84, 15.625, 750.0));
    }

    [Fact]
    public void ModelViewport_Number0_SmallScale_IsIncluded()
    {
        // C-401 model viewport, handle A93: viewHeight 6.04 vs height 10.57.
        Assert.False(ViewportSheetFilter.IsSheetViewport(0, 15.3125, 17.715, 8.1, 6.58, 10.57, 6.04));
    }

    [Fact]
    public void ModelViewport_Number0_CenteredAtSheetCenter_ButViewingModel_IsIncluded()
    {
        // Same paper position as a sheet viewport but the view targets model space.
        Assert.False(ViewportSheetFilter.IsSheetViewport(0, 18.0, 12.0, 5000.0, 300.0, 26.4, 800.0));
    }

    [Fact]
    public void ModelViewport_NormalNumber_IsIncluded()
    {
        // DWG-native numbering: model viewports keep ids > 1.
        Assert.False(ViewportSheetFilter.IsSheetViewport(3, 9.5, 12.675, 8.1, 5.58, 8.04, 8.04));
        Assert.False(ViewportSheetFilter.IsSheetViewport(4, 15.3125, 8.4825, 8.1, 2.73, 4.095, 2.34));
    }

    [Fact]
    public void DegenerateSheetViewport_Number0_MatchingGeometry_IsExcluded()
    {
        // Defensive: a sheet viewport whose number was also zeroed still matches
        // the paper-look geometry and is excluded.
        Assert.True(ViewportSheetFilter.IsSheetViewport(0, 18.0, 12.0, 18.0, 12.0, 26.4, 26.4));
    }

    [Fact]
    public void DegenerateSheetViewport_WithinTolerance_IsExcluded()
    {
        Assert.True(ViewportSheetFilter.IsSheetViewport(0, 18.0, 12.0, 18.00005, 11.99995, 26.4, 26.40005));
    }

    [Fact]
    public void DegenerateViewport_ViewHeightMismatch_IsIncluded()
    {
        // View center matches but the view height does not: it is looking at
        // something other than the paper it sits on.
        Assert.False(ViewportSheetFilter.IsSheetViewport(0, 18.0, 12.0, 18.0, 12.0, 26.4, 800.0));
    }

    [Fact]
    public void OddNumber_WithPaperLookGeometry_IsExcluded()
    {
        // Deliberate trade-off (review F8/F5): geometry is checked for EVERY number != 1, so a
        // sheet viewport that landed on an odd number is still excluded. The cost — a model
        // viewport looking at exactly its own paper rect (coincidental, ~impossible at 1e-4
        // tolerance) — is silently dropped from viewport_list. Pinned so the choice is explicit.
        Assert.True(ViewportSheetFilter.IsSheetViewport(3, 18.0, 12.0, 18.0, 12.0, 26.4, 26.4));
    }

    [Fact]
    public void IsModelViewport_IsNegation()
    {
        Assert.False(ViewportSheetFilter.IsModelViewport(1, 18.0, 12.0, 18.0, 12.0, 26.4, 26.4));
        Assert.True(ViewportSheetFilter.IsModelViewport(0, 9.9375, 12.675, 1933.0, 516.84, 15.625, 750.0));
    }

    [Fact]
    public void ViewportListUsesSharedSheetFilterNotBareNumber()
    {
        // TOOL-GAPS #29: DXF-loaded layouts zero the model-viewport Numbers, so a bare
        // "vp.Number <= 1" filter dropped every viewport. The loop must go through the
        // shared sheet-viewport discriminator instead.
        var pluginDir = PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin");
        var source = File.ReadAllText(Path.Combine(pluginDir, "PluginCommandProcessor.RegistryAndViewport.cs"));

        var loopStart = source.IndexOf("private static ForgeResult ViewportList(", StringComparison.Ordinal);
        var loopEnd = source.IndexOf("tr.Commit();", loopStart, StringComparison.Ordinal);
        Assert.True(loopStart >= 0 && loopEnd > loopStart, "ViewportList body must be locatable");
        var body = source.Substring(loopStart, loopEnd - loopStart);

        Assert.Contains("ViewportSheetFilter.IsSheetViewport(", body);
        Assert.Contains("vp.ViewCenter", body);
        Assert.Contains("vp.ViewHeight", body);
        Assert.DoesNotContain("vp.Number <= 1", body);
        Assert.DoesNotContain("vp.Number > 1", body);
    }
}
