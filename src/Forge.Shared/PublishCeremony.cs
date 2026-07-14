namespace Forge.Shared;

public sealed class BlastRadiusBudget
{
    public int MaxSheets { get; init; } = 50;
    public int MaxDestructiveExecs { get; init; } = 5;
    public int MaxPathRewrites { get; init; } = 100;

    public int SheetsUsed { get; set; }
    public int DestructiveExecsUsed { get; set; }
    public int PathRewritesUsed { get; set; }

    public IReadOnlyList<QaFinding> Evaluate()
    {
        var findings = new List<QaFinding>();
        if (SheetsUsed > MaxSheets)
        {
            findings.Add(new QaFinding(
                "blast_radius_sheets",
                "error",
                $"Sheet budget exceeded: {SheetsUsed}/{MaxSheets}.",
                "Split the issue set or raise MaxSheets with human approval.",
                "forge_publish_ceremony_check"));
        }

        if (DestructiveExecsUsed > MaxDestructiveExecs)
        {
            findings.Add(new QaFinding(
                "blast_radius_destructive",
                "error",
                $"Destructive executor budget exceeded: {DestructiveExecsUsed}/{MaxDestructiveExecs}.",
                SuggestedTool: "forge_publish_ceremony_check"));
        }

        if (PathRewritesUsed > MaxPathRewrites)
        {
            findings.Add(new QaFinding(
                "blast_radius_path_rewrites",
                "error",
                $"Path rewrite budget exceeded: {PathRewritesUsed}/{MaxPathRewrites}.",
                SuggestedTool: "forge_publish_ceremony_check"));
        }

        return findings;
    }
}

public static class PublishCeremony
{
    public static IReadOnlyList<QaFinding> Evaluate(
        bool dryRunDone,
        bool preflightPassed,
        bool issueAcknowledged,
        bool publishedStatusAck = false,
        bool requirePublishedAck = false,
        BlastRadiusBudget? budget = null,
        IEnumerable<QaFinding>? evidenceFindings = null)
    {
        var findings = new List<QaFinding>();
        if (!dryRunDone)
        {
            findings.Add(new QaFinding(
                "ceremony_dry_run_required",
                "error",
                "Publish ceremony requires a completed dry-run before issue (attested unless dryRunAuditId binds evidence).",
                SuggestedTool: "forge_plot_publish"));
        }

        if (!preflightPassed)
        {
            findings.Add(new QaFinding(
                "ceremony_preflight_required",
                "error",
                "Publish ceremony requires a passing preflight (attested unless preflightAuditId binds evidence).",
                SuggestedTool: "forge_qa_preflight"));
        }

        if (!issueAcknowledged)
        {
            findings.Add(new QaFinding(
                "ceremony_issue_ack_required",
                "error",
                "Human issueAcknowledged=true is required for Issued/Published ceremony (attested — not a security boundary).",
                SuggestedTool: "forge_publish_ceremony_check"));
        }

        if (requirePublishedAck && !publishedStatusAck)
        {
            findings.Add(new QaFinding(
                "ceremony_published_ack_required",
                "error",
                "Second acknowledgement required for Published/CDE status transition.",
                SuggestedTool: "forge_cde_gate_evaluate"));
        }

        if (budget is not null)
        {
            findings.AddRange(budget.Evaluate());
        }

        if (evidenceFindings is not null)
        {
            findings.AddRange(evidenceFindings);
        }

        return findings;
    }
}
