using Forge.Shared;

namespace Forge.Tests;

/// <summary>
/// smoke r2 D12: with no caller plotArea the layout's stored plot type wins (client sheets
/// store Window + a stored window) — the default must not silently become layout/extents —
/// and a PDF that comes out EMPTY is a failure, never a warning.
/// live-smoke-20261030 fix4: the stored type arrives as <see cref="StoredPlotType"/> BY NAME —
/// the managed PlotType is 0-based (Display=0 … Layout=5), NOT the 1-based ActiveX AcPlotType
/// the r2 int mapping assumed (stored Window read 4 → was resolved as View, stored View 3 →
/// Limits → eInvalidInput, stored Layout 5 → Window).
/// </summary>
public sealed class D12StoredPlotAreaTests
{
    private static readonly double[] SheetWindow = [10.0, 20.0, 400.0, 260.0];

    [Fact]
    public void AnExplicitCallerAreaAlwaysWins()
    {
        var r = PlotLayoutMath.ResolveArea(PlotAreaKind.Layout, false, StoredPlotType.Window, SheetWindow, null, false);
        Assert.Equal(PlotAreaKind.Layout, r.Area);
        Assert.Equal("requested", r.Source);
        Assert.Null(r.Window);
    }

    [Fact]
    public void ModelKeepsTheExtentsDefaultRegardlessOfStoredType()
    {
        var r = PlotLayoutMath.ResolveArea(null, true, StoredPlotType.Window, SheetWindow, null, false);
        Assert.Equal(PlotAreaKind.Extents, r.Area);
        Assert.Equal("default_model_extents", r.Source);
    }

    [Fact]
    public void StoredWindowPlotsTheStoredWindow()
    {
        var r = PlotLayoutMath.ResolveArea(null, false, StoredPlotType.Window, SheetWindow, null, false);
        Assert.Equal(PlotAreaKind.Window, r.Area);
        Assert.Equal("stored", r.Source);
        Assert.Equal(SheetWindow, r.Window);
    }

    [Fact]
    public void AStoredWindowStillAppliesWhenThePaperWasMapped()
    {
        // D9+D12 together: stored media was mapped to a same-size device medium — the stored
        // window is still the layout's own intent and must not be replaced by extents.
        var r = PlotLayoutMath.ResolveArea(null, false, StoredPlotType.Window, SheetWindow, null, true);
        Assert.Equal(PlotAreaKind.Window, r.Area);
        Assert.Equal("stored", r.Source);
    }

    [Fact]
    public void ADegenerateStoredWindowFallsBackToDefaults()
    {
        var degenerate = new[] { 10.0, 20.0, 10.0, 260.0 }; // zero width
        Assert.Equal(PlotAreaKind.Layout, PlotLayoutMath.ResolveArea(null, false, StoredPlotType.Window, degenerate, null, false).Area);
        Assert.Equal(PlotAreaKind.Extents, PlotLayoutMath.ResolveArea(null, false, StoredPlotType.Window, degenerate, null, true).Area);
        Assert.Equal(PlotAreaKind.Layout, PlotLayoutMath.ResolveArea(null, false, StoredPlotType.Window, null, null, false).Area);
    }

    [Theory]
    [InlineData(StoredPlotType.Layout, PlotAreaKind.Layout)]
    [InlineData(StoredPlotType.Extents, PlotAreaKind.Extents)]
    [InlineData(StoredPlotType.Display, PlotAreaKind.Display)]
    [InlineData(StoredPlotType.Limits, PlotAreaKind.Limits)]
    public void StoredAreaKindsMapThrough(StoredPlotType storedType, PlotAreaKind expected)
        => Assert.Equal(expected, PlotLayoutMath.ResolveArea(null, false, storedType, null, null, false).Area);

    [Fact]
    public void AStoredLayoutIgnoresALeftoverWindowBox()
    {
        // live-smoke-20261030 fix4: a layout stored as Layout can still carry a stale window
        // box; it must resolve to Layout, never to Window (the r2 off-by-one plotted it as
        // Window with whatever box was left over).
        var r = PlotLayoutMath.ResolveArea(null, false, StoredPlotType.Layout, SheetWindow, null, false);
        Assert.Equal(PlotAreaKind.Layout, r.Area);
        Assert.Equal("stored", r.Source);
        Assert.Null(r.Window);
    }

