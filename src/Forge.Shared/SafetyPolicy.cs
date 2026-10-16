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
    private static readonly Regex EraseAll = new(
        @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:ERASE|E(?![\w-]))\b(?:[\s\r\n;'""]|\(|\))*_?ALL\s*(?:$|[\r\n""');])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Purge = new(
        @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:PURGE|PU)(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Overkill = new(@"\b_?-?OVERKILL\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Recover = new(@"\b_?-?RECOVER(?:ALL)?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AuditFix = new(@"\b_?-?AUDIT\b[\s\r\n;]*(?:_?Y|YES|1|TRUE)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex BroadSelectionStarAsArg = new(
        @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:ERASE|E(?![\w-])|SELECT|MOVE|COPY|SCALE|ROTATE|MIRROR|STRETCH|EXPLODE|CHPROP|CHANGE|LAYDEL|DELLAYER|WBLOCK)\b(?:[\s\r\n;'""]|\(|\))*\*|\(\s*(?:command|vl-cmdf)\s+""[^""]*(?:ERASE|SELECT)[^""]*""\s+""?\*""?\)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SaveOverwrite = new(
        @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:SAVEAS|QSAVE|WBLOCK|SAVE)(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SessionTermination = new(
        @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:QUIT|EXIT|CLOSEALL)(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ShellExec = new(
        @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:SHELL|SH|START|AI_EDITCUSTFILE)(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ModuleLoad = new(
        @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:NETLOAD|ARX|APPLOAD|VBALOAD|VBARUN|VBASTMT|RSCRIPT)(?![\w-])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LayerDelete = new(@"\b_?-?(?:LAYDEL|DELLAYER)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SsgGetAllErase = new(
        @"\bSSGET\b(?:[\s\r\n;'""]|\(|\))*""_?[XA]""[\s\S]*\b(?:_?-?(?:ERASE|E)|ENTDEL|VLA-DELETE|VLA-ERASE)\b|\b(?:_?-?(?:ERASE|E)|ENTDEL|VLA-DELETE|VLA-ERASE)\b[\s\S]*\bSSGET\b(?:[\s\r\n;'""]|\(|\))*""_?[XA]""|\(\s*(?:VLAX-INVOKE(?:-METHOD)?\b[\s\S]{0,80}?'?DELETE\b|VLA-(?:DELETE|ERASE)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LispOsAccess = new(
        @"\(\s*(?:STARTAPP|VL-CMDF\s+""(?:_\.?|-)?(?:SHELL|SH|START)|VL-REGISTRY-WRITE|VL-REGISTRY-DELETE|VLAX-CREATE-OBJECT|VLAX-GET-OR-CREATE-OBJECT|VLAX-GET-OBJECT|VLA-GETINTERFACEOBJECT|VL-FILE-DELETE|VL-FILE-RENAME|VL-FILE-COPY|VL-FILE-MKTEMP|VL-MKDIR|ARXLOAD|STARTSERVER|NETLOAD|LOAD|AUTOLOAD|VL-LOAD-ALL|VL-VBALOAD|VL-VBARUN|OPEN|WRITE-LINE|WRITE-CHAR|PRINC-TO-FILE)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex LispDynamicEval = new(
        @"(?:\(\s*(?:EVAL|READ|APPLY)\b|\(\s*(?:SETVAR|VLA-SETVARIABLE|COMMAND|VL-CMDF)\s+\(\s*(?:STRCAT|CHR|VL-LIST->STRING|SUBSTR|READ|EVAL)\b)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public SafetyDecision Evaluate(ForgeCommand command)
    {
        var metadata = ForgeToolRegistry.Get(command.Tool);

        if (metadata.ReadOnly)
        {
            return SafetyDecision.Allow();
        }

        if (metadata.OpenWorld)
        {
            var text = ExtractText(command.Args);
            if (!string.IsNullOrWhiteSpace(text))
            {
                var textDecision = EvaluateText(command.Tool, text);
                if (!textDecision.Allowed)
                {
                    return textDecision;
                }
            }
        }

        if (metadata.Unsafe && !command.UnsafeAcknowledged)
        {
            return SafetyDecision.Deny(
                "unsafe_not_acknowledged",
                $"{command.Tool} is disabled unless unsafe operations are explicitly acknowledged.",
                "Set FORGE_ENABLE_UNSAFE_OPS=true in config and pass unsafeAcknowledged=true only for trusted snippets.");
        }

        return SafetyDecision.Allow();
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
            return SafetyDecision.Deny("deny_ssget_all_erase", "Blocked AutoLISP all-selection or VLA delete pattern.", "Use scoped handles or a typed query/delete workflow.");
        }

        if (ShellExec.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_shell_exec", "Blocked OS shell command through generic execution.", "External shell execution (SHELL/SH/START) is forbidden.");
        }

        if (ModuleLoad.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_module_load", "Blocked external module/macro load command through generic execution.", "Loading external DLL/ARX/LSP/VBA modules via generic executors is forbidden.");
        }

        if (LispOsAccess.IsMatch(text))
        {
            return SafetyDecision.Deny(
                "deny_lisp_os_access",
                "Blocked AutoLISP process/COM/file/module primitive through generic execution.",
                "Use typed Forge tools instead of startapp, vlax-create-object, vla-getinterfaceobject, open/write-line, load, or vl-file-copy.");
        }

        if (LispDynamicEval.IsMatch(text))
        {
            return SafetyDecision.Deny(
                "deny_lisp_dynamic_eval",
                "Blocked dynamic evaluation or string-constructed command/sysvar call.",
                "Avoid eval/read/apply or strcat-constructed setvar/command calls; use typed Forge tools instead.");
        }

        if (SysvarPolicy.ContainsDeniedSysvar(text, out var deniedVar))
        {
            return SafetyDecision.Deny(
                SysvarPolicy.DenyCode,
                $"Blocked access to restricted system variable {deniedVar} through generic execution.",
                "Leave trust and startup variables unchanged. Use forge_system_getvar or typed tools.");
        }

        if (SaveOverwrite.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_save_wblock_overwrite", "Blocked SAVEAS/QSAVE/WBLOCK/SAVE through generic execution.", "Use forge_doc_save with overwriteAcknowledged and automatic backup.");
        }

        if (SessionTermination.IsMatch(text))
        {
            return SafetyDecision.Deny("deny_session_termination", "Blocked QUIT/EXIT/CLOSEALL through generic execution.", "Closing the AutoCAD session via generic executors is forbidden.");
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

        // Bare ALL as a selection answer after a modifying command (not "ALL" inside prose like text "E ALL PIPES").
        if (Regex.IsMatch(
                text,
                @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:ERASE|E(?![\w-])|SELECT|MOVE|COPY|SCALE|ROTATE|MIRROR|STRETCH|EXPLODE|CHPROP|CHANGE|LAYDEL|DELLAYER|WBLOCK|BLOCK)\b[\s\S]{0,80}?(?:^|[\s""()'])_?ALL\s*(?:$|[\r\n""');])",
                RegexOptions.IgnoreCase))
        {
            return true;
        }

        // Standalone selection token lines common in scripts: "ALL" or "*" alone after a command.
        if (Regex.IsMatch(text, @"(?m)^\s*(?:_?ALL|\*)\s*$", RegexOptions.IgnoreCase)
            && Regex.IsMatch(text, @"(?:(?:^|[\r\n])\s*|\(\s*(?:command|vl-cmdf)\s+""\s*)(?:_\.?|-)?(?:ERASE|E(?![\w-])|SELECT|MOVE|COPY|SCALE|ROTATE|MIRROR|STRETCH|EXPLODE|CHPROP|CHANGE)\b", RegexOptions.IgnoreCase))
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
