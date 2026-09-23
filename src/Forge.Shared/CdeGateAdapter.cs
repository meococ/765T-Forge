using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Shared;

/// <summary>CAD-side ISO 19650-lite gate — not a CDE product.</summary>
public sealed class CdeGateRules
{
    public List<string> AllowedStatuses { get; init; } = ["WIP", "Shared", "Published", "S0", "S1", "S2", "S3", "S4", "A1"];
    public string? NamingRegex { get; init; }
    public string? RevisionScheme { get; init; }
    public List<string> PublishedRequiresStatuses { get; init; } = ["Published", "S4", "A1"];

    public IReadOnlyList<QaFinding> Evaluate(string? status, string? rev, string? drawingNo, bool treatingAsIssued = false)
    {
        var findings = new List<QaFinding>();
        if (!string.IsNullOrWhiteSpace(status)
            && AllowedStatuses.Count > 0
            && !AllowedStatuses.Any(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new QaFinding(
                "cde_status_invalid",
                "error",
                $"Status '{status}' is not in the allowed CDE set.",
                SuggestedTool: "forge_cde_gate_evaluate"));
        }

        if (treatingAsIssued
            && !string.IsNullOrWhiteSpace(status)
            && PublishedRequiresStatuses.Count > 0
            && !PublishedRequiresStatuses.Any(s => s.Equals(status, StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new QaFinding(
                "cde_issued_status_blocked",
                "error",
                $"Refusing Issued publish while status='{status}'.",
                "Promote information container status per BEP before Issued PDF.",
                "forge_cde_gate_evaluate"));
        }

        if (!string.IsNullOrWhiteSpace(NamingRegex) && !string.IsNullOrWhiteSpace(drawingNo))
        {
            try
            {
                var naming = new Regex(NamingRegex!, RegexOptions.CultureInvariant, ForgeConstants.RegexMatchTimeout);
                if (!naming.IsMatch(drawingNo))
                {
                    findings.Add(new QaFinding(
                        "cde_naming_invalid",
                        "error",
                        $"Drawing number '{drawingNo}' fails CDE naming regex.",
                        SuggestedTool: "forge_cde_gate_evaluate"));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                findings.Add(new QaFinding(
                    "regex_timeout",
                    "error",
                    $"CDE naming regex '{NamingRegex}' exceeded {ForgeConstants.RegexMatchTimeoutMilliseconds} ms for drawing number '{drawingNo}'.",
                    SuggestedTool: "forge_cde_gate_evaluate"));
            }
        }

        if (!string.IsNullOrWhiteSpace(RevisionScheme) && !string.IsNullOrWhiteSpace(rev))
        {
            try
            {
                var scheme = new Regex(RevisionScheme!, RegexOptions.CultureInvariant, ForgeConstants.RegexMatchTimeout);
                if (!scheme.IsMatch(rev))
                {
                    findings.Add(new QaFinding(
                        "cde_rev_invalid",
                        "error",
                        $"Revision '{rev}' fails scheme '{RevisionScheme}'.",
                        SuggestedTool: "forge_cde_gate_evaluate"));
                }
            }
            catch (RegexMatchTimeoutException)
            {
                findings.Add(new QaFinding(
                    "regex_timeout",
                    "error",
                    $"CDE revision scheme regex '{RevisionScheme}' exceeded {ForgeConstants.RegexMatchTimeoutMilliseconds} ms for revision '{rev}'.",
                    SuggestedTool: "forge_cde_gate_evaluate"));
            }
        }

        return findings;
    }

    public static string? WriteSidecar(string directory, object metadata)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "cde-sidecar.json");
            AtomicFile.WriteAllText(path, JsonSerializer.Serialize(metadata, ForgeJson.Options));
            return path;
        }
        catch
        {
            return null;
        }
    }
}