    [Fact]
    public void StoredNamedViewNeedsTheStoredViewName()
    {
        Assert.Equal(PlotAreaKind.View, PlotLayoutMath.ResolveArea(null, false, StoredPlotType.View, null, "SHEET-01", false).Area);
        // A stored View without a name cannot be applied: defaults instead of guessing.
        Assert.Equal(PlotAreaKind.Layout, PlotLayoutMath.ResolveArea(null, false, StoredPlotType.View, null, null, false).Area);
        Assert.Equal(PlotAreaKind.Layout, PlotLayoutMath.ResolveArea(null, false, StoredPlotType.View, null, " ", false).Area);
    }

    [Fact]
    public void NoStoredTypeKeepsTheExistingDefaults()
    {
        Assert.Equal(PlotAreaKind.Extents, PlotLayoutMath.ResolveArea(null, false, null, null, null, true).Area);
        Assert.Equal(PlotAreaKind.Layout, PlotLayoutMath.ResolveArea(null, false, null, null, null, false).Area);
    }
}

/// <summary>D12: an empty PDF is a failure for every area, no longer a gated warning.</summary>
public sealed class D12EmptyPdfGateTests
{
    [Fact]
    public void AnEmptyPdfAlwaysFails()
    {
        foreach (var area in Enum.GetValues<PlotAreaKind>())
        {
            foreach (var planHadContent in new[] { true, false })
            {
                var d = PlotPdfGate.DecideCoverage(area, fit: true, planEstimateTiny: null, planHadContent, "empty", false, null, null, null);
                Assert.True(d.Failed, $"empty pdf must fail for area={area} planHadContent={planHadContent}");
                Assert.Equal(PlotPdfGate.ContentEmptyCode, d.FailureCode);
                Assert.Null(d.Warning);
            }
        }
    }

    [Fact]
    public void AnEmptyPdfOnAnUnfilledWindowStillFails()
    {
        // A window the caller expected to be small still must not produce an empty sheet.
        var d = PlotPdfGate.DecideCoverage(PlotAreaKind.Window, fit: true, planEstimateTiny: true, planHadContent: false, "empty", false, null, null, null);
        Assert.True(d.Failed);
        Assert.Equal(PlotPdfGate.ContentEmptyCode, d.FailureCode);
    }

    [Fact]
    public void AContentPdfStillWarnsOnlyWhenTinyOutsideTheGates()
    {
        var d = PlotPdfGate.DecideCoverage(PlotAreaKind.Layout, fit: true, planEstimateTiny: null, planHadContent: true, "ok", true, 0.2, 0.3, null);
        Assert.False(d.Failed);
        Assert.NotNull(d.Warning);
    }

    [Fact]
    public void AnUnreadablePdfStillWarnsAndDoesNotFail()
    {
        var d = PlotPdfGate.DecideCoverage(PlotAreaKind.Window, fit: true, planEstimateTiny: null, planHadContent: true, "unavailable", false, null, null, "no page content");
        Assert.False(d.Failed);
        Assert.Equal(PlotPdfGate.SourceUnavailable, d.CoverageSource);
        Assert.NotNull(d.Warning);
    }
}

/// <summary>
/// review-wp2-r1 finding 6 (D12): when the plot area comes from the stored page setup, the
/// stored centring/offset wins — the r1 code wrote centered=true over it whenever the caller
/// had not passed centered. An explicit caller centered always wins.
/// </summary>
public sealed class D12StoredCentringTests
{
    [Fact]
    public void AStoredAreaWithNoCallerCentringKeepsTheStoredCentring()
    {
        var on = PlotCentring.Resolve(null, "stored", storedCentered: true);
        Assert.True(on.Centered);
        Assert.True(on.KeepStored);

        var off = PlotCentring.Resolve(null, "stored", storedCentered: false);
        Assert.False(off.Centered);
        Assert.True(off.KeepStored);
    }

    [Fact]
    public void AnExplicitCallerCentringAlwaysWins()
    {
        foreach (var source in new[] { "stored", "requested", "default_layout", "default_model_extents" })
        {
            var c = PlotCentring.Resolve(true, source, storedCentered: false);
            Assert.True(c.Centered);
            Assert.False(c.KeepStored);

            c = PlotCentring.Resolve(false, source, storedCentered: true);
            Assert.False(c.Centered);
            Assert.False(c.KeepStored);
        }
    }

