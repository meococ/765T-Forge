using System.Text.RegularExpressions;
using Forge.Shared;

namespace Forge.Tests;

/// <summary>fix-smoke-20261029 D1/D7: document activation and save-as readback decisions.</summary>
public sealed class DocumentReadBackTests
{
    [Theory]
    [InlineData("forge_doc_new", true)]
    [InlineData("forge_doc_open", true)]
    [InlineData("FORGE_DOC_ACTIVATE", true)]
    [InlineData("forge_doc_save", false)]
    [InlineData("forge_draw_line", false)]
    public void DocumentSwitchingToolsRunInApplicationContext(string tool, bool expected)
        => Assert.Equal(expected, DocumentContextPolicy.RunsInApplicationContext(tool));

    [Fact]
    public void LifecycleToolsPollUntilShortlyBeforeTheRequestDeadline()
    {
        Assert.Equal(55_000, DocumentContextPolicy.PollWindowMs("forge_doc_new", 60_000));
        Assert.Equal(55_000, DocumentContextPolicy.PollWindowMs("forge_doc_open", 60_000));
        Assert.Equal(DocumentContextPolicy.ActivatePollMs, DocumentContextPolicy.PollWindowMs("forge_doc_activate", 60_000));
        Assert.Equal(DocumentContextPolicy.ActivatePollMs, DocumentContextPolicy.PollWindowMs("forge_doc_new", 3_000));
        Assert.Contains("doc_new_unconfirmed", DocumentContextPolicy.UnconfirmedCodes);
        Assert.Contains("doc_open_unconfirmed", DocumentContextPolicy.UnconfirmedCodes);
    }

    [Theory]
    [InlineData(true, "Drawing6.dwg", "Drawing6.dwg", true)]
    [InlineData(true, @"C:\work\Plan.dwg", "plan.DWG", true)]
    [InlineData(true, "Drawing6.dwg", "Drawing5.dwg", false)] // r1: DWGNAME still named the old drawing
    [InlineData(false, "Drawing6.dwg", "Drawing6.dwg", false)] // r1: the old drawing was still the active document
    [InlineData(true, null, "Drawing6.dwg", false)]
    [InlineData(true, "Drawing6.dwg", null, false)]
    public void ActivationIsConfirmedOnlyWhenTheActiveDocumentAndDwgnameAgree(bool activeIsTarget, string? target, string? dwgName, bool expected)
        => Assert.Equal(expected, DocumentReadBack.IsConfirmed(activeIsTarget, target, dwgName));

    private static readonly DateTime Start = new(2026, 10, 30, 1, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SaveAsIsConfirmedOnlyWhenTheFileIsFreshAndTheDocumentCarriesTheNewName()
    {
        const string target = @"D:\out\Renamed.dwg";
        Assert.Equal(DocumentReadBack.SaveConfirmed,
            DocumentReadBack.SaveDecision(target, target, target, "Renamed.dwg", true, 30_000, Start.AddSeconds(1), Start));
        Assert.Equal(DocumentReadBack.SaveConfirmed,
            DocumentReadBack.SaveDecision(target, "d:/out/renamed.dwg", target, "renamed.dwg", true, 30_000, Start.AddSeconds(-1), Start));

        // r1: the file was written but the open document kept its old name.
        Assert.Equal(DocumentReadBack.SaveNotRenamed,
            DocumentReadBack.SaveDecision(target, @"D:\out\Drawing5.dwg", @"D:\out\Drawing5.dwg", "Drawing5.dwg", true, 30_000, Start.AddSeconds(1), Start));
        Assert.Equal(DocumentReadBack.SaveNotRenamed,
            DocumentReadBack.SaveDecision(target, target, target, "Drawing5.dwg", true, 30_000, Start.AddSeconds(1), Start));
        Assert.Equal(DocumentReadBack.SaveNotRenamed,
            DocumentReadBack.SaveDecision(target, null, target, "Renamed.dwg", true, 30_000, Start.AddSeconds(1), Start));

        Assert.Equal(DocumentReadBack.SaveNotWritten,
            DocumentReadBack.SaveDecision(target, target, target, "Renamed.dwg", false, 0, DateTime.MinValue, Start));
        Assert.Equal(DocumentReadBack.SaveNotWritten,
            DocumentReadBack.SaveDecision(target, target, target, "Renamed.dwg", true, 0, Start.AddSeconds(1), Start));
        Assert.Equal(DocumentReadBack.SaveNotWritten,
            DocumentReadBack.SaveDecision(target, target, target, "Renamed.dwg", true, 30_000, Start.AddMinutes(-5), Start));
    }

    [Theory]
    [InlineData(@"C:\a\b.dwg", "c:/A/B.DWG", true)]
    [InlineData(@"C:\a\b.dwg", @"C:\a\c.dwg", false)]
    [InlineData(null, @"C:\a\b.dwg", false)]
    [InlineData(" ", @"C:\a\b.dwg", false)]
    public void PathsCompareCaseAndSlashInsensitively(string? a, string? b, bool expected)
        => Assert.Equal(expected, DocumentReadBack.SamePath(a, b));
}

/// <summary>fix-smoke-20261029 D2: expected hatch definition after scale and angle.</summary>
public sealed class HatchMathTests
{
    private const double Deg = Math.PI / 180;

