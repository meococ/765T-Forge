using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// smoke r2 D9: a layout whose stored media name is not on the plot device must not die as
/// plot_device_invalid. The stored sheet size is mapped onto a device medium of equal size
/// (within 0.5 mm, either orientation); none or ambiguous fails as plot_paper_not_on_device.
/// </summary>
public sealed class D9PaperMatchTests
{
    /// <summary>DWG To PDF-style device list (canonical names embed the sheet size).</summary>
    private static readonly PaperMedia[] Device =
    [
        PaperMatch.ToMedia("ISO_A1_(841.00_x_594.00_MM)", "ISO A1 (841.00 x 594.00 MM)"),
        PaperMatch.ToMedia("ISO_full_bleed_A1_(841.00_x_594.00_MM)", "ISO full bleed A1 (841.00 x 594.00 MM)"),
        PaperMatch.ToMedia("ISO_expand_A1_(841.00_x_594.00_MM)", "ISO expand A1 (841.00 x 594.00 MM)"),
        PaperMatch.ToMedia("Overflow_A1_(841.00_x_594.00_MM)", "Overflow A1 (841.00 x 594.00 MM)"),
        PaperMatch.ToMedia("ISO_A4_(210.00_x_297.00_MM)", "ISO A4 (210.00 x 297.00 MM)"),
        PaperMatch.ToMedia("ANSI_B_(17.00_x_11.00_Inches)", "ANSI B (17.00 x 11.00 Inches)"),
        PaperMatch.ToMedia("Letter_(8.50_x_11.00_Inches)", "Letter (8.50 x 11.00 Inches)"),
        PaperMatch.ToMedia("User_defined_Raster_(640.00_x_480.00_Pixels)", "User defined Raster (640.00 x 480.00 Pixels)"),
    ];

    [Theory]
    [InlineData("ISO_A1_(841.00_x_594.00_MM)", 841.0, 594.0)]
    [InlineData("ezdxf_(841.00_x_594.00_MM)", 841.0, 594.0)]
    [InlineData("ISO A4 (210.00 x 297.00 MM)", 210.0, 297.0)]
    [InlineData("ANSI_B_(17.00_x_11.00_Inches)", 431.8, 279.4)]
    [InlineData("Letter (8.50 x 11.00 Inches)", 215.9, 279.4)]
    public void ParsesSheetSizeFromCanonicalAndLocaleNames(string name, double w, double h)
    {
        var size = PaperMatch.ParseSizeMm(name);
        Assert.NotNull(size);
        Assert.Equal(w, size.Value.WidthMm, 3);
        Assert.Equal(h, size.Value.HeightMm, 3);
    }

    [Theory]
    [InlineData("User_defined_Raster_(640.00_x_480.00_Pixels)")] // pixels are not mm
    [InlineData("ISO_A1")]
    [InlineData("")]
    [InlineData(null)]
    public void NamesWithoutAMetricSizeParseAsNull(string? name)
        => Assert.Null(PaperMatch.ParseSizeMm(name));

    [Fact]
    public void EzdxfCustomMediaMapsToThePlainA1NotTheVariants()
    {
        // The r2 failure: stored ezdxf_(841.00_x_594.00_MM) has four same-size candidates on
        // DWG To PDF; the deterministic preference picks the base name, not expand/full-bleed/overflow.
        var m = PaperMatch.MatchBySize(841.0, 594.0, Device);
        Assert.True(m.Matched);
        Assert.False(m.Ambiguous);
        Assert.Equal("ISO_A1_(841.00_x_594.00_MM)", m.Canonical);
    }

    [Fact]
    public void ARotatedMatchIsUsedWhenItIsTheOnlyCandidate()
    {
        // 594x841 stored: every same-size medium is rotated; the plain name still wins its group.
        var m = PaperMatch.MatchBySize(594.0, 841.0, Device);
        Assert.True(m.Matched);
        Assert.Equal("ISO_A1_(841.00_x_594.00_MM)", m.Canonical);
    }

    [Fact]
    public void SizesWithinHalfAMillimetreMatchAndBeyondDoNot()
    {
        Assert.True(PaperMatch.MatchBySize(841.49, 594.49, Device).Matched);
        Assert.False(PaperMatch.MatchBySize(840.0, 593.0, Device).Matched);
    }

    [Fact]
    public void TwoEquallyPlainCandidatesAreAmbiguous()
    {
        var device = new[]
        {
            PaperMatch.ToMedia("SHOP_A_(841.00_x_594.00_MM)", null),
            PaperMatch.ToMedia("SHOP_B_(841.00_x_594.00_MM)", null),
            PaperMatch.ToMedia("ISO_A4_(210.00_x_297.00_MM)", null),
        };
        var m = PaperMatch.MatchBySize(841.0, 594.0, device);
        Assert.False(m.Matched);
        Assert.True(m.Ambiguous);
        Assert.Contains("SHOP_A_(841.00_x_594.00_MM)", m.Nearest);
        Assert.Contains("SHOP_B_(841.00_x_594.00_MM)", m.Nearest);
    }

