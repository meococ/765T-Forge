using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// TOOL-GAP #27: outputFormat="png" drives the same guarded plot pipeline onto
/// PublishToWeb PNG.pc3 — the clone pc3's user-defined medium is rewritten to the
/// sheet's pixel size at dpi, and a PNG signature probe replaces the PDF probe.
/// The pc3 fixture below is the real text block layout of PublishToWeb PNG.pc3
/// (zlib container decoded by Pc3File): one stored user-defined size + resolution.
/// </summary>
public sealed class D27RasterPlotTests
{
    private const string PngPc3Text = """
media{
 selection_method=2
 type="
 dm_orientation=1
 actual_printable_bounds_llx=0.0
 actual_printable_bounds_lly=0.0
 actual_printable_bounds_urx=1599.0
 actual_printable_bounds_ury=1279.0
 number_of_copies=1
 size{
  name="Sun_Hi-Res_(1600.00_x_1280.00_Pixels)"
  group=13
  landscape_mode=TRUE
  longplot_reduction=1.0
  media_description{
   printable_bounds_llx=0.0
   printable_bounds_lly=0.0
   printable_bounds_urx=1599.0
   printable_bounds_ury=1279.0
   printable_area=2048000.0000 (      ! /T&$W0)
   dimensional=FALSE
   media_bounds{
    urx=1600.0
    ury=1280.0
   }
  }
 }
}
resolution{
 name="100dpi"
 addr_resolution_x=100.0
 addr_resolution_y=100.0
 effective_resolution_x=100.0
 effective_resolution_y=100.0
}
""";

    [Fact]
    public void Apply_RewritesMediumAndResolution()
    {
        var patched = RasterPc3.Apply(PngPc3Text, 2480, 1754, 150, out var err);
        Assert.Null(err);
        Assert.Contains("name=\"Forge_2480x1754px\"", patched, StringComparison.Ordinal);
        // 0-indexed printable bounds
        Assert.Contains("actual_printable_bounds_urx=2479", patched, StringComparison.Ordinal);
        Assert.Contains("printable_bounds_ury=1753", patched, StringComparison.Ordinal);
        Assert.Contains("urx=2480", patched, StringComparison.Ordinal);
        Assert.Contains("ury=1754", patched, StringComparison.Ordinal);
        Assert.Contains("addr_resolution_x=150", patched, StringComparison.Ordinal);
        Assert.Contains("effective_resolution_y=150", patched, StringComparison.Ordinal);
        // the stored medium name is gone everywhere
        Assert.DoesNotContain("Sun_Hi-Res", patched, StringComparison.Ordinal);
        Assert.True(RasterPc3.Verify(patched, 2480, 1754, 150));
        Assert.False(RasterPc3.Verify(PngPc3Text, 2480, 1754, 150), "unpatched text must not verify");
    }

    [Fact]
    public void Apply_NonRasterText_FailsWithoutMutation()
    {
        var original = "media{\n type=\"DWG To PDF\"\n}\n";
        var result = RasterPc3.Apply(original, 100, 100, 150, out var err);
        Assert.NotNull(err);
        Assert.Equal(original, result);
    }

    [Fact]
    public void Apply_InvalidDims_Fails()
    {
        RasterPc3.Apply(PngPc3Text, 0, 100, 150, out var err);
        Assert.NotNull(err);
    }

    [Fact]
    public void MediaMath_MmToPixels()
    {
        Assert.Equal(1754, RasterPc3.MmToPixels(297.0, 150)); // A4 long side at 150dpi
        Assert.Equal(4966, RasterPc3.MmToPixels(841.0, 150)); // A1: 4966.5 -> floored to even
        Assert.Equal("Forge_4966x3508px", RasterPc3.MediaName(4966, 3508));
    }

    private static byte[] FakePng(int wPx, int hPx)
    {
        var b = new byte[32];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        b[12] = (byte)'I'; b[13] = (byte)'H'; b[14] = (byte)'D'; b[15] = (byte)'R';
        b[16] = (byte)(wPx >> 24); b[17] = (byte)(wPx >> 16); b[18] = (byte)(wPx >> 8); b[19] = (byte)wPx;
        b[20] = (byte)(hPx >> 24); b[21] = (byte)(hPx >> 16); b[22] = (byte)(hPx >> 8); b[23] = (byte)hPx;
        return b;
    }