    [Fact]
    public void Ansi31AtScale20Angle30MatchesTheHeadlessReadback()
    {
        // hatch2-old: ANSI31 (ISO) first line at 45° with 3.175 spacing; PSAP order gave 75° and 63.5.
        var (angle, spacing) = HatchMath.ExpectedDefinition(45 * Deg, 3.175, 20, 30 * Deg);
        Assert.Equal(75 * Deg, angle, 9);
        Assert.Equal(63.5, spacing, 9);
    }

    [Fact]
    public void ExpectedAngleWrapsAndSpacingIgnoresSign()
    {
        var (angle, spacing) = HatchMath.ExpectedDefinition(300 * Deg, 2, -3, 90 * Deg);
        Assert.Equal(30 * Deg, angle, 9);
        Assert.Equal(6, spacing, 9);
    }

    [Fact]
    public void PerpendicularSpacingReadsTheWorldOffsetOfARotatedLine()
    {
        // ANSI31: offset (0, 3.175) in the line's frame is (-2.245, 2.245) in world at 45°.
        var dx = -3.175 * Math.Sin(45 * Deg);
        var dy = 3.175 * Math.Cos(45 * Deg);
        Assert.Equal(3.175, HatchMath.PerpendicularSpacing(45 * Deg, dx, dy), 9);
        Assert.Equal(0, HatchMath.PerpendicularSpacing(0, 5, 0), 9); // offset along the line only
    }

    [Fact]
    public void AnglesWrapIntoOneTurn()
    {
        Assert.Equal(270 * Deg, HatchMath.WrapAngle(-90 * Deg), 9);
        Assert.Equal(0, HatchMath.WrapAngle(2 * Math.PI));
        Assert.Equal(0, HatchMath.WrapAngle(2 * Math.PI - 1e-9));
        Assert.Equal(10 * Deg, HatchMath.WrapAngle(370 * Deg), 9);
    }

    [Fact]
    public void LineAnglesAreEqualModuloHalfATurn()
    {
        Assert.True(HatchMath.SameLineAngle(45 * Deg, 225 * Deg));
        Assert.True(HatchMath.SameLineAngle(0, Math.PI - 1e-9));
        Assert.False(HatchMath.SameLineAngle(45 * Deg, 75 * Deg));
    }

