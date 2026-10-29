using System.Text;

namespace Forge.Server;

/// <summary>
/// How forge_run_script hands a user script to accoreconsole.exe. accoreconsole's <c>/s</c> switch
/// only runs .scr files — given "probe.lsp" it looks for "probe.lsp.scr" and fails with
/// "Can't find file". A .lsp is therefore copied (the exact bytes the safety policy checked) to a
/// temp .lsp and driven by a generated temp .scr that loads it. The generated (load …) line is
/// Forge's own trusted text: the SafetyPolicy scan runs on the user's .lsp content, never on the
/// wrapper (whose <c>load</c> the LISP denylist would otherwise refuse).
/// </summary>
public static class AccoreScriptPlan
{
    public enum ScriptKind
    {
        Script,
        Lisp
    }

    public static bool TryGetKind(string scriptPath, out ScriptKind kind, out string? error)
    {
        var ext = Path.GetExtension(scriptPath);
        error = null;
        if (string.IsNullOrEmpty(ext) || ext.Equals(".scr", StringComparison.OrdinalIgnoreCase))
        {
            kind = ScriptKind.Script;
            return true;
        }

        if (ext.Equals(".lsp", StringComparison.OrdinalIgnoreCase))
        {
            kind = ScriptKind.Lisp;
            return true;
        }

        kind = ScriptKind.Script;
        error = $"Unsupported script extension '{ext}'. forge_run_script accepts .scr or .lsp.";
        return false;
    }

    /// <summary>AutoLISP string literal for a Windows path: forward slashes, quotes/backslashes escaped.</summary>
    public static string LispPathLiteral(string path)
    {
        var normalized = path.Replace('\\', '/');
        var sb = new StringBuilder(normalized.Length + 2);
        sb.Append('"');
        foreach (var ch in normalized)
        {
            switch (ch)
            {
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\\':
                    sb.Append("\\\\");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    /// <summary>
    /// The .scr text that loads a .lsp. CMDECHO/FILEDIA are not touched (the user script owns its
    /// session); the trailing <c>(princ)</c> keeps the console quiet about the load return value.
    /// </summary>
    public static string LoaderScript(string lispPath)
        => "(load " + LispPathLiteral(lispPath) + ")\r\n(princ)\r\n";

    /// <summary>
    /// Bytes for the generated loader .scr: plain ASCII when the path is ASCII, otherwise UTF-8 with
    /// BOM (AutoCAD 2021+ reads BOM-marked UTF-8 scripts; a bare UTF-8 path would be read as ANSI).
    /// </summary>
    public static byte[] LoaderScriptBytes(string lispPath)
    {
        var text = LoaderScript(lispPath);
        var ascii = text.All(c => c < 128);
        return ascii
            ? Encoding.ASCII.GetBytes(text)
            : Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
    }
}