    [Fact]
    public void NoSizeMatchListsUpToThreeNearestDeviceMedia()
    {
        var m = PaperMatch.MatchBySize(500.0, 500.0, Device);
        Assert.False(m.Matched);
        Assert.False(m.Ambiguous);
        Assert.InRange(m.Nearest.Count, 1, 3);
        Assert.Equal("ANSI B (17.00 x 11.00 Inches)", m.Nearest[0]); // closest by max-axis distance, display name
    }

    [Fact]
    public void AnUnknownStoredSizeCannotMatchButStillNamesNearest()
    {
        var m = PaperMatch.MatchBySize(null, null, Device);
        Assert.False(m.Matched);
        Assert.Equal(3, m.Nearest.Count);

        var empty = PaperMatch.MatchBySize(841.0, 594.0, []);
        Assert.False(empty.Matched);
        Assert.Empty(empty.Nearest);
    }

    [Fact]
    public void PixelMediaCannotWinASizeMatch()
    {
        var m = PaperMatch.MatchBySize(640.0, 480.0, Device);
        Assert.False(m.Matched);
    }
}

/// <summary>D9 plugin wiring guards: the stored-paper branch maps by size, never guesses.</summary>
public sealed class D9PaperMatchSourceGuards
{
    private static string PlotSource()
        => File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.Plot.cs"));

    private static string MethodBody(string signature)
    {
        var source = PlotSource();
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {signature}");
        var end = System.Text.RegularExpressions.Regex.Match(source[start..], @"\n    \}\r?\n");
        Assert.True(end.Success, $"end of method not found: {signature}");
        return source.Substring(start, end.Index + end.Length);
    }

    [Fact]
    public void StoredMediaMappingGoesThroughPaperMatchAndFailsClosed()
    {
        var source = PlotSource();
        Assert.Contains("PaperMatch.MatchBySize(", source, StringComparison.Ordinal);
        Assert.Contains("PaperMatch.NotOnDeviceCode", source, StringComparison.Ordinal);
        Assert.Contains("mapped_to_device_media", source, StringComparison.Ordinal);
        // The layout-paper fallback no longer calls SetClosestMediaName in layout units:
        // passing PlotPaperSize (inches on imperial templates) as millimeters was the
        // eInvalidInput behind plot_device_invalid.
        Assert.DoesNotContain("closest_to_layout", source);
        Assert.DoesNotContain("SetClosestMediaName(ps, layout.PlotPaperSize", source);
    }

    [Fact]
    public void MediaAndPaperResolutionRunBeforeAnyLayoutSwitch()
    {
        // review-wp2-r1 finding 4 (D9): a typed media/paper failure on a non-current layout
        // must leave no side effect — no initialized layout, DBMOD unchanged. That only holds
        // when TryResolveMedia runs before PlanLayoutSwitch decides and before CurrentLayout
        // is ever assigned.
        var body = MethodBody("private static ForgeResult PlotToPdf(");
        var resolve = body.IndexOf("TryResolveMedia(", StringComparison.Ordinal);
        var plan = body.IndexOf("PlanLayoutSwitch(", StringComparison.Ordinal);
        var assign = body.IndexOf("CurrentLayout = layoutName", StringComparison.Ordinal);
        Assert.True(resolve >= 0, "TryResolveMedia must run inside PlotToPdf");
        Assert.True(resolve < plan, "media/paperSize resolution must precede PlanLayoutSwitch");
        Assert.True(plan >= 0 && plan < assign, "the switch decision must precede the CurrentLayout assignment");
    }

    [Fact]
    public void PostSwitchCodeCannotEmitMediaFailures()
    {
        // After the switch the plot body must be unable to fail on media/paper — every media
        // failure code lives in the pre-switch TryResolveMedia.
        var body = MethodBody("private static ForgeResult PlotOnLayout(");
        Assert.DoesNotContain("GetCanonicalMediaNameList", body, StringComparison.Ordinal);
        Assert.DoesNotContain("NotOnDeviceCode", body, StringComparison.Ordinal);
        Assert.DoesNotContain("paper_size_not_found", body, StringComparison.Ordinal);
        Assert.DoesNotContain("plot_device_invalid", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CopyFrom(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void MediaFailureCodesLiveInThePreSwitchResolver()
    {
        var body = MethodBody("private static ForgeResult? TryResolveMedia(");
        Assert.Contains("paper_size_not_found", body, StringComparison.Ordinal);
        Assert.Contains("PaperMatch.NotOnDeviceCode", body, StringComparison.Ordinal);
        Assert.Contains("plot_device_invalid", body, StringComparison.Ordinal);
        Assert.Contains("mapped_to_device_media", body, StringComparison.Ordinal);
    }
}