    [Fact]
    public void PngProbe_AcceptsSignature_AndRejectsGarbage()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"d27-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var good = Path.Combine(dir, "ok.png");
            File.WriteAllBytes(good, FakePng(2480, 1754));
            var ok = PngProbeResult.Probe(good);
            Assert.True(ok.Passed);
            Assert.Equal(2480, ok.WidthPx);
            Assert.Equal(1754, ok.HeightPx);

            var bad = Path.Combine(dir, "bad.png");
            File.WriteAllText(bad, "%PDF-1.4 not a png at all, longer than twenty-four bytes");
            Assert.False(PngProbeResult.Probe(bad).Passed);

            Assert.False(PngProbeResult.Probe(Path.Combine(dir, "missing.png")).Passed);

            var stale = Path.Combine(dir, "stale.png");
            File.WriteAllBytes(stale, FakePng(100, 100));
            File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-1));
            Assert.False(PngProbeResult.Probe(stale, notBeforeUtc: DateTimeOffset.UtcNow).Passed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void PngProbe_ExpectedDims_CatchesTransposedOrWrongSize()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"d27-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "sheet.png");
            File.WriteAllBytes(path, FakePng(1754, 2480)); // transposed vs the request
            var mismatch = PngProbeResult.Probe(path, expectedW: 2480, expectedH: 1754);
            Assert.False(mismatch.Passed);
            Assert.Contains("1754x2480", mismatch.Message, StringComparison.Ordinal);
            Assert.True(PngProbeResult.Probe(path, expectedW: 1754, expectedH: 2480).Passed);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Plot_BranchesPngBeforePdfProbe()
    {
        var source = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.Plot.cs"));
        Assert.Contains("\"PublishToWeb PNG.pc3\"", source, StringComparison.Ordinal);
        var pngAt = source.IndexOf("PngProbeResult.Probe(outputPath", StringComparison.Ordinal);
        var pdfAt = source.IndexOf("var probe = PdfProbeResult.Probe(outputPath", StringComparison.Ordinal);
        Assert.True(pngAt > 0 && pdfAt > pngAt, "the PNG probe branch must return before the PDF probe runs");
        // the raster medium must be patched before the device is registered
        var patchAt = source.IndexOf("RasterPc3.Apply(pc3Text", StringComparison.Ordinal);
        var registerAt = source.IndexOf("psv.SetPlotConfigurationName(ps, rasterDevice ?? viewer.PlotDevice, null)", StringComparison.Ordinal);
        Assert.True(patchAt > 0 && registerAt > patchAt, "pc3 raster patch must precede SetPlotConfigurationName");
        // pixel paper units feed the mm conversion via dpi
        Assert.Contains("PlotPaperUnit.Pixels => 25.4 / rasterDpi", source, StringComparison.Ordinal);
        // dry-run must NOT SetCanonicalMediaName on the unpatched device (eInvalidInput)
        Assert.Contains("mediaSource != \"raster_planned\"", source, StringComparison.Ordinal);
        // a device without View_New_File still gets a writable clone for the raster patch
        Assert.Contains("File.Copy(viewer.SourcePc3, rasterPc3Path, overwrite: true)", source, StringComparison.Ordinal);
        // the legacy -PLOT engine is refused for raster (pixel units don't map to its prompts)
        Assert.Contains("is not supported for outputFormat=", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_PortraitMedium_FlipsLandscapeFlag()
    {
        var patched = RasterPc3.Apply(PngPc3Text, 1240, 1754, 150, out var err);
        Assert.Null(err);
        Assert.Contains("landscape_mode=FALSE", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void PlotExposes_OutputFormatAndDpi()
    {
        var plugin = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.cs"));
        Assert.Contains("public string? OutputFormat { get; init; }", plugin, StringComparison.Ordinal);
        Assert.Contains("public int? Dpi { get; init; }", plugin, StringComparison.Ordinal);

        var mcp = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Server"), "ForgeMcpTools.cs"));
        Assert.Contains("string? outputFormat = null", mcp, StringComparison.Ordinal);
        Assert.Contains("int? dpi = null", mcp, StringComparison.Ordinal);
        Assert.Contains("outputFormat, dpi, overwriteAcknowledged", mcp, StringComparison.Ordinal);
    }
}
