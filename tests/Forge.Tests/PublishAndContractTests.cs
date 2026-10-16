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
