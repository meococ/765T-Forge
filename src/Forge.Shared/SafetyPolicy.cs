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
    // Alias E / _.E is not the word ERASE. Kept separate so the ERASE pattern is unchanged.
    private static readonly Regex EraseAliasAll = new(@"\b_?-?\.?E\b(?:[\s\r\n;'""]|\(|\))*_?(?:ALL\b|\*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex DeleteAll = new(
        @"(?im)(?:^|[;\r\n])\s*(?:\._|\.|_|-)*DELETE\b(?:[\s\r\n;'""]|\(|\))*_?(?:ALL\b|\*)|\(\s*command\s+""(?:\._|\.|_|-)*DELETE\b[^""]*""\s+""?(?:ALL|\*)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    // Command-anchored. A bare \bSH\b would match directory names such as \SH\.
    private static readonly Regex AnchoredExternalCommand = new(
        @"(?im)(?:^|[;\r\n])\s*(?:\._|\.|_|-)*(?:NETLOAD|APPLOAD|ARXLOAD|SCRIPT|SHELL|SH)\b|\(\s*command\s+""(?:\._|\.|_|-)*(?:NETLOAD|APPLOAD|ARXLOAD|SCRIPT|SHELL|SH)\b|\(\s*load(?:\s|""|\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ArxCommandLoad = new(
        @"(?im)(?:^|[;\r\n])\s*(?:\._|\.|_|-)*ARX\b(?:[\s\r\n;'""]|\(|\))*_?(?:LOAD|L)\b|\(\s*arxload(?:\s|""|\))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AnchoredExecutorSave = new(
        @"(?im)(?:^|[;\r\n])\s*(?:\._|\.|_|-)*(?:SAVEAS|QSAVE|WBLOCK|SAVE)\b|\(\s*command\s+""(?:\._|\.|_|-)*(?:SAVEAS|QSAVE|WBLOCK|SAVE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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
        if (EraseAll.IsMatch(text) || EraseAliasAll.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_erase_all", "Blocked destructive ERASE ALL command.", "Use a scoped selection set or entity handles.");
        }

        if (DeleteAll.IsMatch(text))
        {
            return SafetyDecision.Deny(
                "deny_delete_all",
                "Blocked destructive DELETE ALL command.",
                "Use a scoped selection set or entity handles.");
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

        if (IsSsgetDestructive(text))
        {
            return SafetyDecision.Deny("deny_ssget_destructive", "Blocked ssget all-selection combined with erase, delete, or command.", "Use scoped handles or a typed query/delete workflow.");
        }

        if (IsObfuscatedSelection(text))
        {
            return SafetyDecision.Deny("deny_obfuscated_selection", "Blocked strcat/eval combined with ALL or ssget.", "Do not build selection text at runtime. Pass explicit handles to a typed tool.");
        }

        if (AnchoredExternalCommand.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_external_command", "Blocked NETLOAD, APPLOAD, ARXLOAD, load, SCRIPT, or SHELL/SH command.", "Do not load foreign code or start a shell from an executor.");
        }

        if (ArxCommandLoad.IsMatch(text))
        {
            return SafetyDecision.Deny(
                "deny_arx_load",
                "Blocked ARX Load or arxload.",
                "Do not load ARX modules from an executor.");
        }

        if (SaveOverwrite.IsMatch(text) && (BroadSelectionAll.IsMatch(text) || BroadSelectionStarAsArg.IsMatch(text)))
        {
            return SafetyDecision.Deny("deny_save_wblock_overwrite", "Blocked SAVEAS/QSAVE/WBLOCK pattern with broad selection.", "Use a typed save/export tool with a backup path.");
        }

        var metadata = ForgeToolRegistry.Get(tool);
        if (metadata.OpenWorld && AnchoredExecutorSave.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_executor_save", "Blocked SAVE, QSAVE, SAVEAS, or WBLOCK on an executor.", "Use forge_doc_save or another typed save/export tool.");
        }

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

    private static bool IsSsgetDestructive(string text)
    {
        var hasMode = Regex.IsMatch(
            text,
            @"\bssget\b[\s\S]*""_?(?:X|A)""|""_?(?:X|A)""[\s\S]*\bssget\b",
            RegexOptions.IgnoreCase);
        if (!hasMode)
        {
            return false;
        }

        return Regex.IsMatch(text, @"\b(?:entdel|vla-erase|vla-delete|command)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsObfuscatedSelection(string text)
    {
        if (!Regex.IsMatch(text, @"\b(?:strcat|eval)\b", RegexOptions.IgnoreCase))
        {
            return false;
        }

        return Regex.IsMatch(text, @"\bALL\b|\bssget\b", RegexOptions.IgnoreCase);
    }

    private static string ExtractText(JsonElement args)
    {
        var values = new List<string>();
        Extract(args, values);
        return string.Join("\n", values);
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
