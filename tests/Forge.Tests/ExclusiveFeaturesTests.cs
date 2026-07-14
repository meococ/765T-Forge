using Forge.Shared;

namespace Forge.Tests;

public sealed class ExclusiveFeaturesTests
{
    [Fact]
    public void FingerprintHashIsStable()
    {
        var fp = new PlotEnvironmentFingerprint
        {
            ProductVersion = "0.3.0",
            PstyleMode = 1,
            BackgroundPlot = 0,
            DeviceNames = ["DWG To PDF.pc3"]
        }.WithHash();
        var rebuilt = new PlotEnvironmentFingerprint
        {
            ProductVersion = fp.ProductVersion,
            PstyleMode = fp.PstyleMode,
            BackgroundPlot = fp.BackgroundPlot,
            DeviceNames = fp.DeviceNames
        }.WithHash();
        Assert.Equal(fp.FingerprintHash, rebuilt.FingerprintHash);
        Assert.False(string.IsNullOrWhiteSpace(fp.FingerprintHash));
    }

    [Fact]
    public void FingerprintFlagsBackgroundPlot()
    {
        var fp = new PlotEnvironmentFingerprint { BackgroundPlot = 2 };
        var findings = fp.EvaluateAgainstPack(new StandardsPack { PackId = "p", RequireForegroundPlot = true });
        Assert.Contains(findings, f => f.Code == "plot_env_background_plot" && f.Severity == "error");
    }

    [Fact]
    public void DualSourceDetectsMismatch()
    {
        var findings = TitleblockDualSource.Compare(
            new Dictionary<string, string> { ["DWG_NO"] = "MTR-001" },
            new Dictionary<string, string> { ["DWG_NO"] = "WRONG" });
        Assert.Contains(findings, f => f.Code == "titleblock_dual_source_mismatch");
    }

    [Fact]
    public void DependencyClosureFlagsMissing()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".ctb");
        var findings = DependencyClosure.Evaluate([DependencyClosure.FromPath(missing, "plotstyle")]);
        Assert.Contains(findings, f => f.Code == "dependency_missing");
    }

    [Fact]
    public void XrefPinCompareDetectsChange()
    {
        var pin = new XrefClosurePin
        {
            Nodes =
            [
                new XrefPinNode { Name = "X1", Path = @"C:\a.dwg", Length = 10, ContentHash = "AAAA" }
            ]
        };
        var current = new[]
        {
            new XrefPinNode { Name = "X1", Path = @"C:\a.dwg", Length = 11, ContentHash = "BBBB" }
        };
        var findings = XrefClosurePin.Compare(current, pin);
        Assert.Contains(findings, f => f.Code == "xref_pin_mismatch");
    }

    [Fact]
    public void TransmittalSealRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"forge-seal-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "hello");
        try
        {
            var seal = TransmittalSeal.Create([path], receiptId: "r1", hmacKey: "secret");
            Assert.True(TransmittalSeal.Verify(seal, "secret"));
            Assert.False(TransmittalSeal.Verify(seal, "wrong"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CeremonyRequiresIssueAck()
    {
        var findings = PublishCeremony.Evaluate(dryRunDone: true, preflightPassed: true, issueAcknowledged: false);
        Assert.Contains(findings, f => f.Code == "ceremony_issue_ack_required");
    }

    [Fact]
    public void CdeGateBlocksIssuedFromWip()
    {
        var rules = new CdeGateRules();
        var findings = rules.Evaluate("WIP", "A", "MTR-001", treatingAsIssued: true);
        Assert.Contains(findings, f => f.Code == "cde_issued_status_blocked");
    }

    [Fact]
    public void ModalTrapFlagsFileDia()
    {
        var findings = ModalTrapHints.EvaluateAutomationSysvars(fileDia: 1);
        Assert.Contains(findings, f => f.Code == "modal_trap_filedia");
    }
}