    [Fact]
    public void SpacingUsesARelativeTolerance()
    {
        Assert.True(HatchMath.SameSpacing(63.5, 63.5 + 1e-7));
        Assert.False(HatchMath.SameSpacing(63.5, 3.175)); // r1-style "scale ignored" result
        Assert.False(HatchMath.SameSpacing(63.5, 63.6));
    }
}

/// <summary>fix-smoke-20261029 D4: making the plotted layout current, and restoring it.</summary>
public sealed class PlotLayoutSwitchTests
{
    [Theory]
    [InlineData("Model", "Layout1", false, 1, 2, false, PlotLayoutAction.Switch)] // r1: Layout1 from Model
    [InlineData("Layout1", "Layout1", false, 0, 2, false, PlotLayoutAction.SwitchToPaperSpace)]
    [InlineData("Layout1", "Layout1", false, 0, 1, false, PlotLayoutAction.None)]
    [InlineData("Model", "Model", true, 1, 2, false, PlotLayoutAction.None)]
    [InlineData("Layout1", "Model", true, 0, 1, false, PlotLayoutAction.Switch)]
    [InlineData("Model", "Model", true, 0, 1, false, PlotLayoutAction.Switch)]
    [InlineData("Model", "Layout1", false, 1, 2, true, PlotLayoutAction.DeferValidation)]
    [InlineData("layout1", "Layout1", false, 0, 2, true, PlotLayoutAction.None)]
    [InlineData(null, "Layout1", false, 0, 1, false, PlotLayoutAction.Switch)]
    public void PlanSwitchesOnlyForARealPlot(string? current, string target, bool targetIsModel, int tilemode, int cvport, bool dryRun, PlotLayoutAction expected)
        => Assert.Equal(expected, PlotLayoutMath.PlanLayoutSwitch(current, target, targetIsModel, tilemode, cvport, dryRun));

    [Theory]
    [InlineData("Layout1", "Layout1", "Layout1", false, 0, 1, true)]
    [InlineData("Layout1", "Layout1", "Layout1", false, 0, 2, false)] // a model-space viewport is active
    [InlineData("Layout1", "Model", "Layout1", false, 0, 1, false)] // CTAB lags
    [InlineData("Layout1", "Layout1", "Model", false, 0, 1, false)]
    [InlineData("Layout1", "Layout1", "Layout1", false, 1, 1, false)]
    [InlineData("Model", "Model", "Model", true, 1, 2, true)]
    [InlineData("Model", "Model", "Model", true, 0, 1, false)]
    public void TheGateNeedsCtabCurrentLayoutTilemodeAndCvport(string target, string? ctab, string? current, bool targetIsModel, int tilemode, int cvport, bool expected)
        => Assert.Equal(expected, PlotLayoutMath.IsLayoutReady(target, ctab, current, targetIsModel, tilemode, cvport));

    [Fact]
    public void RestoreMismatchesNameEachDifference()
    {
        Assert.Empty(PlotLayoutMath.LayoutRestoreMismatches("Model", 1, 2, "MODEL", 1, 2));

        var one = PlotLayoutMath.LayoutRestoreMismatches("Model", 1, 2, "Layout1", 1, 2);
        Assert.Single(one);
        Assert.Contains("CTAB", one[0], StringComparison.Ordinal);

        var all = PlotLayoutMath.LayoutRestoreMismatches("Model", 1, 2, "Layout1", 0, 1);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public void LayoutNamesCompareTrimmedAndCaseInsensitive()
    {
        Assert.True(PlotLayoutMath.SameLayout(" Layout1 ", "layout1"));
        Assert.False(PlotLayoutMath.SameLayout(null, "Layout1"));
        Assert.False(PlotLayoutMath.SameLayout("Layout1", "Layout2"));
    }
}

/// <summary>fix-smoke-20261029 D5: looksTiny and the plot gate come from the PDF readback.</summary>
public sealed class PlotPdfCoverageTests
{
    private static PlotCoverageDecision Decide(PlotAreaKind area, bool fit, string status, bool tiny, bool? planTiny = null, bool planHadContent = true)
        => PlotPdfGate.DecideCoverage(area, fit, planTiny, planHadContent, status, tiny, tiny ? 0.08 : 0.9, tiny ? 0.05 : 0.7, status == "ok" || status == "empty" ? null : "no content stream");

