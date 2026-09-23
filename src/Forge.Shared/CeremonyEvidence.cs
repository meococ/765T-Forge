using System.Text.Json;

namespace Forge.Shared;

/// <summary>
/// Optional evidence binding for publish ceremony (ADR 0004).
/// Booleans remain attested; when IDs are supplied they are enforced against on-disk artifacts.
/// </summary>
public static class CeremonyEvidence
{
    private static readonly HashSet<string> DryRunTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_plot_publish",
        "forge_plot_to_pdf",
        "forge_recipe_issue_set",
        "forge_pack_and_go"
    };

    private static readonly HashSet<string> PreflightTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_qa_preflight"
    };

    private static readonly HashSet<string> PublishTools = new(StringComparer.OrdinalIgnoreCase)
    {
        "forge_plot_publish",
        "forge_recipe_issue_set"
    };

    public static IReadOnlyList<QaFinding> Evaluate(
        string? auditDirectory,
        string? dryRunAuditId = null,
        string? preflightAuditId = null,
        string? receiptAuditId = null,
        string? receiptsDirectory = null)
    {
        var findings = new List<QaFinding>();

        if (!string.IsNullOrWhiteSpace(dryRunAuditId))
        {
            if (!TryFindAudit(auditDirectory, dryRunAuditId!, out var tool, out var dryRun))
            {
                findings.Add(Missing("ceremony_evidence_missing", $"dryRunAuditId '{dryRunAuditId}' not found in audit JSONL."));
            }
            else if (!DryRunTools.Contains(tool ?? ""))
            {
                findings.Add(Missing(
                    "ceremony_evidence_tool_mismatch",
                    $"dryRunAuditId '{dryRunAuditId}' tool '{tool}' is not a plot/publish dry-run tool."));
            }
            else if (dryRun != true)
            {
                findings.Add(Missing(
                    "ceremony_evidence_not_dry_run",
                    $"dryRunAuditId '{dryRunAuditId}' exists but DryRun was not true."));
            }
        }

        if (!string.IsNullOrWhiteSpace(preflightAuditId))
        {
            if (!TryFindAudit(auditDirectory, preflightAuditId!, out var tool, out _))
            {
                findings.Add(Missing("ceremony_evidence_missing", $"preflightAuditId '{preflightAuditId}' not found in audit JSONL."));
            }
            else if (!PreflightTools.Contains(tool ?? ""))
            {
                findings.Add(Missing(
                    "ceremony_evidence_tool_mismatch",
                    $"preflightAuditId '{preflightAuditId}' tool '{tool}' is not forge_qa_preflight."));
            }
        }

        if (!string.IsNullOrWhiteSpace(receiptAuditId))
        {
            if (TryFindReceiptByAuditId(receiptsDirectory, receiptAuditId!))
            {
                // OK — receipt artifact stamped with AuditId.
            }
            else if (!TryFindAudit(auditDirectory, receiptAuditId!, out var tool, out _))
            {
                findings.Add(Missing(
                    "ceremony_evidence_missing",
                    $"receiptAuditId '{receiptAuditId}' not found as receipt or publish audit."));
            }
            else if (!PublishTools.Contains(tool ?? ""))
            {
                findings.Add(Missing(
                    "ceremony_evidence_tool_mismatch",
                    $"receiptAuditId '{receiptAuditId}' tool '{tool}' is not a publish tool."));
            }
        }

        return findings;
    }

    private static QaFinding Missing(string code, string message)
        => new(
            code,
            "error",
            message,
            "Pass AuditIds from real dry-run / forge_qa_preflight / publish receipt, or omit evidence IDs (attested-only mode).",
            "forge_publish_ceremony_check");

    public static bool TryFindAudit(string? auditDirectory, string auditId, out string? tool, out bool? dryRun)
    {
        tool = null;
        dryRun = null;
        if (string.IsNullOrWhiteSpace(auditDirectory) || !Directory.Exists(auditDirectory))
        {
            return false;
        }

        var files = Directory.GetFiles(auditDirectory, "*.jsonl")
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .Take(14)
            .ToArray();

        foreach (var file in files)
        {
            foreach (var line in File.ReadLines(file))
            {
                if (string.IsNullOrWhiteSpace(line) || line.IndexOf(auditId, StringComparison.Ordinal) < 0)
                {
                    continue;
                }

                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("auditId", out var idEl) ||
                        !string.Equals(idEl.GetString(), auditId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    tool = root.TryGetProperty("tool", out var t) ? t.GetString() : null;
                    if (root.TryGetProperty("dryRun", out var d))
                    {
                        dryRun = d.ValueKind == JsonValueKind.True;
                    }

                    return true;
                }
                catch
                {
                    // Skip malformed.
                }
            }
        }

        return false;
    }

    public static bool TryFindReceiptByAuditId(string? receiptsDirectory, string auditId)
    {
        var root = receiptsDirectory
                   ?? Path.Combine(
                       Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                       "765T-Forge",
                       "receipts");
        if (!Directory.Exists(root))
        {
            return false;
        }

        foreach (var file in Directory.GetFiles(root, "receipt-*.json").OrderByDescending(File.GetLastWriteTimeUtc).Take(50))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                if (doc.RootElement.TryGetProperty("auditId", out var id) &&
                    string.Equals(id.GetString(), auditId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch
            {
                // Skip.
            }
        }

        return false;
    }
}
