using System.Text.Json;
using System.Text.RegularExpressions;

namespace Forge.Shared;

public sealed record SafetyDecision(bool Allowed, string Code, string Message, string? Suggestion = null)
{
    public static SafetyDecision Allow() => new(true, "allowed", "Allowed");

    public static SafetyDecision Deny(string code, string message, string? suggestion = null)
    {
        return new SafetyDecision(false, code, message, suggestion);
    }
}

public sealed class SafetyPolicy
{
    private static readonly Regex EraseAll = new(@"\b_?-?ERASE\b(?:[\s\r\n;'""]|\(|\))*_?(?:ALL|\*)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Purge = new(@"\b_?-?PURGE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Overkill = new(@"\b_?-?OVERKILL\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Recover = new(@"\b_?-?RECOVER\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AuditFix = new(@"\b_?-?AUDIT\b[\s\r\n;]*(?:_?Y|YES|1|TRUE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Broad selection idioms only — not every asterisk (layer filters, ZOOM *, filenames).
    private static readonly Regex BroadSelectionAll = new(@"\b_?ALL\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex BroadSelectionStarAsArg = new(
        @"\b_?-?(?:ERASE|SELECT|MOVE|COPY|SCALE|ROTATE|MIRROR|STRETCH|EXPLODE|CHPROP|CHANGE|LAYDEL|DELLAYER|WBLOCK)\b(?:[\s\r\n;'""]|\(|\))*\*|\(command\s+""[^""]*(?:ERASE|SELECT)[^""]*""\s+""?\*""?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SaveOverwrite = new(@"\b_?-?(?:SAVEAS|QSAVE|WBLOCK)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LayerDelete = new(@"\b_?-?(?:LAYDEL|DELLAYER)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex SsgGetAllErase = new(@"\bSSGET\b(?:[\s\r\n;'""]|\(|\))*""X""[\s\S]*\b_?-?ERASE\b|\b_?-?ERASE\b[\s\S]*\bSSGET\b(?:[\s\r\n;'""]|\(|\))*""X""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SafetyDecision Evaluate(ForgeCommand command)
    {
        var metadata = ForgeToolRegistry.Get(command.Tool);

        if (metadata.Unsafe && !command.UnsafeAcknowledged)
        {
            return SafetyDecision.Deny(
                "unsafe_not_acknowledged",
                $"{command.Tool} is disabled unless unsafe operations are explicitly acknowledged.",
                "Set enable_unsafe_ops in config and pass unsafeAcknowledged=true only for trusted snippets.");
        }

        if (metadata.ReadOnly)
        {
            return SafetyDecision.Allow();
        }

        if (!metadata.OpenWorld)
        {
            return SafetyDecision.Allow();
        }

        var text = ExtractText(command.Args);
        if (string.IsNullOrWhiteSpace(text))
        {
            return SafetyDecision.Allow();
        }

        return EvaluateText(command.Tool, text);
    }

    public SafetyDecision EvaluateText(string tool, string text)
    {
        if (EraseAll.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_erase_all", "Blocked destructive ERASE ALL command.", "Use a scoped selection set or entity handles.");
        }

        if (Purge.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_purge", "Blocked PURGE through generic execution.", "Use a typed purge/audit tool with backup and dry-run.");
        }

        if (Overkill.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_overkill", "Blocked OVERKILL through generic execution.", "Run a dry-run QA pass first and execute manually if needed.");
        }

        if (Recover.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_recover", "Blocked RECOVER through generic execution.", "Open a backed-up copy and run recovery manually.");
        }

        if (AuditFix.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_audit_fix", "Blocked AUDIT with fix enabled through generic execution.", "Use a typed audit tool with backup and explicit confirmation.");
        }

        if (LayerDelete.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_layer_delete", "Blocked layer deletion command through generic execution.", "Use typed layer tools with an explicit layer name and dry-run first.");
        }

        if (SsgGetAllErase.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_ssget_all_erase", "Blocked AutoLISP all-selection erase pattern.", "Use scoped handles or a typed query/delete workflow.");
        }

        if (SaveOverwrite.IsMatch(text) && (BroadSelectionAll.IsMatch(text) || BroadSelectionStarAsArg.IsMatch(text)))
        {
            return SafetyDecision.Deny("deny_save_wblock_overwrite", "Blocked SAVEAS/QSAVE/WBLOCK pattern with broad selection.", "Use a typed save/export tool with a backup path.");
        }

        var metadata = ForgeToolRegistry.Get(tool);
        if (metadata.OpenWorld && HasBroadOpenWorldSelection(text))
        {
            return SafetyDecision.Deny(
                "deny_openworld_all_selection",
                "Blocked open-world executor command with broad ALL/* selection.",
                "Use typed tools or pass explicit handles. Harmless * (e.g. ZOOM *, layer filters) is allowed.");
        }

        return SafetyDecision.Allow();
    }

    /// <summary>
    /// True when text uses ALL or * as a selection operand for destructive/edit commands,
    /// not merely any asterisk in the payload.
    /// </summary>
    internal static bool HasBroadOpenWorldSelection(string text)
    {
        if (BroadSelectionStarAsArg.IsMatch(text))
        {
            return true;
        }

        // Bare ALL as a selection answer after a modifying command (not "ALL" inside prose alone).
        if (Regex.IsMatch(
                text,
                @"\b_?-?(?:ERASE|SELECT|MOVE|COPY|SCALE|ROTATE|MIRROR|STRETCH|EXPLODE|CHPROP|CHANGE|LAYDEL|DELLAYER|WBLOCK|BLOCK)\b[\s\S]{0,80}\b_?ALL\b",
                RegexOptions.IgnoreCase))
        {
            return true;
        }

        // Standalone selection token lines common in scripts: "ALL" or "*" alone after a command.
        if (Regex.IsMatch(text, @"(?m)^\s*(?:_?ALL|\*)\s*$", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"\b_?-?(?:ERASE|SELECT|MOVE|COPY|SCALE|ROTATE|MIRROR|STRETCH|EXPLODE|CHPROP|CHANGE)\b", RegexOptions.IgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string ExtractText(JsonElement args)
    {
        var values = new List<string>();
        Extract(args, values);
        return string.Join('\n', values);
    }

    private static void Extract(JsonElement element, List<string> values)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                values.Add(element.GetString() ?? "");
                break;
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Extract(property.Value, values);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Extract(item, values);
                }
                break;
        }
    }
}