    [Fact]
    public void ATinyExtentsFitPlotFails()
    {
        // r1: Model extents from stale header values; the PDF inked a small corner while the plan said looksTiny=false.
        var d = Decide(PlotAreaKind.Extents, fit: true, "ok", tiny: true, planTiny: false);
        Assert.True(d.Failed);
        Assert.Equal(PlotPdfGate.ContentTinyCode, d.FailureCode);
        Assert.Equal(true, d.LooksTiny);
        Assert.Equal(PlotPdfGate.SourcePdf, d.CoverageSource);
    }

    [Fact]
    public void AFilledPlotPassesWithLooksTinyFalse()
    {
        var d = Decide(PlotAreaKind.Extents, fit: true, "ok", tiny: false);
        Assert.False(d.Failed);
        Assert.Equal(false, d.LooksTiny);
        Assert.Null(d.Warning);
    }

    [Theory]
    [InlineData(PlotAreaKind.Window, true, false, true)] // the plan expected a filled sheet
    [InlineData(PlotAreaKind.Window, true, null, false)]
    [InlineData(PlotAreaKind.Window, true, true, false)]
    [InlineData(PlotAreaKind.Layout, false, null, false)]
    [InlineData(PlotAreaKind.Extents, false, false, false)] // explicit scale: small can be intended
    public void OtherAreasOnlyWarnUnlessAFilledSheetWasExpected(PlotAreaKind area, bool fit, bool? planTiny, bool fails)
    {
        var d = Decide(area, fit, "ok", tiny: true, planTiny: planTiny);
        Assert.Equal(fails, d.Failed);
        Assert.Equal(true, d.LooksTiny);
        if (!fails)
        {
            Assert.NotNull(d.Warning);
        }
    }

    [Fact]
    public void AnEmptyPdfFailsWhetherOrNotTheDrawingHadContent()
    {
        var d = Decide(PlotAreaKind.Extents, fit: true, "empty", tiny: false, planHadContent: true);
        Assert.Equal(PlotPdfGate.ContentEmptyCode, d.FailureCode);
        Assert.Equal(PlotPdfGate.SourcePdfEmpty, d.CoverageSource);

        // fix-smoke-20261029-r2 D12: nothing on the sheet is never a success, whatever the plan expected.
        var blank = Decide(PlotAreaKind.Extents, fit: true, "empty", tiny: false, planHadContent: false);
        Assert.True(blank.Failed);
        Assert.Equal(PlotPdfGate.ContentEmptyCode, blank.FailureCode);
    }

    [Fact]
    public void AnUnreadablePdfLeavesLooksTinyUnknown()
    {
        var d = Decide(PlotAreaKind.Extents, fit: true, "unavailable", tiny: false);
        Assert.False(d.Failed);
        Assert.Null(d.LooksTiny);
        Assert.Equal(PlotPdfGate.SourceUnavailable, d.CoverageSource);
        Assert.Contains("not verified", d.Warning, StringComparison.Ordinal);
        Assert.Contains("no content stream", d.Warning, StringComparison.Ordinal);
    }

    /// <summary>The plugin's mapping from the PDF readback to the gate (PlotOnLayout).</summary>
    private static PlotCoverageDecision FromPdf(string fixture, PlotAreaKind area, bool fit, bool? planTiny)
    {
        var pdf = PdfContentBox.Read(Path.Combine(PluginDispatchSyncTests.RepoPath("tests", "Forge.Tests", "Fixtures"), fixture));
        var status = pdf.Status switch { PdfInkStatus.Ok => "ok", PdfInkStatus.Empty => "empty", _ => "unavailable" };
        return PlotPdfGate.DecideCoverage(area, fit, planTiny, true, status, pdf.LooksTiny == true, pdf.FillX, pdf.FillY, pdf.Reason);
    }

