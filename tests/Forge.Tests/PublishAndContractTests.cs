using Forge.Shared;

namespace Forge.Tests;

public sealed class PublishAndContractTests
{
    [Fact]
    public void PdfProbeFailsOnMissingFile()
    {
        var probe = PdfProbeResult.Probe(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".pdf"), expectedPages: 2);
        Assert.False(probe.Passed);
        Assert.False(probe.Exists);
    }

    [Fact]
    public void PdfProbePassesNonEmptyFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-probe-{Guid.NewGuid():N}.pdf");
        // Minimal PDF-like content with one /Type /Page
        File.WriteAllText(path, "%PDF-1.4\n1 0 obj<< /Type /Page >>endobj\ntrailer\n%%EOF\n");
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 1);
            Assert.True(probe.Exists);
            Assert.True(probe.NonEmpty);
            Assert.True(probe.Passed);
            Assert.Equal(1, probe.PageCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PdfProbeFailsOnNonPdfHeaderMissingEofNullPageCountOrStaleMtime()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-probe-bad-{Guid.NewGuid():N}.pdf");
        try
        {
            File.WriteAllText(path, "NOT_A_PDF\n1 0 obj<< /Type /Page >>endobj\n%%EOF\n");
            var nonPdf = PdfProbeResult.Probe(path, expectedPages: 1);
            Assert.False(nonPdf.Passed);
            Assert.Contains("%PDF-", nonPdf.Message);

            // Missing %%EOF trailer -> truncated/incomplete PDF must fail
            File.WriteAllText(path, "%PDF-1.4\n1 0 obj<< /Type /Page >>endobj\n");
            var noEof = PdfProbeResult.Probe(path, expectedPages: 1);
            Assert.False(noEof.Passed);
            Assert.Contains("%%EOF", noEof.Message);

            // expectedPages specified but no /Type /Page markers (pageCount == null) -> must fail
            File.WriteAllText(path, "%PDF-1.4\n1 0 obj<< /Type /Catalog >>endobj\ntrailer\n%%EOF\n");
            var nullPages = PdfProbeResult.Probe(path, expectedPages: 1);
            Assert.False(nullPages.Passed);
            Assert.Null(nullPages.PageCount);

            File.WriteAllText(path, "%PDF-1.4\n1 0 obj<< /Type /Page >>endobj\ntrailer\n%%EOF\n");
            var future = DateTimeOffset.UtcNow.AddMinutes(5);
            var stale = PdfProbeResult.Probe(path, expectedPages: 1, notBeforeUtc: future);
            Assert.False(stale.Passed);
            Assert.Contains("stale", stale.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void DsdWriterResolvesMultiSheetType5CandidateAndActualPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"forge-dsd5-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var dwgPath = Path.Combine(tempDir, "MTR-Station.dwg");
            var outPdf = Path.Combine(tempDir, "bundle.pdf");
            var layouts = new[] { "A-101", "A-102" };

            var candidates = DsdWriter.GetCandidateOutputPaths(dwgPath, outPdf, layouts, singlePdf: false);
            Assert.Contains(Path.Combine(tempDir, "MTR-Station-A-101.pdf"), candidates, StringComparer.OrdinalIgnoreCase);
            Assert.Contains(Path.Combine(tempDir, "A-101.pdf"), candidates, StringComparer.OrdinalIgnoreCase);

            var sheet1Path = Path.Combine(tempDir, "MTR-Station-A-101.pdf");
            File.WriteAllText(sheet1Path, "%PDF-1.4\n1 0 obj<< /Type /Page >>endobj\ntrailer\n%%EOF\n");
            var resolved = DsdWriter.ResolveSheetOutputPath(dwgPath, outPdf, "A-101", layouts.Length);
            Assert.Equal(sheet1Path, resolved, ignoreCase: true);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IssueSetContractRejectsUnknownLayout()
    {
        var contract = new IssueSetContract
        {
            ContractId = "demo",
            Sheets =
            [
                new IssueSetSheet { Layout = "A101", DrawingNo = "MTR-001", Rev = "A" }
            ]
        };
        contract.Validate();
        var findings = contract.ValidateAgainst(["Model", "A102"]);
        Assert.Contains(findings, f => f.Code == "issue_set_layout_missing");
    }

    [Fact]
    public void SheetInventoryCsvRoundTripsToContract()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-inv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "layout,drawingNo,rev\nA101,MTR-001,A\nA102,MTR-002,A\n");
        try
        {
            var inv = SheetInventory.LoadFromCsv(path);
            var contract = inv.ToContract("imported-demo", "demo-project");
            Assert.Equal(2, contract.Sheets.Count);
            Assert.Equal("A101", contract.Sheets[0].Layout);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // readiness-20261118 gap 7: columns are mapped by header name, so a swapped
    // title/rev order can no longer land in the wrong fields.
    [Fact]
    public void SheetInventoryCsvMapsColumnsByHeaderName()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-inv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "layout,drawingNo,title,rev\nS-1,MTR-001,SITE PLAN,1\n");
        try
        {
            var inv = SheetInventory.LoadFromCsv(path);
            Assert.Single(inv.Sheets);
            var sheet = inv.Sheets[0];
            Assert.Equal("S-1", sheet.Layout);
            Assert.Equal("MTR-001", sheet.DrawingNo);
            Assert.Equal("SITE PLAN", sheet.Title);
            Assert.Equal("1", sheet.Rev);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SheetInventoryCsvNamedHeaderNormalOrderStillWorks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-inv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "layout,drawingNo,rev,title\nS-1,MTR-001,1,SITE PLAN\nS-2,MTR-002,2,ELEVATIONS\n");
        try
        {
            var inv = SheetInventory.LoadFromCsv(path);
            Assert.Equal(2, inv.Sheets.Count);
            Assert.Equal("1", inv.Sheets[0].Rev);
            Assert.Equal("SITE PLAN", inv.Sheets[0].Title);
            Assert.Equal("ELEVATIONS", inv.Sheets[1].Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SheetInventoryCsvHeaderWithoutRevOrTitleLeavesThemNull()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-inv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "layout,drawingNo\nS-1,MTR-001\n");
        try
        {
            var inv = SheetInventory.LoadFromCsv(path);
            Assert.Single(inv.Sheets);
            Assert.Null(inv.Sheets[0].Rev);
            Assert.Null(inv.Sheets[0].Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SheetInventoryCsvHeaderlessPositionalStillWorks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-inv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "S-1,MTR-001,1,SITE PLAN\n");
        try
        {
            var inv = SheetInventory.LoadFromCsv(path);
            Assert.Single(inv.Sheets);
            Assert.Equal("S-1", inv.Sheets[0].Layout);
            Assert.Equal("MTR-001", inv.Sheets[0].DrawingNo);
            Assert.Equal("1", inv.Sheets[0].Rev);
            Assert.Equal("SITE PLAN", inv.Sheets[0].Title);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SheetInventoryCsvHeaderMissingRequiredColumnFails()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-inv-{Guid.NewGuid():N}.csv");
        File.WriteAllText(path, "layout,drawingRef,title,rev\nS-1,D1,SITE PLAN,1\n");
        try
        {
            var ex = Assert.Throws<InvalidOperationException>(() => SheetInventory.LoadFromCsv(path));
            Assert.Contains("drawingNo", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // readiness-20261118 gap 5: the publish media gate mirrors the plot_to_pdf resolution —
    // exact stored-media match or an unambiguous size map passes; everything else is refused
    // before PublishDsd can enter the paper-mismatch path and wedge the lane.
    private static readonly PaperMedia[] DwgToPdfMedia =
    [
        PaperMatch.ToMedia("ISO_A3_(420.00_x_297.00_MM)", "ISO A3 (420.00 x 297.00 MM)"),
        PaperMatch.ToMedia("ISO_A4_(297.00_x_210.00_MM)", "ISO A4 (297.00 x 210.00 MM)"),
        PaperMatch.ToMedia("ANSI_B_(17.00_x_11.00_Inches)", "ANSI B (17.00 x 11.00 Inches)")
    ];

    [Fact]
    public void PublishMediaGateRefusesPlaceholderMediaNotOnDevice()
    {
        // The virgin-layout repro: a psk:* placeholder name and a ~5.5x7m stored sheet that
        // maps to no real device medium.
        var verdict = PublishMediaGate.Evaluate(
            "psk:NorthAmericaLetter", 5483.0, 7096.0, DwgToPdfMedia, out var mapped, out var nearest);

        Assert.Equal(PublishMediaVerdict.NotOnDevice, verdict);
        Assert.Null(mapped);
        Assert.NotEmpty(nearest);
    }

    [Fact]
    public void PublishMediaGatePassesExactStoredMedia()
    {
        var verdict = PublishMediaGate.Evaluate(
            "ISO_A3_(420.00_x_297.00_MM)", 420.0, 297.0, DwgToPdfMedia, out var mapped, out _);

        Assert.Equal(PublishMediaVerdict.OnDevice, verdict);
        Assert.Null(mapped);
    }

    [Fact]
    public void PublishMediaGateMapsUnlistedMediaByStoredSize()
    {
        var verdict = PublishMediaGate.Evaluate(
            "ezdxf_(420.00_x_297.00_MM)", 420.0, 297.0, DwgToPdfMedia, out var mapped, out _);

        Assert.Equal(PublishMediaVerdict.MappedBySize, verdict);
        Assert.Equal("ISO_A3_(420.00_x_297.00_MM)", mapped);
    }

    [Fact]
    public void PublishMediaGateRefusesAmbiguousSizeMap()
    {
        // Two equally-plain device media at the stored size — MatchBySize refuses to guess.
        var media = new[]
        {
            PaperMatch.ToMedia("ISO_A3_(420.00_x_297.00_MM)", "ISO A3 (420.00 x 297.00 MM)"),
            PaperMatch.ToMedia("EZDXF_SHEET_(420.00_x_297.00_MM)", "ezdxf (420.00 x 297.00 MM)")
        };
        var verdict = PublishMediaGate.Evaluate("stored_(420.00_x_297.00_MM)", 420.0, 297.0, media, out _, out var nearest);

        Assert.Equal(PublishMediaVerdict.Ambiguous, verdict);
        Assert.Equal(2, nearest.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("None")]
    [InlineData("none_device")]
    public void PublishMediaGateDetectsMissingDevice(string? device)
    {
        Assert.True(PublishMediaGate.DeviceIsMissing(device));
        Assert.False(PublishMediaGate.DeviceIsMissing("DWG To PDF.pc3"));
    }

    // readiness-20261118 gap 8: point-loop terminator decision for forge_exec_command.
    [Theory]
    [InlineData("_.LINE 0,0 10,10", true)]
    [InlineData("_.LINE", true)]
    [InlineData("_.PLINE 0,0 10,10", true)]
    [InlineData("_.PLINE 0,0 10,10 C", false)]   // self-terminating Close — an Enter would repeat at the prompt
    [InlineData("_.MLINE 0,0 10,10", true)]
    [InlineData("_.RECTANG 0,0 10,10", false)]
    [InlineData("_.CIRCLE 0,0 5", false)]
    [InlineData("_.ZOOM _E", false)]
    [InlineData("(setq e 42)", false)]
    public void PointLoopTerminatorOnlyAppendsInsideAnUnterminatedLoop(string command, bool expected)
    {
        var tokens = AutoCadCommandSequence.Tokenize(command);
        Assert.Equal(expected, AutoCadCommandSequence.EndsInUnterminatedPointLoop(tokens));
    }

    // readiness-20261118 gap 8: a bare point-loop command can never finish — refuse it before
    // dispatch instead of parking the lane for the whole watchdog window.
    [Theory]
    [InlineData("_.LINE", true)]
    [InlineData("_.PLINE", true)]
    [InlineData("MLINE", true)]
    [InlineData("_.LINE 0,0", false)]
    [InlineData("_.PLINE 0,0 10,10 C", false)]
    [InlineData("_.CIRCLE", false)]
    [InlineData("_.ZOOM _E", false)]
    public void BarePointLoopCommandIsUnfinishable(string command, bool expected)
    {
        var tokens = AutoCadCommandSequence.Tokenize(command);
        Assert.Equal(expected, AutoCadCommandSequence.IsUnfinishable(tokens));
    }

    [Fact]
    public void IssueSetDiffDetectsAddedLayouts()
    {
        var prev = new PublishReceipt { Layouts = ["A101"], OutputBytes = 100 };
        var curr = new PublishReceipt { Layouts = ["A101", "A102"], OutputBytes = 200 };
        var diff = IssueSetDiff.Compare(prev, curr);
        Assert.Contains("A102", diff.AddedLayouts);
        Assert.True(diff.OutputBytesChanged);
    }

    [Fact]
    public void PackV2FlagsBackgroundPlot()
    {
        var pack = new StandardsPack { PackId = "p", RequireForegroundPlot = true };
        var findings = pack.EvaluatePlotBindings(null, null, null, backgroundPlot: 1);
        Assert.Contains(findings, f => f.Code == "pack_background_plot" && f.Severity == "error");
    }

    [Fact]
    public void PlotToPdfGateFailsWhenPdfProbeFails()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-empty-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, []);
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 1);
            var result = PlotPdfGate.FromProbe("cmd-plot", probe, new { outputPath = path, pdfProbe = probe });

            Assert.False(result.Ok);
            Assert.Equal(PlotPdfGate.FailedCode, result.Error?.Code);
            Assert.False(result.Verification?.Passed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlotToPdfGatePassesWhenPdfProbePasses()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-valid-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "%PDF-1.4\n1 0 obj<< /Type /Page >>endobj\ntrailer\n%%EOF\n");
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 1);
            var result = PlotPdfGate.FromProbe("cmd-plot", probe, new { outputPath = path, pdfProbe = probe });

            Assert.True(result.Ok);
            Assert.True(result.Verification?.Passed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AccoreConsoleScriptCheckDetectsCancelAndUnknownCommand()
    {
        Assert.True(Forge.Server.AccoreConsoleScriptCheck.HasScriptError("Command: _PLOT\n*Cancel*\n", ""));
        Assert.True(Forge.Server.AccoreConsoleScriptCheck.HasScriptError("Unknown command \"FOOBAR\". Press F1 for help.", ""));
        Assert.True(Forge.Server.AccoreConsoleScriptCheck.HasScriptError("", "*Invalid*"));
        Assert.True(Forge.Server.AccoreConsoleScriptCheck.HasScriptError("", "*Invalid selection*"));
        Assert.False(Forge.Server.AccoreConsoleScriptCheck.HasScriptError("Command: _QSAVE\n", ""));
    }

    [Fact]
    public void BatchResumeStateRoundTripsJobTimeoutSeconds()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"forge-batch-{Guid.NewGuid():N}");
        try
        {
            var state = new BatchResumeState
            {
                Jobs =
                [
                    new BatchJobState { DwgPath = "a.dwg", ScriptPath = "a.scr", TimeoutSeconds = 45, Status = "failed" }
                ]
            };
            state.Save(dir);

            var loaded = BatchResumeState.Load(state.ArtifactPath!);
            Assert.NotNull(loaded);
            Assert.Equal(45, loaded.Jobs[0].TimeoutSeconds);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