    [Fact]
    public void NonStoredAreasKeepTheCenteredDefault()
    {
        foreach (var source in new[] { "requested", "default_layout", "default_extents_because_paper_differs_from_layout", "default_model_extents", null })
        {
            var c = PlotCentring.Resolve(null, source, storedCentered: false);
            Assert.True(c.Centered);
            Assert.False(c.KeepStored);
        }
    }
}

/// <summary>
/// review-wp2-r2 M1 (D12): the -PLOT offset-prompt answer on engine="command". A stored page
/// setup answers "" (Enter = keep the stored offset AND the stored centred flag) — the r2
/// defect answered "0,0", which actively overrides a stored, uncentred Window/View offset.
/// live-smoke-20261030 fix4: Layout and View answer "" unconditionally — the api engine never
/// writes centring/origin for them, and a Layout-area -PLOT prompt has no [Center] option on
/// AutoCAD 2025 — so "0,0" (which reset a stored Layout's offset live) and "Center" are both
/// wrong there.
/// </summary>
public sealed class D12LegacyOffsetPromptTests
{
    [Theory]
    [InlineData(PlotAreaKind.Window)]
    [InlineData(PlotAreaKind.View)]
    [InlineData(PlotAreaKind.Layout)]
    [InlineData(PlotAreaKind.Extents)]
    public void AStoredAreaAnswersEnterSoTheStoredOffsetSurvives(PlotAreaKind area)
    {
        // KeepStored is only produced for a stored source: both stored centring states keep
        // the prompt's stored default — "0,0" would clobber a stored offset, "x,y" could
        // clobber a stored centred flag.
        foreach (var storedCentered in new[] { true, false })
        {
            var c = PlotCentring.Resolve(null, "stored", storedCentered);
            Assert.True(c.KeepStored);
            Assert.Equal("", c.OffsetPromptAnswer(area));
        }
    }

    [Theory]
    [InlineData(PlotAreaKind.Layout)]
    [InlineData(PlotAreaKind.View)]
    public void LayoutAndViewAlwaysAnswerEnterLikeTheApiEngine(PlotAreaKind area)
    {
        // live-smoke-20261030 fix4: for Layout and View the api engine never calls
        // SetPlotCentered/SetPlotOrigin — the CopyFrom'd stored offset and centred flag always
        // stand — so the command engine answers Enter in EVERY centring state to match, and a
        // Layout-area prompt has no [Center] option anyway (AutoCAD 2025 -PLOT capture).
        foreach (var centered in new bool?[] { true, false, null })
        {
            foreach (var source in new[] { "stored", "requested", "default_layout", null })
            {
                Assert.Equal("", PlotCentring.Resolve(centered, source, storedCentered: false).OffsetPromptAnswer(area));
            }
        }
    }

    [Fact]
    public void AnExplicitCentredNonLayoutAreaAnswersCenter()
    {
        foreach (var area in new[] { PlotAreaKind.Window, PlotAreaKind.Extents, PlotAreaKind.Display, PlotAreaKind.Limits })
        {
            Assert.Equal("Center", PlotCentring.Resolve(true, "requested", false).OffsetPromptAnswer(area));
            Assert.Equal("Center", PlotCentring.Resolve(null, "default_model_extents", false).OffsetPromptAnswer(area));
        }
    }

    [Fact]
    public void AnExplicitUncentredAnswerKeepsTheOriginOffset()
    {
        // Caller passed centered=false on a stored window: KeepStored is off, so "0,0" is the
        // honest uncentred answer — the api engine agrees (it writes SetPlotCentered(false) +
        // SetPlotOrigin(0,0) for window/extents/display/limits).
        var c = PlotCentring.Resolve(false, "stored", storedCentered: true);
        Assert.False(c.KeepStored);
        Assert.Equal("0,0", c.OffsetPromptAnswer(PlotAreaKind.Window));
        foreach (var area in new[] { PlotAreaKind.Extents, PlotAreaKind.Display, PlotAreaKind.Limits })
        {
            Assert.Equal("0,0", c.OffsetPromptAnswer(area));
        }
    }
}

