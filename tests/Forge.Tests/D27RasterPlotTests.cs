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
    // The REAL PublishToWeb PNG.pc3 layout (zlib body decoded by Pc3File from
    // C:\...\plotters\PublishToWeb PNG.pc3 on AutoCAD 2025): names are UNTERMINATED —
    // `name="value` runs to end-of-line with no closing quote. live-r8 proved the
    // [^"]* capture ate media_bounds + resolution fields on this shape; the fixture
    // must keep the real form so the regression stays pinned.
    private const string PngPc3Text = """
meta{
 canonical_family_name="Raster File Formats
 canonical_model_name="png
 localized_model_name="Portable Network Graphics PNG (LZH Compression)
 file_only=TRUE
 udm_description="
 config_autospool=FALSE
}
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
  name="Sun_Hi-Res_(1600.00_x_1280.00_Pixels)
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
io{
 type=2
 pathname="
 allsysvalid=FALSE
 plot_to_file=TRUE
}
res_color_mem{
 name="RGBA
 num_colors=16777216
 color_depth=32
 resolution{
  name="100dpi
  addr_resolution_x=100.0
  addr_resolution_y=100.0
  effective_resolution_x=100.0
  effective_resolution_y=100.0
 }
}
custom{
 0{
  name="RASTER_CALS_Twist
  value=FALSE
 }
 4{
  name="RASTER_IE_Compression_Type
  value="PNG
 }
}
""";

    [Fact]
    public void Apply_RewritesMediumAndResolution()
    {
        var patched = RasterPc3.Apply(PngPc3Text, 2480, 1754, 150, out var err);
        Assert.Null(err);
        // the real file's names are unterminated — the patch preserves that form, and the
        // selected size now points at the driver's own user-medium name convention
        Assert.Contains("name=\"UserDefinedRaster (2480.00 x 1754.00Pixels)", patched, StringComparison.Ordinal);
        // 0-indexed printable bounds
        Assert.Contains("actual_printable_bounds_urx=2479", patched, StringComparison.Ordinal);
        Assert.Contains("printable_bounds_ury=1753", patched, StringComparison.Ordinal);
        Assert.Contains("urx=2480", patched, StringComparison.Ordinal);
        Assert.Contains("ury=1754", patched, StringComparison.Ordinal);
        Assert.Contains("addr_resolution_x=150", patched, StringComparison.Ordinal);
        Assert.Contains("effective_resolution_y=150", patched, StringComparison.Ordinal);
        // live-r8 mangled-write regression: these blocks were eaten by the greedy
        // [^"]* name capture — they must survive the patch
        Assert.Contains("media_bounds{", patched, StringComparison.Ordinal);
        Assert.Contains("io{", patched, StringComparison.Ordinal);
        Assert.Contains("custom{", patched, StringComparison.Ordinal);
        Assert.Contains("media_description{", patched, StringComparison.Ordinal);
        // the stored medium name is gone everywhere
        Assert.DoesNotContain("Sun_Hi-Res", patched, StringComparison.Ordinal);
        Assert.True(RasterPc3.Verify(patched, 2480, 1754, 150));
        Assert.False(RasterPc3.Verify(PngPc3Text, 2480, 1754, 150), "unpatched text must not verify");
    }

    // The REAL PublishToWeb PNG.pmp layout (zlib body decoded from PMP Files\):
    // meta{ mod{ del{ udm{media{size{ N{...} } description{ N{...} }}} hidden{ — the
    // udm size list holds the selectable user names, description the bounds, linked by
    // media_description_name. mod{ may itself carry populated size{/description{ lists
    // (stock media); hidden{ trails the file. Injection must stay inside udm{...}.
    private const string PmpText = """
meta{
 user_defined_model_basename="
}
mod{
 media{
  size{
   0{
    caps_type=2
    name="Sun_Hi-Res_(4000.00_x_3000.00Pixels)
    localized_name="Sun_Hi-Res (4000.00 x 3000.00 Pixels)
    media_description_name="Sun_Hi-Res Landscape 4000.00W x 3000.00H - (0, 0) x (3999, 2999) =12000000 Pixels
    media_group=16
    landscape_mode=TRUE
   }
  }
  description{
   0{
    caps_type=2
    name="Sun_Hi-Res Landscape 4000.00W x 3000.00H - (0, 0) x (3999, 2999) =12000000 Pixels
    media_bounds_urx=4000.0
    media_bounds_ury=3000.0
    dimensional=FALSE
   }
  }
 }
}
del{
}
udm{
 calibration{
  _x=1.0
 }
 media{
  abilities="500505500500505555000005550000000550000500000500555
  size_max_x=320000.00000
  size{
   0{
    caps_type=2
    name="UserDefinedRaster (1362.00 x 962.00Pixels)
    localized_name="User 1 (1362.00 x 962.00 Pixels)
    media_description_name="UserDefinedRaster Landscape 1362.00W x 962.00H - (0, 0) x (1361, 961) =1310244 Pixels
    media_group=16
    landscape_mode=TRUE
   }
  }
  description{
   0{
    caps_type=2
    name="UserDefinedRaster Landscape 1362.00W x 962.00H - (0, 0) x (1361, 961) =1310244 Pixels
    media_bounds_urx=1362.0
    media_bounds_ury=962.0
    printable_bounds_llx=0.0
    printable_bounds_lly=0.0
    printable_bounds_urx=1361.0
    printable_bounds_ury=961.0
    printable_area=1310244.0000
    dimensional=FALSE
   }
  }
 }
}
hidden{
 media{
  abilities="500505500500505555000005550000000550000500000500555
  caps_state="000000000000000000000000000000000000000000000000000
 }
}
""";

    [Fact]
    public void EnsurePmpMedium_InjectsSizeAndDescription()
    {
        var patched = RasterPc3.EnsurePmpMedium(PmpText, 5400, 3600, out var injected, out var err);
        Assert.Null(err);
        Assert.True(injected);
        Assert.NotNull(patched);
        // the selectable name uses the driver's own convention and a fresh index
        Assert.Contains("1{\n    caps_type=2\n    name=\"UserDefinedRaster (5400.00 x 3600.00Pixels)", patched, StringComparison.Ordinal);
        Assert.Contains("localized_name=\"User 1 (5400.00 x 3600.00 Pixels)", patched, StringComparison.Ordinal);
        // the description entry carries the real bounds (media = dims, printable = 0-indexed)
        Assert.Contains("name=\"UserDefinedRaster Landscape 5400.00W x 3600.00H - (0, 0) x (5399, 3599) =19440000 Pixels", patched, StringComparison.Ordinal);
        Assert.Contains("media_bounds_urx=5400", patched, StringComparison.Ordinal);
        Assert.Contains("printable_bounds_urx=5399", patched, StringComparison.Ordinal);
        Assert.Contains("landscape_mode=TRUE", patched, StringComparison.Ordinal);
        // injection lands inside udm{...} — never into mod{ (stock) or after hidden{
        var udmAt = patched.IndexOf("udm{", StringComparison.Ordinal);
        var udmEnd = patched.IndexOf("\n}", udmAt, StringComparison.Ordinal);
        Assert.InRange(patched.IndexOf("UserDefinedRaster (5400.00 x 3600.00Pixels)", StringComparison.Ordinal), udmAt, udmEnd);
        Assert.InRange(patched.IndexOf("UserDefinedRaster Landscape 5400.00W", StringComparison.Ordinal), udmAt, udmEnd);
        // existing entries and surrounding sections survive untouched
        Assert.Contains("UserDefinedRaster (1362.00 x 962.00Pixels)", patched, StringComparison.Ordinal);
        Assert.Contains("Sun_Hi-Res_(4000.00_x_3000.00Pixels)", patched, StringComparison.Ordinal);
        Assert.Contains("hidden{", patched, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsurePmpMedium_RefusesHalfInjectedState()
    {
        // size entry present, description missing (e.g. a killed/bad earlier write):
        // the idempotent skip must not cement this — it is a typed error, not a patch.
        var half = PmpText.Replace(
            "media_description_name=\"UserDefinedRaster Landscape 1362.00W",
            "media_description_name=\"UserDefinedRaster Landscape 5400.00W x 3600.00H - (0, 0) x (5399, 3599) =19440000 Pixels\n   }\n   1{\n    caps_type=2\n    name=\"UserDefinedRaster (5400.00 x 3600.00Pixels)\n    media_description_name=\"UserDefinedRaster Landscape 5400.00W");
        Assert.Null(RasterPc3.EnsurePmpMedium(half, 5400, 3600, out var injected, out var err));
        Assert.False(injected);
        Assert.Contains("half-injected", err, StringComparison.Ordinal);
    }

    [Fact]
    public void EnsurePmpMedium_IsIdempotent_AndRejectsNonPmp()
    {
        var once = RasterPc3.EnsurePmpMedium(PmpText, 5400, 3600, out var injected1, out _);
        Assert.True(injected1);
        var twice = RasterPc3.EnsurePmpMedium(once!, 5400, 3600, out var injected2, out _);
        Assert.False(injected2);
        Assert.Equal(once, twice); // already present -> returned unchanged

        Assert.Null(RasterPc3.EnsurePmpMedium("media{\n}\n", 100, 100, out _, out var err));
        Assert.NotNull(err);
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
        Assert.Equal("UserDefinedRaster (4966.00 x 3508.00Pixels)", RasterPc3.MediaName(4966, 3508));
        Assert.Equal("UserDefinedRaster Landscape 5400.00W x 3600.00H - (0, 0) x (5399, 3599) =19440000 Pixels",
            RasterPc3.DescriptionName(5400, 3600));
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
    public void Plot_RefreshesDeviceList_BeforeListingVerify()
    {
        // live-r7: GetPlotDeviceList is built once per session; RefreshCode.All alone
        // could not see a pc3 dropped mid-session. The dedicated devices refresh plus
        // the validator's own array rebuild must run BEFORE the listing verify.
        var source = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.Plot.cs"));
        var copyAt = source.IndexOf("File.Copy(viewer.SourcePc3, rasterPc3Path, overwrite: true)", StringComparison.Ordinal);
        var devRefreshAt = source.IndexOf("RefreshCode.RefreshDevicesList", StringComparison.Ordinal);
        var psvRefreshAt = source.IndexOf("psv.RefreshLists(ps)", devRefreshAt, StringComparison.Ordinal);
        var listedAt = source.IndexOf("var listed = psv.GetPlotDeviceList()", StringComparison.Ordinal);
        Assert.True(devRefreshAt > copyAt, "RefreshDevicesList must run after the clone copy");
        Assert.True(psvRefreshAt > devRefreshAt, "psv.RefreshLists must run after RefreshDevicesList");
        Assert.True(listedAt > psvRefreshAt, "the GetPlotDeviceList verify must run after both refreshes");
    }

    [Fact]
    public void PrepareNoViewerDevice_RefreshesDeviceList_BeforeListingVerify()
    {
        // live-r8 class fix: the no-viewer clone written mid-session hits the same
        // once-per-session GetPlotDeviceList staleness — a fresh machine failed
        // plot_viewer_suppression_failed. RefreshDevicesList + a throwaway-probe
        // RefreshLists must run before the listing check, inside the definition.
        var source = File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.Plot.cs"));
        var defAt = source.IndexOf("private static ViewerSuppression PrepareNoViewerDevice", StringComparison.Ordinal);
        var devRefreshAt = source.IndexOf("RefreshCode.RefreshDevicesList", defAt, StringComparison.Ordinal);
        var probeAt = source.IndexOf("RefreshLists(probe)", devRefreshAt, StringComparison.Ordinal);
        var knownAt = source.IndexOf("GetPlotDeviceList()", probeAt, StringComparison.Ordinal);
        Assert.True(devRefreshAt > defAt && probeAt > devRefreshAt && knownAt > probeAt,
            "no-viewer clone must refresh devices + validator arrays before the listing verify");
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
