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
    public void PdfProbeFailsWithoutPdfHeader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-probe-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "not a pdf\n");
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 1);
            Assert.True(probe.Exists);
            Assert.True(probe.NonEmpty);
            Assert.False(probe.Passed);
            Assert.Contains("%PDF-", probe.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PdfProbeFailsWhenExpectedPagesAndPageCountUnknown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-probe-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "%PDF-1.4\n%%EOF\n");
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 2);
            Assert.Null(probe.PageCount);
            Assert.False(probe.Passed);

            var withoutExpectation = PdfProbeResult.Probe(path);
            Assert.True(withoutExpectation.Passed);
        }
        finally
        {
            File.Delete(path);
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
    public void PlotPdfGateFailsWhenHeaderMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-plot-gate-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "not a pdf\n");
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 1);
            var result = PlotPdfGate.FromProbe("cmd", probe, new { outputPath = path, pdfProbe = probe });
            Assert.False(probe.Passed);
            Assert.False(result.Ok);
            Assert.Equal("plot_probe_failed", result.Error!.Code);
            Assert.NotNull(result.Data);
            Assert.False(result.Verification!.Passed);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlotPdfGateFailsWhenPageCountUnknown()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-plot-gate-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "%PDF-1.4\n%%EOF\n");
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 1);
            Assert.Null(probe.PageCount);
            var result = PlotPdfGate.FromProbe("cmd", probe, new { pdfProbe = probe });
            Assert.False(result.Ok);
            Assert.Equal("plot_probe_failed", result.Error!.Code);
            Assert.NotNull(result.Data);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlotPdfGatePassesMinimalSinglePagePdf()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-plot-gate-{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "%PDF-1.4\n1 0 obj<< /Type /Page >>endobj\ntrailer\n%%EOF\n");
        try
        {
            var probe = PdfProbeResult.Probe(path, expectedPages: 1);
            var result = PlotPdfGate.FromProbe("cmd", probe, new { pdfProbe = probe });
            Assert.True(probe.Passed);
            Assert.True(result.Ok);
            Assert.True(result.Verification!.Passed);
            Assert.Null(result.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlotToPdfSourceProbesBeforeSuccess()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "Forge.Plugin", "PluginCommandProcessor.cs"));
        var start = source.IndexOf("private static ForgeResult PlotToPdf", StringComparison.Ordinal);
        var end = source.IndexOf("private static ForgeResult PlotPublish", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var body = source[start..end];
        Assert.Contains("PdfProbeResult.Probe", body, StringComparison.Ordinal);
        Assert.Contains("expectedPages: 1", body, StringComparison.Ordinal);
        Assert.Contains("PlotPdfGate.FromProbe", body, StringComparison.Ordinal);
        Assert.DoesNotContain("PDF file exists after plot.", body, StringComparison.Ordinal);
        var dry = body.IndexOf("if (command.DryRun)", StringComparison.Ordinal);
        var missing = body.IndexOf("plot_no_output", StringComparison.Ordinal);
        var probe = body.IndexOf("PdfProbeResult.Probe", StringComparison.Ordinal);
        Assert.True(dry >= 0 && missing > dry && probe > missing);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "765T-Forge.ServerOnly.slnf")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Repository root was not found.");
    }
}