/// <summary>
/// live-smoke-20261030 fix4-r4 (review F1): an explicit caller centered on a Layout or View
/// area is dropped by BOTH engines — api never writes centring/origin there and command
/// answers Enter at the offset prompt — so the caller gets a warning like the scale=fit one
/// instead of silence.
/// </summary>
public sealed class D12CenteredIgnoredWarningTests
{
    [Theory]
    [InlineData(PlotAreaKind.Layout, true)]
    [InlineData(PlotAreaKind.Layout, false)]
    [InlineData(PlotAreaKind.View, true)]
    [InlineData(PlotAreaKind.View, false)]
    public void AnExplicitCenteredOnLayoutOrViewWarns(PlotAreaKind area, bool centered)
    {
        var warning = PlotCentring.CenteredIgnoredWarning(centered, area);
        Assert.NotNull(warning);
        Assert.Contains("centered does not apply", warning, StringComparison.Ordinal);
        Assert.Contains($"plotArea={area.ToString().ToLowerInvariant()}", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void NoWarningWhenCenteredWasNotPassed()
    {
        foreach (var area in Enum.GetValues<PlotAreaKind>())
        {
            Assert.Null(PlotCentring.CenteredIgnoredWarning(null, area));
        }
    }

    [Theory]
    [InlineData(PlotAreaKind.Window)]
    [InlineData(PlotAreaKind.Extents)]
    [InlineData(PlotAreaKind.Display)]
    [InlineData(PlotAreaKind.Limits)]
    public void NoWarningForTheAreasThatHonourCentring(PlotAreaKind area)
    {
        Assert.Null(PlotCentring.CenteredIgnoredWarning(true, area));
        Assert.Null(PlotCentring.CenteredIgnoredWarning(false, area));
    }
}

/// <summary>D12 plugin wiring guards.</summary>
public sealed class D12StoredPlotAreaSourceGuards
{
    private static string PlotSource()
        => File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), "PluginCommandProcessor.Plot.cs"));

    [Fact]
    public void TheStoredPlotTypeFeedsAreaResolutionAndTheStoredWindow()
    {
        var source = PlotSource();
        Assert.Contains("PlotLayoutMath.ResolveArea(", source, StringComparison.Ordinal);
        Assert.Contains("layout.PlotWindowArea", source, StringComparison.Ordinal);
        Assert.Contains("layout.PlotType switch", source, StringComparison.Ordinal);
        Assert.Contains("storedPlotType = storedPlotType?.ToString()", source, StringComparison.Ordinal);
        Assert.Contains("AcPlotType.View", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStoredPlotTypeIsConvertedByNameNeverCastToInt()
    {
        // live-smoke-20261030 fix4: the managed PlotType is 0-based but the r2 code read it
        // through the 1-based ActiveX AcPlotType numbering — stored Window resolved as View,
        // stored View as Limits (eInvalidInput), stored Layout as Window. The enum must be
        // converted member-by-member by name; an int cast must never come back.
        var source = PlotSource();
        Assert.DoesNotContain("(int)layout.PlotType", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Convert.ToInt32(layout.PlotType", source, StringComparison.Ordinal);
        foreach (var member in new[] { "Display", "Extents", "Limits", "View", "Window", "Layout" })
        {
            Assert.Contains($"AcPlotType.{member} => StoredPlotType.{member}", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void CentringResolvesThroughTheSharedPolicy()
    {
        // finding 6 (D12): no unconditional `args.Centered ?? true` — the stored setup's
        // centring/offset must survive, and SetPlotOrigin must only fire on an explicit or
        // defaulted non-stored centered=false.
        var source = PlotSource();
        Assert.Contains("PlotCentring.Resolve(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("args.Centered ?? true", source, StringComparison.Ordinal);
        Assert.Contains("centring.KeepStored", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLegacyCommandPathHonoursKeepStored()
    {
        // review-wp2-r2 M1: the -PLOT offset prompt is answered by the shared policy — never a
        // bare "0,0"/"Center" decision that drops KeepStored (and with it the stored offset).
        var source = PlotSource();
        Assert.Contains("PlotCentring centring", source, StringComparison.Ordinal);
        Assert.Contains("centring.OffsetPromptAnswer(area)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("centered && area != PlotAreaKind.Layout ? \"Center\" : \"0,0\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLayoutAndViewPathWarnsOnAnExplicitCentered()
    {
        // review-wp2-r5 F1: D12CenteredIgnoredWarningTests pins the shared helper, but nothing
        // guarded that the plugin calls it — deleting the call hunk left the suite green. The
        // Layout/View branch must warn on the raw caller centered, like the scale=fit warning.
        var source = PlotSource();
        Assert.Contains("CenteredIgnoredWarning(args.Centered", source, StringComparison.Ordinal);
    }
}
