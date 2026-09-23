using System.Text.Json;
using Forge.Shared;

namespace Forge.Tests;

public sealed class TrustHardening030Tests
{
    [Fact]
    public void ForceGateDeniesWhenEnvOff()
    {
        Assert.False(ForcePublishGate.IsAllowed(forceRequested: true, allowForcePublish: false));
        Assert.True(ForcePublishGate.IsAllowed(forceRequested: false, allowForcePublish: false));
        Assert.True(ForcePublishGate.IsAllowed(forceRequested: true, allowForcePublish: true));
    }

    [Fact]
    public void CeremonyEvidenceFlagsMissingAuditId()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forge-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var findings = CeremonyEvidence.Evaluate(dir, dryRunAuditId: "missing-id");
            Assert.Contains(findings, f => f.Code == "ceremony_evidence_missing");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CeremonyEvidenceAcceptsDryRunAudit()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forge-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var auditId = Guid.NewGuid().ToString("N");
            var payload = new
            {
                auditId,
                source = "server",
                tool = "forge_plot_publish",
                commandId = "c1",
                dryRun = true,
                allowed = true
            };
            File.WriteAllText(
                Path.Combine(dir, $"{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl"),
                JsonSerializer.Serialize(payload, ForgeJson.Options) + Environment.NewLine);

            var findings = CeremonyEvidence.Evaluate(dir, dryRunAuditId: auditId);
            Assert.Empty(findings);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CeremonyEvidenceRejectsNonDryRunForDryRunId()
    {
        var dir = Path.Combine(Path.GetTempPath(), "forge-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var auditId = Guid.NewGuid().ToString("N");
            var payload = new
            {
                auditId,
                source = "server",
                tool = "forge_plot_publish",
                commandId = "c1",
                dryRun = false,
                allowed = true
            };
            File.WriteAllText(
                Path.Combine(dir, $"{DateTimeOffset.UtcNow:yyyyMMdd}.jsonl"),
                JsonSerializer.Serialize(payload, ForgeJson.Options) + Environment.NewLine);

            var findings = CeremonyEvidence.Evaluate(dir, dryRunAuditId: auditId);
            Assert.Contains(findings, f => f.Code == "ceremony_evidence_not_dry_run");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CeremonyEvidenceFindsReceiptByAuditId()
    {
        var receipts = Path.Combine(Path.GetTempPath(), "forge-receipts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(receipts);
        try
        {
            var auditId = Guid.NewGuid().ToString("N");
            var receipt = new PublishReceipt { AuditId = auditId, OutputPath = "x.pdf" };
            var path = PublishReceipt.TryWriteArtifact(receipt, out var writeError, receipts);
            Assert.Null(writeError);
            Assert.NotNull(path);
            Assert.True(CeremonyEvidence.TryFindReceiptByAuditId(receipts, auditId));
            var findings = CeremonyEvidence.Evaluate(auditDirectory: null, receiptAuditId: auditId, receiptsDirectory: receipts);
            Assert.Empty(findings);
        }
        finally
        {
            Directory.Delete(receipts, recursive: true);
        }
    }

    [Fact]
    public void PublishCeremonyMergesEvidenceFindings()
    {
        var evidence = new[]
        {
            new QaFinding("ceremony_evidence_missing", "error", "missing", SuggestedTool: "forge_publish_ceremony_check")
        };
        var findings = PublishCeremony.Evaluate(
            dryRunDone: true,
            preflightPassed: true,
            issueAcknowledged: true,
            evidenceFindings: evidence);
        Assert.Contains(findings, f => f.Code == "ceremony_evidence_missing");
    }

    [Fact]
    public void XrefFailClosedFlagsMissing()
    {
        var nodes = new[]
        {
            new XrefClosureNode { Name = "A", Path = @"C:\missing.dwg", Depth = 1, WalkStatus = XrefWalkStatuses.Missing }
        };
        var findings = XrefClosureEval.EvaluateFailClosed(nodes);
        Assert.Contains(findings, f => f.Code == "xref_nested_incomplete");
    }

    [Fact]
    public void XrefPinKeyIncludesParent()
    {
        var node = new XrefClosureNode { Name = "Child", ParentName = "Host", Depth = 2 };
        Assert.Equal("Host>Child", node.PinKey);
    }

    [Fact]
    public void ClampMaxDepthBounds()
    {
        Assert.Equal(4, XrefClosureEval.ClampMaxDepth(null));
        Assert.Equal(1, XrefClosureEval.ClampMaxDepth(0));
        Assert.Equal(8, XrefClosureEval.ClampMaxDepth(99));
    }
}
