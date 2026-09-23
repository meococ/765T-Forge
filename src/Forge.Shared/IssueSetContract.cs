using System.Text.Json;

namespace Forge.Shared;

/// <summary>
/// Declares the sheets an agent is allowed to publish for an issue set.
/// Extends drawing-number registry from "don't invent numbers" to "don't invent the set".
/// </summary>
public sealed class IssueSetContract
{
    public string ContractId { get; init; } = "";
    public string? ProjectId { get; init; }
    public string? Rev { get; init; }
    public List<IssueSetSheet> Sheets { get; init; } = [];
    public string? SourcePath { get; set; }

    public static IssueSetContract LoadFromFile(string path)
    {
        var json = File.ReadAllText(path);
        var contract = JsonSerializer.Deserialize<IssueSetContract>(json, ForgeJson.Options)
                       ?? throw new InvalidOperationException("Issue set contract JSON deserialized to null.");
        contract.SourcePath = Path.GetFullPath(path);
        contract.Validate();
        return contract;
    }

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(ContractId))
        {
            throw new InvalidOperationException("Issue set contract requires contractId.");
        }

        if (Sheets.Count == 0)
        {
            throw new InvalidOperationException("Issue set contract requires at least one sheet.");
        }

        var seenLayouts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenNos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in Sheets)
        {
            if (string.IsNullOrWhiteSpace(sheet.Layout) || string.IsNullOrWhiteSpace(sheet.DrawingNo))
            {
                throw new InvalidOperationException("Each sheet requires layout and drawingNo.");
            }

            if (!seenLayouts.Add(sheet.Layout))
            {
                throw new InvalidOperationException($"Duplicate layout '{sheet.Layout}' in contract.");
            }

            if (!seenNos.Add(sheet.DrawingNo))
            {
                throw new InvalidOperationException($"Duplicate drawingNo '{sheet.DrawingNo}' in contract.");
            }
        }
    }

    public IReadOnlyList<QaFinding> ValidateAgainst(
        IEnumerable<string> presentLayouts,
        DrawingRegistry? registry = null)
    {
        var findings = new List<QaFinding>();
        var layouts = new HashSet<string>(presentLayouts, StringComparer.OrdinalIgnoreCase);

        foreach (var sheet in Sheets)
        {
            if (!layouts.Contains(sheet.Layout))
            {
                findings.Add(new QaFinding(
                    "issue_set_layout_missing",
                    "error",
                    $"Contract sheet '{sheet.DrawingNo}' expects layout '{sheet.Layout}' which is not in the drawing.",
                    "Open the correct DWG or fix the contract.",
                    "forge_doc_list_layouts"));
            }

            if (registry is not null && registry.FindByDrawingNo(sheet.DrawingNo) is null)
            {
                findings.Add(new QaFinding(
                    "issue_set_drawing_no_unknown",
                    "error",
                    $"Contract drawingNo '{sheet.DrawingNo}' is not in the loaded registry.",
                    "Do not invent sheet numbers.",
                    "forge_registry_lookup"));
            }

            if (registry?.FindByDrawingNo(sheet.DrawingNo) is { } regSheet
                && !string.IsNullOrWhiteSpace(sheet.Rev)
                && !string.IsNullOrWhiteSpace(regSheet.Rev)
                && !string.Equals(sheet.Rev, regSheet.Rev, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new QaFinding(
                    "issue_set_rev_mismatch",
                    "error",
                    $"Contract rev '{sheet.Rev}' for '{sheet.DrawingNo}' != registry rev '{regSheet.Rev}'.",
                    "Align revision before publish.",
                    "forge_registry_lookup"));
            }
        }

        return findings;
    }
}

public sealed class IssueSetSheet
{
    public string Layout { get; init; } = "";
    public string DrawingNo { get; init; } = "";
    public string? Rev { get; init; }
    public string? Title { get; init; }
}

public static class IssueSetContractStore
{
    private static readonly object Gate = new();
    private static IssueSetContract? _current;

    public static IssueSetContract? Current
    {
        get { lock (Gate) return _current; }
    }

    public static void Set(IssueSetContract? contract)
    {
        lock (Gate) _current = contract;
    }
}
