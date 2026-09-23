namespace Forge.Server;

/// <summary>
/// Positive-evidence-only script check for AccoreConsole output. AccoreConsole can exit 0
/// even when a script command failed, so this looks for AutoCAD's literal abort tokens
/// <c>*Cancel*</c>, <c>Unknown command</c>, and <c>*Invalid*</c> (OrdinalIgnoreCase literal
/// search; no patterns, no fuzzy matching).
///
/// Semantics, stated exactly:
/// <list type="bullet">
/// <item>A match fails the run with <see cref="ErrorCode"/>.</item>
/// <item>A non-match does NOT prove the script succeeded. The absence of these literal markers
/// is not evidence of success and must never be read as verification.</item>
/// <item>The only deterministic verification of what happened to a drawing remains readback
/// (<c>forge_qa_readback</c> / <c>forge_qa_readback_after_timeout</c>).</item>
/// </list>
/// </summary>
public static class AccoreConsoleScriptCheck
{
    public const string ErrorCode = "accoreconsole_script_error";

    public static bool HasScriptError(string? stdout, string? stderr)
    {
        var text = (stdout ?? "") + "\n" + (stderr ?? "");
        return text.Contains("*Cancel*", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Unknown command", StringComparison.OrdinalIgnoreCase)
            || text.Contains("*Invalid*", StringComparison.OrdinalIgnoreCase);
    }
}