    [Fact]
    public void TheR1ModelExtentsPdfNowFailsInsteadOfReportingLooksTinyFalse()
    {
        // r1 live: Model, plotArea=extents, scale=fit; the plan said looksTiny=false and the call passed.
        var d = FromPdf("plot-r1-model-stale-extents.pdf", PlotAreaKind.Extents, fit: true, planTiny: false);
        Assert.Equal(PlotPdfGate.ContentTinyCode, d.FailureCode);
        Assert.Equal(true, d.LooksTiny);

        var window = FromPdf("plot-r1-window-a3.pdf", PlotAreaKind.Window, fit: true, planTiny: false);
        Assert.False(window.Failed);
        Assert.Equal(false, window.LooksTiny);
    }

    [Fact]
    public void TheCoverageGateTurnsAPassingProbeIntoANamedFailure()
    {
        var probe = new PdfProbeResult { Exists = true, Bytes = 9000, NonEmpty = true, PageCount = 1, Passed = true, Message = "PDF ok" };
        var data = new { outputPath = "x.pdf" };
        var tiny = Decide(PlotAreaKind.Extents, fit: true, "ok", tiny: true);

        var failed = PlotPdfGate.FromProbe("c1", probe, data, tiny);
        Assert.False(failed.Ok);
        Assert.Equal(PlotPdfGate.ContentTinyCode, failed.Error!.Code);
        Assert.False(failed.Verification!.Passed);
        Assert.Same(data, failed.Data);

        var ok = PlotPdfGate.FromProbe("c2", probe, data, Decide(PlotAreaKind.Extents, fit: true, "ok", tiny: false));
        Assert.True(ok.Ok);
        Assert.True(ok.Verification!.Passed);

        var probeFailed = PlotPdfGate.FromProbe("c3", new PdfProbeResult { Exists = true, Passed = false, Message = "no %PDF header" }, data, Decide(PlotAreaKind.Extents, fit: true, "ok", tiny: false));
        Assert.Equal(PlotPdfGate.FailedCode, probeFailed.Error!.Code);

        Assert.True(PlotPdfGate.FromProbe("c4", probe, data).Ok);
    }
}

/// <summary>fix-smoke-20261029 D6: an ATTRIB on layer 0 takes its own INSERT's effective layer.</summary>
public sealed class AttributeEffectiveLayerTests
{
    [Theory]
    [InlineData("0", "A-DOOR", "A-UNIT", "A-DOOR")] // r1: nested INNER(A-DOOR) inside OUTER(A-UNIT) reported A-UNIT
    [InlineData("0", "0", "A-UNIT", "A-UNIT")] // INNER on 0 follows OUTER
    [InlineData("0", "A-DOOR", null, "A-DOOR")] // top level: r1 reported "0"
    [InlineData("A-TAG", "A-DOOR", "A-UNIT", "A-TAG")] // own layer wins
    [InlineData("0", "0", null, "0")]
    public void AttributeLayerFollowsItsInsert(string attrib, string insert, string? insertParent, string expected)
        => Assert.Equal(expected, BlockWalk.AttributeEffectiveLayer(attrib, insert, insertParent));
}

/// <summary>Source guards for fixes that only the plugin (not unit-testable without AutoCAD) can carry.</summary>
public sealed class FixSmoke20261029SourceGuards
{
    private static string PluginSource(string file) => File.ReadAllText(Path.Combine(PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin"), file));

    /// <summary>The text of one method: from its signature to the first closing brace at member indent.</summary>
    private static string MethodBody(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"method not found: {signature}");
        var end = Regex.Match(source[start..], @"\n    \}\r?\n");
        Assert.True(end.Success, $"end of method not found: {signature}");
        return source.Substring(start, end.Index + end.Length);
    }

    /// <summary>The method text without // comments (comments may name the calls that must not be made).</summary>
    private static string MethodCode(string source, string signature)
        => Regex.Replace(MethodBody(source, signature), @"//[^\r\n]*", "");

    [Fact]
    public void D2_HatchPatternIsSetBeforeAndAfterScaleAndAngle()
    {
        var body = MethodBody(PluginSource("PluginCommandProcessor.Draw.cs"), "private static void ApplyHatchPattern(");
        var first = body.IndexOf("SetHatchPattern(", StringComparison.Ordinal);
        var scale = body.IndexOf("PatternScale =", StringComparison.Ordinal);
        var angle = body.IndexOf("PatternAngle =", StringComparison.Ordinal);
        var last = body.LastIndexOf("SetHatchPattern(", StringComparison.Ordinal);
        Assert.True(first >= 0 && first < scale && scale < angle && angle < last, "order must be SetHatchPattern, PatternScale, PatternAngle, SetHatchPattern");
    }

    [Fact]
    public void D2_EveryHatchPathUsesTheSharedOrderAndReadback()
    {
        var pluginDir = PluginDispatchSyncTests.RepoPath("src", "Forge.Plugin");
        var assignments = Directory.GetFiles(pluginDir, "*.cs")
            .SelectMany(f => File.ReadAllLines(f).Select(l => (File: Path.GetFileName(f), Line: l)))
            .Where(x => Regex.IsMatch(x.Line, @"\.Pattern(Angle|Scale)\s*=[^=]"))
            .ToList();
        Assert.Equal(2, assignments.Count); // only inside ApplyHatchPattern
        Assert.All(assignments, a => Assert.Equal("PluginCommandProcessor.Draw.cs", a.File));

        var draw = MethodBody(PluginSource("PluginCommandProcessor.Draw.cs"), "private static ObjectId CreateHatchOnBoundary(");
        Assert.Contains("ApplyHatchPattern(", draw, StringComparison.Ordinal);
        Assert.Contains("VerifyHatch(", draw, StringComparison.Ordinal);

        var wall = MethodBody(PluginSource("PluginCommandProcessor.Spatial.cs"), "private static ForgeResult DrawWall(");
        Assert.Equal(2, Regex.Matches(wall, @"ApplyHatchPattern\(").Count);
        Assert.Equal(2, Regex.Matches(wall, @"VerifyHatch\(").Count);
        Assert.Contains("catch (DrawStepException", wall, StringComparison.Ordinal);
    }

    [Fact]
    public void D7_SaveRenamesTheDocumentAndIsReadBack()
    {
        var body = MethodCode(PluginSource("PluginCommandProcessor.cs"), "private ForgeResult SaveDocument(");
        Assert.DoesNotContain(".Save()", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"SaveAs\([^,()]*,[^,()]*\)", body); // the 2-argument SaveAs writes a copy only
        Assert.Contains("SaveAs(target, true,", body, StringComparison.Ordinal);
        Assert.Contains("DocumentReadBack.SaveDecision(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void D1_DocumentToolsAreRoutedToApplicationContextFirst()
    {
        var server = PluginSource("NamedPipePluginServer.cs");
        var body = MethodBody(server, "private Task<ForgeResult> ExecuteOnMainThreadAsync(");
        var route = body.IndexOf("DocumentContextPolicy.Route(", StringComparison.Ordinal);
        var appLane = body.IndexOf("DocDispatchDecision.ApplicationLane", StringComparison.Ordinal);
        var docContext = body.IndexOf("ExecuteInCommandContextAsync(command)", StringComparison.Ordinal);
        // D1 (kept under D13): the Shared decision table routes doc_new/doc_open/doc_activate to
        // the Idle lane before any document-context path can run.
        Assert.True(route >= 0 && appLane > route && appLane < docContext,
            "doc_new/doc_open/doc_activate must go to the Idle lane before any document-context path");
        Assert.Contains("ExecuteDocActivateWhenIdleAsync", body, StringComparison.Ordinal);
    }

    [Fact]
    public void D3_UndoReportsOnlyTheVerifiedOutcome()
    {
        var body = MethodCode(PluginSource("PluginCommandProcessor.Entities.cs"), "private static ForgeResult Undo(");
        Assert.Contains("UndoVerifier.Run(", body, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"undone\s*=\s*true", body);
    }

    [Fact]
    public void D4_OnlyThePlotSwitchAndItsRestoreChangeTheCurrentLayout()
    {
        var plot = PluginSource("PluginCommandProcessor.Plot.cs");
        Assert.Equal(2, Regex.Matches(plot, @"CurrentLayout\s*=[^=]").Count);
        var legacy = MethodCode(plot, "private static void RunLegacyPlotCommand(");
        Assert.DoesNotMatch(@"CurrentLayout\s*=[^=]", legacy);
        var restore = MethodBody(plot, "private static void RestoreLayout(");
        Assert.Matches(@"CurrentLayout\s*=[^=]", restore);
    }

    [Fact]
    public void D8_DocCloseDecidesEveryRefusalBeforeSavingOrClosingAndNeverTouchesTheClosedDocument()
    {
        var body = MethodCode(PluginSource("PluginCommandProcessor.DocClose.cs"), "private ForgeResult DocClose(");
        var refusal = body.IndexOf("DocClosePolicy.Refusal(", StringComparison.Ordinal);
        var appContext = body.IndexOf("Documents.IsApplicationContext", StringComparison.Ordinal);
        var save = body.IndexOf("SaveAs(path, true, DwgVersion.Current", StringComparison.Ordinal);
        var close = body.IndexOf("doc.CloseAndDiscard()", StringComparison.Ordinal);
        Assert.True(refusal >= 0 && refusal < appContext && appContext < save && save < close, "refusals, then the app-context guard, then save, then close");
        Assert.DoesNotMatch(@"\.Save\(\)", body);
        Assert.DoesNotMatch(@"CloseAndSave", body);
        Assert.DoesNotMatch(@"\bdoc\.", body[(close + "doc.CloseAndDiscard()".Length)..]);

        var readBack = MethodCode(PluginSource("PluginCommandProcessor.DocClose.cs"), "internal static ForgeResult ReadBackClosedDocument(");
        Assert.Contains("ReferenceEquals(d, pending.TargetDocument)", readBack, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"TargetDocument\.", readBack);
    }

    [Fact]
    public void D8_DbmodCountsOnlyWhenDwgnameNamesTheTargetAndTheComFlagIsAlwaysRead()
    {
        var body = MethodCode(PluginSource("PluginCommandProcessor.DocClose.cs"), "private static bool? ReadUnsaved(");
        var confirm = body.IndexOf("DocumentReadBack.IsConfirmed(true, target.Name, dwgName)", StringComparison.Ordinal);
        var dbmod = body.IndexOf("\"DBMOD\"", StringComparison.Ordinal);
        Assert.True(confirm >= 0 && confirm < dbmod, "DBMOD is read only after DWGNAME confirms the target");
        Assert.Contains("com.Saved", body, StringComparison.Ordinal);
        Assert.Contains("DocClosePolicy.Unsaved(dbmodSaysModified, comSaved)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void D8_TheUndoScopeNeverReadsTheDocumentAfterTheCall()
    {
        var ledger = PluginSource("PluginCommandProcessor.UndoLedger.cs");
        Assert.DoesNotMatch(@"scope\.Doc\??\.", ledger);
    }

    [Fact]
    public void D8_DocCloseIsPolledOnTheIdleLane()
    {
        var server = PluginSource("NamedPipePluginServer.cs");
        Assert.Contains("PluginCommandProcessor.ReadBackClosedDocument(command.Id, closing)", server, StringComparison.Ordinal);
    }
}

/// <summary>fix-smoke-20261029 D8: forge_doc_close refusals are a pure decision taken before anything is saved or closed.</summary>
public sealed class DocClosePolicyTests
{
    [Theory]
    [InlineData(0, 3, false, false, false, false, false, false, true, "document_not_found")]
    [InlineData(2, 3, false, false, false, false, false, false, true, "ambiguous_document")]
    [InlineData(1, 1, false, false, false, true, false, false, true, "last_document")]
    [InlineData(1, 2, true, false, false, true, false, false, true, "command_active")]
    [InlineData(1, 2, false, true, false, false, false, false, true, "discard_not_acknowledged")]
    [InlineData(1, 2, false, null, false, false, false, false, true, "discard_not_acknowledged")] // unknown counts as unsaved
    [InlineData(1, 2, false, true, false, true, false, false, true, null)]
    [InlineData(1, 2, false, null, false, true, false, false, true, null)]
    [InlineData(1, 2, false, false, false, false, false, false, true, null)]
    [InlineData(1, 2, false, true, true, false, true, false, true, "save_requires_path")]
    [InlineData(1, 2, false, true, true, false, false, true, true, "document_read_only")]
    [InlineData(1, 2, false, true, true, false, false, false, false, "invalid_path")]
    [InlineData(1, 2, false, true, true, false, false, false, true, null)]
    [InlineData(1, 2, false, null, false, false, true, false, false, "discard_not_acknowledged")]
    public void RefusalsComeInPlanOrder(int matches, int open, bool commandActive, bool? unsaved, bool save, bool ack, bool untitled, bool readOnly, bool isDwg, string? expected)
        => Assert.Equal(expected, DocClosePolicy.Refusal(matches, open, commandActive, unsaved, save, ack, untitled, readOnly, isDwg));

    [Theory]
    [InlineData(true, true, true)]      // DBMOD says modified: unsaved even if COM says saved
    [InlineData(false, false, true)]    // COM says not saved: unsaved even if DBMOD says clean
    [InlineData(null, false, true)]
    [InlineData(true, null, true)]
    [InlineData(false, true, false)]
    [InlineData(null, true, false)]     // DBMOD not trusted (DWGNAME names another drawing): COM decides
    [InlineData(false, null, false)]
    [InlineData(null, null, null)]      // nothing readable: unknown, which the refusal treats as unsaved
    public void UnsavedIfEitherSourceSaysSoAndUnknownWhenNeitherIsReadable(bool? dbmodModified, bool? comSaved, bool? expected)
        => Assert.Equal(expected, DocClosePolicy.Unsaved(dbmodModified, comSaved));

    [Fact]
    public void DocCloseRunsInApplicationContextAndPollsLikeTheOtherLifecycleTools()
    {
        Assert.True(DocumentContextPolicy.RunsInApplicationContext("forge_doc_close"));
        Assert.Contains("doc_close_unconfirmed", DocumentContextPolicy.UnconfirmedCodes);
        Assert.Equal(55_000, DocumentContextPolicy.PollWindowMs("forge_doc_close", 60_000));
    }

    [Fact]
    public void DocCloseIsDestructiveHasNoPreCallBackupAndIsInTheAuthoringProfile()
    {
        var meta = ForgeToolRegistry.Get("forge_doc_close");
        Assert.False(meta.ReadOnly);
        Assert.True(meta.Destructive);
        Assert.False(meta.RequiresBackup);
        Assert.False(meta.Unsafe);
        Assert.True(ToolProfiles.TryGet("authoring", out var authoring));
        Assert.Contains("forge_doc_close", authoring);
    }

    [Theory]
    [InlineData(true, 10, 0, true)]
    [InlineData(true, 10, -1, true)] // coarse file-system tolerance (2 s)
    [InlineData(true, 10, -5, false)]
    [InlineData(true, 0, 0, false)]
    [InlineData(false, 10, 0, false)]
    public void TheSaveBeforeCloseCountsOnlyAFileWrittenDuringTheCall(bool exists, long length, int secondsFromStart, bool expected)
    {
        var start = new DateTime(2026, 10, 30, 3, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, DocumentReadBack.FileWritten(exists, length, start.AddSeconds(secondsFromStart), start));
    }
}
